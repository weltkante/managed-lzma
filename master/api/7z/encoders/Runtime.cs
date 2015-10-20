using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    public sealed class ArchiveEncoderSession : IDisposable
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

        internal ArchiveEncoderSession(ArchiveWriter writer, IDisposable[] encoders, ArchiveEncoderInput source)
        {
            mWriter = writer;
            mEncoders = encoders;
            source.Connect(this);
        }

        internal async Task<int> ReadInternalAsync(byte[] buffer, int offset, int count)
        {
            Stream stream;

            lock (mLockObject)
            {
                while (mPendingLength == 0)
                    Monitor.Wait(mLockObject);

                if (count > mPendingLength)
                    count = (int)mPendingLength;

                stream = mPendingStream;
            }

            var result = await stream.ReadAsync(buffer, offset, count);

            if (result <= 0 || result > count)
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

    internal interface IArchiveEncoderInputStream : IDisposable
    {
        Task<int> ReadAsync(byte[] buffer, int offset, int count);
    }

    internal interface IArchiveEncoderOutputStream : IDisposable
    {
        int Write(byte[] buffer, int offset, int count);
    }

    internal sealed class ArchiveEncoderInput : IArchiveEncoderInputStream
    {
        private ArchiveEncoderSession mSession;

        internal void Connect(ArchiveEncoderSession session)
        {
            mSession = session;
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            return mSession.ReadInternalAsync(buffer, offset, count);
        }
    }

    internal sealed class ArchiveEncoderStorage : IArchiveEncoderOutputStream
    {
        private Stream mStream;

        public ArchiveEncoderStorage(Stream stream)
        {
            mStream = stream;
        }

        public void Dispose()
        {
            mStream.Dispose();
        }

        public int Write(byte[] buffer, int offset, int count)
        {
            mStream.Write(buffer, offset, count);
            return count;
        }
    }

    internal sealed class ArchiveEncoderConnection : IArchiveEncoderInputStream, IArchiveEncoderOutputStream
    {
        private TaskCompletionSource<int> mResult;
        private byte[] mBuffer;
        private int mOffset;
        private int mLength;

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            lock (this)
            {
                if (mBuffer != null)
                    throw new InternalFailureException();

                mBuffer = buffer;
                mOffset = offset;
                mLength = count;
                mResult = new TaskCompletionSource<int>();
                Monitor.PulseAll(this);
                return mResult.Task;
            }
        }

        public int Write(byte[] buffer, int offset, int count)
        {
            lock (this)
            {
                while (mBuffer == null)
                    Monitor.Wait(this);

                var result = Math.Min(count, mLength);
                Buffer.BlockCopy(buffer, offset, mBuffer, mOffset, result);
                mResult.SetResult(result);
                return result;
            }
        }
    }
}
