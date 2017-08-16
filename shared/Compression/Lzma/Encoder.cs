using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedLzma.LZMA.Master;

namespace ManagedLzma.LZMA
{
    public sealed class AsyncEncoder : IDisposable
    {
        private sealed class InputGate : LZMA.Master.LZMA.ISeqInStream
        {
            private AsyncEncoder mContext;
            private AsyncInputProvider mInput;

            public InputGate(AsyncEncoder context, IStreamReader stream)
            {
                mContext = context;
                mInput = new AsyncInputProvider(stream);
            }

            Master.LZMA.SRes LZMA.Master.LZMA.ISeqInStream.Read(P<byte> buf, ref long size)
            {
                mContext.UpdateInputEstimate(0);

                long temp = size;
                var result = ((Master.LZMA.ISeqInStream)mInput).Read(buf, ref temp);

                if (result == Master.LZMA.SZ_OK)
                    mContext.UpdateInputEstimate(temp);

                size = temp;
                return result;
            }
        }

        private sealed class OutputGate : LZMA.Master.LZMA.ISeqOutStream
        {
            private AsyncEncoder mContext;
            private AsyncOutputProvider mOutput;

            public OutputGate(AsyncEncoder context, IStreamWriter stream)
            {
                mContext = context;
                mOutput = new AsyncOutputProvider(stream);
            }

            long Master.LZMA.ISeqOutStream.Write(P<byte> buf, long size)
            {
                mContext.UpdateOutputEstimate(0);
                var result = ((Master.LZMA.ISeqOutStream)mOutput).Write(buf, size);
                mContext.UpdateOutputEstimate(checked((int)result));
                return result;
            }
        }

        #region Variables

        // immutable
        private readonly object mSyncObject;

        // multithreaded access (under lock)
        private Task mEncoderTask;
        private Task mDisposeTask;
        private bool mRunning;

        // owned by encoder task
        private Master.LZMA.CLzmaEnc mEncoder;

        #endregion

        #region Implementation

        public AsyncEncoder(EncoderSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            mSyncObject = new object();

            mEncoder = new Master.LZMA.CLzmaEnc();
            mEncoder.LzmaEnc_SetProps(settings.GetInternalSettings());
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

        public Task DisposeAsync()
        {
            // We need to ensure that cleanup only happens once, so we need to remember that we started it.
            // We also need to make sure that the returned task completes *after* everything has been disposed.
            // Both can be covered by keeping track of the disposal via a Task object.
            //
            lock (mSyncObject)
            {
                if (mDisposeTask == null)
                {
                    if (mRunning)
                    {
                        mDisposeTask = mEncoderTask.ContinueWith(new Action<Task>(delegate {
                            lock (mSyncObject)
                                DisposeInternal();
                        }));

                        Monitor.PulseAll(mSyncObject);
                    }
                    else
                    {
                        DisposeInternal();
                        mDisposeTask = Utilities.CompletedTask;
                    }
                }

                return mDisposeTask;
            }
        }

        private void DisposeInternal()
        {
            System.Diagnostics.Debug.Assert(Monitor.IsEntered(mSyncObject));
            System.Diagnostics.Debug.Assert(!mRunning);

            // mDisposeTask may not be set yet if we complete mEncoderTask from another thread.
            // However even if mDisposeTask is not set we can be sure that the encoder is not running.

            mEncoder.LzmaEnc_Destroy(Master.LZMA.ISzAlloc.SmallAlloc, Master.LZMA.ISzAlloc.BigAlloc);
        }

        public Task EncodeAsync(IStreamReader input, IStreamWriter output, CancellationToken ct = default(CancellationToken))
        {
            lock (mSyncObject)
            {
                if (mDisposeTask != null)
                    throw new OperationCanceledException();

                // TODO: make this wait async as well
                while (mRunning)
                {
                    Monitor.Wait(mSyncObject);

                    if (mDisposeTask != null)
                        throw new OperationCanceledException();
                }

                mRunning = true;
            }

            var task = Task.Run(async delegate {
                var res = mEncoder.LzmaEnc_Encode(new OutputGate(this, output), new InputGate(this, input), null, Master.LZMA.ISzAlloc.SmallAlloc, Master.LZMA.ISzAlloc.BigAlloc);
                if (res != Master.LZMA.SZ_OK)
                    throw new InvalidOperationException();

                await output.CompleteAsync().ConfigureAwait(false);
            }, ct);

            mEncoderTask = task.ContinueWith(delegate {
                lock (mSyncObject)
                {
                    System.Diagnostics.Debug.Assert(mRunning);
                    mRunning = false;
                    Monitor.PulseAll(mSyncObject);
                }
            }, CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default);

            return task;
        }

        public event Action OnUpdateInternalLength;
        public int InternalInputLength { get; private set; }
        public int InternalOutputLength { get; private set; }

        private void UpdateInputEstimate(long fetched)
        {
            // the number of (unprocessed) input bytes in the LZMA encoder
            var pMatchFinder = mEncoder.mMatchFinderBase;
            var pMatchFinderCachedBytes = pMatchFinder.mStreamPos - pMatchFinder.mPos + fetched;

            // the number of (unflushed) output bytes in the LZMA encoder
            var pRangeCoder = mEncoder.mRC;
            var pRangeCoderCachedBytes = pRangeCoder.mBuf - pRangeCoder.mBufBase;

            InternalInputLength = checked((int)pMatchFinderCachedBytes);
            InternalOutputLength = checked((int)pRangeCoderCachedBytes);

            OnUpdateInternalLength?.Invoke();
        }

        private void UpdateOutputEstimate(int written)
        {
            // the number of (unprocessed) input bytes in the LZMA encoder
            var pMatchFinder = mEncoder.mMatchFinderBase;
            var pMatchFinderCachedBytes = pMatchFinder.mStreamPos - pMatchFinder.mPos;

            // the number of (unflushed) output bytes in the LZMA encoder
            var pRangeCoder = mEncoder.mRC;
            var pRangeCoderCachedBytes = pRangeCoder.mBuf - pRangeCoder.mBufBase - written;

            InternalInputLength = checked((int)pMatchFinderCachedBytes);
            InternalOutputLength = checked((int)pRangeCoderCachedBytes);

            OnUpdateInternalLength?.Invoke();
        }

        #endregion
    }
}
