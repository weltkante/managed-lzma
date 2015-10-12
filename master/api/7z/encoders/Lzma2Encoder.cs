using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip.Encoders
{
    public sealed class Lzma2ArchiveEncoderSettings : ArchiveEncoderSettings
    {
        private readonly LZMA2.EncoderSettings mSettings;

        internal override CompressionMethod GetDecoderType() => CompressionMethod.LZMA2;

        public Lzma2ArchiveEncoderSettings(LZMA2.EncoderSettings settings)
        {
            mSettings = settings;
        }
    }
}
