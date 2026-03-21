using System.Diagnostics;
using System.Runtime.InteropServices;
using CTMemoryEditor.Models;

namespace CTMemoryEditor.Services;

/// <summary>
/// High-level service for reading and writing Chrono Trigger game state memory.
/// </summary>
public sealed class GameMemoryService : IDisposable
{
    private IntPtr _processHandle = IntPtr.Zero;
    private uint _gameStateBase;
    private uint _battleDataBase;
    private uint _rngSeedAddress;
    private uint _moduleBase;
    private int  _pid;

    // Pending split-phase LCG snapshot (used by TryRefindRngSeed to avoid blocking sleeps).
    private List<(uint Base, uint Size)>? _lcgPendingRegions;
    private byte[][]? _lcgPendingSnap;
    private DateTime _lcgPendingTime;
    public string RngDiagnostic { get; private set; } = "";

    // RVA of the game state pointer relative to the module base (Ghidra image base 0x00100000).
    // Calibrated: Ghidra VA 0x001ae693 == process VA 0x00E6E693 → delta 0x00CC0000, imagebase 0x00DC0000-0x00CC0000=0x00100000.
    private const uint GhidraImageBase = 0x00100000;
    private const uint GameStatePointerRVA = GameOffsets.GameStatePointerVA - GhidraImageBase;

    public bool IsAttached => _processHandle != IntPtr.Zero;
    public uint GameStateBase => _gameStateBase;
    public uint BattleDataBase => _battleDataBase;
    public bool IsRngSeedAvailable => _rngSeedAddress != 0;

    public (bool Success, string Message) TryAttach()
    {
        if (IsAttached)
            return (true, "Already attached.");

        Process[] processes = Process.GetProcessesByName(GameOffsets.ProcessName);
        if (processes.Length == 0)
            return (false, "Chrono Trigger process not found. Is the game running?");

        Process target = processes[0];
        int pid = target.Id;
        IntPtr handle = NativeMethods.OpenProcess(NativeMethods.DesiredAccess, false, pid);

        if (handle == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            return (false, $"Failed to open process (PID {pid}), Win32 error {err}. Try running as Administrator.");
        }

        uint moduleBase = FindMainModuleBase(handle);

        // Strategy 1: Try RVA-based pointer resolution (fast)
        uint stateBase = TryResolveViaPointer(handle, moduleBase);

        // Strategy 2: Signature scan for the character array in heap memory (slower, robust)
        if (stateBase == 0)
            stateBase = ScanForCharacterArray(handle);

        if (stateBase == 0)
        {
            NativeMethods.CloseHandle(handle);
            return (false,
                $"PID {pid}, module 0x{moduleBase:X8}. " +
                "Could not find game state in memory. Make sure a save file is loaded (not title screen).");
        }

        uint battleDataBase = TryResolveViaDirectRead(handle, moduleBase,
                                 GameOffsets.BattleDataPointerVA - GhidraImageBase)
                             ?? ScanForBattleDataBase(handle, stateBase);

        uint rngSeedAddr = FindRngSeedAddress(handle, moduleBase, pid);

        _processHandle = handle;
        _gameStateBase = stateBase;
        _battleDataBase = battleDataBase;
        _rngSeedAddress = rngSeedAddr;
        _moduleBase = moduleBase;
        _pid = pid;

        string rngInfo = rngSeedAddr != 0
            ? $", rng seed at 0x{rngSeedAddr:X8} | {RngDiagnostic}"
            : $", rng seed not found ({RngDiagnostic})";
        return (true, $"Attached! PID {pid}, module 0x{moduleBase:X8}, state at 0x{stateBase:X8}, battle data at 0x{battleDataBase:X8}{rngInfo}");
    }

