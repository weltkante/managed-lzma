using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// TODO: Replace BinaryWriter by a custom one which is always little-endian.

// Design:
// - constructor writes header for empty 7z archive
// - call methods to add content to the archive
//   this will also build up header information in memory
// - call WriteFinalHeader to output the secondary header and update the primary header
//
// In any case the archive will be valid, but unless you call WriteFinalHeader
// it will just be an empty archive possibly followed by unreachable content.
//

namespace ManagedLzma.LZMA.Master.SevenZip
{
    // TODO: I don't like the StreamBasedArchiveWriterEntry, too much danger with the stream management.
    // suggested steps for resoultion:
    // - turn the interface into an abstract base class and make the Open() method internal-protected
    // - we need to ensure that Open() is called only once because the origin stream may not be seekable
    // - no need to make a proxy which prevents close on the stream beacause of the previous point

    public interface IArchiveWriterEntry
    {
        string Name { get; }
        FileAttributes? Attributes { get; }
        DateTime? CreationTime { get; }
        DateTime? LastWriteTime { get; }
        DateTime? LastAccessTime { get; }
    }

    internal static class FileNameHelper
    {
        public static string CalculateName(DirectoryInfo root, DirectoryInfo folder)
        {
            if(root.Root.FullName != folder.Root.FullName)
                throw new InvalidOperationException("Unrelated directories.");

            Stack<DirectoryInfo> rootList = new Stack<DirectoryInfo>();
            while(root.FullName != root.Root.FullName)
            {
                rootList.Push(root);
                root = root.Parent;
            }

            Stack<DirectoryInfo> itemList = new Stack<DirectoryInfo>();
            while(folder.FullName != folder.Root.FullName)
            {
                itemList.Push(folder);
                folder = folder.Parent;
            }

            while(rootList.Count != 0 && itemList.Count != 0 && rootList.Peek().Name == itemList.Peek().Name)
            {
                rootList.Pop();
                itemList.Pop();
            }

            if(rootList.Count != 0)
                throw new InvalidOperationException("Item is not contained in root.");

            if(itemList.Count == 0)
                return null;

            return Compat.String.Join("/", itemList.Select(item => item.Name));
        }

        public static string CalculateName(DirectoryInfo root, FileInfo file)
        {
            string path = CalculateName(root, file.Directory);
            return String.IsNullOrEmpty(path) ? file.Name : path + "/" + file.Name;
        }
    }

    internal sealed class FileBasedArchiveWriterEntry: IArchiveWriterEntry
    {
        private FileInfo mFile;
        private string mName;

        public FileBasedArchiveWriterEntry(DirectoryInfo root, FileInfo file)
        {
            mFile = file;
            mName = FileNameHelper.CalculateName(root, file);
        }

        public string Name
        {
            get { return mName; }
        }

        public FileAttributes? Attributes
        {
            get { return mFile.Attributes; }
        }

        public DateTime? CreationTime
        {
            get { return mFile.CreationTimeUtc; }
        }

        public DateTime? LastWriteTime
        {
            get { return mFile.LastWriteTimeUtc; }
        }

        public DateTime? LastAccessTime
        {
            get { return mFile.LastAccessTimeUtc; }
        }
    }

    public class ArchiveWriter
    {
        #region Configuration Elements

        private abstract class StreamRef
        {
            public abstract long GetSize(FileSet fileset);
            public abstract uint? GetHash(FileSet fileset);
        }

        private class InputStreamRef: StreamRef
        {
            public int PackedStreamIndex;

            public override long GetSize(FileSet fileset)
            {
                return fileset.InputStreams[PackedStreamIndex].Size;
            }

            public override uint? GetHash(FileSet fileset)
            {
                return fileset.InputStreams[PackedStreamIndex].Hash;
            }
        }

        private class CoderStreamRef: StreamRef
        {
            public int CoderIndex;
            public int StreamIndex;

            public override long GetSize(FileSet fileset)
            {
                return fileset.Coders[CoderIndex].OutputStreams[StreamIndex].Size;
            }

            public override uint? GetHash(FileSet fileset)
            {
                return fileset.Coders[CoderIndex].OutputStreams[StreamIndex].Hash;
            }
        }

