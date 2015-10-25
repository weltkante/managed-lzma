using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip.Encoders
{
    public sealed class Lzma2EncoderSettings : EncoderSettings
    {
        private readonly LZMA2.EncoderSettings mSettings;

        internal override CompressionMethod GetDecoderType() => CompressionMethod.LZMA2;

        public Lzma2EncoderSettings(LZMA2.EncoderSettings settings)
        {
            mSettings = settings;
        }

        internal override EncoderNode CreateEncoder()
        {
            return new Lzma2EncoderNode(mSettings);
        }
    }

    internal sealed class Lzma2EncoderNode : EncoderNode
    {
        private readonly LZMA2.EncoderSettings mSettings;

        public Lzma2EncoderNode(LZMA2.EncoderSettings settings)
        {
            mSettings = settings;
        }

        public override IStreamWriter GetInputSink(int index)
        {
            throw new NotImplementedException();
        }

        public override void SetInputSource(int index, IStreamReader stream)
        {
            throw new NotImplementedException();
        }

        public override IStreamReader GetOutputSource(int index)
        {
            throw new NotImplementedException();
        }

        public override void SetOutputSink(int index, IStreamWriter stream)
        {
            throw new NotImplementedException();
        }

        public override void Start()
        {
            throw new NotImplementedException();
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
