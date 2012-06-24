using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.LZMA.Master.SevenZip
{
    class Archive
    {
    }

    class FolderInfo
    {
        public uint? UnpackCRC;
        public ulong[] UnpackSizes;
        public CoderInfo[] Coders;
        public BindPair[] BindPairs;
        public int[] PackStreams;

        public int FindBindPairForInStream(int inIndex)
        {
            for(int i = 0; i < BindPairs.Length; i++)
                if(BindPairs[i].InIndex == inIndex)
                    return i;

            return -1;
        }
    }

    class CoderInfo
    {
        public ulong MethodId;
        public int NumInStreams;
        public int NumOutStreams;
        public byte[] Props;
    }

    struct BindPair
    {
        public readonly int InIndex;
        public readonly int OutIndex;

        public BindPair(int inIndex, int outIndex)
        {
            this.InIndex = inIndex;
            this.OutIndex = outIndex;
        }
    }
}
