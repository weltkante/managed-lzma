using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedLzma.LZMA
{
    public sealed class AsyncEncoder : IDisposable
    {
        private sealed class AsyncInputProvider : Master.LZMA.ISeqInStream
        {
            private IStreamReader mStream;
            private bool mCompleted;

            internal AsyncInputProvider(IStreamReader stream)
            {
                mStream = stream;
            }

            Master.LZMA.SRes Master.LZMA.ISeqInStream.Read(P<byte> buf, ref long size)
            {
                System.Diagnostics.Debug.Assert(size > 0);

                if (mCompleted)
                {
                    size = 0;
                    return Master.LZMA.SZ_OK;
                }

                var capacity = size < Int32.MaxValue ? (int)size : Int32.MaxValue;
                var fetched = mStream.ReadAsync(buf.mBuffer, buf.mOffset, capacity, StreamMode.Partial).GetAwaiter().GetResult();

                if (fetched < 0 || fetched > capacity)
                    throw new InvalidOperationException("IInputStream.ReadAsync returned an invalid result.");

                if (fetched == 0)
                    mCompleted = true;

                size = fetched;
                return Master.LZMA.SZ_OK;
            }
        }

        private sealed class AsyncOutputProvider : Master.LZMA.ISeqOutStream
        {
            private IStreamWriter mStream;

            internal AsyncOutputProvider(IStreamWriter stream)
            {
                mStream = stream;
            }

            long Master.LZMA.ISeqOutStream.Write(P<byte> buf, long size)
            {
                System.Diagnostics.Debug.Assert(size > 0);

                var buffer = buf.mBuffer;
                var offset = buf.mOffset;
                var result = size;

                while (size > Int32.MaxValue)
                {
                    var written = mStream.WriteAsync(buffer, offset, Int32.MaxValue, StreamMode.Partial).GetAwaiter().GetResult();
                    if (written <= 0)
                        throw new InvalidOperationException("IOutputStream.WriteAsync returned an invalid result.");

                    offset += written;
                    size -= written;
                }

                if (size > 0)
                {
                    var written = mStream.WriteAsync(buffer, offset, (int)size, StreamMode.Complete).GetAwaiter().GetResult();
                    if (written != size)
                        throw new InvalidOperationException("IOutputStream.WriteAsync returned an invalid result.");
                }

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

        // owned by decoder task
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
                        mDisposeTask = Task.CompletedTask;
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

            mEncoder.LzmaEnc_Destroy(null, null);
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

            var task = Task.Run(delegate {
                var res = mEncoder.LzmaEnc_Encode(new AsyncOutputProvider(output), new AsyncInputProvider(input), null, null, null);
                if (res != Master.LZMA.SZ_OK)
                    throw new InvalidOperationException();
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

        #endregion
    }

    public sealed class AsyncInputQueue : IStreamReader, IStreamWriter
    {
        private sealed class Frame
        {
            public byte[] mBuffer;
            public int mOrigin;
            public int mOffset;
            public int mEnding;
            public TaskCompletionSource<int> mCompletion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private object mSyncObject;
        private Queue<Frame> mQueue = new Queue<Frame>();
        private Task mDisposeTask;
        private bool mRunning;
        private bool mCompleted;

        public Task<int> ReadAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            Utilities.CheckStreamArguments(buffer, offset, length, mode);

            int total = 0;

            while (length > 0)
            {
                Frame frame;
                lock (mSyncObject)
                {
                    System.Diagnostics.Debug.Assert(mRunning);

                    if (mDisposeTask != null)
                        throw new OperationCanceledException();

                    while (mQueue.Count == 0)
                    {
                        if (mCompleted)
                            return Task.FromResult(total);

                        Monitor.Wait(mSyncObject);

                        if (mDisposeTask != null)
                            throw new OperationCanceledException();
                    }

                    frame = mQueue.Peek();
                }

                System.Diagnostics.Debug.Assert(frame.mOffset < frame.mEnding);
                var processed = Math.Min(frame.mEnding - frame.mOffset, length);
                System.Diagnostics.Debug.Assert(processed > 0);
                Buffer.BlockCopy(frame.mBuffer, frame.mOffset, buffer, offset, processed);
                frame.mOffset += processed;
                total += processed;
                offset += processed;
                length -= processed;

                if (frame.mOffset == frame.mEnding)
                {
                    frame.mCompletion.SetResult(frame.mOffset - frame.mOrigin);

                    lock (mSyncObject)
                    {
                        System.Diagnostics.Debug.Assert(mRunning);
                        var other = mQueue.Dequeue();
                        System.Diagnostics.Debug.Assert(other == frame);

                        if (mDisposeTask != null)
                            throw new OperationCanceledException();
                    }
                }

                if (mode == StreamMode.Partial)
                    break;
            }

            return Task.FromResult(total);
        }

        public Task<int> WriteAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (length < 0 || length > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(length));

            var frame = new Frame();
            frame.mBuffer = buffer;
            frame.mOrigin = offset;
            frame.mOffset = offset;
            frame.mEnding = offset + length;

            lock (mSyncObject)
            {
                if (mDisposeTask != null)
                    throw new ObjectDisposedException(null);

                if (mCompleted)
                    throw new InvalidOperationException();

                mQueue.Enqueue(frame);
                Monitor.PulseAll(mSyncObject);
            }

            return frame.mCompletion.Task;
        }

        public Task CompleteAsync()
        {
            return Task.Run(() => {
                lock (mSyncObject)
                {
                    mCompleted = true;
                    Monitor.PulseAll(mSyncObject);

                    while (mQueue.Count > 0)
                        Monitor.Wait(mSyncObject);
                }
            });
        }
    }

    public sealed class AsyncOutputQueue : IStreamReader, IStreamWriter
    {
        private sealed class Frame
        {
            public byte[] mBuffer;
            public int mOrigin;
            public int mOffset;
            public int mEnding;
            public TaskCompletionSource<int> mCompletion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            public StreamMode mMode;
        }

        private object mSyncObject;
        private Queue<Frame> mQueue = new Queue<Frame>();
        private Task mDisposeTask;
        private bool mRunning;

        public Task<int> ReadAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            Utilities.CheckStreamArguments(buffer, offset, length, mode);

            var frame = new Frame();
            frame.mBuffer = buffer;
            frame.mOrigin = offset;
            frame.mOffset = offset;
            frame.mEnding = offset + length;
            frame.mMode = mode;

            lock (mSyncObject)
            {
                if (mDisposeTask != null)
                    throw new ObjectDisposedException(null);

                mQueue.Enqueue(frame);
                Monitor.Pulse(mSyncObject);
            }

            return frame.mCompletion.Task;
        }

        public Task<int> WriteAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            Utilities.CheckStreamArguments(buffer, offset, length, mode);

            int processed = 0;

            while (length > 0)
            {
                Frame frame;
                lock (mSyncObject)
                {
                    System.Diagnostics.Debug.Assert(mRunning);

                    if (mDisposeTask != null)
                        throw new OperationCanceledException();

                    while (mQueue.Count == 0)
                    {
                        Monitor.Wait(mSyncObject);

                        if (mDisposeTask != null)
                            throw new OperationCanceledException();
                    }

                    frame = mQueue.Peek();
                }

                var capacity = frame.mEnding - frame.mOffset;
                var copySize = Math.Min(capacity, length > Int32.MaxValue ? Int32.MaxValue : (int)length);
                Buffer.BlockCopy(buffer, offset, frame.mBuffer, frame.mOffset, copySize);
                frame.mOffset += copySize;
                offset += copySize;
                processed += copySize;
                length -= copySize;

                if (copySize == capacity || frame.mMode == StreamMode.Partial)
                {
                    frame.mCompletion.SetResult(frame.mOffset - frame.mOrigin);

                    lock (mSyncObject)
                    {
                        System.Diagnostics.Debug.Assert(mRunning);
                        var other = mQueue.Dequeue();
                        System.Diagnostics.Debug.Assert(other == frame);
                    }
                }
            }

            return Task.FromResult(processed);
        }

        public Task CompleteAsync()
        {
            throw new NotImplementedException();
        }
    }
}
