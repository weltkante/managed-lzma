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

        public static ArchiveWriter Create(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (!stream.CanRead)
                throw new ArgumentException("Stream must be readable.", nameof(stream));

            if (!stream.CanSeek)
                throw new ArgumentException("Stream must be seekable.", nameof(stream));

            if (!stream.CanWrite)
                throw new ArgumentException("Stream must be writeable.", nameof(stream));

            stream.SetLength(ArchiveMetadataReader.HeaderLength);
            var writer = new ArchiveWriter(stream, new ArchiveMetadata());
            writer.WriteHeader();
            return writer;
        }

        public static ArchiveWriter Open(Stream stream, ArchiveMetadata metadata)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (!stream.CanRead)
                throw new ArgumentException("Stream must be readable.", nameof(stream));

            if (!stream.CanSeek)
                throw new ArgumentException("Stream must be seekable.", nameof(stream));

            if (!stream.CanWrite)
                throw new ArgumentException("Stream must be writeable.", nameof(stream));

            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            return new ArchiveWriter(stream, metadata);
        }

        private const byte kMajorVersion = 0;
        private const byte kMinorVersion = 3;

        private Stream mArchiveStream;
        private ImmutableArray<ArchiveFileSection>.Builder mFileSections;
        private ImmutableArray<ArchiveDecoderSection>.Builder mDecoderSections;
        private ArchiveWriterStreamProvider mStreamProvider;
        private IArchiveMetadataStorage mMetadataStorage;
        private List<EncoderSession> mEncoderSessions = new List<EncoderSession>();
        private long mAppendPosition;

        private ArchiveWriter(Stream stream, ArchiveMetadata metadata)
        {
            mArchiveStream = stream;
            mFileSections = metadata.FileSections.ToBuilder();
            mDecoderSections = metadata.DecoderSections.ToBuilder();

            var lastSection = mFileSections.LastOrDefault();
            if (lastSection != null)
                mAppendPosition = lastSection.Offset + lastSection.Length;
            else
                mAppendPosition = ArchiveMetadataReader.HeaderLength;
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes existing metadata from the end of the file to reduce file size when modifying an existing archive.
        /// This modifies the file header, terminating the process without writing new metadata means the archive can no longer be opened.
        /// </summary>
        public void DiscardMetadata()
        {
            // TODO: Get rid of this method? I think it is not possible to have gaps in the file streams
            //       anyways so you don't get away with leaving the original metadata streams intact.

            throw new NotImplementedException();
        }

        /// <summary>
        /// Appends a new metadata section to the end of the file.
        /// </summary>
        public void WriteMetadata()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Updates the file header to refer to the last written metadata section.
        /// </summary>
        public void WriteHeader()
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

            mArchiveStream.Position = 0;
            mArchiveStream.Write(buffer, 0, buffer.Length);
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
        public TransferSession BeginTransfer()
        {
            // TODO: parameters
            // - source archive from which to copy
            // - specify which subarchive to copy
            // - interface to resolve filename conflicts (cannot be null)
            //   + alternate overload which takes a boolean "overwrite = true/false"
            throw new NotImplementedException();
        }

        /// <summary>
        /// Can be used to include an empty directory in the archive metadata.
        /// Does not need to be called for directories which are not empty.
        /// </summary>
        /// <param name="name">The name of the directory relative to the archive root.</param>
        public void CreateEmptyDirectory(string name)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Can be used to remove an existing file or directory from archive metadata.
        /// Removing a directory also removes all contained archive entries.
        /// </summary>
        /// <param name="name">The name of the archive entry relative to the archive root.</param>
        public void DeleteArchiveEntry(string name)
        {
            throw new NotImplementedException();
        }

        #region Internal Methods - Encoder Session

        internal void AppendFileInternal(int section, string name, long length, Checksum? checksum, FileAttributes? attributes, DateTime? creationDate, DateTime? lastWriteDate, DateTime? lastAccessDate)
        {
            mMetadataStorage.AppendFile(section, name, length, checksum, attributes, creationDate, lastWriteDate, lastAccessDate);
        }

        internal void AppendEmptyFileInternal(int section, string name, FileAttributes? attributes, DateTime? creationDate, DateTime? lastWriteDate, DateTime? lastAccessDate)
        {
            mMetadataStorage.AppendEmptyFile(section, name, attributes, creationDate, lastWriteDate, lastAccessDate);
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

    public abstract class ArchiveWriterMetadataProvider
    {
        public abstract string GetName(int section, int index);
        public abstract bool HasStream(int section, int index);
        public abstract bool IsDirectory(int section, int index);
        public abstract bool IsDeleted(int section, int index);
        public abstract long GetLength(int section, int index);
        public abstract Checksum? GetChecksum(int section, int index);
        public abstract FileAttributes? GetAttributes(int section, int index);
        public abstract DateTime? GetCreationDate(int section, int index);
        public abstract DateTime? GetLastWriteDate(int section, int index);
        public abstract DateTime? GetLastAccessDate(int section, int index);
    }

    public interface IArchiveMetadataStorage
    {
        void AppendFile(int section, string name, long length, Checksum? checksum, FileAttributes? attributes, DateTime? creationDate, DateTime? lastWriteDate, DateTime? lastAccessDate);
        void AppendEmptyFile(int section, string name, FileAttributes? attributes, DateTime? creationDate, DateTime? lastWriteDate, DateTime? lastAccessDate);
        void AppendEmptyDirectory(int section, string name);
    }

    public sealed class TransferSession : IDisposable
    {
        // TODO: awaitable

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public void Discard()
        {
            throw new NotImplementedException();
        }

        public void AppendSection(Stream stream, ArchiveMetadata metadata, int section, ArchiveWriterMetadataProvider provider)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            if (section < 0 || section >= metadata.DecoderSections.Length)
                throw new ArgumentOutOfRangeException(nameof(section));

            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            var decoderSection = metadata.DecoderSections[section];
            var count = decoderSection.Streams.Length;
            if (count == 0)
                return;

            for (int i = 0; i < count; i++)
            {
                var name = provider.GetName(section, i);

                // ...
            }

            throw new NotImplementedException();
        }
    }
}
