using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.LZMA.Master.SevenZip
{
    public struct ReaderConstraint: IDisposable
    {
        private DataReader mReader;

        internal ReaderConstraint(DataReader reader)
        {
            mReader = reader;
        }

        public void Dispose()
        {
            mReader.ReleaseConstraint();
        }
    }

    public class DataReader: IDisposable
    {
        private const int kBufferSize = 64;

        private Stream mStream;
        private Stack<long> mConstraints;
        private long mRemaining;

        private byte[] mBuffer;
        private int mOffset;
        private int mEnding;

        public DataReader(Stream stream)
        {
            mStream = stream;
        }

        public virtual void Dispose()
        {
            mStream.Dispose();
        }

        public int Position
        {
            get { throw new NotImplementedException(); }
        }

        public void Seek(long position)
        {
            if(mConstraints.Count != 0)
                throw new InvalidOperationException();

            throw new NotImplementedException();
        }

        public void Skip(long size)
        {
            throw new NotImplementedException();
        }

        public uint CalculateCRC(long length)
        {
            uint crc = CRC.kInitCRC;
            if(length <= 0)
                return CRC.Finish(crc);

            int cached = mEnding - mOffset;
            if(length <= cached)
            {
                cached = (int)length;
                crc = CRC.Update(crc, mBuffer, mOffset, cached);
                mOffset += cached;
                return CRC.Finish(crc);
            }
            else if(cached > 0)
            {
                crc = CRC.Update(crc, mBuffer, mOffset, cached);
                length -= cached;
            }

            mEnding = 0;
            mOffset = 0;

            for(; ; )
            {
                cached = mStream.Read(mBuffer, 0, kBufferSize);
                if(cached <= 0)
                    throw new InvalidDataException();

                if(length <= cached)
                    break;

                length -= cached;
                crc = CRC.Update(crc, mBuffer, 0, cached);
            }

            mEnding = cached;
            mOffset = (int)length;
            return CRC.Finish(CRC.Update(crc, mBuffer, 0, mOffset));
        }

        public long Remaining
        {
            get { return mRemaining; }
        }

        public ReaderConstraint Constrain(long size)
        {
            mConstraints.Push(mRemaining);
            mRemaining = size;
            return new ReaderConstraint(this);
        }

        internal void ReleaseConstraint()
        {
            mRemaining = mConstraints.Pop();
        }

        private void CheckConstraint(long size)
        {
            if(size > mRemaining)
                throw new InvalidDataException();
        }

        private void Fetch(int size)
        {
            if(mEnding - mOffset < size)
                FetchSlow(size);
        }

        private void FetchSlow(int size)
        {
            if(mOffset == mEnding)
            {
                mOffset = 0;
                mEnding = 0;
            }

            if(mOffset > kBufferSize / 2)
            {
                Buffer.BlockCopy(mBuffer, mOffset, mBuffer, 0, mEnding - mOffset);
                mEnding -= mOffset;
                mOffset = 0;
            }

            while(mEnding - mOffset < size)
            {
                int fetched = mStream.Read(mBuffer, mEnding, kBufferSize - mEnding);
                if(fetched <= 0)
                    throw new InvalidDataException();

                mEnding += fetched;
            }
        }

        public byte ReadByte()
        {
            Fetch(1);
            return mBuffer[mOffset++];
        }

        public byte[] ReadBytes(int length)
        {
            byte[] buffer = new byte[length];
            ReadBytes(buffer, 0, length);
            return buffer;
        }

        public void ReadBytes(byte[] buffer, int offset, int length)
        {
            if(buffer == null)
                throw new ArgumentNullException("buffer");

            if(offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException("offset");

            if(length < 0 || length > buffer.Length - offset)
                throw new ArgumentOutOfRangeException("length");

            int ending = offset + length;
            while(offset < ending && mOffset < mEnding)
                buffer[offset++] = mBuffer[mOffset++];

            while(offset < ending)
            {
                int read = mStream.Read(buffer, offset, ending - offset);
                if(read <= 0)
                    throw new InvalidDataException();

                offset += read;
            }
        }

        public int ReadInt32()
        {
            Fetch(4);
            int value = mBuffer[mOffset]
                + (mBuffer[mOffset + 1] << 8)
                + (mBuffer[mOffset + 2] << 16)
                + (mBuffer[mOffset + 3] << 24);
            mOffset += 4;
            return value;
        }

        public uint ReadUInt32()
        {
            return (uint)ReadInt32();
        }

        public long ReadInt64()
        {
            return (long)ReadUInt64();
        }

        public ulong ReadUInt64()
        {
            Fetch(8);

            ulong value = 0;
            for(int i = 0; i < 8; i++)
                value += (ulong)mBuffer[mOffset++] << (i * 8);

            return value;
        }

        // ReadNumber32 in original C source, ReadNum/CNum in C++
        public int ReadPackedUInt31()
        {
            ulong value = ReadPackedUInt64();
            if(value >= 0x80000000)
                throw new InvalidDataException();
            return (int)value;
        }

        // ReadNumber in original source
        public ulong ReadPackedUInt64()
        {
            ulong value = 0;
            byte mask = 0x80;
            byte firstByte = ReadByte();

            for(int i = 0; i < 8; i++)
            {
                if((firstByte & mask) == 0)
                    return value + (ulong)(firstByte & (mask - 1u)) << (i * 8);

                value |= (ulong)ReadByte() << (i * 8);
                mask >>= 1;
            }

            return value;
        }

        public BitVector ReadBitVector(int length)
        {
            byte value = 0;
            byte mask = 0;

            BitVector bits = new BitVector(length);
            for(int i = 0; i < length; i++)
            {
                if(mask == 0)
                {
                    value = ReadByte();
                    mask = 0x80;
                }

                if((value & mask) != 0)
                    bits.SetBit(i);

                mask >>= 1;
            }

            // TODO: Make this a warning, not an exception.
            //while(mask != 0)
            //{
            //    if((value & mask) != 0)
            //        throw new InvalidDataException();
            //
            //    mask >>= 1;
            //}

            return bits;
        }

        public BitVector ReadBitVector2(int length)
        {
            byte allSet = ReadByte();
            if(allSet == 0)
                return ReadBitVector(length);

            var bits = new BitVector(length);
            for(int i = 0; i < length; i++)
                bits.SetBit(i);
            return bits;
        }

        public string ReadString()
        {
            int len = 0;
            for(; ; )
            {
                Fetch((len + 1) * 2);
                if(mBuffer[mOffset + len * 2] == 0 && mBuffer[mOffset + len * 2 + 1] == 0)
                    break;
                len++;
            }

            string text = Encoding.Unicode.GetString(mBuffer, mOffset, len * 2);
            mOffset += (len + 1) * 2;
            return text;
        }
    }
}
