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
                stream.SetLength(ArchiveMetadataReader.HeaderLength);
                stream.Write(writer.PrepareHeader(), 0, ArchiveMetadataReader.HeaderLength);

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
                stream.SetLength(ArchiveMetadataReader.HeaderLength);
                await stream.WriteAsync(writer.PrepareHeader(), 0, ArchiveMetadataReader.HeaderLength);

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
            mAppendPosition = ArchiveMetadataReader.HeaderLength;
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
            throw new NotImplementedException();
        }

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
            long metadataOffset = ArchiveMetadataReader.HeaderLength;
            long metadataLength = 0;
            Checksum metadataChecksum = new Checksum((int)LZMA.Master.SevenZip.CRC.Finish(LZMA.Master.SevenZip.CRC.kInitCRC));

            uint crc = LZMA.Master.SevenZip.CRC.kInitCRC;
            crc = LZMA.Master.SevenZip.CRC.Update(crc, metadataOffset);
            crc = LZMA.Master.SevenZip.CRC.Update(crc, metadataLength);
            crc = LZMA.Master.SevenZip.CRC.Update(crc, metadataChecksum.Value);
            crc = LZMA.Master.SevenZip.CRC.Finish(crc);

            var buffer = new byte[ArchiveMetadataReader.HeaderLength];

            var signature = ArchiveMetadataReader.FileSignature;
            for (int i = 0; i < signature.Length; i++)
                buffer[i] = signature[i];

            buffer[6] = kMajorVersion;
            buffer[7] = kMinorVersion;
            PutInt32(buffer, 8, (int)crc);
            PutInt64(buffer, 12, metadataOffset);
            PutInt64(buffer, 20, metadataLength);
            PutInt32(buffer, 28, metadataChecksum.Value);

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
