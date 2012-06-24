using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.LZMA.Master
{
    public class Lzma2DecoderStream: Stream
    {
        #region Stream Methods - Unsupported

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
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

        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        #endregion

        private Stream mInputStream;
        private LZMA.CLzma2Dec mDecoder;
        private byte[] mBuffer;
        private int mOffset;
        private int mEnding;
        private bool mInputEnd;
        private bool mOutputEnd;

        public Lzma2DecoderStream(Stream inputStream, byte prop)
        {
            mInputStream = inputStream;

            mDecoder = new LZMA.CLzma2Dec();
            mDecoder.Lzma2Dec_Construct();
            if(mDecoder.Lzma2Dec_Allocate(prop, LZMA.ISzAlloc.SmallAlloc) != LZMA.SZ_OK)
                throw new InvalidDataException();
            mDecoder.Lzma2Dec_Init();

            mBuffer = new byte[4 << 10];
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

            if(mDecoder.mDecoder.mDicPos == mDecoder.mDecoder.mDicBufSize)
                mDecoder.mDecoder.mDicPos = 0;

            long origin = mDecoder.mDecoder.mDicPos;
            if(count > mDecoder.mDecoder.mDicBufSize - origin)
                count = (int)(mDecoder.mDecoder.mDicBufSize - origin);

            LZMA.ELzmaStatus status;
            long srcLen = mEnding - mOffset;
            var res = mDecoder.Lzma2Dec_DecodeToDic(origin + count, P.From(mBuffer, mOffset), ref srcLen,
                mInputEnd ? LZMA.ELzmaFinishMode.LZMA_FINISH_END : LZMA.ELzmaFinishMode.LZMA_FINISH_ANY, out status);
            if(res != LZMA.SZ_OK)
                throw new InvalidDataException();

            mOffset += (int)srcLen;
            int processed = (int)(mDecoder.mDecoder.mDicPos - origin);
            Buffer.BlockCopy(mDecoder.mDecoder.mDic.mBuffer, mDecoder.mDecoder.mDic.mOffset + (int)origin, buffer, offset, processed);
            count -= processed;
            offset += processed;

            if(status == LZMA.ELzmaStatus.LZMA_STATUS_FINISHED_WITH_MARK)
                mOutputEnd = true;

            if(status == LZMA.ELzmaStatus.LZMA_STATUS_MAYBE_FINISHED_WITHOUT_MARK && mInputEnd && mOffset == mEnding)
                mOutputEnd = true;

            if(status == LZMA.ELzmaStatus.LZMA_STATUS_NEEDS_MORE_INPUT)
            {
                if(mOffset != mEnding)
                    throw new Exception(); // huh?
            }

            if(processed == 0)
                throw new Exception(); // huh?

            return processed;
        }
    }
}
