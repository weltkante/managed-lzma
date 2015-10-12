using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip.Encoders
{
    public sealed class LzmaArchiveEncoderSettings : ArchiveEncoderSettings
    {
        private readonly LZMA.EncoderSettings mSettings;

        internal override CompressionMethod GetDecoderType() => CompressionMethod.LZMA;

        public LzmaArchiveEncoderSettings(LZMA.EncoderSettings settings)
        {
            mSettings = settings;
        }
    }
}
