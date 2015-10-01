using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    public abstract class ArchiveDecoder : IDisposable
    {
        public abstract void Dispose();
        public abstract Stream SetInputStream(int index, Stream stream);
        public abstract Stream GetOutputStream(int index);
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

    internal sealed class CoordinatedStream : Stream
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

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                return mEnding - mOffset;
            }
        }

        public override long Position
        {
            get
            {
                return mCursor - mOffset;
            }
            set
            {
                throw new InvalidOperationException();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (offset < 0 || origin != SeekOrigin.Current)
                throw new InvalidOperationException();

            var remaining = mEnding - mCursor;
            if (offset > remaining)
                throw new ArgumentOutOfRangeException(nameof(offset));

            mCursor += offset;
            return mCursor - mOffset;
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
    }

    public sealed class DecodedSectionStream : Stream
    {
        private static Stream SelectStream(DecoderInputMetadata metadata, Stream[] streams, ArchiveDecoder[] decoders)
        {
            var decoderIndex = metadata.DecoderIndex;
            if (decoderIndex.HasValue)
                return decoders[decoderIndex.Value].GetOutputStream(metadata.StreamIndex);
            else
                return streams[metadata.StreamIndex];
        }

        private StreamCoordinator mCoordinator;
        private Stream[] mInputStreams;
        private ArchiveDecoder[] mDecoders;
        private Stream mOutputStream;

        public DecodedSectionStream(Stream stream, ArchiveMetadata metadata, int index)
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
            var inputStreams = new Stream[metadata.FileSections.Length];
            for (int i = 0; i < inputStreams.Length; i++)
                inputStreams[i] = new CoordinatedStream(inputCoordinator, metadata.FileSections[i].Offset, metadata.FileSections[i].Length);

            var decoderSection = metadata.DecoderSections[index];
            var decoderDefinitions = decoderSection.Decoders;
            var decoders = new ArchiveDecoder[decoderDefinitions.Length];

            for (int i = 0; i < decoders.Length; i++)
            {
                var decoderDefinition = decoderDefinitions[i];
                decoders[i] = decoderDefinition.DecoderType.CreateDecoder(decoderDefinition.Settings);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var input in mInputStreams)
                    input.Dispose();

                foreach (var decoder in mDecoders)
                    decoder.Dispose();
            }

            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override long Length
        {
            get { return mOutputStream.Length; }
        }

        public override long Position
        {
            get { return mOutputStream.Position; }
            set { throw new InvalidOperationException(); }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin != SeekOrigin.Current || offset < 0)
                throw new InvalidOperationException();

            return mOutputStream.Seek(offset, origin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return mOutputStream.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return mOutputStream.ReadAsync(buffer, offset, count, cancellationToken);
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
