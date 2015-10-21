using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip.Encoders
{
    public sealed class LzmaEncoderSettings : ArchiveEncoderSettings
    {
        private readonly LZMA.EncoderSettings mSettings;

        internal override CompressionMethod GetDecoderType() => CompressionMethod.LZMA;

        public LzmaEncoderSettings(LZMA.EncoderSettings settings)
        {
            mSettings = settings;
        }

        internal override IDisposable CreateEncoder(IArchiveEncoderInputStream[] input, IArchiveEncoderOutputStream[] output)
        {
            return new LzmaEncoderNode(mSettings, input[0], output[0]);
        }
    }

    internal sealed class LzmaEncoderNode : IArchiveEncoderNode
    {
        private IArchiveEncoderInputStream mInput;
        private IArchiveEncoderOutputStream mOutput;
        private LZMA.AsyncEncoder mEncoder;
        private Task mInputTask;
        private Task mOutputTask;
        private byte[] mInputBuffer;
        private byte[] mOutputBuffer;
        private int mInputOffset;
        private int mInputEnding;
        private int mOutputOffset;
        private int mOutputEnding;
        private bool mInputComplete;
        private bool mOutputComplete;

        public LzmaEncoderNode(LZMA.EncoderSettings settings, IArchiveEncoderInputStream input, IArchiveEncoderOutputStream output)
        {
            this.mInput = input;
            this.mOutput = output;
            mEncoder = new LZMA.AsyncEncoder(settings);
            mInputBuffer = new byte[0x10000];
            mOutputBuffer = new byte[0x10000];
        }

        public void Dispose()
        {
        }

        public void Start()
        {
            mInputTask = Task.Run(async () => {
                for (;;)
                {
                    if (!mInputComplete && mInputEnding < mInputBuffer.Length)
                    {
                        var fetched = await mInput.ReadAsync(mInputBuffer, mInputEnding, mInputBuffer.Length - mInputEnding);
                        if (fetched == 0)
                            mInputComplete = true;
                        else
                            mInputEnding += fetched;
                    }

                    if (mInputOffset < mInputEnding)
                    {
                        await mEncoder.WriteInputAsync(mInputBuffer, mInputOffset, mInputEnding - mInputOffset);
                        mInputOffset = mInputEnding;
                    }

                    if (mInputOffset == mInputEnding)
                    {
                        if (mInputComplete)
                        {
                            await mEncoder.CompleteInputAsync();
                            return;
                        }
                        else
                        {
                            mInputOffset = 0;
                            mInputEnding = 0;
                        }
                    }
                }
            });

            mOutputTask = Task.Run(async () => {
                for (;;)
                {
                    if (!mOutputComplete && mOutputEnding < mOutputBuffer.Length)
                    {
                        var fetched = await mEncoder.ReadOutputAsync(mOutputBuffer, mOutputEnding, mOutputBuffer.Length - mOutputEnding, ReadMode.ReturnEarly);
                        if (fetched == 0)
                            mOutputComplete = true;
                        else
                            mOutputEnding += fetched;
                    }

                    if (mOutputOffset < mOutputEnding)
                        mOutputOffset += mOutput.Write(mOutputBuffer, mOutputOffset, mOutputEnding - mOutputOffset);

                    if (mOutputOffset == mOutputEnding)
                    {
                        if (mOutputComplete)
                        {
                            mOutput.Done();
                            return;
                        }
                        else
                        {
                            mOutputOffset = 0;
                            mOutputEnding = 0;
                        }
                    }
                }
            });
        }
    }
}
