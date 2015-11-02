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
    /// <summary>
    /// Transfers external input into an encoder.
    /// </summary>
    internal sealed class EncoderInput : IStreamReader
    {
        #region Implementation

        private EncoderSession mSession;
        private IStreamWriter mEncoderInput;
        private Task mTransferTask;

        internal void Connect(EncoderSession session)
        {
            mSession = session;
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public int GetFinalLength()
        {
            throw new NotImplementedException();
        }

        public Checksum? GetFinalChecksum()
        {
            throw new NotImplementedException();
        }

        public void SetInputStream(EncoderNode encoder, int index)
        {
            System.Diagnostics.Debug.Assert(mEncoderInput == null);

            mEncoderInput = encoder.GetInputSink(index);
            if (mEncoderInput == null)
                encoder.SetInputSource(index, this);
        }

        public void Start()
        {
            if (mEncoderInput != null)
                mTransferTask = Task.Run(TransferLoop);
        }

        private async Task TransferLoop()
        {
            System.Diagnostics.Debug.WriteLine("PERF: EncoderInput enters transfer loop. Avoid this if possible.");

            var buffer = new byte[0x10000];
            for (;;)
            {
                var fetched = await mSession.ReadInternalAsync(buffer, 0, buffer.Length, StreamMode.Partial).ConfigureAwait(false);
                System.Diagnostics.Debug.Assert(0 <= fetched && fetched <= buffer.Length);
                if (fetched == 0)
                {
                    await mEncoderInput.CompleteAsync().ConfigureAwait(false);
                    return;
                }

                int offset = 0;
                while (fetched > 0)
                {
                    var written = await mEncoderInput.WriteAsync(buffer, offset, fetched, StreamMode.Complete).ConfigureAwait(false);
                    System.Diagnostics.Debug.Assert(0 < written && written <= fetched);
                    offset += written;
                    fetched -= written;
                }
            }
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            System.Diagnostics.Debug.Assert(mEncoderInput == null);

            return mSession.ReadInternalAsync(buffer, offset, length, mode);
        }

        #endregion
    }

    /// <summary>
    /// Transfers encoder output into external storage.
    /// </summary>
    internal sealed class EncoderStorage : IStreamWriter
    {
        #region Implementation

        private Stream mStream;
        private IStreamReader mEncoderOutput;
        private Task mTransferTask;
        private bool mComplete;

        public EncoderStorage(Stream stream)
        {
            mStream = stream;
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public Stream GetFinalStream()
        {
            return mStream;
        }

        public Checksum? GetFinalChecksum()
        {
            throw new NotImplementedException();
        }

        public void SetOutputStream(EncoderNode encoder, int index)
        {
            mEncoderOutput = encoder.GetOutputSource(index);
            if (mEncoderOutput == null)
                encoder.SetOutputSink(index, this);
        }

        public void Start()
        {
            if (mEncoderOutput != null)
                mTransferTask = Task.Run(TransferLoop);
        }

        private async Task TransferLoop()
        {
            System.Diagnostics.Debug.WriteLine("PERF: EncoderStorage enters transfer loop. Avoid this if possible.");

            var buffer = new byte[0x10000];
            for (;;)
            {
                var fetched = await mEncoderOutput.ReadAsync(buffer, 0, buffer.Length, StreamMode.Partial);
                System.Diagnostics.Debug.Assert(0 <= fetched && fetched <= buffer.Length);
                if (fetched == 0)
                {
                    mComplete = true;
                    return;
                }

                await mStream.WriteAsync(buffer, 0, fetched);
            }
        }

        public async Task<int> WriteAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            System.Diagnostics.Debug.Assert(mEncoderOutput == null);
            System.Diagnostics.Debug.Assert(!mComplete);
            await mStream.WriteAsync(buffer, offset, length);
            return length;
        }

        public Task CompleteAsync()
        {
            System.Diagnostics.Debug.Assert(mEncoderOutput == null);
            System.Diagnostics.Debug.Assert(!mComplete);
            mComplete = true;
            return Task.CompletedTask;
        }

        #endregion
    }

    /// <summary>
    /// Transfers data from the output of one encoder to the input of another encoder.
    /// </summary>
    internal sealed class EncoderConnection : IStreamReader, IStreamWriter
    {
        #region Variables

        private IStreamReader mEncoderOutputToConnectionInput;
        private IStreamWriter mConnectionOutputToEncoderInput;
        private Task mTransferTask;
        private TaskCompletionSource<int> mResult;
        private byte[] mBuffer;
        private int mOffset;
        private int mEnding;
        private bool mComplete;

        #endregion

        #region Implementation

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public long GetFinalLength()
        {
            throw new NotImplementedException();
        }

        public void SetInputStream(EncoderNode encoder, int index)
        {
            mConnectionOutputToEncoderInput = encoder.GetInputSink(index);
            if (mConnectionOutputToEncoderInput == null)
                encoder.SetInputSource(index, this);
        }

        public void SetOutputStream(EncoderNode encoder, int index)
        {
            mEncoderOutputToConnectionInput = encoder.GetOutputSource(index);
            if (mEncoderOutputToConnectionInput == null)
                encoder.SetOutputSink(index, this);
        }

        public void Start()
        {
            // If the inputs and outputs are both provided by the encoder nodes then we
            // need to bridge this by providing a temporary buffer and drive a copy loop.
            if (mConnectionOutputToEncoderInput != null && mEncoderOutputToConnectionInput != null)
                mTransferTask = Task.Run(TransferLoop);
        }

        private async Task TransferLoop()
        {
            System.Diagnostics.Debug.WriteLine("PERF: EncoderConnection enters transfer loop. Avoid this if possible.");

            var buffer = new byte[0x10000];
            var offset = 0;
            var ending = 0;

            for (;;)
            {
                if (!mComplete && ending < buffer.Length)
                {
                    var fetched = await mEncoderOutputToConnectionInput.ReadAsync(buffer, ending, buffer.Length - ending, StreamMode.Partial);
                    System.Diagnostics.Debug.Assert(0 <= fetched && fetched <= buffer.Length - ending);

                    if (fetched == 0)
                        mComplete = true;
                    else
                        ending += fetched;
                }

                if (offset < ending)
                {
                    var written = await mConnectionOutputToEncoderInput.WriteAsync(buffer, offset, ending - offset, StreamMode.Partial);
                    System.Diagnostics.Debug.Assert(0 < written && written <= ending - offset);

                    offset += written;
                }

                if (offset == ending)
                {
                    offset = 0;
                    ending = 0;

                    if (mComplete)
                    {
                        await mConnectionOutputToEncoderInput.CompleteAsync();
                        return;
                    }
                }
            }
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, StreamMode mode)
        {
            System.Diagnostics.Debug.Assert(mConnectionOutputToEncoderInput == null);
            System.Diagnostics.Debug.Assert(buffer != null);
            System.Diagnostics.Debug.Assert(0 <= offset && offset < buffer.Length);
            System.Diagnostics.Debug.Assert(0 <= count && count < buffer.Length - offset);

            if (mEncoderOutputToConnectionInput != null)
                return mEncoderOutputToConnectionInput.ReadAsync(buffer, offset, count, mode);

            lock (this)
            {
                if (mBuffer != null)
                    throw new InternalFailureException();

                mBuffer = buffer;
                mOffset = offset;
                mEnding = offset + count;
                mResult = new TaskCompletionSource<int>();
                Monitor.PulseAll(this);
                return mResult.Task;
            }
        }

        public Task<int> WriteAsync(byte[] buffer, int offset, int count, StreamMode mode)
        {
            System.Diagnostics.Debug.Assert(mEncoderOutputToConnectionInput == null);
            System.Diagnostics.Debug.Assert(buffer != null);
            System.Diagnostics.Debug.Assert(0 <= offset && offset < buffer.Length);
            System.Diagnostics.Debug.Assert(0 <= count && count < buffer.Length - offset);

            if (mConnectionOutputToEncoderInput != null)
                return mConnectionOutputToEncoderInput.WriteAsync(buffer, offset, count, mode);

            lock (this)
            {
                while (mBuffer == null)
                    Monitor.Wait(this);

                var result = Math.Min(count, mEnding - mOffset);
                Buffer.BlockCopy(buffer, offset, mBuffer, mOffset, result);
                mResult.SetResult(result);
                return Task.FromResult(result);
            }
        }

        public Task CompleteAsync()
        {
            System.Diagnostics.Debug.Assert(mEncoderOutputToConnectionInput == null);

            if (mConnectionOutputToEncoderInput != null)
                return mConnectionOutputToEncoderInput.CompleteAsync();

            throw new NotImplementedException();
        }

        #endregion
    }

    internal abstract class EncoderNode : IDisposable
    {
        #region Implementation

        public abstract void Start();
        public abstract void Dispose();

        // The encoder can decide between providing in input sink or accepting an input source.
        // It is preferable if the encoder can accept an input source so he can pull data directly from the connected encoder.
        public abstract IStreamWriter GetInputSink(int index);
        public abstract void SetInputSource(int index, IStreamReader stream);

        // The encode can decide between providing an output source or accepting an output sink.
        // It is preferable if the encoder can accept an output sink so he can push data directly to the connected encoder.
        public abstract IStreamReader GetOutputSource(int index);
        public abstract void SetOutputSink(int index, IStreamWriter stream);

        #endregion
    }

    public sealed class EncoderSession : IDisposable
    {
        // TODO: awaitable

        private sealed class ChecksumStream : Stream
        {
            private Stream mStream;

            public ChecksumStream(Stream stream)
            {
                mStream = stream;
            }

            public Checksum GetChecksum()
            {
                throw new NotImplementedException();
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;

            public override int Read(byte[] buffer, int offset, int count)
            {
                return mStream.Read(buffer, offset, count);
            }

            #region Invalid Operations

            public override long Length
            {
                get { throw new InvalidOperationException(); }
            }

            public override long Position
            {
                get { throw new InvalidOperationException(); }
                set { throw new InvalidOperationException(); }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new InvalidOperationException();
            }

            public override void SetLength(long value)
            {
                throw new InvalidOperationException();
            }

            public override void Flush()
            {
                throw new InvalidOperationException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new InvalidOperationException();
            }

            #endregion
        }

        private object mLockObject = new object();
        private ArchiveWriter mWriter;
        private EncoderDefinition mDefinition;
        private EncoderInput mInput;
        private EncoderStorage[] mStorage;
        private EncoderConnection[] mConnections;
        private EncoderNode[] mEncoders;
        private ImmutableArray<DecodedStreamMetadata>.Builder mContent;
        private Stream mPendingStream;
        private long mResultLength;
        private int mSection;

        internal EncoderSession(ArchiveWriter writer, int section, EncoderDefinition definition, EncoderInput input, EncoderStorage[] storage, EncoderConnection[] connections, EncoderNode[] encoders)
        {
            mWriter = writer;
            mSection = section;
            mDefinition = definition;
            mInput = input;
            mStorage = storage;
            mConnections = connections;
            mEncoders = encoders;

            input.Connect(this);
        }

        internal async Task<int> ReadInternalAsync(byte[] buffer, int offset, int length, StreamMode mode)
        {
            Utilities.DebugCheckStreamArguments(buffer, offset, length, mode);

            Stream stream;
            lock (mLockObject)
            {
                while (mPendingStream == null)
                    Monitor.Wait(mLockObject);

                stream = mPendingStream;
            }

            int result = 0;
            while (result == 0 || mode == StreamMode.Complete && length > 0)
            {
                var fetched = await stream.ReadAsync(buffer, offset, length);

                if (fetched < 0 || fetched > length)
                    throw new InvalidOperationException("Source stream violated stream contract.");

                result += fetched;
                offset += fetched;
                length -= fetched;

                mResultLength += fetched; // could be interlocked but doesn't need to be since we are currently 'owning' the stream (and also consider ourselves 'owning' this counter)

                if (fetched == 0)
                {
                    lock (mLockObject)
                    {
                        if (mPendingStream != stream)
                            throw new InternalFailureException();

                        mPendingStream = null;
                        Monitor.PulseAll(mLockObject);

                        do { Monitor.Wait(mLockObject); }
                        while (mPendingStream == null);

                        stream = mPendingStream;
                    }
                }
            }

            return result;
        }

        public void Dispose()
        {
            foreach (var encoder in mEncoders)
                encoder.Dispose();

            foreach (var stream in mConnections)
                stream.Dispose();

            foreach (var stream in mStorage)
                stream.Dispose();

            mInput.Dispose();
        }

        public void Discard()
        {
            throw new NotImplementedException();
        }

        public void Complete()
        {
            int totalInputCount = 0;
            var firstInputOffset = new int[mEncoders.Length];
            for (int i = 0; i < mEncoders.Length; i++)
            {
                firstInputOffset[i] = totalInputCount;
                totalInputCount += mDefinition.GetEncoder(i).InputCount;
            }

            var decoders = ImmutableArray.CreateBuilder<DecoderMetadata>(mEncoders.Length);
            for (int i = 0; i < mEncoders.Length; i++)
            {
                var encoder = mDefinition.GetEncoder(i);
                var settings = encoder.Settings;
                var decoderType = settings.GetDecoderType();

                var decoderInputCount = decoderType.GetInputCount();
                var decoderInputs = ImmutableArray.CreateBuilder<DecoderInputMetadata>(decoderInputCount);
                for (int j = 0; j < decoderInputCount; j++)
                {
                    var encoderOutput = encoder.GetOutput(j).Target;
                    if (encoderOutput.IsStorage)
                        decoderInputs.Add(new DecoderInputMetadata(null, encoderOutput.Index));
                    else
                        decoderInputs.Add(new DecoderInputMetadata(encoderOutput.Node.Index, encoderOutput.Index));
                }

                var decoderOutputCount = decoderType.GetOutputCount();
                var decoderOutputs = ImmutableArray.CreateBuilder<DecoderOutputMetadata>(decoderOutputCount);
                for (int j = 0; j < decoderOutputCount; j++)
                {
                    var encoderInput = encoder.GetInput(j).Source;
                    if (encoderInput.IsContent)
                        decoderOutputs.Add(new DecoderOutputMetadata(mInput.GetFinalLength()));
                    else
                        decoderOutputs.Add(new DecoderOutputMetadata(mConnections[firstInputOffset[encoderInput.Node.Index] + encoderInput.Index].GetFinalLength()));
                }

                decoders.Add(new DecoderMetadata(decoderType, settings.SerializeSettings(), decoderInputs.MoveToImmutable(), decoderOutputs.MoveToImmutable()));
            }

            var inputSource = mDefinition.GetContentSource().Target;
            var definition = new ArchiveDecoderSection(
                decoders.MoveToImmutable(),
                new DecoderInputMetadata(inputSource.Node.Index, inputSource.Index),
                mInput.GetFinalLength(),
                mInput.GetFinalChecksum(),
                mContent.ToImmutable());

            mWriter.CompleteEncoderSession(this, mSection, definition, mStorage);
        }

        public Task<EncodedFileResult> AppendStream(Stream stream, bool checksum)
        {
            // TODO: validate relative filename (in particular the directory separator and checking for invalid components like drives, '..' and '.')

            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (checksum)
                stream = new ChecksumStream(stream);

            return Task.Run(delegate {
                lock (mLockObject)
                {
                    if (mPendingStream != null)
                        throw new InvalidOperationException();

                    mPendingStream = stream;
                    mResultLength = 0;

                    Monitor.PulseAll(mLockObject);

                    while (mPendingStream != null)
                        Monitor.Wait(mLockObject);

                    return new EncodedFileResult(mResultLength, checksum ? ((ChecksumStream)stream).GetChecksum() : default(Checksum?));
                }
            });
        }
    }

    public struct EncodedFileResult
    {
        public long Length { get; }
        public Checksum? Checksum { get; }

        public EncodedFileResult(long length, Checksum? checksum)
        {
            this.Length = length;
            this.Checksum = checksum;
        }
    }
}