        private class Coder
        {
            public master._7zip.Legacy.CMethodId MethodId;
            public byte[] Settings;
            public StreamRef[] InputStreams;
            public CoderStream[] OutputStreams;
        }

        private class CoderStream
        {
            public long Size;
            public uint? Hash;
        }

        private class InputStream
        {
            public long Size;
            public uint? Hash;
        }

        private class FileSet
        {
            public InputStream[] InputStreams;
            public Coder[] Coders;
            public StreamRef DataStream;
            public FileEntry[] Files;
        }

        private class FileEntry
        {
            public string Name;
            public uint? Flags;
            public DateTime? CTime;
            public DateTime? MTime;
            public DateTime? ATime;
            public long Size;
            public uint Hash;
        }

        #endregion

        #region Configuration Implementations

        private abstract class EncoderConfig
        {
            private static DateTime? EnsureUTC(DateTime? value)
            {
                if(value.HasValue)
                    return value.Value.ToUniversalTime();
                else
                    return null;
            }

            private long mOrigin;
            private long mProcessed;
            private Stream mTargetStream;
            private master._7zip.Legacy.CrcBuilderStream mCurrentStream;
            private List<FileEntry> mFiles;
            private FileEntry mCurrentFile;

            private void FinishCurrentFile()
            {
                if(mTargetStream == null)
                    throw new ObjectDisposedException(null);

                if(mCurrentFile == null)
                    return;

                mCurrentFile.Hash = mCurrentStream.Finish();
                mCurrentFile.Size = mCurrentStream.Processed;
                mProcessed += mCurrentFile.Size;

                mCurrentStream.Close();
                mCurrentStream = null;

                if(mFiles == null)
                    mFiles = new List<FileEntry>();

                mFiles.Add(mCurrentFile);
                mCurrentFile = null;
            }

            protected EncoderConfig(Stream targetStream)
            {
                mTargetStream = targetStream;
                mOrigin = targetStream.Position;
            }

            public Stream BeginWriteFile(IArchiveWriterEntry file)
            {
                FinishCurrentFile();

                if(file == null)
                    throw new ArgumentNullException("file");

                mCurrentFile = new FileEntry {
                    Name = file.Name,
                    CTime = EnsureUTC(file.CreationTime),
                    MTime = EnsureUTC(file.LastWriteTime),
                    ATime = EnsureUTC(file.LastAccessTime),
                };

                var attributes = file.Attributes;
                if(attributes.HasValue)
                    mCurrentFile.Flags = (uint)attributes.Value;

                Stream writerStream = GetNextWriterStream(mTargetStream);
                mCurrentStream = new master._7zip.Legacy.CrcBuilderStream(writerStream);
                return mCurrentStream;
            }

            public FileSet Finish()
            {
                FinishCurrentFile();

                var fileset = FinishFileSet(mTargetStream, mFiles.ToArray(), mProcessed, mOrigin);

                mTargetStream = null;
                mFiles = null;

                return fileset;
            }

            protected abstract Stream GetNextWriterStream(Stream stream);
            protected abstract FileSet FinishFileSet(Stream stream, FileEntry[] entries, long processed, long origin);
        }

        private abstract class EncoderStream: Stream
        {
            public sealed override bool CanRead
            {
                get { return false; }
            }

            public sealed override bool CanSeek
            {
                get { return false; }
            }

            public sealed override bool CanWrite
            {
                get { return true; }
            }

            public sealed override long Length
            {
                get { throw new NotSupportedException(); }
            }

            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public sealed override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public sealed override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public sealed override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Flush() { }
            public abstract override void Write(byte[] buffer, int offset, int count);
        }

        private sealed class ForwardingEncoderStream: EncoderStream
        {
            private Stream mTargetStream;

            public ForwardingEncoderStream(Stream targetStream)
            {
                if(targetStream == null)
                    throw new ArgumentNullException("targetStream");

                mTargetStream = targetStream;
            }

            protected override void Dispose(bool disposing)
            {
                mTargetStream = null;
                base.Dispose(disposing);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if(mTargetStream == null)
                    throw new ObjectDisposedException(null);

                mTargetStream.Write(buffer, offset, count);
            }
        }

