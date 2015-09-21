using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    internal sealed class ArchiveMetadataFileFormat
    {
    }

    internal sealed class DecoderFileFormat
    {
        public Checksum? Checksum;
        public int OutputCount;
        public ImmutableArray<DecoderOutputMetadata>.Builder OutputInfo;
    }
}
