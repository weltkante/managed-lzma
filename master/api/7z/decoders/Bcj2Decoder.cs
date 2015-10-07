using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    internal sealed class Bcj2ArchiveDecoder : DecoderNode
    {
        private sealed class InputStream : Stream
        {
            private ReaderNode mInput;

            public void SetInput(ReaderNode stream)
            {
                if (stream == null)
                    throw new ArgumentNullException(nameof(stream));

                if (mInput != null)
                    throw new InvalidOperationException();

                mInput = stream;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (mInput == null)
                    throw new InvalidOperationException();

                return mInput.Read(buffer, offset, count);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;

            public override long Length
            {
                get { throw new InvalidOperationException(); }
            }

            public override long Position
            {
                get { throw new InvalidOperationException(); }
                set { throw new InvalidOperationException(); }
            }

            public override void Flush()
            {
                throw new InvalidOperationException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new InvalidOperationException();
            }

            public override void SetLength(long value)
            {
                throw new InvalidOperationException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new InvalidOperationException();
            }
        }

        private sealed class OutputStream : ReaderNode
        {
            private Bcj2ArchiveDecoder mOwner;
            public OutputStream(Bcj2ArchiveDecoder owner) { mOwner = owner; }
            public override void Dispose() { mOwner = null; }
            public override void Skip(int count) => mOwner.Skip(count);
            public override int Read(byte[] buffer, int offset, int count) => mOwner.Read(buffer, offset, count);
        }

        private master._7zip.Legacy.Bcj2DecoderStream mDecoder;
        private InputStream[] mInputs;
        private OutputStream mOutput;
        private long mLength;
        private long mPosition;

        public Bcj2ArchiveDecoder(ImmutableArray<byte> settings, long length)
        {
            mInputs = new InputStream[4];
            mInputs[0] = new InputStream();
            mInputs[1] = new InputStream();
            mInputs[2] = new InputStream();
            mInputs[3] = new InputStream();
            mOutput = new OutputStream(this);
            mDecoder = new master._7zip.Legacy.Bcj2DecoderStream(mInputs, settings.ToArray(), length);
            mLength = length;
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }

        public override void SetInputStream(int index, ReaderNode stream, long length)
        {
            if (index < 0 || index >= 4)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            mInputs[index].SetInput(stream);
        }

        public override ReaderNode GetOutputStream(int index)
        {
            if (index != 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            return mOutput;
        }

        private void Skip(int count)
        {
            throw new NotImplementedException();
        }

        private int Read(byte[] buffer, int offset, int count)
        {
            var result = mDecoder.Read(buffer, offset, count);
            mPosition += result;
            return result;
        }
    }
}
