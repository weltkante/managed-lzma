using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

namespace ManagedLzma.LZMA
{
    public static class Server
    {
        private static Control mControl;

        [STAThread]
        public static void Main(params string[] args)
        {
            if(Array.IndexOf(args, "-debug") >= 0)
            {
                if(!System.Diagnostics.Debugger.IsAttached)
                    System.Diagnostics.Debugger.Launch();
                else
                    System.Diagnostics.Debugger.Break();
            }

            for(int i = 0; i < args.Length; i++)
                if(args[i] == "-remote" && i + 1 < args.Length)
                    LocalManager.RegisterRemote(args[++i]);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Exception error = null;
            EventHandler handler = null;
            Application.Idle += handler = delegate {
                // Must be reentrant because we are triggered by the application-idle event.
                // (The next event may be "on air" before we are executed and unsubscribe ourselves.)
                if(handler == null)
                    return;

                Application.Idle -= handler;
                handler = null;

                try
                {
                    mControl = new Control();
                    mControl.CreateControl();

                    Initialize(args);
                }
                catch(Exception ex)
                {
                    error = ex;
                }
            };

            EventHandler exitHandler = null;
            Application.ApplicationExit += exitHandler = delegate {
                if(exitHandler == null)
                    return;

                Application.ApplicationExit -= exitHandler;
                exitHandler = null;

                for(int i = 0; i < args.Length; i++)
                {
                    if(args[i] == "-remote" && i + 1 < args.Length)
                    {
                        try { LocalManager.ShutdownRemote(args[++i]); }
                        catch { }
                    }
                }
            };

            Application.Run();

            if(mControl != null)
            {
                try
                {
                    mControl.Dispose();
                }
                catch
                {
                    // Ignore error if we already have something more important.
                    if(error == null)
                        throw;
                }

                mControl = null;
            }

            if(error != null)
                throw new TargetInvocationException(error);
        }

        private static bool IsChannelIdChar(char ch)
        {
            // Channel Id is a GUID formatted with ToString("N") or ToString("D").
            // If this needs to be changed remember that it must be a valid file name!
            // (In particular we don't want '{' or '}' from some GUID formats in there.)
            return ch == '-'
                || '0' <= ch && ch <= '9'
                || 'a' <= ch && ch <= 'z'
                || 'A' <= ch && ch <= 'Z';
        }

        private static bool IsChannelId(string arg)
        {
            if(string.IsNullOrEmpty(arg))
                return false;

            for(int i = 0; i < arg.Length; i++)
                if(!IsChannelIdChar(arg[i]))
                    return false;

            return true;
        }

        private static void Initialize(string[] args)
        {
            int index = Array.IndexOf(args, "-ipc") + 1; // zero if not found
            if(0 < index && index < args.Length && IsChannelId(args[index]))
            {
                LocalManager.EnsureServer(args[index]);
            }
            else
            {
                try { Program.RunSandbox(); }
                finally { Application.Exit(); }
            }
        }

        #region Invoke

        public static bool InvokeRequired
        {
            get
            {
                if(mControl == null)
                    throw new InvalidOperationException();

                return mControl.InvokeRequired;
            }
        }

        public static void Invoke(Action action)
        {
            if(mControl == null)
                throw new InvalidOperationException();

            mControl.Invoke(action);
        }

        public static void Invoke<T1>(Action<T1> action, T1 arg1)
        {
            if(mControl == null)
                throw new InvalidOperationException();

            mControl.Invoke(action, arg1);
        }

        public static void Invoke<T1, T2>(Action<T1, T2> action, T1 arg1, T2 arg2)
        {
            if(mControl == null)
                throw new InvalidOperationException();

            mControl.Invoke(action, arg1, arg2);
        }

        public static void Invoke<T1, T2, T3>(Action<T1, T2, T3> action, T1 arg1, T2 arg2, T3 arg3)
        {
            if(mControl == null)
                throw new InvalidOperationException();

            mControl.Invoke(action, arg1, arg2, arg3);
        }

        public static IAsyncResult BeginInvoke(Action action)
        {
            if(mControl == null)
                throw new InvalidOperationException();

            return mControl.BeginInvoke(action);
        }

        public static object EndInvoke(IAsyncResult result)
        {
            if(mControl == null)
                throw new InvalidOperationException();

            return mControl.EndInvoke(result);
        }

        #endregion
    }
}
