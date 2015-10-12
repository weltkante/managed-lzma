using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip.Encoders
{
    public sealed class CopyArchiveEncoderSettings : ArchiveEncoderSettings
    {
        public static readonly CopyArchiveEncoderSettings Instance = new CopyArchiveEncoderSettings();

        internal override CompressionMethod GetDecoderType() => CompressionMethod.Copy;

        private CopyArchiveEncoderSettings() { }
    }
}
