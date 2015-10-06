using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    public abstract class ReaderNode : IDisposable
    {
        public abstract void Dispose();
        public abstract void Skip(int count);
        public abstract int Read(byte[] buffer, int offset, int count);
    }

    public abstract class DecoderNode : IDisposable
    {
        public abstract void Dispose();
        public abstract void SetInputStream(int index, ReaderNode stream);
        public abstract ReaderNode GetOutputStream(int index);
    }

    internal sealed class StreamCoordinator
    {
        private Stream mStream;

        public StreamCoordinator(Stream stream)
        {
            mStream = stream;
        }

        public int ReadAt(long position, byte[] buffer, int offset, int count)
        {
            mStream.Position = position;
            return mStream.Read(buffer, offset, count);
        }
    }

    internal sealed class CoordinatedStream : ReaderNode
    {
        private StreamCoordinator mCoordinator;
        private long mOffset;
        private long mCursor;
        private long mEnding;

        public CoordinatedStream(StreamCoordinator coordinator, long offset, long length)
        {
            mCoordinator = coordinator;
            mOffset = offset;
            mCursor = offset;
            mEnding = offset + length;
        }

        public override void Dispose()
        {
            mCoordinator = null;
        }

        public override void Skip(int count)
        {
            var remaining = mEnding - mCursor;
            if (count < 0 || count > remaining)
                throw new ArgumentOutOfRangeException(nameof(count));

            mCursor += count;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = mEnding - mCursor;
            if (remaining == 0)
                return 0;

            if (count > remaining)
                count = (int)remaining;

            var result = mCoordinator.ReadAt(mCursor, buffer, offset, count);
            if (result <= 0 || result > count)
                throw new InternalFailureException();

            mCursor += result;
            return result;
        }
    }

    public sealed class ArchiveSectionDecoder : IDisposable
    {
        private static ReaderNode SelectStream(DecoderInputMetadata metadata, ReaderNode[] streams, DecoderNode[] decoders)
        {
            var decoderIndex = metadata.DecoderIndex;
            if (decoderIndex.HasValue)
                return decoders[decoderIndex.Value].GetOutputStream(metadata.StreamIndex);
            else
                return streams[metadata.StreamIndex];
        }

        private StreamCoordinator mCoordinator;
        private ReaderNode[] mInputStreams;
        private DecoderNode[] mDecoders;
        private ReaderNode mOutputStream;

        public ArchiveSectionDecoder(Stream stream, ArchiveMetadata metadata, int index, Lazy<string> password)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (!stream.CanRead)
                throw new ArgumentException("Stream must be readable.", nameof(stream));

            if (!stream.CanSeek)
                throw new ArgumentException("Stream must be seekable.", nameof(stream));

            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            if (index < 0 || index >= metadata.DecoderSections.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            var inputCoordinator = new StreamCoordinator(stream);
            var inputStreams = new ReaderNode[metadata.FileSections.Length];
            for (int i = 0; i < inputStreams.Length; i++)
                inputStreams[i] = new CoordinatedStream(inputCoordinator, metadata.FileSections[i].Offset, metadata.FileSections[i].Length);

            var decoderSection = metadata.DecoderSections[index];
            var decoderDefinitions = decoderSection.Decoders;
            var decoders = new DecoderNode[decoderDefinitions.Length];

            for (int i = 0; i < decoders.Length; i++)
            {
                var decoderDefinition = decoderDefinitions[i];
                decoders[i] = decoderDefinition.DecoderType.CreateDecoder(decoderDefinition.Settings, decoderDefinition.OutputStreams, password);
            }

            for (int i = 0; i < decoders.Length; i++)
            {
                var decoder = decoders[i];
                var decoderDefinition = decoderDefinitions[i];
                var decoderInputDefinitions = decoderDefinition.InputStreams;

                for (int j = 0; j < decoderInputDefinitions.Length; j++)
                    decoder.SetInputStream(j, SelectStream(decoderInputDefinitions[j], inputStreams, decoders));
            }

            mCoordinator = inputCoordinator;
            mInputStreams = inputStreams;
            mDecoders = decoders;
            mOutputStream = SelectStream(decoderSection.DecodedStream, inputStreams, decoders);
        }

        public void Dispose()
        {
            foreach (var input in mInputStreams)
                input.Dispose();

            foreach (var decoder in mDecoders)
                decoder.Dispose();
        }

        public void Skip(int offset)
        {
            mOutputStream.Skip(offset);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            return mOutputStream.Read(buffer, offset, count);
        }
    }

    public sealed class DecodedArchiveSectionStream : Stream
    {
        private ArchiveSectionDecoder mReader;
        private long mLength;
        private long mPosition;

        public DecodedArchiveSectionStream(Stream stream, ArchiveMetadata metadata, int index, Lazy<string> password)
        {
            mReader = new ArchiveSectionDecoder(stream, metadata, index, password);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                mReader.Dispose();

            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        /// <summary>Only provided for convenience, does not implement full CanSeek API contract.</summary>
        public override long Length
        {
            get { return mLength; }
        }

        /// <summary>Only provided for convenience, does not implement full CanSeek API contract.</summary>
        public override long Position
        {
            get { return mPosition; }
            set
            {
                if (value < mPosition || value > mLength)
                    throw new ArgumentOutOfRangeException(nameof(value));

                Skip(value - mPosition);
            }
        }

        /// <summary>Only provided for convenience, does not implement full CanSeek API contract.</summary>
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    if (offset < mPosition || offset > mLength)
                        throw new ArgumentOutOfRangeException(nameof(offset));

                    offset -= mPosition;
                    break;

                case SeekOrigin.Current:
                    if (offset < 0 || offset > mLength - mPosition)
                        throw new ArgumentOutOfRangeException(nameof(offset));

                    break;

                case SeekOrigin.End:
                    if (offset > 0 || offset < mPosition - mLength)
                        throw new ArgumentOutOfRangeException(nameof(offset));

                    offset += mLength;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(origin));
            }

            Skip(offset);
            return mPosition;
        }

        public void Skip(long offset)
        {
            if (offset < 0 || offset > mLength - mPosition)
                throw new ArgumentOutOfRangeException(nameof(offset));
            
            while (offset > Int32.MaxValue)
            {
                mReader.Skip(Int32.MaxValue);
                mPosition += Int32.MaxValue;
                offset -= Int32.MaxValue;
            }

            if (offset > 0)
            {
                mReader.Skip((int)offset);
                mPosition += offset;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0 || count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(count));

            var result = mReader.Read(buffer, offset, count);
            mPosition += result;
            return result;
        }

        #region Invalid Operations

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
