using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ManagedLzma.LZMA
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
