using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip.Encoders
{
    public sealed class AesEncoderSettings : EncoderSettings
    {
        private Lazy<string> mPassword;

        public AesEncoderSettings(Lazy<string> password)
        {
            mPassword = password;
        }

        internal override CompressionMethod GetDecoderType()
        {
            return CompressionMethod.AES;
        }

        internal override EncoderNode CreateEncoder()
        {
            return new AesEncoderNode(mPassword);
        }
    }

    internal sealed class AesEncoderNode : EncoderNode
    {
        private Lazy<string> mPassword;

        public AesEncoderNode(Lazy<string> password)
        {
            mPassword = password;
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
