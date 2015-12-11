using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ManagedLzma.SevenZip.Metadata;
using ManagedLzma.SevenZip.Reader;

namespace ManagedLzma.SevenZip.FileModel
{
    public sealed class ArchiveFileModel
    {
        /// <summary>The metadata of the archive.</summary>
        public ArchiveMetadata Metadata { get; }

        public ArchivedFolder RootFolder { get; }

        internal ArchiveFileModel(ArchivedFolder rootFolder, ArchiveMetadata metadata)
        {
            this.RootFolder = rootFolder;
            this.Metadata = metadata;
        }
    }

    public abstract class ArchivedItem
    {
        public abstract class Builder
        {
            public string Name { get; set; }

            internal Builder() { }

            internal Builder(ArchivedItem original)
            {
                this.Name = original.Name;
            }

            internal abstract ArchivedItem ToImmutableCore();
            public ArchivedItem ToImmutable() => ToImmutableCore();
        }

        public string Name { get; }

        internal ArchivedItem(string Name)
        {
            this.Name = Name;
        }
    }

    public sealed class ArchivedFile : ArchivedItem
    {
        public new sealed class Builder : ArchivedItem.Builder
        {
            public long Length { get; set; }
            public Checksum? Checksum { get; set; }
            public FileAttributes? Attributes { get; set; }
            public DateTime? Creation { get; set; }
            public DateTime? LastWrite { get; set; }
            public DateTime? LastAccess { get; set; }

            public Builder() { }

            public Builder(ArchivedFile original)
                : base(original)
            {
                Length = original.Length;
                Checksum = original.Checksum;
                Attributes = original.Attributes;
                Creation = original.Creation;
                LastWrite = original.LastWrite;
                LastAccess = original.LastAccess;
            }

            internal override ArchivedItem ToImmutableCore() => ToImmutable();
            public new ArchivedFile ToImmutable() => new ArchivedFile(Name, Length, Checksum, Attributes, Creation, LastWrite, LastAccess);
        }

        public long Length { get; }
        public Checksum? Checksum { get; }
        public FileAttributes? Attributes { get; }
        public DateTime? Creation { get; }
        public DateTime? LastWrite { get; }
        public DateTime? LastAccess { get; }

        public ArchivedFile(string Name, long Length, Checksum? Checksum, FileAttributes? Attributes, DateTime? Creation, DateTime? LastWrite, DateTime? LastAccess)
            : base(Name)
        {
            this.Length = Length;
            this.Checksum = Checksum;
            this.Attributes = Attributes;
            this.Creation = Creation;
            this.LastWrite = LastWrite;
            this.LastAccess = LastAccess;
        }
    }

    public sealed class ArchivedFolder : ArchivedItem
    {
        public new sealed class Builder : ArchivedItem.Builder
        {
            private ArchivedFolder mOriginal;
            private ImmutableList<ArchivedItem>.Builder mItemsBuilder;
            public ImmutableList<ArchivedItem>.Builder Items => mItemsBuilder ?? CreateItemsBuilder();

            private ImmutableList<ArchivedItem>.Builder CreateItemsBuilder()
            {
                if (mOriginal == null)
                    mItemsBuilder = ImmutableList.CreateBuilder<ArchivedItem>();
                else
                    mItemsBuilder = mOriginal.Items.ToBuilder();

                return mItemsBuilder;
            }

            private ImmutableList<ArchivedItem> GetImmutableItems()
            {
                if (mItemsBuilder != null)
                    return mItemsBuilder.ToImmutable();
                else if (mOriginal != null)
                    return mOriginal.Items;
                else
                    return ImmutableList<ArchivedItem>.Empty;
            }

            public Builder() { }

            public Builder(ArchivedFolder original)
                : base(original)
            {
                this.mOriginal = original;
            }

            internal override ArchivedItem ToImmutableCore() => ToImmutable();
            public new ArchivedFolder ToImmutable() => new ArchivedFolder(Name, GetImmutableItems());
        }

        public ImmutableList<ArchivedItem> Items { get; }

        public ArchivedFolder(string Name, ImmutableList<ArchivedItem> Items)
            : base(Name)
        {
            this.Items = Items;
        }
    }

    /// <summary>
    /// Reads archive metadata and constructs a class hierarchy of the contained files.
    /// </summary>
    public sealed class ArchiveFileModelMetadataReader : ArchiveMetadataReader
    {
        private List<string> mFileNames;
        private List<long?> mOffsets;
        private List<FileAttributes?> mAttributes;
        private List<DateTime?> mCDates;
        private List<DateTime?> mMDates;
        private List<DateTime?> mADates;
        private ArchivedFolder.Builder mRootFolder;

        public ArchiveFileModel ReadMetadata(Stream stream) => ReadMetadata(stream, null);

        public ArchiveFileModel ReadMetadata(Stream stream, PasswordStorage password)
        {
            if (mRootFolder != null)
                throw new InvalidOperationException("Recursive invocation.");

            try
            {
                mRootFolder = new ArchivedFolder.Builder();
                var metadata = ReadMetadataCore(stream, password);
                return new ArchiveFileModel(mRootFolder.ToImmutable(), metadata);
            }
            finally
            {
                // clean up in case of exceptions
                mRootFolder = null;
            }
        }

        protected override void ReadNames(MetadataStringReader data)
        {
            mFileNames = new List<string>(data.Count);
            for (int i = 0; i < data.Count; i++)
                mFileNames.Add(data.ReadString());
        }

        protected override void ReadEmptyFileMarkers(MetadataBitReader data)
        {
        }

        protected override void ReadEmptyStreamMarkers(MetadataBitReader data)
        {
        }

        protected override void ReadRemovedFileMarkers(MetadataBitReader data)
        {
        }

        protected override void ReadOffsets(MetadataNumberReader data)
        {
            mOffsets = new List<long?>(data.Count);
            for (int i = 0; i < data.Count; i++)
                mOffsets.Add(data.ReadNumber());
        }

        protected override void ReadAttributes(MetadataAttributeReader data)
        {
            mAttributes = new List<FileAttributes?>();
            for (int i = 0; i < data.Count; i++)
                mAttributes.Add(data.ReadAttributes());
        }

        protected override void ReadCTime(MetadataDateReader data)
        {
            mCDates = new List<DateTime?>(data.Count);
            for (int i = 0; i < data.Count; i++)
                mCDates.Add(data.ReadDate());
        }

        protected override void ReadMTime(MetadataDateReader data)
        {
            mMDates = new List<DateTime?>(data.Count);
            for (int i = 0; i < data.Count; i++)
                mMDates.Add(data.ReadDate());
        }

        protected override void ReadATime(MetadataDateReader data)
        {
            mADates = new List<DateTime?>(data.Count);
            for (int i = 0; i < data.Count; i++)
                mADates.Add(data.ReadDate());
        }
    }
}
