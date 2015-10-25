using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    internal sealed class AesArchiveDecoder : DecoderNode
    {
        private sealed class PasswordProvider : master._7zip.Legacy.IPasswordProvider
        {
            private readonly PasswordStorage mPassword;

            public PasswordProvider(PasswordStorage password)
            {
                mPassword = password;
            }

            string master._7zip.Legacy.IPasswordProvider.CryptoGetTextPassword()
            {
                return new string(mPassword.GetPassword());
            }
        }

        private sealed class InputStream : Stream
        {
            private ReaderNode mInput;
            private long mInputLength;

            public void SetInput(ReaderNode stream, long length)
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
                get { return mInputLength; }
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
            private AesArchiveDecoder mOwner;
            public OutputStream(AesArchiveDecoder owner) { mOwner = owner; }
            public override void Dispose() { mOwner = null; }
            public override void Skip(int count) => mOwner.Skip(count);
            public override int Read(byte[] buffer, int offset, int count) => mOwner.Read(buffer, offset, count);
        }

        private master._7zip.Legacy.AesDecoderStream mDecoder;
        private InputStream mInput;
        private OutputStream mOutput;
        private long mLength;
        private long mPosition;

        public AesArchiveDecoder(ImmutableArray<byte> settings, PasswordStorage password, long length)
        {
            if (password == null)
                throw new InvalidOperationException("Password required.");

            mInput = new InputStream();
            mOutput = new OutputStream(this);
            mDecoder = new master._7zip.Legacy.AesDecoderStream(mInput, settings.ToArray(), new PasswordProvider(password), length);
            mLength = length;
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }

        public override void SetInputStream(int index, ReaderNode stream, long length)
        {
            if (index != 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            mInput.SetInput(stream, length);
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
