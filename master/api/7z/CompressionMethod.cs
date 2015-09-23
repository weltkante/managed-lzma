using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
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
    public struct CompressionMethod : IEquatable<CompressionMethod>
    {
        private const int kCopy = 0x00;
        private const int kDelta = 0x03;
        private const int kLZMA2 = 0x21;
        private const int kLZMA = 0x030101;
        private const int kPPMD = 0x030401;
        private const int kBCJ = 0x03030103;
        private const int kBCJ2 = 0x0303011B;
        private const int kDeflate = 0x040108;
        private const int kBZip2 = 0x040202;
        private const int kAES = 0x06F10701;

        public static CompressionMethod Undefined => default(CompressionMethod);
        public static CompressionMethod Copy => new CompressionMethod(kCopy);
        public static CompressionMethod Delta => new CompressionMethod(kDelta);
        public static CompressionMethod LZMA2 => new CompressionMethod(kLZMA2);
        public static CompressionMethod LZMA => new CompressionMethod(kLZMA);
        public static CompressionMethod PPMD => new CompressionMethod(kPPMD);
        public static CompressionMethod BCJ => new CompressionMethod(kBCJ);
        public static CompressionMethod BCJ2 => new CompressionMethod(kBCJ2);
        public static CompressionMethod Deflate => new CompressionMethod(kDeflate);
        public static CompressionMethod BZip2 => new CompressionMethod(kBZip2);
        public static CompressionMethod AES => new CompressionMethod(kAES);

        #region Internal Methods

        internal static CompressionMethod TryDecode(int value)
        {
            switch (value)
            {
                case kCopy:
                case kDelta:
                case kLZMA2:
                case kLZMA:
                case kPPMD:
                case kBCJ:
                case kBCJ2:
                case kDeflate:
                case kBZip2:
                case kAES:
                    return new CompressionMethod(value);

                default:
                    return Undefined;
            }
        }

        internal void CheckInputOutputCount(int inputCount, int outputCount)
        {
            switch (~mSignature)
            {
                case kCopy:
                case kDeflate:
                case kLZMA:
                case kLZMA2:
                case kAES:
                    if (inputCount != 1)
                        throw new InvalidDataException();

                    if (outputCount != 1)
                        throw new InvalidDataException();

                    break;

                case kDelta:
                case kPPMD:
                case kBCJ:
                case kBCJ2:
                case kBZip2:
                    throw new NotImplementedException();

                default:
                    throw new InvalidDataException();
            }
        }

        #endregion

        private int mSignature;

        private CompressionMethod(int signature)
        {
            mSignature = ~signature;
        }

        public bool IsUndefined => mSignature == 0;

        public override string ToString()
        {
            switch (~mSignature)
            {
                case kCopy: return nameof(Copy);
                case kDelta: return nameof(Delta);
                case kLZMA2: return nameof(LZMA2);
                case kLZMA: return nameof(LZMA);
                case kPPMD: return nameof(PPMD);
                case kBCJ: return nameof(BCJ);
                case kBCJ2: return nameof(BCJ2);
                case kDeflate: return nameof(Deflate);
                case kBZip2: return nameof(BZip2);
                case kAES: return nameof(AES);
                default: return nameof(Undefined);
            }
        }

        public override int GetHashCode()
        {
            return mSignature;
        }

        public override bool Equals(object obj)
        {
            return obj is CompressionMethod && ((CompressionMethod)obj).mSignature == mSignature;
        }

        public bool Equals(CompressionMethod other)
        {
            return mSignature == other.mSignature;
        }

        public static bool operator ==(CompressionMethod left, CompressionMethod right)
        {
            return left.mSignature == right.mSignature;
        }

        public static bool operator !=(CompressionMethod left, CompressionMethod right)
        {
            return left.mSignature != right.mSignature;
        }
    }
}
