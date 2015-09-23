using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// These are various reader classes which are passed to to subclasses of the metadata reader.
// This design allows the subclass to decide how to store the data, or whether to read it at all.
// In case the subclass does not want to read the data these readers will skip over it without allocating anything.

namespace ManagedLzma.SevenZip
{
    public sealed class MetadataStringReader
    {
        private ArchiveMetadataReader mReader;
        private int mCount;
        private int mIndex;

        internal MetadataStringReader(ArchiveMetadataReader reader, int count)
        {
            mReader = reader;
            mCount = count;
        }

        internal void Complete()
        {
            while (mIndex < mCount)
                ReadString();

            mReader = null;
        }

        public string ReadString()
        {
            if (mReader == null)
                throw new ObjectDisposedException(null);

            if (mIndex == mCount)
                throw new InvalidOperationException();

            var text = mReader.ReadStringInternal();
            mIndex += 1;
            return text;
        }
    }

    public sealed class MetadataDateReader
    {
        private ArchiveMetadataReader mReader;
        private BitVector mVector;
        private int mCount;
        private int mIndex;

        internal MetadataDateReader(ArchiveMetadataReader reader, int count, BitVector defined)
        {
            mReader = reader;
            mVector = defined;
            mCount = count;
        }

        internal void Complete()
        {
            while (mIndex < mCount)
                ReadDate();

            mReader = null;
        }

        public DateTime ReadDate()
        {
            if (mReader == null)
                throw new ObjectDisposedException(null);

            if (mIndex == mCount)
                throw new InvalidOperationException();

            // FILETIME = 100-nanosecond intervals since January 1, 1601 (UTC)
            var date = mReader.ReadInt64Internal();
            mIndex += 1;
            return DateTime.FromFileTimeUtc(date);
        }
    }

    public sealed class MetadataNumberReader
    {
        private ArchiveMetadataReader mReader;
        private BitVector mVector;
        private int mCount;
        private int mIndex;

        internal MetadataNumberReader(ArchiveMetadataReader reader, int count, BitVector defined)
        {
            mReader = reader;
            mVector = defined;
            mCount = count;
        }

        internal void Complete()
        {
            while (mIndex < mCount)
                ReadNumber();

            mReader = null;
        }

        public long ReadNumber()
        {
            if (mReader == null)
                throw new ObjectDisposedException(null);

            if (mIndex == mCount)
                throw new InvalidOperationException();

            var number = mReader.ReadNumberInternal();
            mIndex += 1;
            return number;
        }
    }

    public sealed class MetadataAttributeReader
    {
        private ArchiveMetadataReader mReader;
        private BitVector mVector;
        private int mCount;
        private int mIndex;

        internal MetadataAttributeReader(ArchiveMetadataReader reader, int count, BitVector defined)
        {
            mReader = reader;
            mVector = defined;
            mCount = count;
            throw new NotImplementedException();
        }

        internal void Complete()
        {
            while (mIndex < mCount)
                ReadAttributes();

            mReader = null;
        }

        public System.IO.FileAttributes ReadAttributes()
        {
            if (mReader == null)
                throw new ObjectDisposedException(null);

            if (mIndex == mCount)
                throw new InvalidOperationException();

            var number = mReader.ReadInt32Internal();
            mIndex += 1;
            return (System.IO.FileAttributes)number;
        }
    }

    public sealed class MetadataChecksumReader
    {
        internal MetadataChecksumReader(ArchiveMetadataReader reader, int count)
        {
            throw new NotImplementedException();
        }

        internal void Complete()
        {
            throw new NotImplementedException();
        }
    }

    public sealed class MetadataBitReader
    {
        private ArchiveMetadataReader mReader;
        private BitVector mVector;
        private int mIndex;

        internal MetadataBitReader(ArchiveMetadataReader reader, BitVector vector)
        {
            mReader = reader;
            mVector = vector;
        }

        internal void Complete()
        {
            while (mIndex < mVector.Count)
                ReadBit();

            mReader = null;
        }

        public bool ReadBit()
        {
            if (mReader == null)
                throw new ObjectDisposedException(null);

            if (mIndex == mVector.Count)
                throw new InvalidOperationException();

            return mVector[mIndex++];
        }
    }
}
