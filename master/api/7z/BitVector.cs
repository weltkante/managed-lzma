using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    internal struct BitVector
    {
        public bool this[int index] { get { throw new NotImplementedException(); } }
        public int CountSetBits() { throw new NotImplementedException(); }
    }

    internal struct ChecksumVector
    {
        public ChecksumVector(BitVector bits, ImmutableArray<Checksum> crcs)
        {
        }
    }
}
