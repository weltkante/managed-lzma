using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    /// <summary>
    /// Transfers external input into an encoder.
    /// </summary>
    internal sealed class EncoderInput : IStreamReader
    {
        #region Implementation

        private EncoderSession mSession;
        private IStreamWriter mEncoderInput;
        private Task mTransferTask;

        internal void Connect(EncoderSession session)
        {
            mSession = session;
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public void SetInputStream(EncoderNode encoder, int index)
        {
            System.Diagnostics.Debug.Assert(mEncoderInput == null);

            mEncoderInput = encoder.GetInputSink(index);
            if (mEncoderInput == null)
                encoder.SetInputSource(index, this);
        }

        public void Start()
        {
            if (mEncoderInput != null)
                mTransferTask = Task.Run(TransferLoop);
        }

        private async Task TransferLoop()
        {
            System.Diagnostics.Debug.WriteLine("PERF: EncoderInput enters transfer loop. Avoid this if possible.");

            var buffer = new byte[0x10000];
            for (;;)
            {
                var fetched = await mSession.ReadInternalAsync(buffer, 0, buffer.Length, StreamMode.Partial).ConfigureAwait(false);
                System.Diagnostics.Debug.Assert(0 <= fetched && fetched <= buffer.Length);
                if (fetched == 0)
                {
                    await mEncoderInput.CompleteAsync().ConfigureAwait(false);
                    return;
                }

                int offset = 0;
                while (fetched > 0)
                {
                    var written = await mEncoderInput.WriteAsync(buffer, offset, fetched, StreamMode.Complete).ConfigureAwait(false);
                    System.Diagnostics.Debug.Assert(0 < written && written <= fetched);
                    offset += written;
                    fetched -= written;
                }
            }
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            System.Diagnostics.Debug.Assert(mEncoderInput == null);

            return mSession.ReadInternalAsync(buffer, offset, length, mode);
        }

        #endregion
    }

    /// <summary>
    /// Transfers encoder output into external storage.
    /// </summary>
    internal sealed class EncoderStorage : IStreamWriter
    {
        #region Implementation

        private Stream mStream;
        private IStreamReader mEncoderOutput;
        private Task mTransferTask;
        private bool mComplete;

        public EncoderStorage(Stream stream)
        {
            mStream = stream;
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public void SetOutputStream(EncoderNode encoder, int index)
        {
            mEncoderOutput = encoder.GetOutputSource(index);
            if (mEncoderOutput == null)
                encoder.SetOutputSink(index, this);
        }

        public void Start()
        {
            if (mEncoderOutput != null)
                mTransferTask = Task.Run(TransferLoop);
        }

        private async Task TransferLoop()
        {
            System.Diagnostics.Debug.WriteLine("PERF: EncoderStorage enters transfer loop. Avoid this if possible.");

            var buffer = new byte[0x10000];
            for (;;)
            {
                var fetched = await mEncoderOutput.ReadAsync(buffer, 0, buffer.Length, StreamMode.Partial);
                System.Diagnostics.Debug.Assert(0 <= fetched && fetched <= buffer.Length);
                if (fetched == 0)
                {
                    mComplete = true;
                    return;
                }

                await mStream.WriteAsync(buffer, 0, fetched);
            }
        }

        public async Task<int> WriteAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            System.Diagnostics.Debug.Assert(mEncoderOutput == null);
            System.Diagnostics.Debug.Assert(!mComplete);
            await mStream.WriteAsync(buffer, offset, length);
            return length;
        }

        public Task CompleteAsync()
        {
            System.Diagnostics.Debug.Assert(mEncoderOutput == null);
            System.Diagnostics.Debug.Assert(!mComplete);
            mComplete = true;
            return Task.CompletedTask;
        }

        #endregion
    }

    /// <summary>
    /// Transfers data from the output of one encoder to the input of another encoder.
    /// </summary>
    internal sealed class EncoderConnection : IStreamReader, IStreamWriter
    {
        #region Variables

        private IStreamReader mEncoderOutputToConnectionInput;
        private IStreamWriter mConnectionOutputToEncoderInput;
        private Task mTransferTask;
        private TaskCompletionSource<int> mResult;
        private byte[] mBuffer;
        private int mOffset;
        private int mEnding;
        private bool mComplete;

        #endregion

        #region Implementation

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public void SetInputStream(EncoderNode encoder, int index)
        {
            mConnectionOutputToEncoderInput = encoder.GetInputSink(index);
            if (mConnectionOutputToEncoderInput == null)
                encoder.SetInputSource(index, this);
        }

        public void SetOutputStream(EncoderNode encoder, int index)
        {
            mEncoderOutputToConnectionInput = encoder.GetOutputSource(index);
            if (mEncoderOutputToConnectionInput == null)
                encoder.SetOutputSink(index, this);
        }

        public void Start()
        {
            // If the inputs and outputs are both provided by the encoder nodes then we
            // need to bridge this by providing a temporary buffer and drive a copy loop.
            if (mConnectionOutputToEncoderInput != null && mEncoderOutputToConnectionInput != null)
                mTransferTask = Task.Run(TransferLoop);
        }

        private async Task TransferLoop()
        {
            System.Diagnostics.Debug.WriteLine("PERF: EncoderConnection enters transfer loop. Avoid this if possible.");

            var buffer = new byte[0x10000];
            var offset = 0;
            var ending = 0;

            for (;;)
            {
                if (!mComplete && ending < buffer.Length)
                {
                    var fetched = await mEncoderOutputToConnectionInput.ReadAsync(buffer, ending, buffer.Length - ending, StreamMode.Partial);
                    System.Diagnostics.Debug.Assert(0 <= fetched && fetched <= buffer.Length - ending);

                    if (fetched == 0)
                        mComplete = true;
                    else
                        ending += fetched;
                }

                if (offset < ending)
                {
                    var written = await mConnectionOutputToEncoderInput.WriteAsync(buffer, offset, ending - offset, StreamMode.Partial);
                    System.Diagnostics.Debug.Assert(0 < written && written <= ending - offset);

                    offset += written;
                }

                if (offset == ending)
                {
                    offset = 0;
                    ending = 0;

                    if (mComplete)
                    {
                        await mConnectionOutputToEncoderInput.CompleteAsync();
                        return;
                    }
                }
            }
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, StreamMode mode)
        {
            System.Diagnostics.Debug.Assert(mConnectionOutputToEncoderInput == null);
            System.Diagnostics.Debug.Assert(buffer != null);
            System.Diagnostics.Debug.Assert(0 <= offset && offset < buffer.Length);
            System.Diagnostics.Debug.Assert(0 <= count && count < buffer.Length - offset);

            if (mEncoderOutputToConnectionInput != null)
                return mEncoderOutputToConnectionInput.ReadAsync(buffer, offset, count, mode);

            lock (this)
            {
                if (mBuffer != null)
                    throw new InternalFailureException();

                mBuffer = buffer;
                mOffset = offset;
                mEnding = offset + count;
                mResult = new TaskCompletionSource<int>();
                Monitor.PulseAll(this);
                return mResult.Task;
            }
        }

        public Task<int> WriteAsync(byte[] buffer, int offset, int count, StreamMode mode)
        {
            System.Diagnostics.Debug.Assert(mEncoderOutputToConnectionInput == null);
            System.Diagnostics.Debug.Assert(buffer != null);
            System.Diagnostics.Debug.Assert(0 <= offset && offset < buffer.Length);
            System.Diagnostics.Debug.Assert(0 <= count && count < buffer.Length - offset);

            if (mConnectionOutputToEncoderInput != null)
                return mConnectionOutputToEncoderInput.WriteAsync(buffer, offset, count, mode);

            lock (this)
            {
                while (mBuffer == null)
                    Monitor.Wait(this);

                var result = Math.Min(count, mEnding - mOffset);
                Buffer.BlockCopy(buffer, offset, mBuffer, mOffset, result);
                mResult.SetResult(result);
                return Task.FromResult(result);
            }
        }

        public Task CompleteAsync()
        {
            System.Diagnostics.Debug.Assert(mEncoderOutputToConnectionInput == null);

            if (mConnectionOutputToEncoderInput != null)
                return mConnectionOutputToEncoderInput.CompleteAsync();

            throw new NotImplementedException();
        }

        #endregion
    }

    internal abstract class EncoderNode : IDisposable
    {
        #region Implementation

        public abstract void Start();
        public abstract void Dispose();

        // The encoder can decide between providing in input sink or accepting an input source.
        // It is preferable if the encoder can accept an input source so he can pull data directly from the connected encoder.
        public abstract IStreamWriter GetInputSink(int index);
        public abstract void SetInputSource(int index, IStreamReader stream);

        // The encode can decide between providing an output source or accepting an output sink.
        // It is preferable if the encoder can accept an output sink so he can push data directly to the connected encoder.
        public abstract IStreamReader GetOutputSource(int index);
        public abstract void SetOutputSink(int index, IStreamWriter stream);

        #endregion
    }

    public sealed class EncoderSession : IDisposable
    {
        // TODO: awaitable

        private sealed class ChecksumStream : Stream
        {
            private Stream mStream;

            public ChecksumStream(Stream stream)
            {
                mStream = stream;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;

            public override int Read(byte[] buffer, int offset, int count)
            {
                return mStream.Read(buffer, offset, count);
            }

            #region Invalid Operations

            public override long Length
            {
                get { throw new InvalidOperationException(); }
            }

            public override long Position
            {
                get { throw new InvalidOperationException(); }
                set { throw new InvalidOperationException(); }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new InvalidOperationException();
            }

            public override void SetLength(long value)
            {
                throw new InvalidOperationException();
            }

            public override void Flush()
            {
                throw new InvalidOperationException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new InvalidOperationException();
            }

            #endregion
        }

        private object mLockObject = new object();
        private ArchiveWriter mWriter;
        private IDisposable[] mEncoders;
        private Stream mPendingStream;
        private long mPendingLength;

        internal EncoderSession(ArchiveWriter writer, IDisposable[] encoders, EncoderInput source)
        {
            mWriter = writer;
            mEncoders = encoders;
            source.Connect(this);
        }

        internal async Task<int> ReadInternalAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            Stream stream;

            lock (mLockObject)
            {
                while (mPendingLength == 0)
                    Monitor.Wait(mLockObject);

                if (length > mPendingLength)
                    length = (int)mPendingLength;

                stream = mPendingStream;
            }

            var result = await stream.ReadAsync(buffer, offset, length);

            if (result <= 0 || result > length)
                throw new InvalidOperationException("Source stream passed to AppendFile violated stream contract.");

            lock (mLockObject)
            {
                if (mPendingStream != stream)
                    throw new InternalFailureException();

                mPendingLength -= result;

                if (mPendingLength == 0)
                {
                    mPendingStream = null;
                    Monitor.PulseAll(mLockObject);
                }
            }

            return result;
        }

        public void Dispose()
        {
            foreach (var encoder in mEncoders)
                encoder.Dispose();
        }

        public void Discard()
        {
            throw new NotImplementedException();
        }

        public void AppendFile(FileInfo file, DirectoryInfo root)
        {
            throw new NotImplementedException();
        }

        public void AppendFile(FileInfo file, string name)
        {
            using (var stream = file.OpenRead())
                AppendFile(stream, stream.Length, name, true, file.Attributes, file.CreationTimeUtc, file.LastWriteTimeUtc, file.LastAccessTimeUtc);
        }

        public void AppendFile(Stream stream, long length, string name, bool checksum, FileAttributes? attributes, DateTime? creationTime, DateTime? lastWriteTime, DateTime? lastAccessTime)
        {
            if (stream == null && length > 0)
                throw new ArgumentNullException(nameof(stream));

            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            if (checksum)
                stream = new ChecksumStream(stream);

            if (length > 0)
            {
                lock (mLockObject)
                {
                    if (mPendingStream != null)
                        throw new InvalidOperationException();

                    mPendingStream = stream;
                    mPendingLength = length;

                    Monitor.PulseAll(mLockObject);

                    while (mPendingStream != null)
                        Monitor.Wait(mLockObject);
                }
            }

            // TODO: record metadata
            throw new NotImplementedException();
        }
    }
}
