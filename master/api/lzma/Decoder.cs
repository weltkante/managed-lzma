using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedLzma.LZMA
{
    public sealed class Decoder : IDisposable
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

        // immutable
        private readonly object mSyncObject;
        private readonly DecoderSettings mSettings;
        private readonly Action mDecodeAction;

        // multithreaded access (under lock)
        private Task mDecodeTask;
        private Queue<InputFrame> mInputQueue;
        private Queue<OutputFrame> mOutputQueue;
        private int mTotalOutputCapacity;
        private bool mStarted;
        private bool mRunning;
        private bool mFlushed;

        // owned by decoder task
        private Master.LZMA.CLzmaDec mDecoder;
        private Master.LZMA.ELzmaStatus mStatus;
        private int mDecoderPosition;

        public Decoder(DecoderSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            mSettings = settings;
            mSyncObject = new object();
            mDecodeAction = new Action(Decode);
            mDecoder = new Master.LZMA.CLzmaDec();
            mDecoder.LzmaDec_Construct();
            if (mDecoder.LzmaDec_Allocate(settings.ToArray(), Master.LZMA.LZMA_PROPS_SIZE, Master.LZMA.ISzAlloc.SmallAlloc) != Master.LZMA.SZ_OK)
                throw new InvalidOperationException();
        }

        public void Dispose()
        {
            for (;;)
            {
                lock (mSyncObject)
                {
                    if (!mRunning)
                        break;


                    throw new NotImplementedException();
                }
            }

            mDecoder.LzmaDec_Free(Master.LZMA.ISzAlloc.SmallAlloc);
        }

        public Task DisposeAsync()
        {
            throw new NotImplementedException();
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

        private void PushInputFrame(InputFrame frame)
        {
            lock (mSyncObject)
            {
                mInputQueue.Enqueue(frame);
                TryStartDecoding();
            }
        }

        private void PushOutputFrame(OutputFrame frame)
        {
            lock (mSyncObject)
            {
                mTotalOutputCapacity += frame.mEnding - frame.mOffset;
                mOutputQueue.Enqueue(frame);
                TryStartDecoding();
            }
        }

        private void TryStartDecoding()
        {
            System.Diagnostics.Debug.Assert(Monitor.IsEntered(mSyncObject));

            if (!mRunning)
            {
                mRunning = true;

                if (!mStarted)
                {
                    mStarted = true;
                    mDecoder.LzmaDec_Init();
                }

                System.Diagnostics.Debug.Assert(mDecodeTask == null);
                mDecodeTask = Task.Run(mDecodeAction);
            }
        }

        private void Decode()
        {
            do { WriteOutput(); }
            while (DecodeInput());
        }

        private void WriteOutput()
        {
            while (mDecoderPosition != mDecoder.mDicPos)
            {
                OutputFrame frame;
                lock (mSyncObject)
                {
                    System.Diagnostics.Debug.Assert(mStarted && mRunning);

                    if (mOutputQueue.Count == 0)
                        break;

                    frame = mOutputQueue.Peek();
                }

                var capacity = frame.mEnding - frame.mOffset;
                var copySize = Math.Min(capacity, checked((int)mDecoder.mDicPos) - mDecoderPosition);
                Buffer.BlockCopy(mDecoder.mDic.mBuffer, mDecoder.mDic.mOffset + mDecoderPosition, frame.mBuffer, frame.mOffset, copySize);

                frame.mOffset += copySize;
                mDecoderPosition += copySize;

                if (copySize == capacity || frame.mMode == ReadMode.ReturnEarly)
                {
                    frame.mCompletion.SetResult(frame.mOffset - frame.mOrigin);

                    lock (mSyncObject)
                    {
                        System.Diagnostics.Debug.Assert(mStarted && mRunning);
                        var other = mOutputQueue.Dequeue();
                        System.Diagnostics.Debug.Assert(other == frame);
                    }
                }
            }
        }

        private bool DecodeInput()
        {
            var mode = Master.LZMA.ELzmaFinishMode.LZMA_FINISH_ANY;
            var capacity = default(int);
            var frame = default(InputFrame);

            lock (mSyncObject)
            {
                System.Diagnostics.Debug.Assert(mStarted && mRunning);

                if (mInputQueue.Count == 0)
                {
                    mRunning = false;
                    return false;
                }

                if (mFlushed && mInputQueue.Count == 1)
                    mode = Master.LZMA.ELzmaFinishMode.LZMA_FINISH_END;

                capacity = mTotalOutputCapacity;
                frame = mInputQueue.Peek();
            }

            if (mDecoder.mDicPos == mDecoder.mDicBufSize)
                mDecoder.mDicPos = 0;

            long limit = Math.Min(mDecoderPosition + capacity, mDecoder.mDicBufSize);
            long input = frame.mEnding - frame.mOffset;
            var res = mDecoder.LzmaDec_DecodeToDic(limit, P.From(frame.mBuffer, frame.mOffset), ref input, mode, out mStatus);
            if (res != Master.LZMA.SZ_OK)
            {
                System.Diagnostics.Debug.Assert(res == Master.LZMA.SZ_ERROR_DATA);
                throw new InvalidDataException();
            }

            frame.mOffset += checked((int)input);

            if (frame.mOffset == frame.mEnding)
            {
                frame.mCompletion.SetResult(null);

                lock (mSyncObject)
                {
                    System.Diagnostics.Debug.Assert(mStarted && mRunning);
                    var other = mInputQueue.Dequeue();
                    System.Diagnostics.Debug.Assert(other == frame);
                }
            }

            return true;
        }
    }
}
