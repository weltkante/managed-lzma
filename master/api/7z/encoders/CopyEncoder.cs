using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip.Encoders
{
    public sealed class CopyEncoderNodeSettings : ArchiveEncoderSettings
    {
        public static readonly CopyEncoderNodeSettings Instance = new CopyEncoderNodeSettings();

        internal override CompressionMethod GetDecoderType() => CompressionMethod.Copy;

        private CopyEncoderNodeSettings() { }

        internal override IDisposable CreateEncoder(IArchiveEncoderInputStream[] input, IArchiveEncoderOutputStream[] output)
        {
            return new CopyEncoderNode(input[0], output[0]);
        }
    }

    internal sealed class CopyEncoderNode : IArchiveEncoderNode
    {
        private IArchiveEncoderInputStream mInput;
        private IArchiveEncoderOutputStream mOutput;
        private Task mTask;
        private byte[] mBuffer;
        private int mOffset;
        private int mEnding;
        private bool mComplete;

        public CopyEncoderNode(IArchiveEncoderInputStream input, IArchiveEncoderOutputStream output)
        {
            mInput = input;
            mOutput = output;
            mBuffer = new byte[0x10000];
        }

        public void Dispose()
        {
            mInput.Dispose();
            mOutput.Dispose();
        }

        public void Start()
        {
            mTask = Task.Run(async () => {
                for (;;)
                {
                    if (!mComplete && mEnding < mBuffer.Length)
                    {
                        var fetched = await mInput.ReadAsync(mBuffer, mEnding, mBuffer.Length - mEnding);
                        if (fetched == 0)
                            mComplete = true;
                        else
                            mEnding += fetched;
                    }

                    if (mOffset < mEnding)
                    {
                        var written = mOutput.Write(mBuffer, mOffset, mEnding - mOffset);
                        mOffset += written;
                        if (mOffset == mEnding)
                            mOffset = mEnding = 0;
                    }

                    if (mComplete && mOffset == mEnding)
                    {
                        mOutput.Done();
                        break;
                    }
                }
            });
        }
    }
}
