using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.LZMA2
{
    public sealed class EncoderSettings
    {
        private LZMA.EncoderSettings mBaseSettings;
        private long mBlockSize;
        private int mBlockThreadCount;
        private int mTotalThreadCount;
    }
}
