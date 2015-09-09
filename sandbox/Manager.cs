using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Ipc;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters;
using System.Reflection;
using System.Threading;

namespace ManagedLzma.LZMA
{
    internal sealed class RemoteManager : MarshalByRefObject
    {
        // TODO: Is this really necessary?!
        public override object InitializeLifetimeService()
        {
            return null; // Prevent garbage collection.
        }

        public void Shutdown()
        {
            System.Windows.Forms.Application.Exit();
        }

        public object Invoke(Type type, string method, params object[] args)
        {
            const BindingFlags flags =
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static |
                BindingFlags.InvokeMethod;

            return type.InvokeMember(method, flags, null, null, args);
        }

        public RemoteResult BeginInvoke(Type type, string method, params object[] args)
        {
            Func<object> action = () => Invoke(type, method, args);
            return new RemoteResult(action.BeginInvoke(null, null), action);
        }

        public object EndInvoke(RemoteResult result)
        {
            return result.mHandler.EndInvoke(result.mResult);
        }
    }

    internal sealed class RemoteResult : MarshalByRefObject, IAsyncResult
    {
        internal IAsyncResult mResult;
        internal Func<object> mHandler;

        public RemoteResult(IAsyncResult result, Func<object> handler)
        {
            mResult = result;
            mHandler = handler;
        }

        #region IAsyncResult Methods

        public object AsyncState
        {
            get { return mResult.AsyncState; }
        }

        public WaitHandle AsyncWaitHandle
        {
            get { return mResult.AsyncWaitHandle; }
        }

        public bool CompletedSynchronously
        {
            get { return mResult.CompletedSynchronously; }
        }

        public bool IsCompleted
        {
            get { return mResult.IsCompleted; }
        }

        #endregion
    }

    internal sealed class LocalManager : IDisposable
    {
        private static HashSet<string> mChannelIdPool = new HashSet<string>();

        private static object mSync = new object();
        private static string mChannelId;
        private static RemoteManager mLocalManager;

        public static void RegisterRemote(string channelId)
        {
            if (String.IsNullOrEmpty(channelId))
                throw new ArgumentException("Invalid channel id.", "channelId");

            if (!mChannelIdPool.Add(channelId))
                throw new InvalidOperationException("Duplicate channel id.");

            bool newSig1, newSig2;
            using (var signal1 = new Semaphore(0, 1, channelId + "/signal1", out newSig1))
            using (var signal2 = new Semaphore(0, 1, channelId + "/signal2", out newSig2))
            {
                signal1.WaitOne();
                signal2.Release();
            }
        }

        public static void ShutdownRemote(string channelId)
        {
            if (String.IsNullOrEmpty(channelId))
                throw new ArgumentException("Invalid channel id.", "channelId");

            if (!mChannelIdPool.Remove(channelId))
                throw new InvalidOperationException("Channel id not registered.");

            var type = typeof(RemoteManager);
            var remote = (RemoteManager)Activator.GetObject(type,
                "ipc://" + channelId + "/" + type.GUID.ToString("N"));
            remote.Shutdown();
        }

        internal static void EnsureServer(string channelId)
        {
            lock (mSync)
            {
                if (mChannelId == null)
                {
                    mChannelId = channelId ?? Guid.NewGuid().ToString("N");

                    // By default the server formatter is configured for low security scenarios like the web,
                    // causing it to reject marshalling delegates or client side object references. Since our
                    // IPC channel is not accessible from other machines we are safe to turn the filter off.
                    // This is required for the server to be able to call back into the client.
                    var provider = new BinaryServerFormatterSinkProvider { TypeFilterLevel = TypeFilterLevel.Full };
                    var settings = new Hashtable();
                    settings["portName"] = mChannelId;
                    ChannelServices.RegisterChannel(new IpcChannel(settings, null, provider), true);

                    var manager = new RemoteManager();
                    RemotingServices.Marshal(manager, typeof(RemoteManager).GUID.ToString("N"));

                    // NOTE: Assigning the manager should be the very last thing done in here.
                    mLocalManager = manager;

                    if (channelId != null)
                    {
                        bool newSig1, newSig2;
                        using (var signal1 = new Semaphore(0, 1, channelId + "/signal1", out newSig1))
                        using (var signal2 = new Semaphore(0, 1, channelId + "/signal2", out newSig2))
                        {
                            signal1.Release();
                            signal2.WaitOne();
                        }
                    }
                }
                else if (channelId != null)
                {
                    if (channelId != mChannelId)
                        throw new NotSupportedException("Multiple server channels are not supported.");
                }
            }
        }