    /// <summary>
    /// Reads a single pointer from moduleBase+rva. Returns null if the read fails or yields zero.
    /// </summary>
    private static uint? TryResolveViaDirectRead(IntPtr handle, uint moduleBase, uint rva)
    {
        if (moduleBase == 0) return null;
        uint va = moduleBase + rva;
        if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)va, out uint value, 4, out _)) return null;
        return value != 0 ? value : null;
    }

    /// <summary>
    /// Tries to find the game state base by reading a pointer at the expected RVA,
    /// scanning a ±64 byte window. Returns 0 on failure.
    /// </summary>
    private static uint TryResolveViaPointer(IntPtr handle, uint moduleBase)
    {
        uint pointerVA = moduleBase != 0
            ? moduleBase + GameStatePointerRVA
            : GameOffsets.GameStatePointerVA;

        const int ScanRange = 64;
        uint scanStart = pointerVA - ScanRange;
        uint scanEnd = pointerVA + ScanRange;

        for (uint candidateVA = scanStart; candidateVA <= scanEnd; candidateVA += 4)
        {
            if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)candidateVA,
                    out uint candidatePtr, 4, out _))
                continue;

            if (candidatePtr == 0 || candidatePtr < 0x00600000)
                continue;

            if (ValidateGameStateBase(handle, candidatePtr))
                return candidatePtr;
        }

        return 0;
    }

    /// <summary>
    /// Scans all committed private/mapped memory regions for the character array signature:
    /// 7 consecutive uint32s at stride 0x120 holding values 0,1,2,3,4,5,6 (character IDs).
    /// Returns the game state base, or 0 on failure.
    /// </summary>
    private static uint ScanForCharacterArray(IntPtr handle)
    {
        // The character array signature: 7 IDs at known stride
        // We search for the pattern in bulk-read memory blocks.
        // Character array starts at gameStateBase + 0x10C0.
        // charBase[i] + 0x00 == i, for i in 0..6, stride = 0x120 bytes.
        // Total span from first ID to last: 6 * 0x120 = 0x6C0 bytes.
        const uint CharSpan = 6 * GameOffsets.CharacterStride; // 0x6C0

        IntPtr address = IntPtr.Zero;
        nuint mbiSize = (nuint)Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();

        while (true)
        {
            nuint result = NativeMethods.VirtualQueryEx(handle, address, out NativeMethods.MEMORY_BASIC_INFORMATION mbi, mbiSize);
            if (result == 0)
                break;

            // Only scan committed, readable, private/mapped regions (heap lives here)
            bool isCommitted = mbi.State == NativeMethods.MEM_COMMIT;
            bool isReadable = NativeMethods.IsReadableProtection(mbi.Protect);
            bool isHeapLike = (mbi.Type == NativeMethods.MEM_PRIVATE) || (mbi.Type == NativeMethods.MEM_MAPPED);

            if (isCommitted && isReadable && isHeapLike && mbi.RegionSize >= CharSpan + 0x120)
            {
                uint found = ScanRegionForSignature(handle, (uint)mbi.BaseAddress, (uint)mbi.RegionSize);
                if (found != 0)
                    return found;
            }

            // Advance to next region
            ulong next = (ulong)mbi.BaseAddress + mbi.RegionSize;
            if (next > 0xFFFFFFFF) // stay within 32-bit address space
                break;
            address = (IntPtr)next;
        }

        return 0;
    }

    /// <summary>
    /// Scans a single memory region for the character ID signature.
    /// Returns gameStateBase or 0.
    /// </summary>
    private static uint ScanRegionForSignature(IntPtr handle, uint regionBase, uint regionSize)
    {
        // Read the entire region into a local buffer
        // Cap at 4MB per read to avoid excessive allocation
        const uint MaxRead = 4 * 1024 * 1024;
        uint readSize = Math.Min(regionSize, MaxRead);
        byte[] buffer = new byte[readSize];

        if (!NativeMethods.ReadProcessMemoryBulk(handle, (IntPtr)regionBase, buffer, (nuint)readSize, out nuint bytesRead))
            return 0;

        uint actualSize = (uint)bytesRead;

        // We need at least charArrayOffset(0x10C0) + 7 records of 0x120 bytes
        // from a candidate gameStateBase. But we're scanning for the char array directly,
        // so we need: position + 6*0x120 + 4 bytes to fit in the buffer.
        uint minNeeded = CharArraySignatureSpan;
        if (actualSize < minNeeded)
            return 0;

        // Scan every 4-byte alignment looking for the character ID pattern
        uint limit = actualSize - minNeeded;
        for (uint offset = 0; offset <= limit; offset += 4)
        {
            // Quick check: first uint32 at this offset should be 0 (Crono's ID)
            uint val0 = BitConverter.ToUInt32(buffer, (int)offset);
            if (val0 != 0)
                continue;

            // Check all 7 character IDs
            bool match = true;
            for (uint i = 1; i < 7; i++)
            {
                uint charOffset = offset + (i * GameOffsets.CharacterStride);
                uint val = BitConverter.ToUInt32(buffer, (int)charOffset);
                if ((val & 0xFF) != i)
                {
                    match = false;
                    break;
                }
            }

            if (!match)
                continue;

            // We found the character array start. The game state base is 0x10C0 before it.
            uint charArrayAddr = regionBase + offset;
            uint gameStateBase = charArrayAddr - GameOffsets.CharacterArrayBase;

            // Extra validation: verify via process read (not buffer) to be sure
            if (ValidateGameStateBase(handle, gameStateBase))
                return gameStateBase;
        }

        return 0;
    }

    private const uint CharArraySignatureSpan = 6 * GameOffsets.CharacterStride + 4;

    /// <summary>
    /// Scans heap memory for BATTLE_DATA_OFFSET by searching region contents for the value
    /// gameStateBase (SOME_BATTLE_OFFSET). CL_GameObj_InitMapSystem stores it at struct+0xfd78,
    /// so any occurrence of gameStateBase at address A implies BATTLE_DATA_OFFSET = A - 0xfd78.
    /// Scanning content (not just region bases) handles structs allocated inside larger heap blocks.
    /// Returns the base address of BATTLE_DATA_OFFSET, or 0 on failure.
    /// </summary>
    private static uint ScanForBattleDataBase(IntPtr handle, uint gameStateBase)
    {
        const uint PointerOffset = 0xfd78;
        const uint MaxRead = 4 * 1024 * 1024;

        byte[] needle = BitConverter.GetBytes(gameStateBase);

        IntPtr address = IntPtr.Zero;
        nuint mbiSize = (nuint)Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();

        while (true)
        {
            nuint result = NativeMethods.VirtualQueryEx(handle, address, out NativeMethods.MEMORY_BASIC_INFORMATION mbi, mbiSize);
            if (result == 0)
                break;

            bool isCommitted = mbi.State  == NativeMethods.MEM_COMMIT;
            bool isReadable  = NativeMethods.IsReadableProtection(mbi.Protect);
            bool isHeapLike  = mbi.Type   == NativeMethods.MEM_PRIVATE || mbi.Type == NativeMethods.MEM_MAPPED;

            if (isCommitted && isReadable && isHeapLike && mbi.RegionSize >= 4)
            {
                uint readSize = (uint)Math.Min(mbi.RegionSize, MaxRead);
                byte[] buffer = new byte[readSize];

                if (NativeMethods.ReadProcessMemoryBulk(handle, address, buffer, readSize, out nuint bytesRead)
                    && bytesRead >= 4)
                {
                    uint limit = (uint)bytesRead - 4;
                    for (uint i = 0; i <= limit; i += 4)
                    {
                        if (buffer[i]     != needle[0] || buffer[i + 1] != needle[1] ||
                            buffer[i + 2] != needle[2] || buffer[i + 3] != needle[3])
                            continue;

                        // Found gameStateBase at region+i → candidate struct base = (region+i) - 0xfd78
                        uint foundVA = (uint)mbi.BaseAddress + i;
                        if (foundVA < PointerOffset)
                            continue;

                        uint candidateBase = foundVA - PointerOffset;
                        return candidateBase;
                    }
                }
            }

            ulong next = (ulong)mbi.BaseAddress + mbi.RegionSize;
            if (next > 0xFFFFFFFF)
                break;
            address = (IntPtr)next;
        }

        return 0;
    }

    /// <summary>
    /// Validates a candidate game state base by reading Crono's ID (0) and Marle's ID (1).
    /// </summary>
    private static bool ValidateGameStateBase(IntPtr handle, uint gameStateBase)
    {
        uint cronoBase = gameStateBase + GameOffsets.CharacterArrayBase;
        if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)cronoBase, out uint cronoId, 4, out _))
            return false;
        if ((cronoId & 0xFF) != 0)
            return false;

        uint marleBase = cronoBase + GameOffsets.CharacterStride;
        if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)marleBase, out uint marleId, 4, out _))
            return false;
        if ((marleId & 0xFF) != 1)
            return false;

        // Also verify Magus (index 6) to reduce false positives
        uint magusBase = cronoBase + (6 * GameOffsets.CharacterStride);
        if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)magusBase, out uint magusId, 4, out _))
            return false;
        if ((magusId & 0xFF) != 6)
            return false;

        return true;
    }

    private static uint FindMainModuleBase(IntPtr processHandle)
    {
        IntPtr[] modules = new IntPtr[1024];
        if (!NativeMethods.EnumProcessModulesEx(processHandle, modules,
                (uint)(modules.Length * IntPtr.Size), out uint cbNeeded,
                NativeMethods.LIST_MODULES_32BIT))
        {
            return 0;
        }

        int moduleCount = (int)(cbNeeded / (uint)IntPtr.Size);
        char[] nameBuffer = new char[260];

        for (int i = 0; i < moduleCount; i++)
        {
            uint len = NativeMethods.GetModuleFileNameExW(processHandle, modules[i], nameBuffer, (uint)nameBuffer.Length);
            if (len == 0) continue;

            string modulePath = new(nameBuffer, 0, (int)len);
            if (modulePath.EndsWith("Chrono Trigger.exe", StringComparison.OrdinalIgnoreCase))
            {
                return (uint)modules[i].ToInt64();
            }
        }

        return 0;
    }

    public void Detach()
    {
        if (_processHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
            _gameStateBase = 0;
            _battleDataBase = 0;
            _rngSeedAddress = 0;
            _moduleBase = 0;
            _pid = 0;
            _lcgPendingRegions = null;
            _lcgPendingSnap = null;
        }
    }

    // --- RNG Seed retry ---

    /// <summary>
    /// Called from the refresh timer when the seed address has not yet been found.
    /// Returns true if the address was located this call.
    ///
    /// Uses a split-phase LCG scan across timer ticks (snapshots ~500 ms apart) rather
    /// than a blocking 60 ms sleep, so CT's infrequent rand() bursts are caught reliably.
    /// </summary>
    public bool TryRefindRngSeed()
    {
        if (!IsAttached || _rngSeedAddress != 0) return false;

        // Try classic msvcrt pattern + ucrtbase FLS — neither blocks.
        uint addr = FindRngSeedAddress(_processHandle, _moduleBase, _pid, runLcgScan: false);
        if (addr != 0)
        {
            _rngSeedAddress = addr;
            _lcgPendingRegions = null;
            _lcgPendingSnap = null;
            RngDiagnostic = $"(found on retry) {RngDiagnostic}";
            return true;
        }

        // Split-phase LCG scan: take a snapshot now, compare on the next tick.
        // The 500 ms timer interval means the two reads are ~500 ms apart — far more
        // likely to span a rand() burst (33 calls on room transition) than 60 ms.
        // Retake the snapshot every 2 s if still no match, so stale data doesn't linger.
        if (_lcgPendingRegions == null
            || (DateTime.UtcNow - _lcgPendingTime).TotalMilliseconds > 2000)
        {
            TakeLcgSnapshot(_processHandle);
            return false;
        }

        addr = TryMatchLcgSnapshot(_processHandle);
        if (addr == 0)
            return false;

        _rngSeedAddress = addr;
        _lcgPendingRegions = null;
        _lcgPendingSnap = null;
        RngDiagnostic = "(found on retry) LCG split-phase scan";
        return true;
    }

    /// <summary>
    /// Enumerates all committed private readable regions and stores a memory snapshot
    /// plus a timestamp for use by <see cref="TryMatchLcgSnapshot"/>.
    /// </summary>
    private void TakeLcgSnapshot(IntPtr processHandle)
    {
        const uint MaxPerRegion = 4 * 1024 * 1024;
        var regions = new List<(uint Base, uint Size)>();
        IntPtr addr = IntPtr.Zero;
        nuint mbiSize = (nuint)Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();

        while (true)
        {
            nuint r = NativeMethods.VirtualQueryEx(processHandle, addr,
                out NativeMethods.MEMORY_BASIC_INFORMATION mbi, mbiSize);
            if (r == 0) break;

            if (mbi.State  == NativeMethods.MEM_COMMIT
             && mbi.Type   == NativeMethods.MEM_PRIVATE
             && NativeMethods.IsReadableProtection(mbi.Protect)
             && mbi.RegionSize >= 4)
            {
                regions.Add(((uint)mbi.BaseAddress,
                             (uint)Math.Min(mbi.RegionSize, MaxPerRegion)));
            }

            ulong next = (ulong)mbi.BaseAddress + mbi.RegionSize;
            if (next > 0xFFFFFFFF) break;
            addr = (IntPtr)next;
        }

        byte[][] snap = new byte[regions.Count][];
        for (int i = 0; i < regions.Count; i++)
        {
            snap[i] = new byte[regions[i].Size];
            NativeMethods.ReadProcessMemoryBulk(processHandle, (IntPtr)regions[i].Base,
                snap[i], regions[i].Size, out _);
        }

        _lcgPendingRegions = regions;
        _lcgPendingSnap    = snap;
        _lcgPendingTime    = DateTime.UtcNow;
    }

    /// <summary>
    /// Re-reads each region from the stored snapshot and checks whether any 4-byte-aligned
    /// word advanced by 1–8 MSVC LCG steps, indicating <c>_holdrand</c>.
    /// Returns the address of the seed or 0 if not found.
    /// </summary>
    private uint TryMatchLcgSnapshot(IntPtr processHandle)
    {
        if (_lcgPendingRegions == null || _lcgPendingSnap == null) return 0;

        const uint LcgMul   = 214013;
        const uint LcgAdd   = 2531011;
        const int  MaxSteps = 8;

        for (int ri = 0; ri < _lcgPendingRegions.Count; ri++)
        {
            (uint regionBase, uint regionSize) = _lcgPendingRegions[ri];
            byte[] buf2 = new byte[regionSize];
            if (!NativeMethods.ReadProcessMemoryBulk(processHandle, (IntPtr)regionBase,
                    buf2, regionSize, out nuint br) || br < 4)
                continue;

            byte[] buf1 = _lcgPendingSnap[ri];
            uint limit = (uint)Math.Min((uint)br, (uint)buf1.Length) - 4;

            for (uint i = 0; i <= limit; i += 4)
            {
                uint v1 = BitConverter.ToUInt32(buf1, (int)i);
                if (v1 == 0) continue;

                uint v2 = BitConverter.ToUInt32(buf2, (int)i);
                if (v1 == v2) continue;

                uint check = v1;
                for (int step = 0; step < MaxSteps; step++)
                {
                    check = unchecked(check * LcgMul + LcgAdd);
                    if (check == v2)
                        return regionBase + i;
                }
            }
        }

        return 0;
    }

    // --- RNG Seed ---

    /// <summary>
    /// Locates the MSVC rand() seed in the loaded CRT DLL.
    ///
    /// Two strategies are tried in order:
    ///
    /// 1. Classic (msvcrt.dll): seed is a global variable; the write-back instruction
    ///    MOV [_ranseed], EAX (A3 xx xx xx xx) encodes the address directly.
    ///
    /// 2. ucrtbase.dll: seed (_holdrand) is a field in the per-thread __acrt_ptd struct,
    ///    accessed via a TLS slot.  We parse rand() for the holdrand offset, then parse
    ///    __acrt_getptd for the TLS slot index, then walk the main thread's TEB32 to
    ///    resolve the __acrt_ptd* and compute the final address.
    ///
    /// Returns 0 if the address cannot be determined.
    /// </summary>
    private uint FindRngSeedAddress(IntPtr handle, uint moduleBase, int pid, bool runLcgScan = true)
    {
        if (moduleBase == 0) return 0;

        // Read the IAT entry for rand() — holds rand()'s runtime address in the CRT DLL.
        uint iatEntryAddr = moduleBase + GameOffsets.RandIatRva;
        if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)iatEntryAddr, out uint randFuncAddr, 4, out _)
            || randFuncAddr == 0)
            return 0;

        byte[] code = new byte[128];
        if (!NativeMethods.ReadProcessMemoryBulk(handle, (IntPtr)randFuncAddr, code, 128, out nuint br)
            || br < 16)
            return 0;
        int len = (int)br;

        // --- Strategy 1: classic msvcrt.dll ---
        // ADD EAX, 0x269EC3 (05 C3 9E 26 00) immediately followed by MOV [abs32], EAX (A3).
        for (int i = 0; i <= len - 10; i++)
        {
            if (code[i] == 0x05 && code[i+1] == 0xC3 && code[i+2] == 0x9E
                && code[i+3] == 0x26 && code[i+4] == 0x00 && code[i+5] == 0xA3)
            {
                RngDiagnostic = "classic msvcrt.dll pattern";
                return BitConverter.ToUInt32(code, i + 6);
            }
        }

        // --- Strategy 2: LCG memory scan (initial attach only; retry path uses split-phase) ---
        // Read the process heap twice (60 ms apart) and look for a 4-byte value that
        // changed by exactly one (or a few) MSVC LCG steps between reads.  This works
        // regardless of FLS/TLS layout and requires no TEB32 navigation.
        if (runLcgScan)
        {
            uint lcgAddr = ScanForLcgSeed(handle);
            if (lcgAddr != 0)
            {
                RngDiagnostic = "LCG scan";
                return lcgAddr;
            }
        }

        // --- Strategy 3: ucrtbase.dll (FLS-based __acrt_ptd._holdrand) ---
        return FindRngSeedUcrtbase(handle, code, len, randFuncAddr, pid);
    }

    /// <summary>
    /// Scans all committed private heap regions twice (with a brief pause between reads)
    /// and returns the first 4-byte-aligned address whose value advanced by 1–8 steps of
    /// the MSVC LCG formula (seed = seed * 214013 + 2531011) between the two reads.
    ///
    /// This directly locates <c>_holdrand</c> without any TEB32/FLS navigation.
    /// The pause gives the game time to call rand() at least once.
    /// </summary>
    private static uint ScanForLcgSeed(IntPtr processHandle)
    {
        const uint LcgMul      = 214013;
        const uint LcgAdd      = 2531011;
        const int  MaxSteps    = 8;          // check up to 8 LCG steps per interval
        const uint MaxPerRegion = 4 * 1024 * 1024;

        // Enumerate committed private readable regions (the process heap lives here).
        var regions = new List<(uint Base, uint Size)>();
        IntPtr addr = IntPtr.Zero;
        nuint mbiSize = (nuint)Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();

        while (true)
        {
            nuint r = NativeMethods.VirtualQueryEx(processHandle, addr,
                out NativeMethods.MEMORY_BASIC_INFORMATION mbi, mbiSize);
            if (r == 0) break;

            if (mbi.State  == NativeMethods.MEM_COMMIT
             && mbi.Type   == NativeMethods.MEM_PRIVATE
             && NativeMethods.IsReadableProtection(mbi.Protect)
             && mbi.RegionSize >= 4)
            {
                regions.Add(((uint)mbi.BaseAddress,
                             (uint)Math.Min(mbi.RegionSize, MaxPerRegion)));
            }

            ulong next = (ulong)mbi.BaseAddress + mbi.RegionSize;
            if (next > 0xFFFFFFFF) break;
            addr = (IntPtr)next;
        }

        // First snapshot.
        var snap = regions.Select(rg =>
        {
            byte[] b = new byte[rg.Size];
            NativeMethods.ReadProcessMemoryBulk(processHandle, (IntPtr)rg.Base, b, rg.Size, out _);
            return b;
        }).ToArray();

        // Wait long enough for the game to call rand() at least once.
        System.Threading.Thread.Sleep(60);

        // Second read — compare against snapshot.
        for (int ri = 0; ri < regions.Count; ri++)
        {
            (uint regionBase, uint regionSize) = regions[ri];
            byte[] buf2 = new byte[regionSize];
            if (!NativeMethods.ReadProcessMemoryBulk(processHandle, (IntPtr)regionBase,
                    buf2, regionSize, out nuint br) || br < 4)
                continue;

            byte[] buf1 = snap[ri];
            uint limit = (uint)Math.Min((uint)br, (uint)buf1.Length) - 4;

            for (uint i = 0; i <= limit; i += 4)
            {
                uint v1 = BitConverter.ToUInt32(buf1, (int)i);
                if (v1 == 0) continue; // zero holdrand means this thread was never seeded

                uint v2 = BitConverter.ToUInt32(buf2, (int)i);
                if (v1 == v2) continue; // unchanged — rand() not called here

                // Walk the LCG up to MaxSteps and see if any step reaches v2.
                uint check = v1;
                for (int step = 0; step < MaxSteps; step++)
                {
                    check = unchecked(check * LcgMul + LcgAdd);
                    if (check == v2)
                        return regionBase + i;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// ucrtbase.dll implementation — rand() disassembly (from live process):
    ///   E8 xxxxxxxx          call __acrt_getptd_noexit   ; returns __acrt_ptd* in EAX
    ///   8B C8                mov ecx, eax
    ///   85 C9                test ecx, ecx
    ///   0F 84 xxxxxxxx       je  (null path — abort/fallback)
    ///   69 41 18 FD43 0300   imul eax, [ecx+18h], 214013 ; _holdrand at ptd+0x18
    ///   05 C39E 2600         add  eax, 2531011
    ///   89 41 18             mov  [ecx+18h], eax          ; write-back
    ///   C1 E8 10             shr  eax, 16
    ///   25 FF7F 0000         and  eax, 7FFFh
    ///   C3                   ret
    ///
    /// The IMUL encodes the immediate AFTER a ModRM byte and optional displacement,
    /// so we must parse ModRM to find where the 4-byte immediate starts.
    /// ptd is returned via FlsGetValue (Fiber Local Storage), so we read it from
    /// TEB32.FlsData (offset 0x0FB4) rather than TlsSlots (offset 0xE10).
    /// </summary>
    private uint FindRngSeedUcrtbase(IntPtr handle, byte[] randCode, int randLen,
                                     uint randFuncAddr, int pid)
    {
        // ── Step 1: locate IMUL r32, r/m32, 214013 (opcode 69) ──────────────────
        // The immediate 214013 = 0x000343FD comes AFTER the ModRM byte and any
        // displacement, so we must decode ModRM to find the imm32 position.
        int imulPos = -1;
        int imulLen = 0;
        for (int i = 0; i <= randLen - 7; i++)
        {
            if (randCode[i] != 0x69 || i + 2 >= randLen) continue;

            byte modrm   = randCode[i + 1];
            int  mod     = (modrm >> 6) & 3;
            int  rm      =  modrm       & 7;
            int  dispSize = mod switch { 1 => 1, 2 => 4, 0 when rm == 5 => 4, _ => 0 };
            if (rm == 4 && mod != 3) dispSize += 1; // SIB byte

            int immPos = i + 2 + dispSize;
            if (immPos + 4 > randLen) continue;

            if (BitConverter.ToUInt32(randCode, immPos) == 0x000343FD)
            {
                imulPos = i;
                imulLen = 2 + dispSize + 4;
                break;
            }
        }
        if (imulPos < 0) { RngDiagnostic = "ucrtbase: IMUL 214013 not found in rand()"; return 0; }

        // ── Step 2: find _holdrand write-back MOV [reg+disp], reg after IMUL ───
        int holdrandOffset = int.MinValue;
        int searchEnd = Math.Min(imulPos + imulLen + 20, randLen - 3);
        for (int i = imulPos + imulLen; i < searchEnd; i++)
        {
            if (randCode[i] != 0x89) continue;
            byte modrm = randCode[i + 1];
            int  mod   = (modrm >> 6) & 3;
            int  rm    =  modrm       & 7;
            if (rm == 4) continue; // SIB — skip
            if (mod == 1 && i + 2 < randLen) { holdrandOffset = (sbyte)randCode[i + 2]; break; }
            if (mod == 2 && i + 5 < randLen) { holdrandOffset = (int)BitConverter.ToUInt32(randCode, i + 2); break; }
        }
        if (holdrandOffset == int.MinValue) { RngDiagnostic = "ucrtbase: _holdrand write-back not found"; return 0; }

        // ── Step 3: find CALL __acrt_getptd before the IMUL ─────────────────────
        uint getptdAddr = 0;
        for (int i = Math.Max(0, imulPos - 40); i < imulPos - 4; i++)
        {
            if (randCode[i] != 0xE8) continue;
            int  rel    = BitConverter.ToInt32(randCode, i + 1);
            uint target = unchecked((uint)(randFuncAddr + i + 5 + rel));
            if (target > 0x10000000 && target < 0xF0000000)
            { getptdAddr = target; break; }
        }
        if (getptdAddr == 0) { RngDiagnostic = "ucrtbase: __acrt_getptd CALL not found"; return 0; }

        // ── Step 4: find the FLS/TLS slot index global ───────────────────────────
        // __acrt_getptd_noexit may not contain the FlsGetValue call directly —
        // it often delegates to a small helper. We search the function itself and
        // then follow any near CALLs or JMPs one level deep.
        uint flsIndexAddr = FindFlsIndexAddr(handle, getptdAddr);
        if (flsIndexAddr == 0) { RngDiagnostic = "ucrtbase: FLS index global not found in __acrt_getptd"; return 0; }

        // ── Step 5: read the FLS/TLS slot index value ────────────────────────────
        if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)flsIndexAddr, out uint flsIndex, 4, out _)
            || flsIndex > 4096)
        { RngDiagnostic = $"ucrtbase: bad FLS index at 0x{flsIndexAddr:X8} (value={flsIndex})"; return 0; }

        // ── Step 6: scan ALL threads for the one whose FLS slot holds a valid __acrt_ptd* ──
        // The main/earliest thread is not necessarily the one that has called rand().
        // Any thread (game loop, audio, etc.) might own the initialized ptd.
        // Prefer the thread whose holdrand field is non-zero — ucrtbase calloc-initialises
        // all fields to 0, so a zero holdrand means srand/rand was never called on that thread.
        // ── Step 6: scan all TEB32 blocks in process memory for the FLS slot ────
        // Thread-enumeration + delta heuristic proved unreliable (wrong TEB32
        // returned for multiple threads). Brute-force memory scan finds all TEB32s.
        // rand() in CT is only called on room transitions; FLS slot 3 may be null
        // at attach time.  TryRefindRngSeed() retries on each 500 ms tick.
        uint ptdPtr = FindPtdViaFls(handle, flsIndex, holdrandOffset);
        if (ptdPtr == 0)
        { RngDiagnostic = $"ucrtbase: FLS slot {flsIndex} empty in all TEB32s (retry on room transition)"; return 0; }

        uint seedAddr = unchecked((uint)((int)ptdPtr + holdrandOffset));
        NativeMethods.ReadProcessMemory(handle, (IntPtr)seedAddr, out uint liveHoldrand, 4, out _);
        RngDiagnostic = $"ucrtbase OK: ptd={ptdPtr:X8}+0x{holdrandOffset:X}={liveHoldrand:X8}";
        // ── Step 7: return ptd + holdrand_offset ─────────────────────────────────
        return seedAddr;
    }

    /// <summary>
    /// Scans all committed private memory for TEB32 blocks (identified by the self-pointer
    /// invariant: TEB32.NtTib.Self at offset 0x18 equals TEB32 base address), then for each
    /// TEB32 found reads FLS slot <paramref name="flsIndex"/> and checks whether it contains
    /// a plausible <c>__acrt_ptd*</c>.
    ///
    /// This replaces the thread-enumeration + delta approach, which produced wrong TEB32
    /// mappings in the CT WOW64 process (multiple threads mapping to the same TEB32).
    /// </summary>
    private uint FindPtdViaFls(IntPtr processHandle, uint flsIndex, int holdrandOffset)
    {
        const uint MaxPerRegion = 8 * 1024 * 1024;

        uint fallbackPtd = 0;
        IntPtr addr = IntPtr.Zero;
        nuint mbiSize = (nuint)Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();

        while (true)
        {
            nuint r = NativeMethods.VirtualQueryEx(processHandle, addr,
                out NativeMethods.MEMORY_BASIC_INFORMATION mbi, mbiSize);
            if (r == 0) break;

            bool candidate = mbi.State == NativeMethods.MEM_COMMIT
                          && mbi.Type  == NativeMethods.MEM_PRIVATE
                          && NativeMethods.IsReadableProtection(mbi.Protect)
                          && mbi.RegionSize >= 0x20;

            if (candidate)
            {
                uint readSize = (uint)Math.Min(mbi.RegionSize, MaxPerRegion);
                byte[] buf = new byte[readSize];
                if (NativeMethods.ReadProcessMemoryBulk(processHandle, addr, buf, readSize, out nuint br) && br >= 0x20)
                {
                    // Scan for TEB32 self-pointer: value at buf[i] should equal regionBase + i - 0x18,
                    // meaning buf[i] is the Self field and regionBase+i-0x18 is the TEB32 base.
                    uint regionBase = (uint)addr.ToInt64();
                    for (uint i = 0x18; i + 4 <= (uint)br; i += 4)
                    {
                        uint val = BitConverter.ToUInt32(buf, (int)i);
                        uint teb32 = regionBase + i - 0x18;
                        if (val != teb32 || teb32 < 0x10000) continue;

                        // TEB32 blocks are always page-aligned.
                        if (teb32 % 0x1000 != 0) continue;

                        // TEB32.ClientId.UniqueProcess (at +0x020) must match our PID.
                        if (!NativeMethods.ReadProcessMemory(processHandle, (IntPtr)(teb32 + 0x020),
                                out uint tebPid, 4, out _) || tebPid != (uint)_pid) continue;

                        // Found a TEB32 at teb32.  Read FlsData directly from the process
                        // (TEB32 may extend beyond the current buffer chunk).
                        if (!NativeMethods.ReadProcessMemory(processHandle, (IntPtr)(teb32 + 0x0FB4),
                                out uint flsDataPtr, 4, out _) || flsDataPtr == 0) continue;

                        // FLS_DATA layout: LIST_ENTRY header (8 bytes on x86) then slot array.
                        if (!NativeMethods.ReadProcessMemory(processHandle,
                                (IntPtr)(flsDataPtr + 8 + flsIndex * 4), out uint ptdPtr, 4, out _)) continue;

                        if (!IsValidHeapAddress(processHandle, ptdPtr)) continue;

                        uint seedAddr = unchecked((uint)((int)ptdPtr + holdrandOffset));
                        if (!NativeMethods.ReadProcessMemory(processHandle, (IntPtr)seedAddr,
                                out uint holdrand, 4, out _)) continue;

                        if (holdrand != 0)
                            return ptdPtr;

                        if (fallbackPtd == 0)
                            fallbackPtd = ptdPtr;
                    }
                }
            }

            ulong next = (ulong)mbi.BaseAddress + mbi.RegionSize;
            if (next > 0xFFFFFFFF) break;
            addr = (IntPtr)next;
        }

        return fallbackPtd;
    }

    /// <summary>
    /// Returns true if <paramref name="address"/> falls inside a committed private
    /// (heap) region — a reasonable sanity check for a <c>__acrt_ptd*</c>.
    /// </summary>
    private static bool IsValidHeapAddress(IntPtr processHandle, uint address)
    {
        if (address < 0x00010000 || address > 0x7FFFFFFF) return false;

        nuint mbiSize = (nuint)Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();
        if (NativeMethods.VirtualQueryEx(processHandle, (IntPtr)address,
                out NativeMethods.MEMORY_BASIC_INFORMATION mbi, mbiSize) == 0)
            return false;

        return mbi.State   == NativeMethods.MEM_COMMIT
            && mbi.Type    == NativeMethods.MEM_PRIVATE
            && NativeMethods.IsReadableProtection(mbi.Protect);
    }

    /// <summary>
    /// Finds the address of the global that holds the FLS/TLS slot index used by
    /// __acrt_getptd_noexit.  Searches the function itself first, then follows any
    /// near CALLs and near/short JMPs one level deep.
    /// </summary>
    private static uint FindFlsIndexAddr(IntPtr handle, uint funcAddr)
    {
        byte[] code = new byte[256];
        if (!NativeMethods.ReadProcessMemoryBulk(handle, (IntPtr)funcAddr, code, 256, out nuint br) || br < 5)
            return 0;
        int len = (int)br;

        uint result = ScanForFlsIndexLoad(handle, code, len);
        if (result != 0) return result;

        // Follow near CALLs (E8) and near/short JMPs (E9/EB) one level deep.
        for (int i = 0; i <= len - 2; i++)
        {
            uint target = 0;
            if ((code[i] == 0xE8 || code[i] == 0xE9) && i + 5 <= len)
            {
                int rel = BitConverter.ToInt32(code, i + 1);
                target = unchecked((uint)(funcAddr + i + 5 + rel));
            }
            else if (code[i] == 0xEB)
            {
                target = unchecked((uint)(funcAddr + i + 2 + (sbyte)code[i + 1]));
            }

            if (target < 0x00400000 || target > 0xF0000000) continue;

            byte[] inner = new byte[128];
            if (!NativeMethods.ReadProcessMemoryBulk(handle, (IntPtr)target, inner, 128, out nuint br2) || br2 < 5)
                continue;

            result = ScanForFlsIndexLoad(handle, inner, (int)br2);
            if (result != 0) return result;
        }

        return 0;
    }

    /// <summary>
    /// Scans machine code for a global-variable load whose value is a plausible
    /// FLS/TLS slot index (small non-negative integer, ≤ 4096).
    ///
    /// Validated by actually reading the value from the process rather than by
    /// requiring a specific instruction sequence to follow — this handles the
    /// common ucrtbase.dll pattern where a CMP/JE sits between the load and the CALL:
    ///   A1 [addr]     mov eax, [flsIndexAddr]   ; value = 3
    ///   83 F8 FF      cmp eax, -1
    ///   74 xx         je  (alt path)
    ///   50            push eax
    ///   E8 ...        call FlsGetValue
    /// </summary>
    private static uint ScanForFlsIndexLoad(IntPtr handle, byte[] code, int len)
    {
        for (int i = 0; i <= len - 5; i++)
        {
            int instrLen = 0;
            uint candidate = 0;

            if      (code[i] == 0xFF && i + 6 <= len && code[i+1] == 0x35) { instrLen = 6; candidate = BitConverter.ToUInt32(code, i+2); }
            else if (code[i] == 0xA1 && i + 5 <= len)                       { instrLen = 5; candidate = BitConverter.ToUInt32(code, i+1); }
            else if (code[i] == 0x8B && i + 6 <= len && code[i+1] == 0x0D) { instrLen = 6; candidate = BitConverter.ToUInt32(code, i+2); }
            else if (code[i] == 0x8B && i + 6 <= len && code[i+1] == 0x15) { instrLen = 6; candidate = BitConverter.ToUInt32(code, i+2); }

            if (instrLen == 0 || candidate < 0x00400000 || candidate >= 0xF0000000) continue;

            // Read the value at this address. FLS/TLS indices are small non-negative
            // integers (0–10 in practice). Use <= 4096 as a conservative upper bound.
            if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)candidate, out uint value, 4, out _))
                continue;
            if (value <= 4096)
                return candidate;
        }
        return 0;
    }

    /// <summary>
    /// Reads an FLS (Fiber Local Storage) slot value from a 32-bit TEB.
    /// TEB32.FlsData (at offset 0x0FB4) points to the per-thread FLS array.
    /// The Windows FLS_DATA structure starts with a LIST_ENTRY header (8 bytes
    /// on x86), so slot N is at FlsData + 8 + N*4.
    /// The ReactOS implementation uses (N+1)*4 (1-pointer header). We try both
    /// and return the first that looks like a plausible heap pointer.
    /// </summary>
    private static uint ReadFlsSlot32(IntPtr handle, uint teb32Base, uint flsIndex)
    {
        if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)(teb32Base + 0x0FB4),
                out uint flsDataPtr, 4, out _) || flsDataPtr == 0)
            return 0;

        // Layout A: LIST_ENTRY header (8 bytes = 2 × PVOID32) then slot array
        uint offsetA = 8 + flsIndex * 4;
        // Layout B: single-pointer header (ReactOS: (index+1)*4)
        uint offsetB = (flsIndex + 1) * 4;

        foreach (uint offset in new[] { offsetA, offsetB })
        {
            if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)(flsDataPtr + offset),
                    out uint candidate, 4, out _))
                continue;
            if (candidate > 0x00010000 && candidate < 0x80000000)
                return candidate;
        }
        return 0;
    }

    /// <summary>
    /// Finds the TEB32 (32-bit Thread Environment Block) address of the process's main thread
    /// (identified as the thread with the earliest creation time).
    ///
    /// On 64-bit Windows, NtQueryInformationThread(ThreadBasicInformation) returns the native
    /// 64-bit TEB.  For WOW64 (32-bit) threads, the 32-bit TEB is always adjacent in memory:
    /// Windows allocates them as a pair, with TEB32 typically at TEB64 ± 0x2000.
    /// We probe a small set of candidate offsets and validate via the TEB's self-pointer
    /// (TEB32.NtTib.Self at offset 0x18 must equal the TEB32 base address).
    /// </summary>
    private static uint FindMainThreadTeb32(IntPtr handle, int pid)
    {
        IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPTHREAD, 0);
        if (snapshot == new IntPtr(-1)) return 0;

        try
        {
            uint mainThreadId = 0;
            ulong earliestCreate = ulong.MaxValue;

            NativeMethods.THREADENTRY32 te = new() { dwSize = (uint)Marshal.SizeOf<NativeMethods.THREADENTRY32>() };
            if (!NativeMethods.Thread32First(snapshot, ref te)) return 0;

            do
            {
                if (te.th32OwnerProcessID != (uint)pid) continue;

                IntPtr th = NativeMethods.OpenThread(NativeMethods.THREAD_QUERY_INFORMATION, false, te.th32ThreadID);
                if (th == IntPtr.Zero) continue;
                try
                {
                    if (NativeMethods.GetThreadTimes(th, out NativeMethods.FILETIME ct, out _, out _, out _))
                    {
                        ulong t = ((ulong)ct.dwHighDateTime << 32) | ct.dwLowDateTime;
                        if (t < earliestCreate) { earliestCreate = t; mainThreadId = te.th32ThreadID; }
                    }
                    else if (mainThreadId == 0)
                    {
                        mainThreadId = te.th32ThreadID; // fallback: first thread found
                    }
                }
                finally { NativeMethods.CloseHandle(th); }
            }
            while (NativeMethods.Thread32Next(snapshot, ref te));

            if (mainThreadId == 0) return 0;

            IntPtr mainThread = NativeMethods.OpenThread(NativeMethods.THREAD_QUERY_INFORMATION, false, mainThreadId);
            if (mainThread == IntPtr.Zero) return 0;
            try
            {
                return GetTeb32(handle, mainThread);
            }
            finally { NativeMethods.CloseHandle(mainThread); }
        }
        finally { NativeMethods.CloseHandle(snapshot); }
    }

    /// <summary>
    /// Given a thread handle (which may belong to a WOW64/32-bit process), returns the
    /// 32-bit TEB base address.
    ///
    /// Strategy 1: TEB64.NtTib.SubSystemTib (at TEB64+0x18) is set by the WOW64 layer to
    /// point to TEB32 on most Windows versions — read it and validate.
    /// Strategy 2: Scan multiples of 0x1000 in ±0x8000 from TEB64 for a page whose value
    /// at +0x18 equals its own base address (the TEB32 self-pointer invariant).
    /// </summary>
    private static uint GetTeb32(IntPtr processHandle, IntPtr threadHandle)
    {
        NativeMethods.THREAD_BASIC_INFORMATION tbi = default;
        int status = NativeMethods.NtQueryInformationThread(
            threadHandle, 0, ref tbi,
            (uint)Marshal.SizeOf<NativeMethods.THREAD_BASIC_INFORMATION>(), out _);
        if (status < 0) return 0;

        long teb64 = tbi.TebBaseAddress.ToInt64();
        if (teb64 == 0) return 0;

        // Helper: validate a candidate TEB32 address via its self-pointer.
        static bool ValidateTeb32(IntPtr ph, long addr)
        {
            if (addr <= 0 || addr > 0xFFFFFFFF) return false;
            return NativeMethods.ReadProcessMemory(ph, (IntPtr)(addr + 0x18),
                       out uint self, 4, out _) && self == (uint)addr;
        }

        // Strategy 1: read TEB64.NtTib.SubSystemTib (offset 0x18 in TEB64).
        // For WOW64 threads, Windows sets this to the TEB32 address.
        if (teb64 + 0x18 <= 0xFFFFFFFFL
            && NativeMethods.ReadProcessMemory(processHandle, (IntPtr)(teb64 + 0x18),
                   out uint subSystemTib, 4, out _)
            && subSystemTib != 0
            && ValidateTeb32(processHandle, subSystemTib))
            return subSystemTib;

        // Strategy 2: scan ±0x8000 from TEB64 in page-aligned steps.
        for (int delta = -0x8000; delta <= 0x8000; delta += 0x1000)
        {
            if (delta == 0) continue; // TEB64 itself won't have a 32-bit self-pointer
            if (ValidateTeb32(processHandle, teb64 + delta))
                return (uint)(teb64 + delta);
        }
        return 0;
    }

    /// <summary>
    /// Reads a dynamic TLS slot value from a 32-bit TEB.
    /// For index &lt; 64: TEB32.TlsSlots[index] at TEB32 + 0xE10 + index*4.
    /// For index >= 64: TEB32.TlsExpansionSlots (pointer at TEB32 + 0xF94) + (index-64)*4.
    /// </summary>
    private static uint ReadTlsSlot32(IntPtr handle, uint teb32Base, uint tlsIndex)
    {
        if (tlsIndex < 64)
        {
            NativeMethods.ReadProcessMemory(handle,
                (IntPtr)(teb32Base + 0xE10 + tlsIndex * 4), out uint slot, 4, out _);
            return slot;
        }
        else
        {
            if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)(teb32Base + 0xF94),
                    out uint expansionPtr, 4, out _) || expansionPtr == 0)
                return 0;
            NativeMethods.ReadProcessMemory(handle,
                (IntPtr)(expansionPtr + (tlsIndex - 64) * 4), out uint slot, 4, out _);
            return slot;
        }
    }

    public uint ReadRngSeed()
    {
        if (!IsAttached || _rngSeedAddress == 0) return 0;
        NativeMethods.ReadProcessMemory(_processHandle, (IntPtr)_rngSeedAddress, out uint value, 4, out _);
        return value;
    }

    public bool WriteRngSeed(uint seed)
    {
        if (!IsAttached || _rngSeedAddress == 0) return false;
        return NativeMethods.WriteProcessMemory(_processHandle, (IntPtr)_rngSeedAddress, in seed, 4, out _);
    }

    public uint ReadUInt32(uint address)
    {
        if (!IsAttached) return 0;
        NativeMethods.ReadProcessMemory(_processHandle, (IntPtr)address, out uint value, 4, out _);
        return value;
    }

    public bool WriteUInt32(uint address, uint value)
    {
        if (!IsAttached) return false;
        return NativeMethods.WriteProcessMemory(_processHandle, (IntPtr)address, in value, 4, out _);
    }

    public uint GetCharacterBase(int charIndex)
    {
        return _gameStateBase + GameOffsets.CharacterArrayBase + (uint)(charIndex * GameOffsets.CharacterStride);
    }

    public CharacterRecord? ReadCharacter(int charIndex)
    {
        if (!IsAttached || charIndex < 0 || charIndex >= GameOffsets.CharacterCount)
            return null;

        uint charBase = GetCharacterBase(charIndex);

        CharacterRecord record = new()
        {
            CharacterId = (byte)(ReadUInt32(charBase + GameOffsets.Character.Id) & 0xFF),

            HPMax = (ushort)(ReadUInt32(charBase + GameOffsets.Character.HPMax) & 0xFFFF),
            HPCurrent = (ushort)(ReadUInt32(charBase + GameOffsets.Character.HPCurrent) & 0xFFFF),
            MPMax = (ushort)(ReadUInt32(charBase + GameOffsets.Character.MPMax) & 0xFFFF),
            MPCurrent = (ushort)(ReadUInt32(charBase + GameOffsets.Character.MPCurrent) & 0xFFFF),
            HPBase = (ushort)(ReadUInt32(charBase + GameOffsets.Character.HPBase) & 0xFFFF),

            Strength = (byte)(ReadUInt32(charBase + GameOffsets.Character.Strength) & 0xFF),
            Stamina = (byte)(ReadUInt32(charBase + GameOffsets.Character.Stamina) & 0xFF),
            Speed = (byte)(ReadUInt32(charBase + GameOffsets.Character.Speed) & 0xFF),
            Magic = (byte)(ReadUInt32(charBase + GameOffsets.Character.Magic) & 0xFF),
            Accuracy = (byte)(ReadUInt32(charBase + GameOffsets.Character.Accuracy) & 0xFF),
            Evasion = (byte)(ReadUInt32(charBase + GameOffsets.Character.Evasion) & 0xFF),
            MagicDefense = (byte)(ReadUInt32(charBase + GameOffsets.Character.MagicDefense) & 0xFF),

            Level = (byte)(ReadUInt32(charBase + GameOffsets.Character.Level) & 0xFF),
            TotalXP = ReadUInt32(charBase + GameOffsets.Character.TotalXP),
            XPToNextLevel = (ushort)(ReadUInt32(charBase + GameOffsets.Character.XPToNextLevel) & 0xFFFF),

            Weapon = (ushort)(ReadUInt32(charBase + GameOffsets.Character.Weapon) & 0xFFFF),
            Armor = (ushort)(ReadUInt32(charBase + GameOffsets.Character.Armor) & 0xFFFF),
            Helmet = (ushort)(ReadUInt32(charBase + GameOffsets.Character.Helmet) & 0xFFFF),
            Accessory = (ushort)(ReadUInt32(charBase + GameOffsets.Character.Accessory) & 0xFFFF),

            ComputedStrength = (byte)(ReadUInt32(charBase + GameOffsets.Character.ComputedStrength) & 0xFF),
            ComputedStamina = (byte)(ReadUInt32(charBase + GameOffsets.Character.ComputedStamina) & 0xFF),
            ComputedSpeed = (byte)(ReadUInt32(charBase + GameOffsets.Character.ComputedSpeed) & 0xFF),
            ComputedMagic = (byte)(ReadUInt32(charBase + GameOffsets.Character.ComputedMagic) & 0xFF),
            ComputedAccuracy = (byte)(ReadUInt32(charBase + GameOffsets.Character.ComputedAccuracy) & 0xFF),
            ComputedEvasion = (byte)(ReadUInt32(charBase + GameOffsets.Character.ComputedEvasion) & 0xFF),
            ComputedMagicDefense = (byte)(ReadUInt32(charBase + GameOffsets.Character.ComputedMagicDefense) & 0xFF),
            AttackPower = (byte)(ReadUInt32(charBase + GameOffsets.Character.AttackPower) & 0xFF),
            Defense = (byte)(ReadUInt32(charBase + GameOffsets.Character.Defense) & 0xFF),
        };

        return record;
    }

    public bool WriteCharacterField(int charIndex, uint fieldOffset, uint value)
    {
        if (!IsAttached || charIndex < 0 || charIndex >= GameOffsets.CharacterCount)
            return false;

        uint address = GetCharacterBase(charIndex) + fieldOffset;
        return WriteUInt32(address, value);
    }

    public bool WriteBaseAndComputedStat(int charIndex, uint baseOffset, uint computedOffset, uint value)
    {
        bool b1 = WriteCharacterField(charIndex, baseOffset, value);
        bool b2 = WriteCharacterField(charIndex, computedOffset, value);
        return b1 && b2;
    }

    /// <summary>
    /// Writes all editable fields from a CharacterRecord back to game memory.
    /// </summary>
    public void WriteCharacterRecord(int charIndex, CharacterRecord record)
    {
        WriteCharacterField(charIndex, GameOffsets.Character.HPCurrent, record.HPCurrent);
        WriteCharacterField(charIndex, GameOffsets.Character.HPMax, record.HPMax);
        WriteCharacterField(charIndex, GameOffsets.Character.HPBase, record.HPBase);
        WriteCharacterField(charIndex, GameOffsets.Character.MPCurrent, record.MPCurrent);
        WriteCharacterField(charIndex, GameOffsets.Character.MPMax, record.MPMax);
        WriteBaseAndComputedStat(charIndex, GameOffsets.Character.Strength,    GameOffsets.Character.ComputedStrength,    record.Strength);
        WriteBaseAndComputedStat(charIndex, GameOffsets.Character.Stamina,     GameOffsets.Character.ComputedStamina,     record.Stamina);
        WriteBaseAndComputedStat(charIndex, GameOffsets.Character.Speed,       GameOffsets.Character.ComputedSpeed,       record.Speed);
        WriteBaseAndComputedStat(charIndex, GameOffsets.Character.Magic,       GameOffsets.Character.ComputedMagic,       record.Magic);
        WriteBaseAndComputedStat(charIndex, GameOffsets.Character.Accuracy,    GameOffsets.Character.ComputedAccuracy,    record.Accuracy);
        WriteBaseAndComputedStat(charIndex, GameOffsets.Character.Evasion,     GameOffsets.Character.ComputedEvasion,     record.Evasion);
        WriteBaseAndComputedStat(charIndex, GameOffsets.Character.MagicDefense, GameOffsets.Character.ComputedMagicDefense, record.MagicDefense);
        WriteCharacterField(charIndex, GameOffsets.Character.Level,    record.Level);
        WriteCharacterField(charIndex, GameOffsets.Character.TotalXP,  record.TotalXP);
        WriteCharacterField(charIndex, GameOffsets.Character.Weapon,   record.Weapon);
        WriteCharacterField(charIndex, GameOffsets.Character.Armor,    record.Armor);
        WriteCharacterField(charIndex, GameOffsets.Character.Helmet,   record.Helmet);
        WriteCharacterField(charIndex, GameOffsets.Character.Accessory, record.Accessory);
    }

    // --- Party Roster ---

    private static readonly uint[] PartySlotOffsets =
    [
        GameOffsets.PartySlot0,
        GameOffsets.PartySlot1,
        GameOffsets.PartySlot2,
    ];

    private static readonly uint[] BattlePartySlotOffsets =
    [
        GameOffsets.BattlePartySlot0,
        GameOffsets.BattlePartySlot1,
        GameOffsets.BattlePartySlot2,
    ];

    public byte[] ReadPartyRoster()
    {
        byte[] roster = new byte[3];
        for (int i = 0; i < 3; i++)
        {
            // Read from expanded battle data — this is what the game uses at runtime.
            // Game state (0x28E4+) is only the save/load copy and may be stale.
            uint value = _battleDataBase != 0
                ? ReadUInt32(_battleDataBase + BattlePartySlotOffsets[i])
                : ReadUInt32(_gameStateBase + PartySlotOffsets[i]);
            roster[i] = (byte)(value & 0xFF);
        }
        return roster;
    }

    // Load-time snapshot offsets (0x1854/58/5C) — kept in sync so save captures the change.
    private static readonly uint[] BattlePartySlotSnapshotOffsets =
    [
        0x1854,
        0x1858,
        0x185C,
    ];

    public bool WritePartySlot(int slotIndex, byte characterId)
    {
        if (slotIndex < 0 || slotIndex >= 3 || characterId > 6)
            return false;

        bool ok = true;
        if (_battleDataBase != 0)
        {
            // Primary live copy — drives active gameplay (updated by SNES script opcode 0x2D).
            ok = WriteUInt32(_battleDataBase + BattlePartySlotOffsets[slotIndex], characterId);
            // Load-time snapshot — kept in sync so FUN_00312c80 saves the right party.
            ok &= WriteUInt32(_battleDataBase + BattlePartySlotSnapshotOffsets[slotIndex], characterId);
        }

        // Save-struct copy in game state — source for the serializer on save.
        ok &= WriteUInt32(_gameStateBase + PartySlotOffsets[slotIndex], characterId);
        return ok;
    }

    // --- Inventory ---

    public InventorySlot ReadInventorySlot(int slotIndex)
    {
        uint slotBase = _gameStateBase + GameOffsets.InventoryBase + (uint)(slotIndex * GameOffsets.InventorySlotSize);
        uint word0 = ReadUInt32(slotBase);
        uint word1 = ReadUInt32(slotBase + 4);
        uint word2 = ReadUInt32(slotBase + 8);

        return new InventorySlot
        {
            SlotIndex = slotIndex,
            ItemIndex = (byte)(word0 & 0xFF),
            Category = (byte)((word0 >> 8) & 0xFF),
            Quantity = (byte)(word1 & 0xFF),
        };
    }

    public List<InventorySlot> ReadAllInventory()
    {
        List<InventorySlot> slots = new();
        for (int i = 0; i < GameOffsets.InventorySlotCount; i++)
        {
            InventorySlot slot = ReadInventorySlot(i);
            slots.Add(slot);
        }
        return slots;
    }

    public bool WriteInventorySlotQuantity(int slotIndex, byte quantity)
    {
        if (slotIndex < 0 || slotIndex >= GameOffsets.InventorySlotCount)
            return false;
        uint slotBase = _gameStateBase + GameOffsets.InventoryBase + (uint)(slotIndex * GameOffsets.InventorySlotSize);
        return WriteUInt32(slotBase + 4, quantity);
    }

    /// <summary>
    /// Writes a brand-new item into a specific slot (qty = 1, flags = 0).
    /// category is the raw category byte (upper nibble = cat ID, e.g. 0x00 weapon, 0x10 armor...).
    /// </summary>
    public bool WriteInventorySlot(int slotIndex, byte itemId, byte category)
    {
        if (slotIndex < 0 || slotIndex >= GameOffsets.InventorySlotCount)
            return false;
        uint slotBase = _gameStateBase + GameOffsets.InventoryBase + (uint)(slotIndex * GameOffsets.InventorySlotSize);
        uint word0 = (uint)itemId | ((uint)category << 8);
        bool ok = WriteUInt32(slotBase, word0);
        ok &= WriteUInt32(slotBase + 4, 1);   // quantity = 1
        ok &= WriteUInt32(slotBase + 8, 0);   // flags = 0
        return ok;
    }

    /// <summary>
    /// Clears a slot by zeroing all three uint32 fields (item ID, quantity, flags).
    /// </summary>
    public bool ClearInventorySlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= GameOffsets.InventorySlotCount)
            return false;
        uint slotBase = _gameStateBase + GameOffsets.InventoryBase + (uint)(slotIndex * GameOffsets.InventorySlotSize);
        bool ok = WriteUInt32(slotBase, 0);
        ok &= WriteUInt32(slotBase + 4, 0);
        ok &= WriteUInt32(slotBase + 8, 0);
        return ok;
    }

    /// <summary>
    /// Finds the first empty slot within the specified category's designated range.
    /// Returns the slot index, or -1 if the category section is full.
    /// </summary>
    public int FindFirstEmptySlotForCategory(int categoryId)
    {
        if (!IsAttached) return -1;
        if (categoryId < 0 || categoryId >= GameOffsets.CategorySlots.Ranges.Length) return -1;

        (int start, int count) = GameOffsets.CategorySlots.Ranges[categoryId];
        for (int i = start; i < start + count; i++)
        {
            uint slotBase = _gameStateBase + GameOffsets.InventoryBase + (uint)(i * GameOffsets.InventorySlotSize);
            uint word0 = ReadUInt32(slotBase);
            if ((word0 & 0xFF) == 0) // item_id == 0 → empty
                return i;
        }
        return -1;
    }


    // --- Gold / Play Time / Storyline ---

    public uint ReadGold()
    {
        return ReadUInt32(_gameStateBase + GameOffsets.Gold);
    }

    public bool WriteGold(uint value)
    {
        return WriteUInt32(_gameStateBase + GameOffsets.Gold, value);
    }

    public uint ReadPlayTime()
    {
        return ReadUInt32(_gameStateBase + GameOffsets.PlayTime);
    }

    // Returns 1–8 (user-facing). Stored in memory as 0–7.
    public int ReadBattleSpeed()
    {
        if (_battleDataBase != 0)
        {
            return (int)(ReadUInt32(_battleDataBase + GameOffsets.BattleSpeedExpanded) & 0xFF) + 1;
        }
        return (int)(ReadUInt32(_gameStateBase + GameOffsets.BattleSpeed) & 0xFF) + 1;
    }

    public bool WriteBattleSpeed(int value)
    {
        uint stored = (uint)Math.Clamp(value - 1, 0, 7);
        bool ok = true;
        
        if (_battleDataBase != 0)
        {
            uint currentExp = ReadUInt32(_battleDataBase + GameOffsets.BattleSpeedExpanded);
            uint newValExp = (currentExp & 0xFFFFFF00) | stored;
            ok &= WriteUInt32(_battleDataBase + GameOffsets.BattleSpeedExpanded, newValExp);
        }

        uint current = ReadUInt32(_gameStateBase + GameOffsets.BattleSpeed);
        uint newVal = (current & 0xFFFFFF00) | stored;
        ok &= WriteUInt32(_gameStateBase + GameOffsets.BattleSpeed, newVal);
        return ok;
    }

    /// <summary>
    /// Reads the storyline counter. The true value used by the active script engine is stored
    /// in the BattleData (expanded state), which overwrites the GameState WRAM backup.
    /// </summary>
    public byte ReadStoryline()
    {
        if (_battleDataBase != 0)
            return (byte)(ReadUInt32(_battleDataBase + GameOffsets.StorylineCounterExpanded) & 0xFF);
        return (byte)(ReadUInt32(_gameStateBase + GameOffsets.StorylineCounter) & 0xFF);
    }

    public byte ReadStorylineGameState()
    {
        return (byte)(ReadUInt32(_gameStateBase + GameOffsets.StorylineCounter) & 0xFF);
    }

    public byte ReadStorylineBattleData()
    {
        if (_battleDataBase != 0)
            return (byte)(ReadUInt32(_battleDataBase + GameOffsets.StorylineCounterExpanded) & 0xFF);
        return 0;
    }

    /// <summary>
    /// Writes a new storyline counter value to both the SNES RAM image and the expanded
    /// BATTLE_DATA_OFFSET copy. FUN_00370720 restores the byte from the expanded copy on every
    /// room transition, so both must be updated together to survive scene changes.
    /// </summary>
    public bool WriteStoryline(byte value)
    {
        // Primary: dense SNES WRAM byte at SOME_BATTLE_OFFSET+0x10000
        uint current = ReadUInt32(_gameStateBase + GameOffsets.StorylineCounter);
        uint newVal = (current & 0xFFFFFF00) | value;
        bool ok = WriteUInt32(_gameStateBase + GameOffsets.StorylineCounter, newVal);

        // Mirror 1: expanded DWORD at BATTLE_DATA_OFFSET+0x110b0 (zero-extended byte)
        if (_battleDataBase != 0)
            ok &= WriteUInt32(_battleDataBase + GameOffsets.StorylineCounterExpanded, value);

        return ok;
    }


    public bool CheckProcessAlive()
    {
        if (!IsAttached) return false;

        // Try reading Crono's ID as a liveness + validity check
        uint cronoBase = _gameStateBase + GameOffsets.CharacterArrayBase;
        if (!NativeMethods.ReadProcessMemory(_processHandle, (IntPtr)cronoBase,
                out uint cronoId, 4, out _) || (cronoId & 0xFF) != 0)
        {
            Detach();
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        Detach();
    }
}
