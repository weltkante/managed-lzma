using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
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

        internal ArchiveEncoderSession CreateEncoderSession(ArchiveWriter writer, Stream[] storage)
        {
            if (!mComplete)
                throw new InvalidOperationException("Incomplete ArchiveEncoderDefinition.");

            if (storage == null)
                throw new ArgumentNullException(nameof(storage));

            if (storage.Length != mStorage.Count)
                throw new ArgumentException("Number of provided storage streams does not match number of declared storage streams.", nameof(storage));

            var storageStreams = new ArchiveEncoderStorage[storage.Length];
            for (int i = 0; i < storage.Length; i++)
                storageStreams[i] = new ArchiveEncoderStorage(storage[i]);

            int totalInputCount = 0;
            var firstInputOffset = new int[mEncoders.Count];
            for (int i = 0; i < mEncoders.Count; i++)
            {
                firstInputOffset[i] = totalInputCount;
                totalInputCount += mEncoders[i].InputCount;
            }

            var contentTarget = mContent.Target;
            var contentIndex = firstInputOffset[contentTarget.Node.Index] + contentTarget.Index;
            var contentStream = new ArchiveEncoderInput();

            var linkedStreams = new ArchiveEncoderConnection[totalInputCount];
            for (int i = 0; i < totalInputCount; i++)
                if (i != contentIndex)
                    linkedStreams[i] = new ArchiveEncoderConnection();

            var encoders = new IDisposable[mEncoders.Count];
            for (int i = 0; i < mEncoders.Count; i++)
            {
                var inputStreams = new IArchiveEncoderInputStream[mEncoders[i].InputCount];
                for (int j = 0; j < inputStreams.Length; j++)
                {
                    var source = mEncoders[i].GetInput(j).Source;
                    if (source.IsContent)
                        inputStreams[j] = contentStream;
                    else
                        inputStreams[j] = linkedStreams[firstInputOffset[i] + j];
                }

                var outputStreams = new IArchiveEncoderOutputStream[mEncoders[i].OutputCount];
                for (int j = 0; j < outputStreams.Length; j++)
                {
                    var target = mEncoders[i].GetOutput(j).Target;
                    if (target.IsStorage)
                        outputStreams[j] = storageStreams[target.Index];
                    else
                        outputStreams[j] = linkedStreams[firstInputOffset[target.Node.Index] + target.Index];
                }

                encoders[i] = mEncoders[i].Settings.CreateEncoder(inputStreams, outputStreams);
            }

            return new ArchiveEncoderSession(writer, encoders, contentStream);
        }
    }

    public abstract class ArchiveEncoderSettings
    {
        internal ArchiveEncoderSettings() { }
        internal abstract CompressionMethod GetDecoderType();
        internal abstract IDisposable CreateEncoder(IArchiveEncoderInputStream[] input, IArchiveEncoderOutputStream[] output);
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
