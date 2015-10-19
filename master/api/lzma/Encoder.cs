using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedLzma.LZMA
{
    public sealed class AsyncEncoder : IDisposable
    {
        private sealed class InputFrame
        {
            public byte[] mBuffer;
            public int mOrigin;
            public int mOffset;
            public int mEnding;
            public TaskCompletionSource<object> mCompletion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class OutputFrame
        {
            public byte[] mBuffer;
            public int mOrigin;
            public int mOffset;
            public int mEnding;
            public TaskCompletionSource<int> mCompletion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            public ReadMode mMode;
        }

        private sealed class InputProvider : Master.LZMA.ISeqInStream
        {
            private AsyncEncoder mEncoder;

            internal InputProvider(AsyncEncoder encoder)
            {
                mEncoder = encoder;
            }

            Master.LZMA.SRes Master.LZMA.ISeqInStream.Read(P<byte> buf, ref long size)
            {
                System.Diagnostics.Debug.Assert(size > 0);

                InputFrame frame;

                lock (mEncoder.mSyncObject)
                {
                    System.Diagnostics.Debug.Assert(mEncoder.mRunning);

                    if (mEncoder.mDisposeTask != null)
                    {
                        size = 0;
                        return Master.LZMA.SZ_ERROR_FAIL;
                    }

                    while (mEncoder.mInputQueue.Count == 0)
                    {
                        if (mEncoder.mFlushed)
                        {
                            size = 0;
                            return Master.LZMA.SZ_OK;
                        }

                        Monitor.Wait(mEncoder.mSyncObject);

                        if (mEncoder.mDisposeTask != null)
                        {
                            size = 0;
                            return Master.LZMA.SZ_ERROR_FAIL;
                        }
                    }

                    frame = mEncoder.mInputQueue.Peek();
                }

                System.Diagnostics.Debug.Assert(frame.mOffset < frame.mEnding);

                int copySize = Math.Min(frame.mEnding - frame.mOffset, size > Int32.MaxValue ? Int32.MaxValue : (int)size);
                Buffer.BlockCopy(frame.mBuffer, frame.mOffset, buf.mBuffer, buf.mOffset, copySize);
                frame.mOffset += copySize;

                if (frame.mOffset == frame.mEnding)
                {
                    frame.mCompletion.SetResult(null);

                    lock (mEncoder.mSyncObject)
                    {
                        System.Diagnostics.Debug.Assert(mEncoder.mRunning);
                        var other = mEncoder.mInputQueue.Dequeue();
                        System.Diagnostics.Debug.Assert(other == frame);

                        if (mEncoder.mDisposeTask != null)
                        {
                            size = 0;
                            return Master.LZMA.SZ_ERROR_FAIL;
                        }
                    }
                }

                size = copySize;
                return Master.LZMA.SZ_OK;
            }
        }

        private sealed class OutputProvider : Master.LZMA.ISeqOutStream
        {
            private AsyncEncoder mEncoder;

            internal OutputProvider(AsyncEncoder encoder)
            {
                mEncoder = encoder;
            }

            long Master.LZMA.ISeqOutStream.Write(P<byte> buf, long size)
            {
                System.Diagnostics.Debug.Assert(size > 0);

                long processed = 0;

                while (size > 0)
                {
                    OutputFrame frame;
                    lock (mEncoder.mSyncObject)
                    {
                        System.Diagnostics.Debug.Assert(mEncoder.mRunning);

                        if (mEncoder.mDisposeTask != null)
                            return 0;

                        while (mEncoder.mOutputQueue.Count == 0)
                        {
                            Monitor.Wait(mEncoder.mSyncObject);

                            if (mEncoder.mDisposeTask != null)
                                return 0;
                        }

                        frame = mEncoder.mOutputQueue.Peek();
                    }

                    var capacity = frame.mEnding - frame.mOffset;
                    var copySize = Math.Min(capacity, size > Int32.MaxValue ? Int32.MaxValue : (int)size);
                    Buffer.BlockCopy(buf.mBuffer, buf.mOffset, frame.mBuffer, frame.mOffset, copySize);
                    frame.mOffset += copySize;
                    buf += copySize;
                    processed += copySize;
                    size -= copySize;

                    if (copySize == capacity || frame.mMode == ReadMode.ReturnEarly)
                    {
                        frame.mCompletion.SetResult(frame.mOffset - frame.mOrigin);

                        lock (mEncoder.mSyncObject)
                        {
                            System.Diagnostics.Debug.Assert(mEncoder.mRunning);
                            var other = mEncoder.mOutputQueue.Dequeue();
                            System.Diagnostics.Debug.Assert(other == frame);
                        }
                    }
                }

                return processed;
            }
        }

        #region Variables

        // immutable
        private readonly object mSyncObject;

        // multithreaded access (under lock)
        private Task mEncoderTask;
        private Task mDisposeTask;
        private Queue<InputFrame> mInputQueue;
        private Queue<OutputFrame> mOutputQueue;
        private bool mRunning;
        private bool mFlushed;

        // owned by decoder task
        private Master.LZMA.CLzmaEnc mEncoder;
        private InputProvider mInput;
        private OutputProvider mOutput;

        #endregion

        #region Public Methods

        public AsyncEncoder(EncoderSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            mSyncObject = new object();
            mInput = new InputProvider(this);
            mOutput = new OutputProvider(this);

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
                        mDisposeTask = Task.CompletedTask;
                    }
                }

                return mDisposeTask;
            }
        }

        public Task CompleteInputAsync()
        {
            throw new NotImplementedException();
        }

        public Task WriteInputAsync(byte[] buffer, int offset, int length)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (length < 0 || length > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(length));

            var frame = new InputFrame();
            frame.mBuffer = buffer;
            frame.mOrigin = offset;
            frame.mOffset = offset;
            frame.mEnding = offset + length;
            PushInputFrame(frame);
            return frame.mCompletion.Task;
        }

        public Task<int> ReadOutputAsync(byte[] buffer, int offset, int length, ReadMode mode)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (length < 0 || length > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(length));

            if (mode != ReadMode.FillBuffer && mode != ReadMode.ReturnEarly)
                throw new ArgumentOutOfRangeException(nameof(mode));

            var frame = new OutputFrame();
            frame.mBuffer = buffer;
            frame.mOrigin = offset;
            frame.mOffset = offset;
            frame.mEnding = offset + length;
            frame.mMode = mode;
            PushOutputFrame(frame);
            return frame.mCompletion.Task;
        }

        #endregion

        #region Private Implementation

        private void DisposeInternal()
        {
            System.Diagnostics.Debug.Assert(Monitor.IsEntered(mSyncObject));
            System.Diagnostics.Debug.Assert(!mRunning);

            // mDisposeTask may not be set yet if we complete mDecoderTask from another thread.
            // However even if mDisposeTask is not set we can be sure that the decoder is not running.

            mEncoder.LzmaEnc_Destroy(null, null);

            foreach (var frame in mInputQueue)
                frame.mCompletion.SetCanceled();

            mInputQueue.Clear();

            foreach (var frame in mOutputQueue)
                frame.mCompletion.SetCanceled();

            mOutputQueue.Clear();
        }

        private void PushInputFrame(InputFrame frame)
        {
            lock (mSyncObject)
            {
                if (mDisposeTask != null)
                    throw new ObjectDisposedException(null);

                if (mFlushed)
                    throw new InvalidOperationException();

                mInputQueue.Enqueue(frame);
                TryStartEncoding();
            }
        }

        private void PushOutputFrame(OutputFrame frame)
        {
            lock (mSyncObject)
            {
                if (mDisposeTask != null)
                    throw new ObjectDisposedException(null);

                mOutputQueue.Enqueue(frame);
                TryStartEncoding();
            }
        }

        private void TryStartEncoding()
        {
            System.Diagnostics.Debug.Assert(Monitor.IsEntered(mSyncObject));

            if (!mRunning)
            {
                mRunning = true;
                System.Diagnostics.Debug.Assert(mEncoderTask == null);
                mEncoderTask = Task.Run(new Action(Encode));
            }
            else
            {
                // If the encoder is already running then it may be waiting for data.
                Monitor.PulseAll(mSyncObject);
            }
        }

        private void Encode()
        {
            try
            {
                var res = mEncoder.LzmaEnc_Encode(mOutput, mInput, null, null, null);
                if (res != Master.LZMA.SZ_OK)
                    throw new InvalidOperationException();
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (mSyncObject)
                    mRunning = false;
            }
        }

        #endregion
    }
}
