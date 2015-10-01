using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    public sealed class DecodedSectionReader : IDisposable
    {
        #region Implementation

        private ArchiveMetadata mMetadata;
        private ArchiveDecoderSection mDecoderSection;
        private Stream mDecodedStream;
        private Stream mCurrentStream;
        private int mIndex;

        public DecodedSectionReader(Stream stream, ArchiveMetadata metadata, int index)
        {
            mDecodedStream = new DecodedSectionStream(stream, metadata, index);
            mMetadata = metadata;
            mDecoderSection = metadata.DecoderSections[index];
        }

        public void Dispose()
        {
            CloseCurrentStream();
            mDecodedStream.Dispose();
        }

        public int StreamCount
        {
            get { return mDecoderSection.Streams.Length; }
        }

        public int CurrentStreamIndex
        {
            get { return mIndex; }
        }

        public long CurrentStreamPosition
        {
            get { return mCurrentStream != null ? mCurrentStream.Position : 0; }
        }

        public long CurrentStreamLength
        {
            get { return mDecoderSection.Streams[mIndex].Length; }
        }

        public Checksum? CurrentStreamChecksum
        {
            get { return mDecoderSection.Streams[mIndex].Checksum; }
        }

        public Stream OpenStream()
        {
            if (this.CurrentStreamIndex == this.StreamCount)
                throw new InvalidOperationException("The reader contains no more streams.");

            if (mCurrentStream != null)
                throw new InvalidOperationException("Each stream can only be opened once.");

            mCurrentStream = new DecodedStream(mDecodedStream, this.CurrentStreamLength);

            return mCurrentStream;
        }

        public void NextStream()
        {
            if (this.CurrentStreamIndex == this.StreamCount)
                throw new InvalidOperationException("The reader contains no more streams.");

            var remaining = CurrentStreamLength - CurrentStreamPosition;

            CloseCurrentStream();

            // Seeking forward must be supported by all decoder streams.
            if (remaining > 0)
                mDecodedStream.Seek(remaining, SeekOrigin.Current);

            mIndex++;
        }

        private void CloseCurrentStream()
        {
            if (mCurrentStream != null)
            {
                mCurrentStream.Dispose();
                mCurrentStream = null;
            }
        }

        #endregion
    }

    internal sealed class DecodedStream : Stream
    {
        #region Implementation

        private Stream mStream;
        private long mOffset;
        private long mLength;

        internal DecodedStream(Stream stream, long length)
        {
            mStream = stream;
            mLength = length;
        }

        protected override void Dispose(bool disposing)
        {
            // We mark the stream as disposed by clearing the base stream reference.
            // Note that we must keep the offset/length fields intact because our owner still needs them.
            mStream = null;

            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                // Returning the length from a non-seekable stream is non-standard.
                // The caller must know otherwise that we support this method.
                return mLength;
            }
        }

        public override long Position
        {
            get
            {
                // Returning the position from a non-seekable stream is non-standard.
                // The caller must know otherwise that we support this method.
                return mOffset;
            }
            set
            {
                throw new InvalidOperationException();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (mStream == null)
                throw new ObjectDisposedException(null);

            if (origin != SeekOrigin.Current)
                throw new InvalidOperationException();

            var remaining = mLength - mOffset;
            if (offset < 0 || offset > remaining)
                throw new InvalidOperationException();

            mStream.Seek(offset, SeekOrigin.Current);
            mOffset += offset;
            return mOffset;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0 || count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (mStream == null)
                throw new ObjectDisposedException(null);

            var remaining = mLength - mOffset;
            if (count > remaining)
                count = (int)remaining;

            if (count == 0)
                return 0;

            var result = mStream.Read(buffer, offset, count);
            if (result <= 0 || result > count)
                throw new InternalFailureException();

            mOffset += result;
            return result;
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        public override void Flush()
        {
            throw new InvalidOperationException();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException();
        }

        #endregion
    }
}
