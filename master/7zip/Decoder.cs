using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.LZMA.Master.SevenZip
{
    public sealed class DecoderId
    {
    }

    public interface IDecoderProvider
    {
        Stream GetDecoder(Stream data, DecoderId id, byte[] info);
    }

    public class BuiltinDecoderProvider: IDecoderProvider
    {
        public virtual Stream GetDecoder(Stream data, DecoderId id, byte[] info)
        {
            return new Lzma2DecoderStream(data, info[0]);
        }
    }
}
