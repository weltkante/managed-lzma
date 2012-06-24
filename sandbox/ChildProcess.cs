using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace ManagedLzma.LZMA
{
    internal sealed class ChildProcess : IDisposable
    {
        private IntPtr mJobHandle;

        public ChildProcess(string path, string args)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Could not find target executable.", path);

            string command = String.Concat("\"", path, "\" ", args);

            try
            {
                // This is inside the try-block because between returning from CreateJobObject
                // and checking the return value there could be a CLR runtime exception.
                // It wouldn't hurt moving it outside, nobody should handle CLR runtime exceptions,
                // so a leaked handle before crashing the process should make no difference;
                // but since it doesn't complicate control flow considerably I just put it in here.
                mJobHandle = Interop.CreateJobObject(IntPtr.Zero, null);
                if (mJobHandle == IntPtr.Zero)
                    throw new Win32Exception();

                // Configure the job object to terminate associated processes when the last job
                // handle is closed. If everything goes well we never close the job handle.
                // Windows will close the handle automatically when our process exits, meaning
                // the process we start here will terminate together with our process.
                var limits = new JobObjectExtendedLimitInformation();
                limits.LimitFlags = Interop.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                if (!Interop.SetInformationJobObject(mJobHandle, Interop.JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf(limits)))
                    throw new Win32Exception();

                // Need to initialize the struct because the C# compiler is too stupid
                // to realize that didStart must be false while proc isn't initialized.
                ProcessInformation proc = new ProcessInformation();
                bool didStart = false;
                try
                {
                    // This code is carefully ordered to be as safe as possible in regard to
                    // CLR runtime exceptions (like thread abort), otherwise it could happen
                    // that the just-started process is left behind while we crash. This can
                    // still happen if we are terminated by unmanaged means (win32 API, user
                    // tools like the windows task manager or a debugger) but the time window
                    // is very small and we can't prevent it, so we have to live with it.

                    try
                    {
                        // Create the process. We specify the break-away flag because running
                        // in the debugger will cause our own process to run with a job object.
                        StartupInfo info = new StartupInfo();
                        info.cb = Marshal.SizeOf(typeof(StartupInfo));
                        didStart = Interop.CreateProcess(path, command, IntPtr.Zero, IntPtr.Zero, false, Interop.CREATE_SUSPENDED | Interop.CREATE_BREAKAWAY_FROM_JOB, IntPtr.Zero, null, ref info, out proc);

                        if (!didStart)
                            throw new Win32Exception();

                        if (!Interop.AssignProcessToJobObject(mJobHandle, proc.hProcess))
                            throw new Win32Exception();
                    }
                    catch
                    {
                        // We couldn't connect the process to the job, so we need to terminate it manually.
                        // At this point the process is still suspended and TerminateProcess does no harm.
                        if (didStart)
                            Interop.TerminateProcess(proc.hProcess, 0);

                        throw;
                    }

                    // At this point the process is configured to terminate when the job handle is closed,
                    // so we no longer need to care about terminating the process in case of an exception.

                    // Since we created the process in suspended state we need to start it here.
                    if (Interop.ResumeThread(proc.hThread) < 0)
                        throw new Win32Exception();

                    // We wait until the process is ready to ensure the IPC channel is up and running.
                    // If this wait fails then the process terminated, probably due to an exception.
                    if (Interop.WaitForInputIdle(proc.hProcess, Interop.INFINITE) != 0)
                        throw new Exception("Child process failed during startup.");
                }
                finally
                {
                    if (didStart)
                    {
                        Interop.CloseHandle(proc.hThread);
                        Interop.CloseHandle(proc.hProcess);
                    }
                }
            }
            catch
            {
                if (mJobHandle != IntPtr.Zero)
                {
                    // Closing the job handle will also terminate the process if
                    // we were past calling AssignProcessToJobObject.
                    Interop.CloseHandle(mJobHandle);
                    mJobHandle = IntPtr.Zero;
                }

                throw;
            }
        }

        ~ChildProcess()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            IntPtr handle = Interlocked.Exchange(ref mJobHandle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
                Interop.CloseHandle(handle);
        }
    }
}
