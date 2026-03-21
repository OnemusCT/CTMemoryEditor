using System.Runtime.InteropServices;

namespace CTMemoryEditor.Services;

/// <summary>
/// P/Invoke declarations for Windows process memory access.
/// </summary>
internal static partial class NativeMethods
{
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint PROCESS_VM_WRITE = 0x0020;
    public const uint PROCESS_VM_OPERATION = 0x0008;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;

    public const uint DesiredAccess =
        PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr hObject);

    // Single-uint32 read (used for individual field reads)
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        out uint lpBuffer,
        nuint nSize,
        out nuint lpNumberOfBytesRead);

    // Bulk read into a byte array (used for signature scanning)
    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadProcessMemory")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadProcessMemoryBulk(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        nuint nSize,
        out nuint lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        in uint lpBuffer,
        nuint nSize,
        out nuint lpNumberOfBytesWritten);

    // For enumerating modules to find the actual base address
    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumProcessModulesEx(
        IntPtr hProcess,
        IntPtr[] lphModule,
        uint cb,
        out uint lpcbNeeded,
        uint dwFilterFlag);

    [LibraryImport("psapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetModuleFileNameExW(
        IntPtr hProcess,
        IntPtr hModule,
        [Out] char[] lpFilename,
        uint nSize);

    public const uint LIST_MODULES_32BIT = 0x01;

    // For enumerating virtual memory regions
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nuint VirtualQueryEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        out MEMORY_BASIC_INFORMATION lpBuffer,
        nuint dwLength);

    // Memory region protection constants
    public const uint PAGE_READWRITE = 0x04;
    public const uint PAGE_EXECUTE_READWRITE = 0x40;
    public const uint PAGE_READONLY = 0x02;
    public const uint PAGE_EXECUTE_READ = 0x20;
    public const uint PAGE_WRITECOPY = 0x08;
    public const uint PAGE_EXECUTE_WRITECOPY = 0x80;

    // Memory state
    public const uint MEM_COMMIT = 0x1000;
    // Memory type
    public const uint MEM_PRIVATE = 0x20000;
    public const uint MEM_MAPPED = 0x40000;

    public static bool IsReadableProtection(uint protect)
    {
        uint p = protect & 0xFF; // mask off guard/nocache modifiers
        return p == PAGE_READWRITE
            || p == PAGE_EXECUTE_READWRITE
            || p == PAGE_READONLY
            || p == PAGE_EXECUTE_READ
            || p == PAGE_WRITECOPY
            || p == PAGE_EXECUTE_WRITECOPY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    // --- Thread enumeration ---

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    public const uint TH32CS_SNAPTHREAD = 0x00000004;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [StructLayout(LayoutKind.Sequential)]
    public struct THREADENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ThreadID;
        public uint th32OwnerProcessID;
        public int  tpBasePri;
        public int  tpDeltaPri;
        public uint dwFlags;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenThread(uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwThreadId);

    public const uint THREAD_QUERY_INFORMATION = 0x0040;
    public const uint THREAD_GET_CONTEXT       = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME { public uint dwLowDateTime; public uint dwHighDateTime; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetThreadTimes(IntPtr hThread,
        out FILETIME lpCreationTime, out FILETIME lpExitTime,
        out FILETIME lpKernelTime,  out FILETIME lpUserTime);

    // --- Thread information (ntdll) ---

    // ThreadBasicInformation = 0
    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationThread(
        IntPtr ThreadHandle,
        int    ThreadInformationClass,
        ref THREAD_BASIC_INFORMATION ThreadInformation,
        uint   ThreadInformationLength,
        out uint ReturnLength);

    // Layout must match the native struct size for the calling process bitness.
    // We run as 64-bit, so pointer fields are 8 bytes.
    [StructLayout(LayoutKind.Sequential)]
    public struct THREAD_BASIC_INFORMATION
    {
        public int    ExitStatus;
        public IntPtr TebBaseAddress;   // 64-bit TEB for WOW64 threads when called from 64-bit process
        public IntPtr UniqueProcessId;
        public IntPtr UniqueThreadId;
        public IntPtr AffinityMask;
        public int    Priority;
        public int    BasePriority;
    }
}