        private ChildProcess mRemoteProcess;
        private RemoteManager mRemoteManager;
        private string mRemoteChannelId;

        public LocalManager()
        {
            EnsureServer(null);
            Type type = typeof(RemoteManager);
            if (mChannelIdPool.Count != 0)
            {
                mRemoteChannelId = mChannelIdPool.First();
                mChannelIdPool.Remove(mRemoteChannelId);
                mRemoteManager = (RemoteManager)Activator.GetObject(type,
                    "ipc://" + mRemoteChannelId + "/" + type.GUID.ToString("N"));
            }
            else
            {
                string path = type.Assembly.Location;
                string remoteChannelId = Guid.NewGuid().ToString("N");
                mRemoteProcess = new ChildProcess(path, "-ipc " + remoteChannelId);
                bool newSig1, newSig2;
                using (var signal1 = new Semaphore(0, 1, remoteChannelId + "/signal1", out newSig1))
                using (var signal2 = new Semaphore(0, 1, remoteChannelId + "/signal2", out newSig2))
                {
                    signal1.WaitOne();
                    signal2.Release();
                }
                mRemoteManager = (RemoteManager)Activator.GetObject(type,
                    "ipc://" + remoteChannelId + "/" + type.GUID.ToString("N"));
            }
        }

        public void Dispose()
        {
            if (mRemoteProcess != null)
                mRemoteProcess.Dispose();

            if (mRemoteChannelId != null)
            {
                mChannelIdPool.Add(mRemoteChannelId);
                mRemoteChannelId = null;
            }
        }

        public object Invoke(Type type, string method, params object[] args)
        {
            return mRemoteManager.Invoke(type, method, args);
        }

        public IAsyncResult BeginInvoke(Type type, string method, params object[] args)
        {
            return mRemoteManager.BeginInvoke(type, method, args);
        }

        public object EndInvoke(IAsyncResult result)
        {
            return mRemoteManager.EndInvoke((RemoteResult)result);
        }
    }

    internal sealed class InprocManager : IDisposable
    {
        private object mSync;
        private Thread mThread;
        private Type mType;
        private string mMethod;
        private object[] mArgs;
        private object mResult;
        private Exception mError;
        private bool mShutdown;

        public InprocManager()
        {
            mSync = new object();
            mThread = new Thread(ThreadProc) { Name = "Inproc Manager" };
            mThread.Start();
        }

        public void Dispose()
        {
            lock (mSync)
            {
                mShutdown = true;
                Monitor.Pulse(mSync);
            }

            mThread.Join();
        }

        private void ThreadProc()
        {
            for (;;)
            {
                Type type;
                string method;
                object[] args;
                lock (mSync)
                {
                    while (mType == null && !mShutdown)
                        Monitor.Wait(mSync);

                    if (mShutdown)
                        break;

                    type = mType;
                    method = mMethod;
                    args = mArgs;
                }

                object result = null;
                Exception error = null;
                try
                {
                    try
                    {
                        const BindingFlags flags =
                            BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.Static |
                            BindingFlags.InvokeMethod;

                        result = type.InvokeMember(method, flags, null, null, args);
                    }
                    catch (Exception ex)
                    {
                        result = null;
                        error = ex;
                    }
                }
                finally
                {
                    lock (mSync)
                    {
                        mType = null;
                        mMethod = null;
                        mArgs = null;
                        mResult = result;
                        mError = error;

                        Monitor.Pulse(mSync);
                    }
                }
            }
        }

        public object Invoke(Type type, string method, params object[] args)
        {
            if (type == null)
                throw new ArgumentNullException("type");

            object result;
            Exception error;
            lock (mSync)
            {
                while (mType != null && !mShutdown)
                    Monitor.Wait(mSync);

                if (mShutdown)
                    throw new InvalidOperationException("Manager has been shut down.");

                mType = type;
                mMethod = method;
                mArgs = args;
                mResult = null;
                mError = null;

                Monitor.Pulse(mSync);

                while (mType != null && !mShutdown)
                    Monitor.Wait(mSync);

                if (mShutdown)
                    throw new InvalidOperationException("Manager has been shut down.");

                result = mResult;
                error = mError;

                mType = null;
                mMethod = null;
                mArgs = null;
                mResult = null;
                mError = null;

                Monitor.Pulse(mSync);
            }

            if (error != null)
                throw new TargetInvocationException(error);

            return result;
        }
    }
}
