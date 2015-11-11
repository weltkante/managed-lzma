using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip.Encoders
{
    public sealed class AesEncoderSettings : EncoderSettings
    {
        private PasswordStorage mPassword;

        public AesEncoderSettings(PasswordStorage password)
        {
            mPassword = password;
        }

        internal override CompressionMethod GetDecoderType()
        {
            return CompressionMethod.AES;
        }

        internal override ImmutableArray<byte> SerializeSettings()
        {
            using (var builder = new AesEncoderBuilder(mPassword))
                return builder.SerializeSettings();
        }

        internal override EncoderNode CreateEncoder()
        {
            using (var builder = new AesEncoderBuilder(mPassword))
                return builder.CreateEncoder();
        }
    }

    internal struct AesEncoderBuilder : IDisposable
    {
        private int mNumCyclesPower;
        private byte[] mSalt;
        private byte[] mKey;
        private byte[] mSeed;
        private byte[] mSeed16;

        public AesEncoderBuilder(PasswordStorage password)
        {
            // NOTE: Following settings match the hardcoded 7z922 settings, which make some questionable choices:
            //
            // - The password salt is not used in the 7z encoder. Should not be a problem unless you create a huge
            //   amount of 7z archives all using the same password.
            //
            // - The RNG for the seed (aka IV) has some questionable choices for seeding itself, but it does
            //   include timing variations over a loop of 1k iterations, so it is not completely predictable.
            //
            // - The seed (aka IV) uses only 8 of 16 bytes, meaning 50% are zeroes. This does not make cracking
            //   the password any easier but may cause problems in AES security, but I'm no expert here.
            //
            // - Setting the NumCyclesPower to a fixed value is ok, raising it just makes brute forcing the
            //   password a bit more expensive, but doing half a million SHA1 iterations is reasonably slow.
            //
            // So while some choices are questionable they still don't allow any obvious attack. If you are going
            // to store highly sensitive material you probably shouldn't rely on 7z encryption alone anyways.
            // (Not to mention you shouldn't be using this library because its totally not finished ;-)
            //

            mNumCyclesPower = 19; // 7z922 has this parameter fixed
            mSalt = new byte[0]; // 7z922 does not use a salt (?)
            mSeed = new byte[8]; // 7z922 uses only 8 byte seeds (?)

            // 7z922 uses a questionable RNG hack, we will just use the standard .NET cryptography RNG
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(mSalt);
                rng.GetBytes(mSeed);
            }

            mSeed16 = new byte[16];
            Buffer.BlockCopy(mSeed, 0, mSeed16, 0, mSeed.Length);

            using (var passwordAccess = password.GetPassword())
            {
                var passwordBytes = default(byte[]);
                try
                {
                    passwordBytes = Encoding.Unicode.GetBytes(passwordAccess);
                    mKey = master._7zip.Legacy.AesDecoderStream.InitKey(mNumCyclesPower, mSalt, passwordBytes);
                }
                finally
                {
                    if (passwordBytes != null)
                        Array.Clear(passwordBytes, 0, passwordBytes.Length);
                }
            }
        }

        public void Dispose()
        {
            mNumCyclesPower = 0;
            Utilities.ClearBuffer(ref mSalt);
            Utilities.ClearBuffer(ref mKey);
            Utilities.ClearBuffer(ref mSeed);
            Utilities.ClearBuffer(ref mSeed16);
        }

        public ImmutableArray<byte> SerializeSettings()
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);

            writer.Write((byte)(mNumCyclesPower | ((mSalt.Length == 0 ? 0 : 1) << 7) | ((mSeed.Length == 0 ? 0 : 1) << 6)));

            if (mSalt.Length > 0 || mSeed.Length > 0)
            {
                var saltSize = mSalt.Length == 0 ? 0 : mSalt.Length - 1;
                var seedSize = mSeed.Length == 0 ? 0 : mSeed.Length - 1;
                writer.Write((byte)((saltSize << 4) | seedSize));
            }

            if (mSalt.Length > 0)
                writer.Write(mSalt);

            if (mSeed.Length > 0)
                writer.Write(mSeed);

            return ImmutableArray.Create(stream.ToArray());
        }

        public EncoderNode CreateEncoder()
        {
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                aes.Padding = System.Security.Cryptography.PaddingMode.None;
                return new AesEncoderNode(aes.CreateEncryptor(mKey, mSeed16));
            }
        }
    }

    internal sealed class AesEncoderNode : EncoderNode
    {
        private sealed class EncryptionStream : IStreamWriter
        {
            private IStreamWriter mOutput;
            private System.Security.Cryptography.ICryptoTransform mEncoder;
            private byte[] mBuffer1;
            private byte[] mBuffer2;
            private int mOffset;

            public EncryptionStream(System.Security.Cryptography.ICryptoTransform encoder)
            {
                mEncoder = encoder;
                mBuffer1 = new byte[16];
                mBuffer2 = new byte[16];
            }

            public void SetOutputSink(IStreamWriter output)
            {
                System.Diagnostics.Debug.Assert(mOutput == null && output != null);
                mOutput = output;
            }

            public async Task<int> WriteAsync(byte[] buffer, int offset, int length, StreamMode mode)
            {
                int result = length;

                while (length > 0)
                {
                    if (mOffset == 16)
                    {
                        mEncoder.TransformBlock(mBuffer1, 0, 16, mBuffer2, 0);
                        var written = await mOutput.WriteAsync(mBuffer2, 0, 16, StreamMode.Complete).ConfigureAwait(false);
                        System.Diagnostics.Debug.Assert(written == 16);
                        mOffset = 0;
                    }

                    int copy = Math.Min(16 - mOffset, length);
                    Buffer.BlockCopy(buffer, offset, mBuffer1, mOffset, copy);
                    mOffset += copy;
                    offset += copy;
                    length -= copy;
                }

                return result;
            }

            public async Task CompleteAsync()
            {
                System.Diagnostics.Debug.Assert(mBuffer1 != null && mBuffer2 != null);

                if (mOffset != 0)
                {
                    while (mOffset < 16)
                        mBuffer1[mOffset++] = 0;

                    System.Diagnostics.Debug.Assert(mOffset == 16);
                    mBuffer2 = mEncoder.TransformFinalBlock(mBuffer1, 0, 16);
                    var written = await mOutput.WriteAsync(mBuffer2, 0, 16, StreamMode.Complete).ConfigureAwait(false);
                    System.Diagnostics.Debug.Assert(written == 16);
                    mOffset = 0;
                }

                System.Diagnostics.Debug.Assert(mOffset == 0);
                await mOutput.CompleteAsync().ConfigureAwait(false);
            }

            public void DisposeInternal()
            {
                mEncoder.Dispose();
            }
        }

        private EncryptionStream mStream;

        public AesEncoderNode(System.Security.Cryptography.ICryptoTransform encoder)
        {
            mStream = new EncryptionStream(encoder);
        }

        public override IStreamWriter GetInputSink(int index)
        {
            if (index != 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            return mStream;
        }

        public override void SetInputSource(int index, IStreamReader stream)
        {
            throw new InternalFailureException();
        }

        public override IStreamReader GetOutputSource(int index)
        {
            if (index != 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            return null;
        }

        public override void SetOutputSink(int index, IStreamWriter stream)
        {
            if (index != 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            mStream.SetOutputSink(stream);
        }

        public override void Start()
        {
        }

        public override void Dispose()
        {
            mStream.DisposeInternal();
        }
    }
}
