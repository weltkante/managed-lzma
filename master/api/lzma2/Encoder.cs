using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.LZMA2
{
    public sealed class Encoder
    {
        private readonly EncoderSettings mSettings;

        public Encoder(EncoderSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            mSettings = settings;

            throw new NotImplementedException();
        }
    }
}