        private sealed class PlainEncoderConfig: EncoderConfig
        {
            internal PlainEncoderConfig(Stream target)
                : base(target) { }

            protected override Stream GetNextWriterStream(Stream stream)
            {
                return new ForwardingEncoderStream(stream);
            }

            protected override FileSet FinishFileSet(Stream stream, FileEntry[] entries, long processed, long origin)
            {
                if(entries == null || entries.Length == 0)
                    return null;

                return new FileSet {
                    Files = entries,
                    DataStream = new InputStreamRef { PackedStreamIndex = 0 },
                    InputStreams = new[] { new InputStream { Size = stream.Position - origin } },
                    Coders = new[] { new Coder {
                        MethodId = master._7zip.Legacy.CMethodId.kCopy,
                        Settings = null,
                        InputStreams = new[] { new InputStreamRef { PackedStreamIndex = 0 } },
                        OutputStreams = new[] { new CoderStream { Size = processed } },
                    } },
                };
            }
        }

        private sealed class LzmaEncoderConfig: EncoderConfig
        {
            private MemoryStream mBuffer;
            private byte[] mSettings;

            private void EncodeBufferedData(Stream stream)
            {
                var settings = LZMA.CLzmaEncProps.LzmaEncProps_Init();
                var encoder = LZMA.LzmaEnc_Create(LZMA.ISzAlloc.SmallAlloc);
                var res = encoder.LzmaEnc_SetProps(settings);
                if(res != LZMA.SZ_OK)
                    throw new InvalidOperationException();

                mSettings = new byte[LZMA.LZMA_PROPS_SIZE];
                long binarySettingsSize = LZMA.LZMA_PROPS_SIZE;
                res = encoder.LzmaEnc_WriteProperties(mSettings, ref binarySettingsSize);
                if(res != LZMA.SZ_OK)
                    throw new InvalidOperationException();
                if(binarySettingsSize != LZMA.LZMA_PROPS_SIZE)
                    throw new NotSupportedException();

                var outBuffer = new LZMA.CSeqOutStream(delegate(P<byte> buf, long sz) {
                    stream.Write(buf.mBuffer, buf.mOffset, checked((int)sz));
                });

                var inBuffer = new LZMA.CSeqInStream(delegate(P<byte> buf, long size) {
                    return mBuffer.Read(buf.mBuffer, buf.mOffset, checked((int)size));
                });

                res = encoder.LzmaEnc_Encode(outBuffer, inBuffer, null, LZMA.ISzAlloc.SmallAlloc, LZMA.ISzAlloc.BigAlloc);
                if(res != LZMA.SZ_OK)
                    throw new InvalidOperationException();

                encoder.LzmaEnc_Destroy(LZMA.ISzAlloc.SmallAlloc, LZMA.ISzAlloc.BigAlloc);
            }

            internal LzmaEncoderConfig(Stream stream)
                : base(stream)
            {
                mBuffer = new MemoryStream();
            }

            protected override Stream GetNextWriterStream(Stream stream)
            {
                return new ForwardingEncoderStream(mBuffer);
            }

            protected override FileSet FinishFileSet(Stream stream, FileEntry[] entries, long processed, long origin)
            {
                if(entries == null || entries.Length == 0)
                    return null;

                mBuffer.Position = 0;
                EncodeBufferedData(stream);
                mBuffer = null;

                return new FileSet {
                    Files = entries,
                    DataStream = new CoderStreamRef { CoderIndex = 0, StreamIndex = 0 },
                    InputStreams = new[] { new InputStream { Size = stream.Position - origin } },
                    Coders = new[] { new Coder {
                        MethodId = master._7zip.Legacy.CMethodId.kLzma,
                        Settings = mSettings,
                        InputStreams = new[] { new InputStreamRef { PackedStreamIndex = 0 } },
                        OutputStreams = new[] { new CoderStream { Size = processed } },
                    } },
                };
            }
        }

        private sealed class Lzma2EncoderConfig: EncoderConfig
        {
            private MemoryStream mBuffer;
            private byte mSettings;

