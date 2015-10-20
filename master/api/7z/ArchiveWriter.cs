using System;
using System.Collections.Generic;
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
        public static ArchiveWriter Create(Stream output)
        {
            throw new NotImplementedException();
        }

        public static ArchiveWriter Open(Stream stream)
        {
            throw new NotImplementedException();
        }

        private Stream mArchiveStream;
        private ArchiveWriterStreamProvider mStreamProvider;
        private List<ArchiveEncoderSession> mEncoderSessions = new List<ArchiveEncoderSession>();
        private List<Stream> mPendingStreams = new List<Stream>();

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
        }

        /// <summary>
        /// Appends a new metadata section to the end of the file.
        /// </summary>
        public void WriteMetadata()
        {
        }

        /// <summary>
        /// Updates the file header to refer to the last written metadata section.
        /// </summary>
        public void WriteHeader()
        {
        }

        public ArchiveEncoderSession BeginEncoding(ArchiveEncoderDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var storage = new Stream[definition.StorageCount];
            for (int i = 0; i < storage.Length; i++)
                storage[i] = mStreamProvider?.CreateBufferStream() ?? new MemoryStream();

            var session = definition.CreateEncoderSession(this, storage);
            mEncoderSessions.Add(session);
            return session;
        }

        /// <summary>
        /// Copies a complete section from an existing archive into this archive.
        /// </summary>
        public ArchiveTransferSession BeginTransfer()
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

        internal void CompleteEncoderSession(ArchiveEncoderSession session, Stream[] storage)
        {
            if (!mEncoderSessions.Remove(session))
                throw new InternalFailureException();

            mPendingStreams.AddRange(storage);
        }
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

    public sealed class ArchiveTransferSession : IDisposable
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
    }
}
