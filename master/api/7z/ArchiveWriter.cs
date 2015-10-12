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

        public void AppendFile(FileInfo file, DirectoryInfo root)
        {
            throw new NotImplementedException();
        }

        public void AppendFile(FileInfo file, string name)
        {
            using (var stream = file.OpenRead())
                AppendFile(stream, name, true, file.Attributes, file.CreationTimeUtc, file.LastWriteTimeUtc, file.LastAccessTimeUtc);
        }

        public void AppendFile(Stream stream, string name, bool checksum, FileAttributes? attributes, DateTime? creationTime, DateTime? lastWriteTime, DateTime? lastAccessTime)
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
        private ArchiveEncoderOutputSlot mContent;
        private List<ArchiveEncoderNode> mEncoders;
        private List<ArchiveEncoderInputSlot> mStorage;
        private bool mComplete;

        public int EncoderCount => mEncoders.Count;
        public int StorageCount => mStorage.Count;

        public ArchiveEncoderDefinition()
        {
            mContent = new ArchiveEncoderOutputSlot(this);
            mEncoders = new List<ArchiveEncoderNode>();
            mStorage = new List<ArchiveEncoderInputSlot>();
        }

        public ArchiveEncoderNode GetEncoder(int index)
        {
            if (index < 0 || index >= mEncoders.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return mEncoders[index];
        }

        public ArchiveEncoderInputSlot GetStorage(int index)
        {
            if (index < 0 || index >= mStorage.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return mStorage[index];
        }

        public ArchiveEncoderOutputSlot GetContentSource()
        {
            return mContent;
        }

        private void CheckComplete()
        {
            if (mComplete)
                throw new InvalidOperationException("Complete encoder definitions cannot be modified.");
        }

        public ArchiveEncoderNode CreateEncoder(ArchiveEncoderSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            CheckComplete();

            var encoder = new ArchiveEncoderNode(this, mEncoders.Count, settings);
            mEncoders.Add(encoder);
            return encoder;
        }

        public ArchiveEncoderInputSlot CreateStorageSink()
        {
            CheckComplete();

            var storage = new ArchiveEncoderInputSlot(this, mStorage.Count);
            mStorage.Add(storage);
            return storage;
        }

        public void Connect(ArchiveEncoderOutputSlot source, ArchiveEncoderInputSlot target)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (target == null)
                throw new ArgumentNullException(nameof(target));

            if (source.Definition != this)
                throw new ArgumentException("Data source belongs to a different encoder definition.", nameof(source));

            if (target.Definition != this)
                throw new ArgumentException("Data target belongs to a different encoder definition.", nameof(target));

            if (source.IsConnected)
                throw new ArgumentException("Data source is already connected.", nameof(source));

            if (target.IsConnected)
                throw new ArgumentException("Data target is already connected.", nameof(target));

            CheckComplete();

            source.ConnectTo(target);
            target.ConnectTo(source);
        }

        public void Complete()
        {
            if (!mComplete)
            {
                if (mEncoders.Count == 0)
                    throw new InvalidOperationException("No encoders defined.");

                if (!mContent.IsConnected)
                    throw new InvalidOperationException("Content source not connected.");

                for (int i = 0; i < mEncoders.Count; i++)
                {
                    var encoder = mEncoders[i];

                    for (int j = 0; j < encoder.InputCount; j++)
                        if (!encoder.GetInput(j).IsConnected)
                            throw new InvalidOperationException(FormattableString.Invariant($"Missing input connection #{j} for encoder #{i}."));

                    for (int j = 0; j < encoder.OutputCount; j++)
                        if (!encoder.GetOutput(j).IsConnected)
                            throw new InvalidOperationException(FormattableString.Invariant($"Missing output connection #{j} for encoder #{i}."));
                }

                mComplete = true;
            }
        }
    }

    public abstract class ArchiveEncoderSettings
    {
        internal ArchiveEncoderSettings() { }
        internal abstract CompressionMethod GetDecoderType();
        internal int GetInputSlots() => GetDecoderType().GetOutputCount(); // encoder input = decoder output
        internal int GetOutputSlots() => GetDecoderType().GetInputCount(); // encoder output = decoder input
    }

    public sealed class ArchiveEncoderNode
    {
        private readonly ArchiveEncoderDefinition mDefinition;
        private readonly ArchiveEncoderSettings mSettings;
        private readonly ArchiveEncoderInputSlot[] mInputSlots;
        private readonly ArchiveEncoderOutputSlot[] mOutputSlots;
        private readonly int mIndex;

        public ArchiveEncoderDefinition Definition => mDefinition;
        public int Index => mIndex;
        public ArchiveEncoderSettings Settings => mSettings;
        public int InputCount => mInputSlots.Length;
        public int OutputCount => mOutputSlots.Length;

        internal ArchiveEncoderNode(ArchiveEncoderDefinition definition, int index, ArchiveEncoderSettings settings)
        {
            mDefinition = definition;
            mIndex = index;
            mSettings = settings;

            mInputSlots = new ArchiveEncoderInputSlot[settings.GetInputSlots()];
            for (int i = 0; i < mInputSlots.Length; i++)
                mInputSlots[i] = new ArchiveEncoderInputSlot(this, i);

            mOutputSlots = new ArchiveEncoderOutputSlot[settings.GetOutputSlots()];
            for (int i = 0; i < mOutputSlots.Length; i++)
                mOutputSlots[i] = new ArchiveEncoderOutputSlot(this, i);
        }

        public ArchiveEncoderInputSlot GetInput(int index)
        {
            return mInputSlots[index];
        }

        public ArchiveEncoderOutputSlot GetOutput(int index)
        {
            return mOutputSlots[index];
        }
    }

    public sealed class ArchiveEncoderInputSlot
    {
        private readonly ArchiveEncoderDefinition mDefinition;
        private readonly ArchiveEncoderNode mNode;
        private ArchiveEncoderOutputSlot mSource;
        private readonly int mIndex;

        public ArchiveEncoderDefinition Definition => mDefinition;
        public ArchiveEncoderNode Node => mNode;
        public int Index => mIndex;
        public bool IsStorage => mNode == null;
        public bool IsConnected => mSource != null;
        public ArchiveEncoderOutputSlot Source => mSource;

        internal ArchiveEncoderInputSlot(ArchiveEncoderDefinition definition, int index)
        {
            mDefinition = definition;
            mIndex = index;
        }

        internal ArchiveEncoderInputSlot(ArchiveEncoderNode node, int index)
        {
            mDefinition = node.Definition;
            mNode = node;
            mIndex = index;
        }

        internal void ConnectTo(ArchiveEncoderOutputSlot source)
        {
            mSource = source;
        }
    }

    public sealed class ArchiveEncoderOutputSlot
    {
        private readonly ArchiveEncoderDefinition mDefinition;
        private readonly ArchiveEncoderNode mNode;
        private ArchiveEncoderInputSlot mTarget;
        private readonly int mIndex;

        public ArchiveEncoderDefinition Definition => mDefinition;
        public ArchiveEncoderNode Node => mNode;
        public int Index => mIndex;
        public bool IsContent => mNode == null;
        public bool IsConnected => mTarget != null;
        public ArchiveEncoderInputSlot Target => mTarget;

        internal ArchiveEncoderOutputSlot(ArchiveEncoderDefinition definition)
        {
            mDefinition = definition;
        }

        internal ArchiveEncoderOutputSlot(ArchiveEncoderNode node, int index)
        {
            mDefinition = node.Definition;
            mNode = node;
            mIndex = index;
        }

        internal void ConnectTo(ArchiveEncoderInputSlot target)
        {
            mTarget = target;
        }
    }
}