            private void EncodeBufferedData(Stream stream)
            {
                var settings = new LZMA.CLzma2EncProps();
                settings.Lzma2EncProps_Init();

                var encoder = new LZMA.CLzma2Enc(LZMA.ISzAlloc.SmallAlloc, LZMA.ISzAlloc.BigAlloc);
                var res = encoder.Lzma2Enc_SetProps(settings);
                if(res != LZMA.SZ_OK)
                    throw new InvalidOperationException();

                mSettings = encoder.Lzma2Enc_WriteProperties();

                var outBuffer = new LZMA.CSeqOutStream(delegate(P<byte> buf, long sz) {
                    stream.Write(buf.mBuffer, buf.mOffset, checked((int)sz));
                });

                var inBuffer = new LZMA.CSeqInStream(delegate(P<byte> buf, long sz) {
                    return mBuffer.Read(buf.mBuffer, buf.mOffset, checked((int)sz));
                });

                res = encoder.Lzma2Enc_Encode(outBuffer, inBuffer, null);
                if(res != LZMA.SZ_OK)
                    throw new InvalidOperationException();

                encoder.Lzma2Enc_Destroy();
            }

            internal Lzma2EncoderConfig(Stream stream)
                : base(stream)
            {
                mBuffer = new MemoryStream();
            }

            protected override Stream GetNextWriterStream(Stream stream)
            {
                return new ForwardingEncoderStream(mBuffer);
            }

            protected override FileSet FinishFileSet(Stream stream, FileEntry[] entries, long processed, long origin)
            {
                if(entries == null || entries.Length == 0)
                    return null;

                mBuffer.Position = 0;
                EncodeBufferedData(stream);
                mBuffer = null;

                return new FileSet {
                    Files = entries,
                    DataStream = new CoderStreamRef { CoderIndex = 0, StreamIndex = 0 },
                    InputStreams = new[] { new InputStream { Size = stream.Position - origin } },
                    Coders = new[] { new Coder {
                        MethodId = master._7zip.Legacy.CMethodId.kLzma2,
                        Settings = new byte[] { mSettings },
                        InputStreams = new[] { new InputStreamRef { PackedStreamIndex = 0 } },
                        OutputStreams = new[] { new CoderStream { Size = processed } },
                    } },
                };
            }
        }

        #endregion

        #region Constants & Variables

