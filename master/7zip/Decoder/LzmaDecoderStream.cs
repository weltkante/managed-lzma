using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using ManagedLzma.LZMA;
using LZMA = ManagedLzma.LZMA.Master.LZMA;

namespace master._7zip.Legacy
{
    class LzmaDecoderStream: DecoderStream
    {
        private Stream mInputStream;
        private LZMA.CLzmaDec mDecoder;
        private byte[] mBuffer;
        private int mOffset;
        private int mEnding;
        private bool mInputEnd;
        private bool mOutputEnd;
        private long mWritten;
        private long mLimit;

        public LzmaDecoderStream(Stream input, byte[] info, long limit)
        {
            mInputStream = input;
            mLimit = limit;

            mBuffer = new byte[4 << 10];

            mDecoder = new LZMA.CLzmaDec();
            mDecoder.LzmaDec_Construct();
            mDecoder.LzmaDec_Allocate(P.From(info, 0), (uint)info.Length, LZMA.ISzAlloc.SmallAlloc);
            mDecoder.LzmaDec_Init();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if(buffer == null)
                throw new ArgumentNullException("buffer");

            if(offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException("offset");

            if(count < 0 || count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException("count");

            if(count == 0 || mOutputEnd)
                return 0;

        retry:
            if(mOffset == mEnding)
            {
                mOffset = 0;
                mEnding = 0;
            }

            if(!mInputEnd && mEnding < mBuffer.Length)
            {
                int read = mInputStream.Read(mBuffer, mEnding, mBuffer.Length - mEnding);
                if(read == 0)
                    mInputEnd = true;
                else
                    mEnding += read;
            }

            if(mDecoder.mDicPos == mDecoder.mDicBufSize)
                mDecoder.mDicPos = 0;

            long origin = mDecoder.mDicPos;
            if(count > mDecoder.mDicBufSize - origin)
                count = (int)(mDecoder.mDicBufSize - origin);
            if(count > mLimit - mWritten)
                count = (int)(mLimit - mWritten);

            if(count == 0)
                System.Diagnostics.Debugger.Break();

            LZMA.ELzmaStatus status;
            long srcLen = mEnding - mOffset;
            var res = mDecoder.LzmaDec_DecodeToDic(origin + count, P.From(mBuffer, mOffset), ref srcLen,
                mWritten + count == mLimit ? LZMA.ELzmaFinishMode.LZMA_FINISH_END : LZMA.ELzmaFinishMode.LZMA_FINISH_ANY, out status);
            if(res != LZMA.SZ_OK)
                throw new InvalidDataException();

            mOffset += (int)srcLen;
            int processed = (int)(mDecoder.mDicPos - origin);
            Buffer.BlockCopy(mDecoder.mDic.mBuffer, mDecoder.mDic.mOffset + (int)origin, buffer, offset, processed);
            mWritten += processed;

            if(status == LZMA.ELzmaStatus.LZMA_STATUS_FINISHED_WITH_MARK)
                mOutputEnd = true;

            if(status == LZMA.ELzmaStatus.LZMA_STATUS_MAYBE_FINISHED_WITHOUT_MARK && mWritten == mLimit)
                mOutputEnd = true;

            if(status == LZMA.ELzmaStatus.LZMA_STATUS_NEEDS_MORE_INPUT)
            {
                if(mOffset != mEnding)
                    throw new Exception(); // huh?
            }

            if(processed == 0 && !mOutputEnd)
            {
                if(mInputEnd || mOffset != mEnding)
                    throw new Exception(); // huh?

                System.Diagnostics.Debugger.Break();
                goto retry;
            }

            return processed;
        }
    }
}
