using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    /// <summary>
    /// Describes a compression method as defined in the 7z file format.
    /// </summary>
    /// <remarks>
    /// These methods are defined by the 7z file format and not by the application.
    /// You cannot add new compression methods here, nobody will understand them.
    /// </remarks>
    public struct CompressionMethod
    {
        private static readonly ImmutableArray<byte> kCopy;
        private static readonly ImmutableArray<byte> kDelta;
        private static readonly ImmutableArray<byte> kLZMA2;
        private static readonly ImmutableArray<byte> kLZMA;
        private static readonly ImmutableArray<byte> kPPMD;
        private static readonly ImmutableArray<byte> kBCJ;
        private static readonly ImmutableArray<byte> kBCJ2;
        private static readonly ImmutableArray<byte> kDeflate;
        private static readonly ImmutableArray<byte> kBZip2;

        public static CompressionMethod Copy => new CompressionMethod(kCopy);
        public static CompressionMethod Delta => new CompressionMethod(kDelta);
        public static CompressionMethod LZMA2 => new CompressionMethod(kLZMA2);
        public static CompressionMethod LZMA => new CompressionMethod(kLZMA);
        public static CompressionMethod PPMD => new CompressionMethod(kPPMD);
        public static CompressionMethod BCJ => new CompressionMethod(kBCJ);
        public static CompressionMethod BCJ2 => new CompressionMethod(kBCJ2);
        public static CompressionMethod Deflate => new CompressionMethod(kDeflate);
        public static CompressionMethod BZip2 => new CompressionMethod(kBZip2);

        private readonly ImmutableArray<byte> mSignature;

        private CompressionMethod(ImmutableArray<byte> signature)
        {
            mSignature = signature;
        }

        public bool IsUndefined => mSignature.IsDefault;
    }
}