        private static readonly byte[] kSignature = { (byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C };

        private long mFileOrigin;
        private Stream mFileStream;
        private BinaryWriter mFileWriter;
        private List<FileSet> mFileSets;
        private EncoderConfig mEncoder;

        #endregion

        #region Public Methods

        public ArchiveWriter(Stream stream)
        {
            if(stream == null)
                throw new ArgumentNullException("stream");

            if(!stream.CanWrite)
                throw new ArgumentException("Stream must support writing.", "stream");

            if(!stream.CanSeek)
                throw new ArgumentException("Stream must support seeking.", "stream");

            mFileStream = stream;
            mFileOrigin = stream.Position;
            mFileWriter = new BinaryWriter(stream, Encoding.Unicode);

            mFileWriter.Write(kSignature);
            mFileWriter.Write((byte)0);
            mFileWriter.Write((byte)3);

            // We don't have a header yet so just write a placeholder. As a side effect
            // the placeholder will make this file look like a valid but empty archive.
            WriteHeaderInfo(0, 0, CRC.Finish(CRC.kInitCRC));

            mFileSets = new List<FileSet>();
        }

        /// <summary>
        /// Returns the amount of data written so far. This is a lower bound for the
        /// current archive size if it would be closed now.
        /// </summary>
        public long WrittenSize
        {
            get { return mFileStream.Position; }
        }

        /// <summary>
        /// Returns an estimated limit for the file size if the archive would be closed now.
        /// This consists of WrittenSize plus an estimated size limit for the header.
        /// The actual archive size may be smaller due to overestimating the header size.
        /// </summary>
        public long CurrentSizeLimit
        {
            get
            {
                if(mEncoder != null)
                    throw new NotSupportedException("Calculating size limits is not possible while an encoder is open.");

                return mFileStream.Position + CalculateHeaderLimit();
            }
        }

        public void WriteFinalHeader()
        {
            FinishCurrentEncoder();

            var files = mFileSets.SelectMany(stream => stream.Files).ToArray();
            if(files.Length != 0)
            {
                long headerOffset = mFileStream.Position;

                WriteNumber(BlockType.Header);

                var inputStreams = mFileSets.SelectMany(fileset => fileset.InputStreams).ToArray();
                if(inputStreams.Any(stream => stream.Size != 0))
                {
                    WriteNumber(BlockType.MainStreamsInfo);
                    WriteNumber(BlockType.PackInfo);
                    WriteNumber((ulong)0); // offset to input streams
                    WriteNumber(inputStreams.Length);
                    WriteNumber(BlockType.Size);
                    foreach(var stream in inputStreams)
                        WriteNumber(stream.Size);
                    WriteNumber(BlockType.End);
                    WriteNumber(BlockType.UnpackInfo);
                    WriteNumber(BlockType.Folder);
                    WriteNumber(mFileSets.Count);
                    mFileWriter.Write((byte)0); // inline data
                    foreach(var fileset in mFileSets)
                    {
                        WriteNumber(fileset.Coders.Length);
                        foreach(var coder in fileset.Coders)
                        {
                            int idlen = coder.MethodId.GetLength();
                            if(idlen >= 8)
                                throw new NotSupportedException();

                            int flags = idlen;

                            if(coder.InputStreams.Length != 1 || coder.OutputStreams.Length != 1)
                                flags |= 0x10;

                            if(coder.Settings != null)
                                flags |= 0x20;

                            mFileWriter.Write((byte)flags);

                            ulong id = coder.MethodId.Id;
                            for(int i = idlen - 1; i >= 0; i--)
                                mFileWriter.Write((byte)(id >> (i * 8)));

                            if((flags & 0x10) != 0)
                            {
                                WriteNumber(coder.InputStreams.Length);
                                WriteNumber(coder.OutputStreams.Length);
                            }

                            if((flags & 0x20) != 0)
                            {
                                WriteNumber(coder.Settings.Length);
                                mFileWriter.Write(coder.Settings);
                            }

                            // TODO: Bind pairs and association to streams ...
                            if(fileset.Coders.Length > 1 || coder.InputStreams.Length != 1 || coder.OutputStreams.Length != 1)
                                throw new NotSupportedException();
                        }
                    }
                    WriteNumber(BlockType.CodersUnpackSize);
                    foreach(var fileset in mFileSets)
                        WriteNumber(fileset.DataStream.GetSize(fileset));
                    WriteNumber(BlockType.End);
                    WriteNumber(BlockType.SubStreamsInfo);
                    WriteNumber(BlockType.NumUnpackStream);
                    foreach(var stream in mFileSets)
                        WriteNumber(stream.Files.Length);
                    WriteNumber(BlockType.Size);
                    foreach(var stream in mFileSets)
                        for(int i = 0; i < stream.Files.Length - 1; i++)
                            WriteNumber(stream.Files[i].Size);
                    WriteNumber(BlockType.End);
                    WriteNumber(BlockType.End);
                }

                WriteNumber(BlockType.FilesInfo);
                WriteNumber(files.Length);

                WriteNumber(BlockType.Name);
                WriteNumber(1 + files.Sum(file => file.Name.Length + 1) * 2);
                mFileWriter.Write((byte)0); // inline names
                for(int i = 0; i < files.Length; i++)
                {
                    string name = files[i].Name;
                    for(int j = 0; j < name.Length; j++)
                        mFileWriter.Write(name[j]);
                    mFileWriter.Write('\0');
                }

                if(files.Any(file => file.Size == 0))
                {
                    int emptyStreams = 0;

                    WriteNumber(BlockType.EmptyStream);
                    WriteNumber((files.Length + 7) / 8);
                    for(int i = 0; i < files.Length; i += 8)
                    {
                        int mask = 0;
                        for(int j = 0; j < 8; j++)
                        {
                            if(i + j < files.Length && files[i + j].Size == 0)
                            {
                                mask |= 1 << (7 - j);
                                emptyStreams++;
                            }
                        }
                        mFileWriter.Write((byte)mask);
                    }

                    WriteNumber(BlockType.EmptyFile);
                    WriteNumber((emptyStreams + 7) / 8);
                    for(int i = 0; i < emptyStreams; i += 8)
                    {
                        int mask = 0;
                        for(int j = 0; j < 8; j++)
                            if(i + j < emptyStreams)
                                mask |= 1 << (7 - j);
                        mFileWriter.Write((byte)mask);
                    }
                }

                WriteNumber(BlockType.End);

                long headerSize = mFileStream.Position - headerOffset;
                mFileStream.Position = headerOffset;
                uint headerCRC = CRC.From(mFileStream, headerSize);
                mFileStream.Position = mFileOrigin + 8;
                WriteHeaderInfo(headerOffset - mFileOrigin - 0x20, headerSize, headerCRC);
            }

            mFileStream.Close(); // so we don't start overwriting stuff accidently by calling more functions
        }

        #endregion

        #region Encoding Methods

        public void FinishCurrentEncoder()
        {
            if(mEncoder != null)
            {
                var fileSet = mEncoder.Finish();
                if(fileSet != null)
                    mFileSets.Add(fileSet);

                mEncoder = null;
            }
        }

        public void InitializePlainEncoder()
        {
            FinishCurrentEncoder();
            mEncoder = new PlainEncoderConfig(mFileStream);
        }

        public void InitializeLzmaEncoder()
        {
            FinishCurrentEncoder();
            mEncoder = new LzmaEncoderConfig(mFileStream);
        }

        public void InitializeLzma2Encoder()
        {
            FinishCurrentEncoder();
            mEncoder = new Lzma2EncoderConfig(mFileStream);
        }

        public Stream BeginWriteFile(IArchiveWriterEntry metadata)
        {
            if(mEncoder == null)
                throw new InvalidOperationException("No encoder has been initialized.");

            return mEncoder.BeginWriteFile(metadata);
        }

        #endregion

        #region Private Helper Methods

        private void WriteHeaderInfo(long offset, long size, uint crc)
        {
            uint infoCRC = CRC.kInitCRC;
            infoCRC = CRC.Update(infoCRC, offset);
            infoCRC = CRC.Update(infoCRC, size);
            infoCRC = CRC.Update(infoCRC, crc);
            infoCRC = CRC.Finish(infoCRC);

            mFileWriter.Write(infoCRC);
            mFileWriter.Write(offset);
            mFileWriter.Write(size);
            mFileWriter.Write(crc);
        }

        private void WriteNumber(BlockType value)
        {
            WriteNumber((byte)value);
        }

        private void WriteNumber(long number)
        {
            WriteNumber(checked((ulong)number));
        }

        private void WriteNumber(ulong number)
        {
            // TODO: Use the short forms if applicable.
            mFileWriter.Write((byte)0xFF);
            mFileWriter.Write(number);
        }

        private long CalculateHeaderLimit()
        {
            const int kMaxNumberLen = 9; // 0xFF + sizeof(ulong)
            const int kBlockTypeLen = kMaxNumberLen;
            const int kZeroNumberLen = kMaxNumberLen;
            long limit = 0;

            var files = mFileSets.SelectMany(stream => stream.Files).ToArray();
            if(files.Length != 0)
            {
                limit += kBlockTypeLen; // BlockType.Header

                var inputStreams = mFileSets.SelectMany(fileset => fileset.InputStreams).ToArray();
                if(inputStreams.Any(stream => stream.Size != 0))
                {
                    limit += kBlockTypeLen; // BlockType.MainStreamsInfo
                    limit += kBlockTypeLen; // BlockType.PackInfo
                    limit += kZeroNumberLen; // zero = offset to input streams
                    limit += kMaxNumberLen; // inputStreams.Length
                    limit += kBlockTypeLen; //BlockType.Size
                    limit += inputStreams.Length * kMaxNumberLen; // inputStreams: inputStream.Size
                    limit += kBlockTypeLen; // BlockType.End
                    limit += kBlockTypeLen; // BlockType.UnpackInfo
                    limit += kBlockTypeLen; // BlockType.Folder
                    limit += kMaxNumberLen; // mFileSets.Count
                    limit += kZeroNumberLen; // zero = inline data
                    foreach(var fileset in mFileSets)
                    {
                        limit += kMaxNumberLen; // fileset.Coders.Length
                        foreach(var coder in fileset.Coders)
                        {
                            limit += 1; // flags
                            limit += coder.MethodId.GetLength(); // coder.MethodId

                            if(coder.InputStreams.Length != 1 || coder.OutputStreams.Length != 1)
                            {
                                limit += kMaxNumberLen; // coder.InputStreams.Length
                                limit += kMaxNumberLen; // coder.OutputStreams.Length
                            }

                            if(coder.Settings != null)
                            {
                                limit += kMaxNumberLen; // coder.Settings.Length
                                limit += coder.Settings.Length; // coder.Settings
                            }

                            // TODO: Bind pairs and association to streams ...
                            if(fileset.Coders.Length > 1 || coder.InputStreams.Length != 1 || coder.OutputStreams.Length != 1)
                                throw new NotSupportedException();
                        }
                    }
                    limit += kBlockTypeLen; // BlockType.CodersUnpackSize
                    limit += mFileSets.Count * kMaxNumberLen; // mFileSets: fileset.DataStream.GetSize(fileset)
                    limit += kBlockTypeLen; // BlockType.End
                    limit += kBlockTypeLen; // BlockType.SubStreamsInfo
                    limit += kBlockTypeLen; // BlockType.NumUnpackStream
                    limit += mFileSets.Count * kMaxNumberLen; // mFileSets: stream.Files.Length
                    limit += kBlockTypeLen; // BlockType.Size
                    limit += mFileSets.Sum(fileset => fileset.Files.Length - 1) * kMaxNumberLen; // mFileSets: fileset.Files[0..n-1]: stream.Files[i].Size
                    limit += kBlockTypeLen; // BlockType.End
                    limit += kBlockTypeLen; // BlockType.End
                }

                limit += kBlockTypeLen; // BlockType.FilesInfo
                limit += kMaxNumberLen; // files.Length

                limit += kBlockTypeLen; // BlockType.Name
                limit += kMaxNumberLen; // 1 + files.Sum(file => file.Name.Length + 1) * 2
                limit += kZeroNumberLen; // zero = inline names
                for(int i = 0; i < files.Length; i++)
                    limit += (files[i].Name.Length + 1) * 2;

                if(files.Any(file => file.Size == 0))
                {
                    limit += kBlockTypeLen; // BlockType.EmptyStream
                    limit += kMaxNumberLen; // (files.Length + 7) / 8
                    limit += (files.Length + 7) / 8; // bit vector
                    limit += kBlockTypeLen; // BlockType.EmptyFile
                    limit += kMaxNumberLen; // (files.Length + 7) / 8 -- this is an upper bound, for an exact size we need to count the number of empty streams
                    limit += (files.Length + 7) / 8; // bit vector
                }

                limit += kBlockTypeLen; // BlockType.End
            }

            return limit;
        }

        #endregion
    }

    public static class ArchiveWriterExtensions
    {
        #region ArchiveWriter Extensions

        public static void WriteFile(this ArchiveWriter writer, IArchiveWriterEntry metadata, Stream content)
        {
            using(var stream = writer.BeginWriteFile(metadata))
                content.CopyTo(stream);
        }

        public static void WriteFile(this ArchiveWriter writer, DirectoryInfo root, FileInfo file)
        {
            using(var content = file.OpenRead())
                writer.WriteFile(new FileBasedArchiveWriterEntry(root, file), content);
        }

        public static void WriteFiles(this ArchiveWriter writer, DirectoryInfo root, IEnumerable<FileInfo> files)
        {
            foreach(var file in files)
                writer.WriteFile(root, file);
        }

        public static void WriteFiles(this ArchiveWriter writer, DirectoryInfo root, params FileInfo[] files)
        {
            writer.WriteFiles(root, (IEnumerable<FileInfo>)files);
        }

        #endregion
    }
}
