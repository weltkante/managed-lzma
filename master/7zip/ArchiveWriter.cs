using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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

            return String.Join("/", itemList.Select(item => item.Name));
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

        private abstract class EncoderConfig: IDisposable
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

            public virtual void Dispose() { }

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

            public abstract long LowerBound { get; }
            public abstract long UpperBound { get; }
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

        private sealed class PlainEncoderConfig: EncoderConfig
        {
            private sealed class PlainEncoderStream: EncoderStream
            {
                private PlainEncoderConfig mOwner;
                private Stream mStream;

                public PlainEncoderStream(PlainEncoderConfig owner, Stream stream)
                {
                    mOwner = owner;
                    mStream = stream;
                }

                protected override void Dispose(bool disposing)
                {
                    mStream = null;
                    base.Dispose(disposing);
                }

                public override void Write(byte[] buffer, int offset, int count)
                {
                    if(mStream == null)
                        throw new ObjectDisposedException(null);

                    mOwner.mWrittenSize += count;
                    mStream.Write(buffer, offset, count);
                }
            }

            private long mWrittenSize;

            internal PlainEncoderConfig(Stream target)
                : base(target) { }

            protected override Stream GetNextWriterStream(Stream stream)
            {
                return new PlainEncoderStream(this, stream);
            }

            public override long LowerBound
            {
                get { return mWrittenSize; }
            }

            public override long UpperBound
            {
                get { return mWrittenSize; }
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
            private sealed class LzmaEncoderStream: EncoderStream
            {
                private Stream mTargetStream;

                public LzmaEncoderStream(Stream targetStream)
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
                return new LzmaEncoderStream(mBuffer);
            }

            public override long LowerBound
            {
                get { return mBuffer.Length; } // the cached input size is of course just an estimate (and may even violate the expected constraints)
            }

            public override long UpperBound
            {
                get { return mBuffer.Length; } // the cached input size is of course just an estimate (and may even violate the expected constraints)
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
            private sealed class Lzma2EncoderStream: EncoderStream
            {
                private Stream mTargetStream;

                public Lzma2EncoderStream(Stream targetStream)
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

            private MemoryStream mBuffer;
            private byte mSettings;

            private void EncodeBufferedData(Stream stream)
            {
                var settings = new LZMA.CLzma2EncProps();
                settings.Lzma2EncProps_Init();
                //settings.mLzmaProps.mNumThreads = 2;
                //settings.mNumBlockThreads = 2;

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
                return new Lzma2EncoderStream(mBuffer);
            }

            public override long LowerBound
            {
                get { return mBuffer.Length; } // the cached input size is of course just an estimate (and may even violate the expected constraints)
            }

            public override long UpperBound
            {
                get { return mBuffer.Length; } // the cached input size is of course just an estimate (and may even violate the expected constraints)
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

        private sealed class Lzma2ThreadedEncoderConfig: EncoderConfig
        {
            private sealed class ThreadedEncoderStream: EncoderStream
            {
                private Lzma2ThreadedEncoderConfig mContext;
                private int mBufferOffset;
                private int mBufferEnding;

                public ThreadedEncoderStream(Lzma2ThreadedEncoderConfig context, int offset, int ending)
                {
                    mContext = context;
                    mBufferOffset = offset;
                    mBufferEnding = ending;
                }

                protected override void Dispose(bool disposing)
                {
                    mContext = null;
                    base.Dispose(disposing);
                }

                public override void Write(byte[] buffer, int offset, int count)
                {
                    if(mContext == null)
                        throw new ObjectDisposedException(null);

                    while(count > 0)
                    {
                        int copy;
                        if(mBufferOffset <= mBufferEnding)
                            copy = Math.Min(count, mBufferEnding - mBufferOffset);
                        else
                            copy = Math.Min(count, kBufferLength - mBufferOffset);

                        if(copy > 0)
                        {
                            Buffer.BlockCopy(buffer, offset, mContext.mInputBuffer, mBufferOffset, copy);
                            count -= copy;
                            offset += copy;
                            mBufferOffset = (mBufferOffset + copy) % kBufferLength;
                        }

                        lock(mContext.mSyncObject)
                        {
                            if(copy > 0)
                            {
                                mContext.mUpperBound += copy;
                                mContext.mInputEnding = mBufferOffset;
                                Monitor.Pulse(mContext.mSyncObject);
                            }

                            for(; ; )
                            {
                                if(mContext.mShutdown)
                                    throw new ObjectDisposedException(null);

                                int offsetMinusOne = (mContext.mInputOffset + kBufferLength - 1) % kBufferLength;
                                if(mContext.mInputEnding != offsetMinusOne)
                                {
                                    mBufferEnding = offsetMinusOne;
                                    break;
                                }

                                Monitor.Wait(mContext.mSyncObject);
                            }
                        }
                    }
                }
            }

            private const int kBufferLength = 1 << 20;

            private object mSyncObject;
            private bool mShutdown;
            private Thread mEncoderThread;
            private byte[] mInputBuffer;
            private int mInputOffset;
            private int mInputEnding;
            private byte mSettings;
            private Stream mTargetStream;
            private int? mThreadCount;
            private long mLowerBound;
            private long mUpperBound;

            public override void Dispose()
            {
                try
                {
                    lock(mSyncObject)
                    {
                        if(mShutdown)
                            return;

                        mShutdown = true;
                        Monitor.Pulse(mSyncObject);
                    }

                    mEncoderThread.Join();
                }
                finally
                {
                    base.Dispose();
                }
            }

            private void EncoderThread()
            {
                var settings = new LZMA.CLzma2EncProps();
                settings.Lzma2EncProps_Init();

                if(mThreadCount.HasValue)
                    settings.mNumBlockThreads = mThreadCount.Value;

                var encoder = new LZMA.CLzma2Enc(LZMA.ISzAlloc.SmallAlloc, LZMA.ISzAlloc.BigAlloc);
                var res = encoder.Lzma2Enc_SetProps(settings);
                if(res != LZMA.SZ_OK)
                    throw new InvalidOperationException();

                mSettings = encoder.Lzma2Enc_WriteProperties();

                var outBuffer = new LZMA.CSeqOutStream(delegate(P<byte> buf, long sz) {
                    mTargetStream.Write(buf.mBuffer, buf.mOffset, checked((int)sz));
                    lock(mSyncObject)
                        mLowerBound += sz;
                });

                var inBuffer = new LZMA.CSeqInStream(delegate(P<byte> buf, long sz) {
                    Utils.Assert(sz != 0);
                    lock(mSyncObject)
                    {
                        for(; ; )
                        {
                            if(mShutdown)
                                return 0;

                            if(mInputOffset != mInputEnding)
                            {
                                int size;
                                if(mInputOffset <= mInputEnding)
                                    size = Math.Min(checked((int)sz), mInputEnding - mInputOffset);
                                else
                                    size = Math.Min(checked((int)sz), kBufferLength - mInputOffset);

                                Utils.Assert(size != 0);
                                Buffer.BlockCopy(mInputBuffer, mInputOffset, buf.mBuffer, buf.mOffset, size);
                                mInputOffset = (mInputOffset + size) % kBufferLength;
                                Monitor.Pulse(mSyncObject);
                                return size;
                            }

                            Monitor.Wait(mSyncObject);
                        }
                    }
                });

                res = encoder.Lzma2Enc_Encode(outBuffer, inBuffer, null);
                if(res != LZMA.SZ_OK)
                    throw new InvalidOperationException();

                encoder.Lzma2Enc_Destroy();
            }

            internal Lzma2ThreadedEncoderConfig(Stream stream, int? threadCount)
                : base(stream)
            {
                mTargetStream = stream;
                mThreadCount = threadCount;
                mSyncObject = new object();
                mInputBuffer = new byte[kBufferLength];
                mEncoderThread = new Thread(EncoderThread);
                mEncoderThread.Name = "LZMA 2 Stream Buffer Thread";
                mEncoderThread.Start();
            }

            protected override Stream GetNextWriterStream(Stream stream)
            {
                lock(mSyncObject)
                {
                    for(; ; )
                    {
                        if(mShutdown)
                            throw new ObjectDisposedException(null);

                        int offsetMinusOne = (mInputOffset + kBufferLength - 1) % kBufferLength;
                        if(offsetMinusOne != mInputEnding)
                            return new ThreadedEncoderStream(this, mInputEnding, offsetMinusOne);

                        Monitor.Wait(mSyncObject);
                    }
                }
            }

            public override long LowerBound
            {
                get
                {
                    lock(mSyncObject)
                        return mLowerBound;
                }
            }

            public override long UpperBound
            {
                get
                {
                    lock(mSyncObject)
                        return mUpperBound;
                }
            }

            protected override FileSet FinishFileSet(Stream stream, FileEntry[] entries, long processed, long origin)
            {
                if(entries == null || entries.Length == 0)
                    return null;

                lock(mSyncObject)
                {
                    Utils.Assert(!mShutdown);

                    while(mInputOffset != mInputEnding)
                        Monitor.Wait(mSyncObject);

                    mShutdown = true;
                    Monitor.Pulse(mSyncObject);
                }

                mEncoderThread.Join();

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
        private long mWrittenSize;
        private long mWrittenSync;
        private Stream mFileStream;
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

            // Seeking is required only because we need to go back to the start of the stream
            // and fix up a reference to the header data. Possible solution: If we fix filesize
            // ahead of time we could reserve space for the header and output a fixed offset.
            if(!stream.CanSeek)
                throw new ArgumentException("Stream must support seeking.", "stream");

            // TODO: Implement the encoding independant of BinaryWriter endianess.
            if(!BitConverter.IsLittleEndian)
                throw new NotSupportedException("BinaryWriter must be little endian.");

            mFileStream = stream;
            mFileOrigin = stream.Position;

            var writer = new BinaryWriter(stream, Encoding.Unicode);

            writer.Write(kSignature);
            writer.Write((byte)0);
            writer.Write((byte)3);

            // We don't have a header yet so just write a placeholder. As a side effect
            // the placeholder will make this file look like a valid but empty archive.
            WriteHeaderInfo(writer, 0, 0, CRC.Finish(CRC.kInitCRC));

            mFileSets = new List<FileSet>();
            mWrittenSync = mFileStream.Position;
        }

        /// <summary>
        /// Returns the amount of data written so far. This is a lower bound for the
        /// current archive size if it would be closed now.
        /// </summary>
        public long WrittenSize
        {
            get
            {
                // TODO: If we have an encoder we shouldn't access the file stream because it's owned by the encoder.

                long size = mWrittenSize;

                if(mEncoder != null)
                    size += mEncoder.LowerBound;

                return size;
            }
        }

        /// <summary>
        /// Returns an estimated limit for the file size if the archive would be closed now.
        /// This consists of WrittenSize plus an estimated size limit for buffered data and the header.
        /// The actual archive size may be smaller due to overestimating the header size.
        /// </summary>
        public long CurrentSizeLimit
        {
            get
            {
                // TODO: CalculateHeaderLimit does not include the metadata from the active encoder!
                long size = mWrittenSize + CalculateHeaderLimit();

                if(mEncoder != null)
                    size += mEncoder.UpperBound;

                return size;
            }
        }

        public void WriteFinalHeader()
        {
            FinishCurrentEncoder();

            var files = mFileSets.SelectMany(stream => stream.Files).ToArray();
            if(files.Length != 0)
            {
                long headerOffset = mFileStream.Position;

                var headerStream = new master._7zip.Legacy.CrcBuilderStream(mFileStream);
                var writer = new BinaryWriter(headerStream, Encoding.Unicode);

                WriteNumber(writer, BlockType.Header);

                var inputStreams = mFileSets.SelectMany(fileset => fileset.InputStreams).ToArray();
                if(inputStreams.Any(stream => stream.Size != 0))
                {
                    WriteNumber(writer, BlockType.MainStreamsInfo);
                    WriteNumber(writer, BlockType.PackInfo);
                    WriteNumber(writer, (ulong)0); // offset to input streams
                    WriteNumber(writer, inputStreams.Length);
                    WriteNumber(writer, BlockType.Size);
                    foreach(var stream in inputStreams)
                        WriteNumber(writer, stream.Size);
                    WriteNumber(writer, BlockType.End);
                    WriteNumber(writer, BlockType.UnpackInfo);
                    WriteNumber(writer, BlockType.Folder);
                    WriteNumber(writer, mFileSets.Count);
                    writer.Write((byte)0); // inline data
                    foreach(var fileset in mFileSets)
                    {
                        WriteNumber(writer, fileset.Coders.Length);
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

                            writer.Write((byte)flags);

                            ulong id = coder.MethodId.Id;
                            for(int i = idlen - 1; i >= 0; i--)
                                writer.Write((byte)(id >> (i * 8)));

                            if((flags & 0x10) != 0)
                            {
                                WriteNumber(writer, coder.InputStreams.Length);
                                WriteNumber(writer, coder.OutputStreams.Length);
                            }

                            if((flags & 0x20) != 0)
                            {
                                WriteNumber(writer, coder.Settings.Length);
                                writer.Write(coder.Settings);
                            }

                            // TODO: Bind pairs and association to streams ...
                            if(fileset.Coders.Length > 1 || coder.InputStreams.Length != 1 || coder.OutputStreams.Length != 1)
                                throw new NotSupportedException();
                        }
                    }
                    WriteNumber(writer, BlockType.CodersUnpackSize);
                    foreach(var fileset in mFileSets)
                        WriteNumber(writer, fileset.DataStream.GetSize(fileset));
                    WriteNumber(writer, BlockType.End);
                    WriteNumber(writer, BlockType.SubStreamsInfo);
                    WriteNumber(writer, BlockType.NumUnpackStream);
                    foreach(var stream in mFileSets)
                        WriteNumber(writer, stream.Files.Length);
                    WriteNumber(writer, BlockType.Size);
                    foreach(var stream in mFileSets)
                        for(int i = 0; i < stream.Files.Length - 1; i++)
                            WriteNumber(writer, stream.Files[i].Size);
                    WriteNumber(writer, BlockType.End);
                    WriteNumber(writer, BlockType.End);
                }

                WriteNumber(writer, BlockType.FilesInfo);
                WriteNumber(writer, files.Length);

                WriteNumber(writer, BlockType.Name);
                WriteNumber(writer, 1 + files.Sum(file => file.Name.Length + 1) * 2);
                writer.Write((byte)0); // inline names
                for(int i = 0; i < files.Length; i++)
                {
                    string name = files[i].Name;
                    for(int j = 0; j < name.Length; j++)
                        writer.Write(name[j]);
                    writer.Write('\0');
                }

                /* had to disable empty streams and files because above BlockType.Size doesn't respect them
                 * if a file is marked as empty stream it doesn't get a size/hash entry in the coder header above
                 * however, to fix that, we'd need to skip coders with only empty files too, so its easier to do it this way for now
                if(files.Any(file => file.Size == 0))
                {
                    int emptyStreams = 0;

                    WriteNumber(writer, BlockType.EmptyStream);
                    WriteNumber(writer, (files.Length + 7) / 8);
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
                        writer.Write((byte)mask);
                    }

                    WriteNumber(writer, BlockType.EmptyFile);
                    WriteNumber(writer, (emptyStreams + 7) / 8);
                    for(int i = 0; i < emptyStreams; i += 8)
                    {
                        int mask = 0;
                        for(int j = 0; j < 8; j++)
                            if(i + j < emptyStreams)
                                mask |= 1 << (7 - j);
                        writer.Write((byte)mask);
                    }
                }
                */

                int ctimeCount = files.Count(file => file.CTime.HasValue);
                if(ctimeCount != 0)
                {
                    WriteNumber(writer, BlockType.CTime);

                    if(ctimeCount == files.Length)
                    {
                        WriteNumber(writer, 2 + ctimeCount * 8);
                        writer.Write((byte)1);
                    }
                    else
                    {
                        WriteNumber(writer, (ctimeCount + 7) / 8 + 2 + ctimeCount * 8);
                        writer.Write((byte)0);

                        for(int i = 0; i < files.Length; i += 8)
                        {
                            int mask = 0;
                            for(int j = 0; j < 8; j++)
                                if(i + j < files.Length && files[i + j].CTime.HasValue)
                                    mask |= 1 << (7 - j);

                            writer.Write((byte)mask);
                        }
                    }

                    writer.Write((byte)0); // inline data

                    for(int i = 0; i < files.Length; i++)
                        if(files[i].CTime.HasValue)
                            writer.Write(files[i].CTime.Value.ToFileTimeUtc());
                }

                int atimeCount = files.Count(file => file.ATime.HasValue);
                if(atimeCount != 0)
                {
                    WriteNumber(writer, BlockType.ATime);

                    if(atimeCount == files.Length)
                    {
                        WriteNumber(writer, 2 + atimeCount * 8);
                        writer.Write((byte)1);
                    }
                    else
                    {
                        WriteNumber(writer, (atimeCount + 7) / 8 + 2 + atimeCount * 8);
                        writer.Write((byte)0);

                        for(int i = 0; i < files.Length; i += 8)
                        {
                            int mask = 0;
                            for(int j = 0; j < 8; j++)
                                if(i + j < files.Length && files[i + j].ATime.HasValue)
                                    mask |= 1 << (7 - j);

                            writer.Write((byte)mask);
                        }
                    }

                    writer.Write((byte)0); // inline data

                    for(int i = 0; i < files.Length; i++)
                        if(files[i].ATime.HasValue)
                            writer.Write(files[i].ATime.Value.ToFileTimeUtc());
                }

                int mtimeCount = files.Count(file => file.MTime.HasValue);
                if(mtimeCount != 0)
                {
                    WriteNumber(writer, BlockType.MTime);

                    if(mtimeCount == files.Length)
                    {
                        WriteNumber(writer, 2 + mtimeCount * 8);
                        writer.Write((byte)1);
                    }
                    else
                    {
                        WriteNumber(writer, (mtimeCount + 7) / 8 + 2 + mtimeCount * 8);
                        writer.Write((byte)0);

                        for(int i = 0; i < files.Length; i += 8)
                        {
                            int mask = 0;
                            for(int j = 0; j < 8; j++)
                                if(i + j < files.Length && files[i + j].MTime.HasValue)
                                    mask |= 1 << (7 - j);

                            writer.Write((byte)mask);
                        }
                    }

                    writer.Write((byte)0); // inline data

                    for(int i = 0; i < files.Length; i++)
                        if(files[i].MTime.HasValue)
                            writer.Write(files[i].MTime.Value.ToFileTimeUtc());
                }

                WriteNumber(writer, BlockType.End);

                uint headerCRC = headerStream.Finish();
                long headerSize = mFileStream.Position - headerOffset;
                mFileStream.Position = mFileOrigin + 8;
                WriteHeaderInfo(new BinaryWriter(mFileStream, Encoding.Unicode), headerOffset - mFileOrigin - 0x20, headerSize, headerCRC);
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

                long position = mFileStream.Position;
                mWrittenSize += position - mWrittenSync;
                mWrittenSync = position;
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

        [Obsolete("Use InitializeLzma2EncoderTB instead.")]
        public void InitializeLzma2Encoder()
        {
            FinishCurrentEncoder();
            mEncoder = new Lzma2EncoderConfig(mFileStream);
        }

        public void InitializeLzma2EncoderTB(int? threadCount)
        {
            FinishCurrentEncoder();
            mEncoder = new Lzma2ThreadedEncoderConfig(mFileStream, threadCount);
        }

        public Stream BeginWriteFile(IArchiveWriterEntry metadata)
        {
            if(mEncoder == null)
                throw new InvalidOperationException("No encoder has been initialized.");

            return mEncoder.BeginWriteFile(metadata);
        }

        #endregion

        #region Private Helper Methods

        private void WriteHeaderInfo(BinaryWriter writer, long offset, long size, uint crc)
        {
            uint infoCRC = CRC.kInitCRC;
            infoCRC = CRC.Update(infoCRC, offset);
            infoCRC = CRC.Update(infoCRC, size);
            infoCRC = CRC.Update(infoCRC, crc);
            infoCRC = CRC.Finish(infoCRC);

            writer.Write(infoCRC);
            writer.Write(offset);
            writer.Write(size);
            writer.Write(crc);
        }

        private void WriteNumber(BinaryWriter writer, BlockType value)
        {
            WriteNumber(writer, (byte)value);
        }

        private void WriteNumber(BinaryWriter writer, long number)
        {
            WriteNumber(writer, checked((ulong)number));
        }

        private void WriteNumber(BinaryWriter writer, ulong number)
        {
            // TODO: Use the short forms if applicable.
            writer.Write((byte)0xFF);
            writer.Write(number);
        }

        private long CalculateHeaderLimit()
        {
            return 1024; // HACK: mFileSets.SelectMany(stream => stream.Files).ToArray() is too slow

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

                limit += kBlockTypeLen; // BlockType.CTime
                limit += kMaxNumberLen; // (ctimeCount + 7) / 8 + 2 + ctimeCount * 8;
                limit += (files.Length + 7) / 8 + 2 + files.Length * 8;

                limit += kBlockTypeLen; // BlockType.ATime
                limit += kMaxNumberLen; // (atimeCount + 7) / 8 + 2 + atimeCount * 8;
                limit += (files.Length + 7) / 8 + 2 + files.Length * 8;

                limit += kBlockTypeLen; // BlockType.MTime
                limit += kMaxNumberLen; // (mtimeCount + 7) / 8 + 2 + mtimeCount * 8;
                limit += (files.Length + 7) / 8 + 2 + files.Length * 8;

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
