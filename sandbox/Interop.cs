using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ManagedLzma.LZMA
{
    internal static class Interop
    {
        private const string kKernel32 = "kernel32";
        private const string kUser32 = "user32";

        public const int JobObjectExtendedLimitInformation = 9;
        public const int JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        public const int CREATE_SUSPENDED = 0x00000004;
        public const int CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
        public const int INFINITE = -1;

        [DllImport(kKernel32, SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport(kKernel32, SetLastError = true)]
        public static extern IntPtr CreateJobObject(IntPtr security, string name);

        [DllImport(kKernel32, SetLastError = true)]
        public static extern bool SetInformationJobObject(IntPtr job, int kind, [In] ref JobObjectExtendedLimitInformation info, int size);

        [DllImport(kKernel32, SetLastError = true)]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport(kKernel32, SetLastError = true)]
        public static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            int dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref StartupInfo lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport(kKernel32, SetLastError = true)]
        public static extern bool TerminateProcess(IntPtr hProcess, int uExitCode);

        [DllImport(kKernel32, SetLastError = true)]
        public static extern int ResumeThread(IntPtr handle);

        [DllImport(kUser32, SetLastError = true)]
        public static extern int WaitForInputIdle(IntPtr hProcess, int dwMilliseconds);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        public int cb;
        private string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        private short cbReserved2;
        private IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectExtendedLimitInformation
    {
        #region BasicLimitInformation

        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public int LimitFlags;
        public IntPtr MinimumWorkingSetSize;
        public IntPtr MaximumWorkingSetSize;
        public int ActiveProcessLimit;
        public IntPtr Affinity;
        public int PriorityClass;
        public int SchedulingClass;

        #endregion

        #region Reserved IoInfo

        private long ReadOperationCount;
        private long WriteOperationCount;
        private long OtherOperationCount;
        private long ReadTransferCount;
        private long WriteTransferCount;
        private long OtherTransferCount;

        #endregion

        public IntPtr ProcessMemoryLimit;
        public IntPtr JobMemoryLimit;
        public IntPtr PeakProcessMemoryUsed;
        public IntPtr PeakJobMemoryUsed;
    }
}
