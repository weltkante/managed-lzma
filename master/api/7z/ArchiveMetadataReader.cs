using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
    public struct MetadataStringReader
    {
    }

    public struct MetadataDateReader
    {
    }

    public struct MetadataChecksumReader
    {
    }

    /// <summary>
    /// Allows subclasses to construct custom archive models from metadata.
    /// </summary>
    /// <remarks>
    /// Archives may contain a large number of files listed in their metadata stream.
    /// Reading all this file metadata into a class based in-memory model may carry significant
    /// memory overhead. Creating a subclass allows you to construct your own model of the archive
    /// metadata containing exactly the information you want and in exactly the format you want.
    /// It is also possible to entirely skip building an in-memory model of the file metadata.
    /// </remarks>
    public abstract class ArchiveMetadataReader
    {
        protected virtual void ReadFilenameVector(MetadataStringReader data) { }
        protected virtual void ReadDateVector(MetadataDateReader data) { }
        protected virtual void ReadChecksumVector(MetadataChecksumReader data) { }

        protected ArchiveMetadata ReadMetadataCore(Stream stream)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Reads only the header of the archive metadata and discards file specific information.
    /// </summary>
    public class ArchiveHeaderMetadataReader : ArchiveMetadataReader
    {
        public ArchiveMetadata ReadMetadata(Stream stream)
        {
            return ReadMetadataCore(stream);
        }
    }
}
