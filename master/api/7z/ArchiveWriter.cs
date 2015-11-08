using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    // TODO: it would be even better if we could separate ArchiveWriter from a "modifyable" archive further
    //       the archive writer should just have enough state (from modifyable archives) that he can write new metadata/header

    public sealed class ArchiveWriter : IDisposable
    {
        private static void PutInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void PutInt64(byte[] buffer, int offset, long value)
        {
            PutInt32(buffer, offset, (int)value);
            PutInt32(buffer, offset + 4, (int)(value >> 32));
        }

        public static ArchiveWriter Create(Stream stream, bool dispose)
        {
            try
            {
                var writer = new ArchiveWriter(stream, dispose);

                stream.Position = 0;
                stream.SetLength(ArchiveMetadataFormat.kHeaderLength);
                stream.Write(writer.PrepareHeader(), 0, ArchiveMetadataFormat.kHeaderLength);

                return writer;
            }
            catch
            {
                if (dispose && stream != null)
                    stream.Dispose();

                throw;
            }
        }

        public static async Task<ArchiveWriter> CreateAsync(Stream stream, bool dispose)
        {
            try
            {
                var writer = new ArchiveWriter(stream, dispose);

                stream.Position = 0;
                stream.SetLength(ArchiveMetadataFormat.kHeaderLength);
                await stream.WriteAsync(writer.PrepareHeader(), 0, ArchiveMetadataFormat.kHeaderLength);

                return writer;
            }
            catch
            {
                if (dispose && stream != null)
                    stream.Dispose();

                throw;
            }
        }

        private const byte kMajorVersion = 0;
        private const byte kMinorVersion = 3;

        private Stream mArchiveStream;
        private ImmutableArray<ArchiveFileSection>.Builder mFileSections;
        private ImmutableArray<ArchiveDecoderSection>.Builder mDecoderSections;
        private ArchiveWriterStreamProvider mStreamProvider;
        private List<EncoderSession> mEncoderSessions = new List<EncoderSession>();
        private long mAppendPosition;
        private long mMetadataPosition;
        private long mMetadataLength;
        private Checksum mMetadataChecksum;
        private bool mDisposeStream;

        private ArchiveWriter(Stream stream, bool dispose)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (!stream.CanWrite)
                throw new ArgumentException("Stream must be writeable.", nameof(stream));

            if (!stream.CanSeek)
                throw new ArgumentException("Stream must be seekable.", nameof(stream));

            mArchiveStream = stream;
            mDisposeStream = dispose;
            mMetadataPosition = ArchiveMetadataFormat.kHeaderLength;
            mMetadataLength = 0;
            mAppendPosition = ArchiveMetadataFormat.kHeaderLength;
            mMetadataChecksum = Checksum.GetEmptyStreamChecksum();
            mFileSections = ImmutableArray.CreateBuilder<ArchiveFileSection>();
            mDecoderSections = ImmutableArray.CreateBuilder<ArchiveDecoderSection>();
        }

        public void Dispose()
        {
            if (mDisposeStream)
                mArchiveStream.Dispose();

            throw new NotImplementedException();
        }

        /// <summary>
        /// Appends a new metadata section to the end of the file.
        /// </summary>
        public Task WriteMetadata(ArchiveMetadataProvider metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            int metadataCount = metadata.GetCount();
            if (metadataCount < 0)
                throw new InvalidOperationException(nameof(ArchiveMetadataProvider) + " returned negative count.");

            // TODO: wait for completion of pending writes

            mMetadataPosition = mAppendPosition;
            mArchiveStream.Position = mAppendPosition;

            var subStreamCount = mDecoderSections.Sum(x => x.Streams.Length);

            WriteToken(ArchiveMetadataToken.Header);

            if (mDecoderSections.Count > 0)
            {
                WriteToken(ArchiveMetadataToken.MainStreams);
                WritePackInfo();
                WriteUnpackInfo();
                WriteSubStreamsInfo();
                WriteToken(ArchiveMetadataToken.End);
            }

            if (subStreamCount > 0)
            {
                WriteToken(ArchiveMetadataToken.Files);
                WriteNumber(metadataCount);

                // TODO: empty streams
                // TODO: names
                // TODO: CTime
                // TODO: ATime
                // TODO: MTime
                // TODO: start positions
                // TODO: attributes

                WriteToken(ArchiveMetadataToken.End);
            }

            WriteToken(ArchiveMetadataToken.End);

            throw new NotImplementedException();
        }

        #region Writer - Structured Data

        private void WriteBitVector(IEnumerable<bool> bits)
        {
            byte b = 0;
            byte mask = 0x80;

            foreach (bool bit in bits)
            {
                if (bit)
                    b |= mask;

                mask >>= 1;

                if (mask == 0)
                {
                    WriteByte(b);
                    mask = 0x80;
                    b = 0;
                }
            }

            if (mask != 0x80)
                WriteByte(b);
        }

        private void WriteAlignedHeaderWithBitVector(IEnumerable<bool> bits, int vectorCount, int itemCount, ArchiveMetadataToken token, int itemSize)
        {
            var vectorSize = (itemCount == vectorCount) ? 0 : (vectorCount + 7) / 8;
            var contentSize = 2 + vectorSize + itemCount * itemSize;

            // Insert padding to align the begin of the content vector at a multiple of the given item size.
            WritePadding(3 + vectorSize + GetNumberSize(contentSize), itemSize);

            WriteToken(token);
            WriteNumber(contentSize);

            if (itemCount == vectorCount)
            {
                WriteByte(1); // all items defined == true
            }
            else
            {
                WriteByte(0); // all items defined == false, followed by a bitvector for the defined items
                WriteBitVector(bits);
            }

            WriteByte(0); // content vector is inline and not packed into a separate stream

            // caller inserts content vector (itemCount * itemSize) right behind this call
        }

        private void WriteChecksumVector(IEnumerable<Checksum?> checksums)
        {
            if (checksums.Any(x => x.HasValue))
            {
                WriteToken(ArchiveMetadataToken.CRC);

                if (checksums.All(x => x.HasValue))
                {
                    WriteByte(1);
                }
                else
                {
                    WriteByte(0);
                    WriteBitVector(checksums.Select(x => x.HasValue));
                }

                foreach (var checksum in checksums)
                    if (checksum.HasValue)
                        WriteInt32(checksum.Value.Value);
            }
        }

        private void WriteDateVector(IEnumerable<DateTime?> dates, ArchiveMetadataToken token)
        {
            WriteUInt64Vector(dates.Select(x => x.HasValue ? (ulong)x.Value.ToFileTimeUtc() : default(ulong?)), token);
        }

        private void WriteUInt64Vector(IEnumerable<ulong?> vector, ArchiveMetadataToken token)
        {
            var count = vector.Count();
            var defined = vector.Count(x => x.HasValue);

            if (defined > 0)
            {
                WriteAlignedHeaderWithBitVector(vector.Select(x => x.HasValue), count, defined, token, 8);
                System.Diagnostics.Debug.Assert((mArchiveStream.Position & 7) == 0);

                foreach (var slot in vector)
                    if (slot.HasValue)
                        WriteUInt64(slot.Value);
            }
        }

        private void WriteUInt32Vector(IEnumerable<uint?> vector, ArchiveMetadataToken token)
        {
            var count = vector.Count();
            var defined = vector.Count(x => x.HasValue);

            if (defined > 0)
            {
                WriteAlignedHeaderWithBitVector(vector.Select(x => x.HasValue), count, defined, token, 4);
                System.Diagnostics.Debug.Assert((mArchiveStream.Position & 3) == 0);

                foreach (var slot in vector)
                    if (slot.HasValue)
                        WriteInt32((int)slot.Value);
            }
        }

        private void WritePackInfo()
        {
            if (mFileSections.Count > 0)
            {
#if DEBUG
                System.Diagnostics.Debug.Assert(mFileSections[0].Offset == ArchiveMetadataFormat.kHeaderLength);
                for (int i = 1; i < mFileSections.Count; i++)
                    System.Diagnostics.Debug.Assert(mFileSections[i].Offset == mFileSections[i - 1].Offset + mFileSections[i - 1].Length);
#endif

                WriteToken(ArchiveMetadataToken.PackInfo);
                WriteNumber(mFileSections[0].Offset - ArchiveMetadataFormat.kHeaderLength);
                WriteNumber(mFileSections.Count);

                WriteToken(ArchiveMetadataToken.Size);
                foreach (var fileSection in mFileSections)
                    WriteNumber(fileSection.Length);

                WriteChecksumVector(mFileSections.Select(x => x.Checksum));

                WriteToken(ArchiveMetadataToken.End);
            }
        }

        private void WriteUnpackInfo()
        {
            if (mDecoderSections.Count > 0)
            {
                WriteToken(ArchiveMetadataToken.UnpackInfo);

                WriteToken(ArchiveMetadataToken.Folder);
                WriteNumber(mDecoderSections.Count);
                WriteByte(0);

                int index = 0;
                foreach (var decoder in mDecoderSections)
                    WriteDecoderSection(decoder, ref index);

                WriteToken(ArchiveMetadataToken.CodersUnpackSize);
                foreach (var decoder in mDecoderSections)
                    WriteNumber(decoder.Length);

                WriteChecksumVector(mDecoderSections.SelectMany(x => x.Streams).Select(x => x.Checksum));

                WriteToken(ArchiveMetadataToken.End);
            }
        }

        private void WriteDecoderSection(ArchiveDecoderSection definition, ref int firstStreamIndex)
        {
            WriteNumber(definition.Decoders.Length);

            var inputOffset = new int[definition.Decoders.Length];
            var outputOffset = new int[definition.Decoders.Length];

            for (int i = 1; i < definition.Decoders.Length; i++)
            {
                inputOffset[i] = inputOffset[i - 1] + definition.Decoders[i - 1].InputStreams.Length;
                outputOffset[i] = outputOffset[i - 1] + definition.Decoders[i - 1].OutputStreams.Length;
            }

            for (int i = 0; i < definition.Decoders.Length; i++)
            {
                var decoder = definition.Decoders[i];

                for (int j = 0; j < decoder.InputStreams.Length; j++)
                {
                    var input = decoder.InputStreams[j];

                    if (input.DecoderIndex.HasValue)
                    {
                        WriteNumber(inputOffset[i] + j);
                        WriteNumber(outputOffset[input.DecoderIndex.Value] + input.StreamIndex);
                    }
                }
            }

            int fileStreamSections = 0;

            foreach (var decoder in definition.Decoders)
            {
                foreach (var input in decoder.InputStreams)
                {
                    if (!input.DecoderIndex.HasValue)
                    {
                        WriteNumber(input.StreamIndex - firstStreamIndex);
                        fileStreamSections += 1;
                    }
                }
            }

            firstStreamIndex += fileStreamSections;
        }

        private void WriteSubStreamsInfo()
        {
            WriteToken(ArchiveMetadataToken.SubStreamsInfo);

            if (mDecoderSections.Any(x => x.Streams.Length != 1))
            {
                WriteToken(ArchiveMetadataToken.NumUnpackStream);
                foreach (var decoderSection in mDecoderSections)
                    WriteNumber(decoderSection.Streams.Length);
            }

            if (mDecoderSections.Any(x => x.Streams.Length > 1))
            {
                WriteToken(ArchiveMetadataToken.Size);
                foreach (var decoderSection in mDecoderSections)
                {
                    var decodedStreams = decoderSection.Streams;
                    for (int i = 0; i < decodedStreams.Length - 1; i++)
                        WriteNumber(decodedStreams[i].Length);
                }
            }

            WriteChecksumVector(
                from decoderSection in mDecoderSections
                where !(decoderSection.Streams.Length == 1 && decoderSection.Checksum.HasValue)
                from decodedStream in decoderSection.Streams
                select decodedStream.Checksum);

            WriteToken(ArchiveMetadataToken.End);
        }

        #endregion

        #region Writer - Raw Data

        private void WriteToken(ArchiveMetadataToken token)
        {
            System.Diagnostics.Debug.Assert(0 <= (int)token && (int)token <= 25);
            WriteByte((byte)token);
        }

        private void WritePadding(int offset, int alignment)
        {
            // 7-Zip 4.50 - 4.58 contain BUG, so they do not support .7z archives with Unknown field.

            offset = (int)(mArchiveStream.Position + offset) & (alignment - 1);

            if (offset > 0)
            {
                var padding = alignment - offset;

                if (padding < 2)
                    padding += alignment;

                padding -= 2;

                WriteToken(ArchiveMetadataToken.Padding);
                WriteByte((byte)padding);

                for (int i = 0; i < padding; i++)
                    WriteByte(0);
            }
        }

        private void WriteByte(byte value)
        {
            mArchiveStream.WriteByte(value);
        }

        private void WriteInt32(int value)
        {
            WriteByte((byte)value);
            WriteByte((byte)(value >> 8));
            WriteByte((byte)(value >> 16));
            WriteByte((byte)(value >> 24));
        }

        private void WriteUInt64(ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                WriteByte((byte)value);
                value >>= 8;
            }
        }

        private void WriteNumber(long value)
        {
            System.Diagnostics.Debug.Assert(value >= 0);
            throw new NotImplementedException();
        }

        private int GetNumberSize(long value)
        {
            System.Diagnostics.Debug.Assert(value >= 0);

            int length = 1;

            while (length < 9 && value < (1L << (length * 7)))
                length++;

            return length;
        }

        #endregion

        /// <summary>
        /// Updates the file header to refer to the last written metadata section.
        /// </summary>
        public async Task WriteHeader()
        {
            // TODO: wait for completion of pending metadata write (if there is any)
            // TODO: discard data from sessions which were started after writing metadata

            var header = PrepareHeader();
            mArchiveStream.Position = 0;
            await mArchiveStream.WriteAsync(header, 0, header.Length);
        }

        public EncoderSession BeginEncoding(EncoderDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var storage = new Stream[definition.StorageCount];
            for (int i = 0; i < storage.Length; i++)
                storage[i] = mStreamProvider?.CreateBufferStream() ?? new MemoryStream();

            var session = definition.CreateEncoderSession(this, mDecoderSections.Count, storage);
            mDecoderSections.Add(null);
            mEncoderSessions.Add(session);
            return session;
        }

        /// <summary>
        /// Allows to copy complete sections from existing archives into this archive.
        /// </summary>
        public async Task TransferSectionAsync(Stream stream, ArchiveMetadata metadata, int section)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            if (section < 0 || section >= metadata.DecoderSections.Length)
                throw new ArgumentOutOfRangeException(nameof(section));

            var decoderSection = metadata.DecoderSections[section];
            var count = decoderSection.Streams.Length;
            if (count == 0)
                throw new InvalidOperationException();

            // TODO: wait for pending writes
            // TODO: translate and append decoder section

            foreach (var decoder in decoderSection.Decoders)
            {
                foreach (var input in decoder.InputStreams)
                {
                    if (!input.DecoderIndex.HasValue)
                    {
                        var fileSection = metadata.FileSections[input.StreamIndex];
                        var offset = mAppendPosition;
                        var length = fileSection.Length;
                        mAppendPosition = checked(offset + length);
                        mFileSections.Add(new ArchiveFileSection(offset, length, fileSection.Checksum));
                        using (var fileSectionStream = new ConstrainedReadStream(mArchiveStream, fileSection.Offset, fileSection.Length))
                            await fileSectionStream.CopyToAsync(mArchiveStream);
                    }
                }
            }
        }

        /// <summary>
        /// Allows to copy partial sections from an existing archive, reencoding selected entries on the fly.
        /// </summary>
        public Task TranscodeSectionAsync(Stream stream, ArchiveMetadata metadata, int section, Func<int, Task<bool>> selector, EncoderDefinition definition)
        {
            throw new NotImplementedException();
        }

        #region Internal Methods - Encoder Session

        private byte[] PrepareHeader()
        {
            uint crc = LZMA.Master.SevenZip.CRC.kInitCRC;
            crc = LZMA.Master.SevenZip.CRC.Update(crc, mMetadataPosition);
            crc = LZMA.Master.SevenZip.CRC.Update(crc, mMetadataLength);
            crc = LZMA.Master.SevenZip.CRC.Update(crc, mMetadataChecksum.Value);
            crc = LZMA.Master.SevenZip.CRC.Finish(crc);

            var buffer = new byte[ArchiveMetadataFormat.kHeaderLength];

            var signature = ArchiveMetadataFormat.kFileSignature;
            for (int i = 0; i < signature.Length; i++)
                buffer[i] = signature[i];

            buffer[6] = kMajorVersion;
            buffer[7] = kMinorVersion;
            PutInt32(buffer, 8, (int)crc);
            PutInt64(buffer, 12, mMetadataPosition);
            PutInt64(buffer, 20, mMetadataLength);
            PutInt32(buffer, 28, mMetadataChecksum.Value);

            return buffer;
        }

        internal void CompleteEncoderSession(EncoderSession session, int section, ArchiveDecoderSection definition, EncoderStorage[] storageList)
        {
            if (!mEncoderSessions.Remove(session))
                throw new InternalFailureException();

            mDecoderSections.Add(definition);

            // TODO: we can write storage lazily (just remember the streams in a list) and don't have to block the caller

            foreach (var storage in storageList)
            {
                var stream = storage.GetFinalStream();
                var offset = mAppendPosition;
                var length = stream.Length;
                mAppendPosition = checked(offset + length);
                var checksum = storage.GetFinalChecksum();
                mFileSections.Add(new ArchiveFileSection(offset, length, checksum));
                mArchiveStream.Position = offset;
                stream.Position = 0;
                stream.CopyTo(mArchiveStream);
            }
        }

        #endregion
    }

    public abstract class ArchiveWriterStreamProvider
    {
        public abstract Stream CreateBufferStream();
    }

    public abstract class ArchiveMetadataProvider
    {
        public abstract int GetCount();
        public abstract string GetName(int index);
        public abstract bool HasStream(int index);
        public abstract bool IsDirectory(int index);
        public abstract bool IsDeleted(int index);
        public abstract long GetLength(int index);
        public abstract Checksum? GetChecksum(int index);
        public abstract FileAttributes? GetAttributes(int index);
        public abstract DateTime? GetCreationDate(int index);
        public abstract DateTime? GetLastWriteDate(int index);
        public abstract DateTime? GetLastAccessDate(int index);
    }
}
