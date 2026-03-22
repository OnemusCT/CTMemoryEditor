using System.Diagnostics;
using System.Runtime.InteropServices;
using CTMemoryEditor.Models;

namespace CTMemoryEditor.Services;

/// <summary>
/// High-level service for reading and writing Chrono Trigger game state memory.
/// </summary>
public sealed class GameMemoryService : IDisposable
{
    
    private const uint CharArraySignatureSpan = 6 * GameOffsets.CharacterStride + 4;
    
    private IntPtr _processHandle = IntPtr.Zero;
    private uint _snesDataPtr;
    private uint _gameDataPtr;
    private uint _rngSeedAddress;
    private int  _pid;

    // Pending split-phase LCG snapshot (used by TryRefindRngSeed to avoid blocking sleeps).
    private List<(uint Base, uint Size)>? _lcgPendingRegions;
    private byte[][]? _lcgPendingSnap;
    private DateTime _lcgPendingTime;
    public string RngDiagnostic { get; private set; } = "";

    public bool IsAttached => _processHandle != IntPtr.Zero;
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

        uint gameDataPtr = TryResolveViaDirectRead(handle, moduleBase, GameOffsets.GameDataPointer) ?? 0;
        if (gameDataPtr == 0)
        {
            NativeMethods.CloseHandle(handle);
            return (false, $"PID {pid}: could not game data pointer. Make sure a save file is loaded (not title screen).");
        }
    
        uint snesDataPtr = ScanForCharacterArray(handle);

        if (snesDataPtr == 0)
        {
            NativeMethods.CloseHandle(handle);
            return (false,
                $"PID {pid}, module 0x{moduleBase:X8}. " +
                "Could not find snes game state in memory. Make sure a save file is loaded (not title screen).");
        }


        _processHandle = handle;
        _snesDataPtr = snesDataPtr;
        _gameDataPtr = gameDataPtr;
        _pid = pid;

        return (true,
            $"Attached! PID {pid}, module 0x{moduleBase:X8}\n" +
            $"  game ptr : 0x{gameDataPtr:X8}\n" +
            $"  snes ptr  : 0x{snesDataPtr:X8}\n" +
            $"  rng seed    : pending (waiting for room transition)");
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
    /// Enumerates all committed, readable memory regions within the 32-bit address space of
    /// <paramref name="processHandle"/>, yielding (Base, Size) tuples with Size capped at
    /// <paramref name="maxPerRegion"/>. When <paramref name="privateOnly"/> is true, only
    /// MEM_PRIVATE regions are returned; otherwise MEM_PRIVATE and MEM_MAPPED are both included.
    /// </summary>
    private static IEnumerable<(uint Base, uint Size)> EnumerateRegions(
        IntPtr processHandle,
        uint maxPerRegion,
        bool privateOnly = true,
        uint minSize = 4)
    {
        IntPtr addr = IntPtr.Zero;
        nuint mbiSize = (nuint)Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();

        while (true)
        {
            if (NativeMethods.VirtualQueryEx(processHandle, addr,
                    out NativeMethods.MEMORY_BASIC_INFORMATION mbi, mbiSize) == 0)
                break;

            bool typeOk = mbi.Type == NativeMethods.MEM_PRIVATE
                       || (!privateOnly && mbi.Type == NativeMethods.MEM_MAPPED);

            if (mbi.State == NativeMethods.MEM_COMMIT
             && NativeMethods.IsReadableProtection(mbi.Protect)
             && typeOk
             && mbi.RegionSize >= minSize)
            {
                yield return ((uint)mbi.BaseAddress, (uint)Math.Min(mbi.RegionSize, maxPerRegion));
            }

            ulong next = (ulong)mbi.BaseAddress + mbi.RegionSize;
            if (next > 0xFFFFFFFF) break; // stay within 32-bit address space
            addr = (IntPtr)next;
        }
    }

