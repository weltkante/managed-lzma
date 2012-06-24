using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.LZMA.Master.SevenZip
{
    public class ArchiveReader: IDisposable
    {
        #region Constants

        private static readonly byte[] kSignature = { (byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C };
        private const int kPrimaryHeaderSize = 0x20;

        #endregion

        #region Variables

        private IDecoderProvider mProvider;
        private Stream mStream;

        private byte mMajorVersion;
        private byte mMinorVersion;
        private bool mCheckCRC = true;

        #endregion

        public ArchiveReader(string filename)
            : this(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)) { }

        public ArchiveReader(Stream stream)
        {
            mStream = stream;
            ReadHeader(new DataReader(stream));
        }

        public void Dispose()
        {
            mStream.Dispose();
        }

        private BlockType? ReadBlockType(DataReader mReader)
        {
            ulong value = mReader.ReadPackedUInt64();
            if(value > 25)
                return null;
            else
                return (BlockType)value;
        }

        private void ReadHeader(DataReader mReader)
        {
            byte[] signature = mReader.ReadBytes(kSignature.Length);
            if(signature.Length != kSignature.Length)
                throw new InvalidDataException();
            for(int i = 0; i < signature.Length; i++)
                if(signature[i] != kSignature[i])
                    throw new InvalidDataException();

            mMajorVersion = mReader.ReadByte();
            mMinorVersion = mReader.ReadByte();

            if(mMajorVersion != 0)
                throw new NotSupportedException();

            // TODO: Weak check of supported minor versions

            uint startHeaderCRC = mReader.ReadUInt32();
            long nextHeaderOffset = mReader.ReadInt64();
            long nextHeaderLength = mReader.ReadInt64();
            uint nextHeaderCRC = mReader.ReadUInt32();

            Utils.Assert(mReader.Position == kPrimaryHeaderSize);

            if(mCheckCRC)
            {
                uint crc = CRC.kInitCRC;
                crc = CRC.Update(crc, nextHeaderOffset);
                crc = CRC.Update(crc, nextHeaderLength);
                crc = CRC.Update(crc, nextHeaderCRC);
                crc = CRC.Finish(crc);
                if(crc != startHeaderCRC)
                    throw new InvalidDataException();
            }

            if(nextHeaderLength < 0 || nextHeaderOffset < 0
                || nextHeaderOffset > mReader.Remaining - nextHeaderLength)
                throw new InvalidDataException();

            if(nextHeaderLength > 0) // zero is ok, empty archive
            {
                mReader.Skip(nextHeaderOffset);

                if(mCheckCRC)
                {
                    if(mReader.CalculateCRC(nextHeaderLength) != nextHeaderCRC)
                        throw new InvalidDataException();

                    mReader.Seek(kPrimaryHeaderSize + nextHeaderOffset);
                }

                using(mReader.Constrain(nextHeaderLength))
                    ReadHeader2(mReader);
            }
        }

        private void ReadHeader2(DataReader mReader)
        {
            var type = ReadBlockType(mReader);

            if(type == BlockType.EncodedHeader)
            {
                // TODO: we want to do sanity checking of any offsets read from the header against the original stream, not the decoded header

                Stream[] streams;
                ReadAndDecodePackedStreams(mReader, out streams);

                // packing an empty header is odd but ok
                if(streams.Length == 0)
                    return; // TODO: record a warning or info message

                // header must be stored in a single stream
                if(streams.Length != 1)
                    throw new InvalidDataException();

                // switch reader to use decoded header
                mReader = new DataReader(streams[0]);
                type = ReadBlockType(mReader);
            }

            if(type != BlockType.Header)
                throw new InvalidDataException();

            type = ReadBlockType(mReader);

            if(type == BlockType.ArchiveProperties)
            {
                ReadArchiveProperties(mReader);
                type = ReadBlockType(mReader);
            }

            if(type == BlockType.AdditionalStreamsInfo)
            {
                ReadAdditionalStreamsInfo(mReader);
                type = ReadBlockType(mReader);
            }

            if(type == BlockType.MainStreamsInfo)
            {
                ReadStreamsInfo(mReader);
                type = ReadBlockType(mReader);
            }

            if(type == BlockType.FilesInfo)
            {
                int numFiles = mReader.ReadPackedUInt31();

                for(; ; )
                {
                    type = ReadBlockType(mReader);
                    if(type == BlockType.End)
                        break;

                    ulong size = mReader.ReadPackedUInt64();
                    if(size > (ulong)mReader.Remaining)
                        throw new InvalidDataException();

                    switch(type)
                    {
                    case BlockType.Name:
                        break;
                    case BlockType.WinAttributes:
                        break;
                    case BlockType.EmptyStream:
                        break;
                    case BlockType.EmptyFile:
                        break;
                    case BlockType.Anti:
                        break;
                    case BlockType.StartPos:
                        break;
                    case BlockType.CTime:
                        break;
                    case BlockType.ATime:
                        break;
                    case BlockType.MTime:
                        break;
                    case BlockType.Dummy:
                        break;
                    default:
                        mReader.Skip(checked((long)size));
                        break;
                    }
                }
            }

            if(type != BlockType.End)
                throw new InvalidDataException();

            // TODO: weak check that we actually are at the end of the header - no exception, just a warning
            if(mReader.Remaining != 0)
                System.Diagnostics.Debugger.Break();
        }

        private void ReadArchiveProperties(DataReader mReader)
        {
            while(ReadBlockType(mReader) != BlockType.End)
                SkipAttribute(mReader);
        }

        private void ReadAdditionalStreamsInfo(DataReader mReader)
        {
            throw new NotSupportedException();
        }

        private void ReadStreamsInfo(DataReader mReader, out ulong[] packSizes, out BitVector packCRCsDefined, out uint[] packCRCs, out FolderInfo[] folders,
            out int[] numUnpackStreamsInFolders, out List<ulong> unpackSizes, out BitVector crcDefined, out uint[] crcs)
        {
            packSizes = null;
            packCRCsDefined = null;
            packCRCs = null;
            folders = null;
            numUnpackStreamsInFolders = null;
            unpackSizes = null;
            crcDefined = null;
            crcs = null;
            for(; ; )
            {
                var type = ReadBlockType(mReader);
                switch(type)
                {
                case BlockType.End:
                    return;
                case BlockType.PackInfo:
                    ReadPackInfo(mReader, out packSizes, out packCRCsDefined, out packCRCs);
                    break;
                case BlockType.UnpackInfo:
                    ReadUnpackInfo(mReader, out folders);
                    break;
                case BlockType.SubStreamsInfo:
                    ReadSubStreamsInfo(mReader, folders, out numUnpackStreamsInFolders, out unpackSizes, out crcDefined, out crcs);
                    break;
                default:
                    throw new InvalidDataException();
                }
            }
        }

        private void ReadPackInfo(DataReader mReader, out ulong[] packSizes, out BitVector packCRCsDefined, out uint[] packCRCs)
        {
            packCRCsDefined = null;
            packCRCs = null;

            ulong dataOffset = mReader.ReadPackedUInt64();
            int numPackStreams = mReader.ReadPackedUInt31();

            WaitAttribute(mReader, BlockType.Size);
            {
                packSizes = new ulong[numPackStreams];
                for(int i = 0; i < numPackStreams; i++)
                    packSizes[i] = mReader.ReadPackedUInt64();
            }

            for(; ; )
            {
                var type = ReadBlockType(mReader);
                if(type == BlockType.End)
                    break;

                if(type == BlockType.CRC)
                    ReadHashDigests(mReader, numPackStreams, out packCRCsDefined, out packCRCs);
                else
                    SkipAttribute(mReader);
            }

            if(packCRCsDefined == null)
            {
                packCRCsDefined = new BitVector(numPackStreams);
                packCRCs = new uint[numPackStreams];
            }
        }

        private FolderInfo GetNextFolderItem(DataReader mReader)
        {
            FolderInfo folder = new FolderInfo();
            int numInStreams = 0;
            int numOutStreams = 0;
            int numCoders = mReader.ReadPackedUInt31();
            folder.Coders = new CoderInfo[numCoders];
            for(int i = 0; i < numCoders; i++)
            {
                CoderInfo coder = new CoderInfo();
                folder.Coders[i] = coder;

                byte mainByte = mReader.ReadByte();
                
                int idSize = mainByte & 15;
                if(idSize > 8) // standard 7z also is limited to 8 byte coder ids
                    throw new InvalidDataException();

                ulong id = 0;
                for(int j = idSize - 1; j >= 0; j--)
                    id += (ulong)mReader.ReadByte() << (j * 8);

                coder.MethodId = id;

                if((mainByte & 0x10) != 0)
                {
                    coder.NumInStreams = mReader.ReadPackedUInt31();
                    coder.NumOutStreams = mReader.ReadPackedUInt31();
                }
                else
                {
                    coder.NumInStreams = 1;
                    coder.NumOutStreams = 1;
                }

                if((mainByte & 0x20) != 0)
                {
                    int propsSize = mReader.ReadPackedUInt31();
                    coder.Props = mReader.ReadBytes(propsSize);
                }

                // TODO: Warn if 0x40 is set, since it is not defined in the supported versions of the format.

                if((mainByte & 0x80) != 0)
                    throw new InvalidDataException();

                numInStreams += coder.NumInStreams;
                numOutStreams += coder.NumOutStreams;
            }

            int numBindPairs = mReader.ReadPackedUInt31();
            folder.BindPairs = new BindPair[numBindPairs];
            for(int i = 0; i < numBindPairs; i++)
            {
                int inIndex = mReader.ReadPackedUInt31();
                int outIndex = mReader.ReadPackedUInt31();
                folder.BindPairs[i] = new BindPair(inIndex, outIndex);
            }

            if(numInStreams < numBindPairs)
                throw new InvalidDataException();

            int numPackStreams = numInStreams - numBindPairs;
            folder.PackStreams = new int[numPackStreams];
            if(numPackStreams == 1)
            {
                bool found = false;
                for(int i = 0; i < numInStreams; i++)
                {
                    if(folder.FindBindPairForInStream(i) < 0)
                    {
                        folder.PackStreams[0] = i;
                        found = true;
                        break;
                    }
                }
                if(!found)
                    throw new InvalidDataException();
            }
            else
            {
                for(int i = 0; i < numPackStreams; i++)
                    folder.PackStreams[i] = mReader.ReadPackedUInt31();
            }

            return folder;
        }

        private void ReadUnpackInfo(DataReader mReader, out FolderInfo[] folders)
        {
            WaitAttribute(mReader, BlockType.Folder);
            {
                int numFolders = mReader.ReadPackedUInt31();
                folders = new FolderInfo[numFolders];
                for(int i = 0; i < folders.Length; i++)
                    folders[i] = GetNextFolderItem(mReader);
            }

            WaitAttribute(mReader, BlockType.CodersUnpackSize);
            {
                for(int i = 0; i < folders.Length; i++)
                {
                    var folder = folders[i];
                    folder.UnpackSizes = new ulong[folder.GetNumOutStreams()];
                    for(int j = 0; j < folder.UnpackSizes.Length; j++)
                        folder.UnpackSizes[j] = mReader.ReadPackedUInt64();
                }
            }

            for(; ; )
            {
                var type = ReadBlockType(mReader);
                if(type == BlockType.End)
                    break;

                if(type == BlockType.CRC)
                {
                    BitVector defined;
                    uint[] crcs;
                    ReadHashDigests(mReader, folders.Length, out defined, out crcs);
                    for(int i = 0; i < folders.Length; i++)
                        if(defined[i])
                            folders[i].UnpackCRC = crcs[i];
                    continue;
                }

                SkipAttribute(mReader);
            }
        }

        private void ReadSubStreamsInfo(DataReader mReader, FolderInfo[] folders, out int[] numUnpackStreamsInFolders, out List<ulong> unpackSizes, out BitVector crcDefined, out uint[] crcs)
        {
            numUnpackStreamsInFolders = new int[folders.Length];
            for(int i = 0; i < numUnpackStreamsInFolders.Length; i++)
                numUnpackStreamsInFolders[i] = 1;

            BlockType? type;
            for(; ; )
            {
                type = ReadBlockType(mReader);

                if(type == BlockType.NumUnpackStream)
                {
                    for(int i = 0; i < numUnpackStreamsInFolders.Length; i++)
                        numUnpackStreamsInFolders[i] = mReader.ReadPackedUInt31();
                    continue;
                }

                if(type == BlockType.End || type == BlockType.CRC || type == BlockType.Size)
                    break;

                SkipAttribute(mReader);
            }

            for(int i = 0; i < numUnpackStreamsInFolders.Length; i++)
            {
                int numSubstreams = numUnpackStreamsInFolders[i];
                if(numSubstreams == 0)
                    continue;

                ulong sum = 0;
                if(type == BlockType.Size)
                {
                    for(int j = 1; j < numSubstreams; j++)
                    {
                        ulong size = mReader.ReadPackedUInt64();
                        unpackSizes.Add(size);
                        sum += size;
                    }
                }

                unpackSizes.Add(folders[i].GetUnpackSize() - sum);
            }

            if(type == BlockType.Size)
                type = ReadBlockType(mReader);

            int numCRCs = 0;
            int numCRCsTotal = 0;
            for(int i = 0; i < folders.Length; i++)
            {
                int numSubstreams = numUnpackStreamsInFolders[i];
                if(numSubstreams != 1 || folders[i].UnpackCRC == null)
                    numCRCs += numSubstreams;
                numCRCsTotal += numSubstreams;
            }

            for(; ; )
            {
                if(type == BlockType.CRC)
                {
                    BitVector defined2;
                    uint[] crcs2;
                    ReadHashDigests(mReader, numCRCs, out defined2, out crcs2);

                }
                else if(type == BlockType.End)
                    break;
                else
                    SkipAttribute(mReader);

                type = ReadBlockType(mReader);
            }

            if(crcDefined == null)
            {
                throw new NotImplementedException();
            }
        }

        private void ReadHashDigests(DataReader mReader, int count, out BitVector defined, out uint[] digests)
        {
            defined = mReader.ReadBitVector2(count);
            digests = new uint[count];
            for(int i = 0; i < digests.Length; i++)
                if(defined[i])
                    digests[i] = mReader.ReadUInt32();
        }

        private void WaitAttribute(DataReader mReader, BlockType type)
        {
            for(; ; )
            {
                var next = ReadBlockType(mReader);
                if(next == type)
                    return;

                if(next == BlockType.End)
                    throw new InvalidDataException();

                SkipAttribute(mReader);
            }
        }

        private void SkipAttribute(DataReader mReader)
        {
            ulong size = mReader.ReadPackedUInt64();
            if(size > (ulong)mReader.Remaining)
                throw new InvalidDataException();

            mReader.Skip((long)size);
        }

        private void ReadAndDecodePackedStreams(DataReader mReader, out Stream[] streams)
        {
            FolderInfo[] folders;
            ReadStreamsInfo(mReader);

            int packIndex = 0;
            for(int i = 0; i < folders.Length; i++)
            {
                var folder = folders[i];

            }
        }
    }
}
