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

    internal sealed class ArchiveSectionMetadataBuilder
    {
        public DecoderMetadataBuilder[] Decoders;
        public int RequiredRawInputStreamCount;
        public DecoderInputMetadata OutputStream;
        public long OutputLength;
        public Checksum? OutputChecksum;
        public int? SubStreamCount;
        public DecodedStreamMetadata[] Subsections;
    }

    internal sealed class DecoderMetadataBuilder
    {
        public CompressionMethod Method;
        public int InputCount;
        public int OutputCount;
        public ImmutableArray<byte> Settings;
        public ImmutableArray<DecoderInputMetadata>.Builder InputInfo;
        public ImmutableArray<DecoderOutputMetadata>.Builder OutputInfo;
    }
}
