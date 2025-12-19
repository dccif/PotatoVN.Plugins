using System;
using System.Runtime.InteropServices;

namespace PotatoVN.App.PluginBase.Services
{
    internal static class NativeMethods
    {
        public const int CnstSystemHandleInformation = 16;
        public const uint StatusInfoLengthMismatch = 0xC0000004;
        public const int ObjectNameInformation = 1;
        public const int ObjectTypeInformation = 2;

        [DllImport("ntdll.dll")]
        public static extern uint NtQuerySystemInformation(
            int systemInformationClass,
            IntPtr systemInformation,
            int systemInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(
            ProcessAccessFlags processAccess,
            bool bInheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle,
            uint dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwOptions);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("ntdll.dll")]
        public static extern int NtQueryObject(
            IntPtr objectHandle,
            int objectInformationClass,
            IntPtr objectInformation,
            int objectInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [Flags]
        public enum ProcessAccessFlags : uint
        {
            DupHandle = 0x0040
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SystemHandleInformation
        {
            public int NumberOfHandles;
            // Variable length array of SystemHandleEntry follows
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SystemHandleEntry
        {
            public int ProcessId;
            public byte ObjectTypeNumber;
            public byte Flags;
            public ushort Handle;
            public IntPtr Object;
            public uint GrantedAccess;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ObjectNameInformationStruct
        {
            public UnicodeString Name;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct ObjectTypeInformationStruct
        {
            public UnicodeString TypeName;
            public uint TotalNumberOfObjects;
            public uint TotalNumberOfHandles;
        }
    }
}