    /// <summary>
    /// Scans all committed private/mapped memory regions for the character array signature:
    /// 7 consecutive uint32s at stride 0x120 holding values 0,1,2,3,4,5,6 (character IDs).
    /// Returns the game state base, or 0 on failure.
    /// </summary>
    private static uint ScanForCharacterArray(IntPtr handle)
    {
        // Character array starts at gameStateBase + 0x10C0.
        // charBase[i] + 0x00 == i, for i in 0..6, stride = 0x120 bytes.
        // Regions smaller than 7 strides can't contain the pattern.
        const uint MinRegionSize = 7 * GameOffsets.CharacterStride;

        foreach ((uint regionBase, uint regionSize) in EnumerateRegions(handle, 4 * 1024 * 1024, privateOnly: false, minSize: MinRegionSize))
        {
            uint found = ScanRegionForSignature(handle, regionBase, regionSize);
            if (found != 0)
                return found;
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

    /// <summary>
    /// Validates a candidate game state base by reading Crono's ID (0) and Marle's ID (1).
    /// </summary>
    private static bool ValidateGameStateBase(IntPtr handle, uint gameStateBase)
    {
        uint cronoBase = gameStateBase + GameOffsets.CharacterArrayBase;
        for (uint i = 0; i <=6; i++)
        {
            uint charBase = cronoBase + (i * GameOffsets.CharacterStride);
            if (!NativeMethods.ReadProcessMemory(handle, (IntPtr)charBase, out uint charId, 4, out _))
                return false;
            if ((charId & 0xFF) != i)
                return false;
        }
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
            _snesDataPtr = 0;
            _gameDataPtr = 0;
            _rngSeedAddress = 0;
            _pid = 0;
            _lcgPendingRegions = null;
            _lcgPendingSnap = null;
        }
    }

    /// <summary>
    /// Called from the refresh timer when the seed address has not yet been found.
    /// Returns true if the address was located this call.
    ///
    /// Uses a split-phase LCG scan across timer ticks (snapshots ~500 ms apart) to catch
    /// CT's rand() bursts on room transitions or heals without blocking the UI thread.
    /// </summary>
    public bool TryRefindRngSeed()
    {
        if (!IsAttached || _rngSeedAddress != 0) return false;

        // Take a snapshot now and compare on the next tick (~500 ms later).
        // Retake the snapshot every 2 s if still no match, so stale data doesn't linger.
        if (_lcgPendingRegions == null
            || (DateTime.UtcNow - _lcgPendingTime).TotalMilliseconds > 2000)
        {
            TakeLcgSnapshot(_processHandle);
            return false;
        }

        uint addr = TryMatchLcgSnapshot(_processHandle);
        if (addr == 0)
            return false;

        _rngSeedAddress = addr;
        _lcgPendingRegions = null;
        _lcgPendingSnap = null;
        RngDiagnostic = "LCG split-phase scan";
        return true;
    }

    /// <summary>
    /// Enumerates all committed private readable regions and stores a memory snapshot
    /// plus a timestamp for use by <see cref="TryMatchLcgSnapshot"/>.
    /// </summary>
    private void TakeLcgSnapshot(IntPtr processHandle)
    {
        const uint MaxPerRegion = 4 * 1024 * 1024;

        List<(uint Base, uint Size)> regions = [..EnumerateRegions(processHandle, MaxPerRegion)];

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
        return _snesDataPtr + GameOffsets.CharacterArrayBase + (uint)(charIndex * GameOffsets.CharacterStride);
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


    public byte[] ReadPartyRoster()
    {
        byte[] roster = new byte[3];
        for (int i = 0; i < 3; i++)
        {
            // Read from expanded battle data - this is what the game uses at runtime.
            // Game state (0x28E4+) is only the save/load copy and may be stale.
            roster[i] = (byte)(ReadUInt32(_gameDataPtr + GameOffsets.BattlePartySlots[i]) & 0xFF);
        }
        return roster;
    }

    public bool WritePartySlot(int slotIndex, byte characterId)
    {
        if (slotIndex < 0 || slotIndex >= 3 || characterId > 6)
            return false;

        // Primary live copy - drives active gameplay (updated by SNES script opcode 0x2D).
        bool ok = WriteUInt32(_gameDataPtr + GameOffsets.BattlePartySlots[slotIndex], characterId);
        // Load-time snapshot - kept in sync so FUN_00312c80 saves the right party.
        ok &= WriteUInt32(_gameDataPtr + GameOffsets.BattlePartySlotSnapshots[slotIndex], characterId);
        // Save-struct copy in game state - source for the serializer on save.
        ok &= WriteUInt32(_snesDataPtr + GameOffsets.PartySlots[slotIndex], characterId);
        return ok;
    }

    public InventorySlot ReadInventorySlot(int slotIndex)
    {
        uint slotBase = _snesDataPtr + GameOffsets.InventoryBase + (uint)(slotIndex * GameOffsets.InventorySlotSize);
        uint word0 = ReadUInt32(slotBase);
        uint word1 = ReadUInt32(slotBase + 4);

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
        uint slotBase = _snesDataPtr + GameOffsets.InventoryBase + (uint)(slotIndex * GameOffsets.InventorySlotSize);
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
        uint slotBase = _snesDataPtr + GameOffsets.InventoryBase + (uint)(slotIndex * GameOffsets.InventorySlotSize);
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
        uint slotBase = _snesDataPtr + GameOffsets.InventoryBase + (uint)(slotIndex * GameOffsets.InventorySlotSize);
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
            uint slotBase = _snesDataPtr + GameOffsets.InventoryBase + (uint)(i * GameOffsets.InventorySlotSize);
            uint word0 = ReadUInt32(slotBase);
            if ((word0 & 0xFF) == 0) // item_id == 0 -> empty
                return i;
        }
        return -1;
    }

    public uint ReadGold()
    {
        return ReadUInt32(_snesDataPtr + GameOffsets.Gold);
    }

    public bool WriteGold(uint value)
    {
        return WriteUInt32(_snesDataPtr + GameOffsets.Gold, value);
    }

    public uint ReadPlayTime()
    {
        return ReadUInt32(_snesDataPtr + GameOffsets.PlayTime);
    }

    // Returns 1–8 (user-facing). Stored in memory as 0–7.
    public int ReadBattleSpeed()
    {
        return (int)(ReadUInt32(_gameDataPtr + GameOffsets.BattleSpeedExpanded) & 0xFF) + 1;
    }

    public bool WriteBattleSpeed(int value)
    {
        uint stored = (uint)Math.Clamp(value - 1, 0, 7);
        uint currentExp = ReadUInt32(_gameDataPtr + GameOffsets.BattleSpeedExpanded);
        bool ok = WriteUInt32(_gameDataPtr + GameOffsets.BattleSpeedExpanded, (currentExp & 0xFFFFFF00) | stored);

        uint current = ReadUInt32(_snesDataPtr + GameOffsets.BattleSpeed);
        uint newVal = (current & 0xFFFFFF00) | stored;
        ok &= WriteUInt32(_snesDataPtr + GameOffsets.BattleSpeed, newVal);
        return ok;
    }

    /// <summary>
    /// Reads the storyline counter. The true value used by the active script engine is stored
    /// in the BattleData (expanded state), which overwrites the GameState WRAM backup.
    /// </summary>
    public byte ReadStoryline()
    {
        return (byte)(ReadUInt32(_gameDataPtr + GameOffsets.StorylineCounterExpanded) & 0xFF);
    }

    public byte ReadStorylineGameState()
    {
        return (byte)(ReadUInt32(_snesDataPtr + GameOffsets.StorylineCounter) & 0xFF);
    }

    public byte ReadStorylineBattleData()
    {
        return (byte)(ReadUInt32(_gameDataPtr + GameOffsets.StorylineCounterExpanded) & 0xFF);
    }

    /// <summary>
    /// Writes a new storyline counter value to both the SNES RAM image and the expanded
    /// GameDataPointer copy. FUN_00370720 restores the byte from the expanded copy on every
    /// room transition, so both must be updated together to survive scene changes.
    /// </summary>
    public bool WriteStoryline(byte value)
    {
        // Primary: dense SNES WRAM byte at SNESDataPointer+0x10000
        uint current = ReadUInt32(_snesDataPtr + GameOffsets.StorylineCounter);
        uint newVal = (current & 0xFFFFFF00) | value;
        bool ok = WriteUInt32(_snesDataPtr + GameOffsets.StorylineCounter, newVal);

        // Mirror 1: expanded DWORD at GameDataPointer+0x110b0 (zero-extended byte)
        ok &= WriteUInt32(_gameDataPtr + GameOffsets.StorylineCounterExpanded, value);

        return ok;
    }


    public bool CheckProcessAlive()
    {
        if (!IsAttached) return false;

        // Try reading Crono's ID as a liveness + validity check
        uint cronoBase = _snesDataPtr + GameOffsets.CharacterArrayBase;
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
