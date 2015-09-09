using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            throw new NotImplementedException();
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
    }

    public sealed class ArchiveEncoderSession : IDisposable
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

        public void AppendFile(FileInfo file, string name)
        {
            throw new NotImplementedException();
        }

        public void AppendFile(Stream stream, string name, bool checksum)
        {
            throw new NotImplementedException();
        }
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

    public sealed class ArchiveEncoderDefinition
    {
        public void Connect(ArchiveEncoderDataSink sink, ArchiveEncoderDataSource source)
        {
            if (sink == null)
                throw new ArgumentNullException(nameof(sink));

            if (source == null)
                throw new ArgumentNullException(nameof(source));

            throw new NotImplementedException();
        }

        public ArchiveEncoder CreateEncoder(ArchiveEncoderType type)
        {
            throw new NotImplementedException();
        }

        public ArchiveEncoderDataSink GetFinalOutputSink()
        {
            throw new NotImplementedException();
        }

        public ArchiveEncoderDataSource CreateStorageStream()
        {
            throw new NotImplementedException();
        }
    }

    public enum ArchiveEncoderType { }

    public sealed class ArchiveEncoder
    {
        internal ArchiveEncoderDefinition Graph { get; }

        internal ArchiveEncoder(ArchiveEncoderDefinition graph)
        {
            this.Graph = graph;

            throw new NotImplementedException();
        }

        public ArchiveEncoderDataSink GetInput(int index)
        {
            throw new NotImplementedException();
        }

        public ArchiveEncoderDataSource GetOutput(int index)
        {
            throw new NotImplementedException();
        }
    }

    public sealed class ArchiveEncoderDataSink { }
    public sealed class ArchiveEncoderDataSource { }
}
