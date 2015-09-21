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
            // TODO: skip remaining data and clean up fields in case someone retains a reference to this instance
            mReader = null;
        }
    }

    public sealed class MetadataDateReader
    {
        internal MetadataDateReader(ArchiveMetadataReader reader, int count, BitVector defined)
        {
            throw new NotImplementedException();
        }

        internal void Complete()
        {
            throw new NotImplementedException();
        }
    }

    public sealed class MetadataNumberReader
    {
        internal MetadataNumberReader(ArchiveMetadataReader reader, int count, BitVector defined)
        {
            throw new NotImplementedException();
        }

        internal void Complete()
        {
            throw new NotImplementedException();
        }
    }

    public sealed class MetadataAttributeReader
    {
        internal MetadataAttributeReader(ArchiveMetadataReader reader, int count, BitVector defined)
        {
            throw new NotImplementedException();
        }

        internal void Complete()
        {
            throw new NotImplementedException();
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
        internal MetadataBitReader(ArchiveMetadataReader reader, BitVector bits)
        {
            throw new NotImplementedException();
        }

        internal MetadataBitReader(ArchiveMetadataReader reader, int count)
        {
            throw new NotImplementedException();
        }

        internal void Complete()
        {
            throw new NotImplementedException();
        }
    }
}
