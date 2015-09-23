using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma.SevenZip
{
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
        #region Static Methods

        /// <summary>Checks if the stream looks like a 7z archive.</summary>
        public static bool CheckFileHeader(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (!stream.CanRead)
                throw new InvalidOperationException("Stream must be readable.");

            byte major;
            byte minor;
            long offset;
            long length;
            Checksum checksum;

            return ReadFileHeader(stream, stream.CanSeek ? stream.Length : Int64.MaxValue, out major, out minor, out offset, out length, out checksum) == null;
        }

        private static Exception ReadFileHeader(Stream stream, long mStreamLength, out byte mMajorVersion, out byte mMinorVersion, out long mMetadataOffset, out long mMetadataLength, out Checksum mMetadataChecksum)
        {
            mMajorVersion = 0;
            mMinorVersion = 0;
            mMetadataOffset = 0;
            mMetadataLength = 0;
            mMetadataChecksum = default(Checksum);

            var header = new byte[kHeaderLength];

            int offset = 0;
            do
            {
                int result = stream.Read(header, offset, kHeaderLength - offset);
                if (result <= 0)
                    return new EndOfStreamException();

                offset += result;
            }
            while (offset < kHeaderLength);

            for (int i = 0; i < 6; i++)
                if (header[i] != kFileSignature[i])
                    return new InvalidDataException("File is not a 7z archive.");

            mMajorVersion = header[6];
            mMinorVersion = header[7];

            if (mMajorVersion != 0)
                return new InvalidDataException("Invalid header version.");

            mMetadataOffset = GetInt64(header, 12);
            mMetadataLength = GetInt64(header, 20);
            mMetadataChecksum = new Checksum(GetInt32(header, 28));

            uint crc = LZMA.Master.SevenZip.CRC.kInitCRC;
            crc = LZMA.Master.SevenZip.CRC.Update(crc, mMetadataOffset);
            crc = LZMA.Master.SevenZip.CRC.Update(crc, mMetadataLength);
            crc = LZMA.Master.SevenZip.CRC.Update(crc, mMetadataChecksum.Value);
            crc = LZMA.Master.SevenZip.CRC.Finish(crc);

            if ((int)crc != GetInt32(header, 8))
                return new InvalidDataException("Invalid header checksum.");

            if (mMetadataOffset < header.Length || mMetadataOffset > mStreamLength - kHeaderLength)
                return new InvalidDataException("Invalid metadata offset.");

            if (mMetadataLength < 0 || mMetadataLength > mStreamLength - kHeaderLength - mMetadataOffset)
                return new InvalidDataException("Invalid metadata length.");

            return null;
        }

        private static int GetInt32(byte[] buffer, int offset)
        {
            return (int)buffer[offset]
                | ((int)buffer[offset + 1] << 8)
                | ((int)buffer[offset + 2] << 16)
                | ((int)buffer[offset + 3] << 24);
        }

        private static long GetInt64(byte[] buffer, int offset)
        {
            return (long)buffer[offset]
                | ((long)buffer[offset + 1] << 8)
                | ((long)buffer[offset + 2] << 16)
                | ((long)buffer[offset + 3] << 24)
                | ((long)buffer[offset + 4] << 32)
                | ((long)buffer[offset + 5] << 40)
                | ((long)buffer[offset + 6] << 48)
                | ((long)buffer[offset + 7] << 56);
        }

        #endregion

        #region Variables

        private static readonly ImmutableArray<byte> kFileSignature = ImmutableArray.Create<byte>((byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C);
        private const int kHeaderLength = 0x20;

        public static ImmutableArray<byte> FileSignature => kFileSignature;

        private Lazy<string> mPassword;
        private StreamScope mScope;
        private Stream mStream;
        private long mStreamLength;
        private byte mMajorVersion;
        private byte mMinorVersion;
        private Checksum mMetadataChecksum;
        private long mMetadataOffset;
        private long mMetadataLength;

        #endregion

        #region Protected API

        protected ArchiveMetadata ReadMetadataCore(Stream stream, Lazy<string> password)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (!stream.CanRead || !stream.CanSeek)
                throw new InvalidOperationException("Stream must support reading and seeking.");

            if (mStream != null)
                throw new InvalidOperationException("Recursive invocation.");

            try
            {
                mPassword = password;
                mStream = stream;
                mStream.Position = 0;
                mStreamLength = stream.Length;

                var exception = ReadFileHeader(mStream, mStreamLength, out mMajorVersion, out mMinorVersion, out mMetadataOffset, out mMetadataLength, out mMetadataChecksum);
                if (exception != null)
                    throw exception;

                if (mMetadataLength == 0)
                    return new ArchiveMetadata();

                // TODO: validate metadata stream checksum

                using (var metadataStream = new ConstrainedReadStream(mStream, kHeaderLength + mMetadataOffset, mMetadataLength))
                using (var scope = new StreamScope(this))
                {
                    scope.SetSource(metadataStream);

                    if (!PrepareMetadata(scope))
                        return new ArchiveMetadata();

                    return ReadArchive();
                }
            }
            finally
            {
                // Drop references to password and stream so they aren't retained.
                mPassword = null;
                mStream = null;

                // It is also important to reset optional values to their defaults. Otherwise if
                // the next archive we are reading doesn't contain some of the optional values
                // we could mistake the values from the previous archive as current values.
                //mFileCount = 0;
            }
        }

        protected virtual void ReadNames(MetadataStringReader data) { }
        protected virtual void ReadAttributes(MetadataAttributeReader data) { }
        protected virtual void ReadOffsets(MetadataNumberReader data) { }
        protected virtual void ReadEmptyStreamMarkers(MetadataBitReader data) { }
        protected virtual void ReadEmptyFileMarkers(MetadataBitReader data) { }
        protected virtual void ReadRemovedFileMarkers(MetadataBitReader data) { }
        protected virtual void ReadCTime(MetadataDateReader data) { }
        protected virtual void ReadATime(MetadataDateReader data) { }
        protected virtual void ReadMTime(MetadataDateReader data) { }
        protected virtual void ReadChecksums(MetadataChecksumReader data) { }

        #endregion

        #region Private Implementation - Metadata Reader

        private bool PrepareMetadata(StreamScope scope)
        {
            // The metadata stream can either be inlined or compressed
            var token = ReadToken();
            if (token == Token.PackedHeader)
            {
                var streams = ReadPackedStreams();

                // Compressed metadata without content is odd but allowed.
                if (streams.IsDefaultOrEmpty)
                    return false;

                // Compressed metadata with multiple streams is not allowed.
                if (streams.Length != 1)
                    throw new InvalidDataException();

                // Switch over to the decoded metadata stream.
                scope.SetSource(streams[0]);
                token = ReadToken();
            }

            // The metadata stream must start with this token.
            if (token != Token.Header)
                throw new InvalidDataException();

            return true;
        }

        private ArchiveMetadata ReadArchive()
        {
            var token = ReadToken();

            if (token == Token.ArchiveProperties)
            {
                ReadArchiveProperties();
                token = ReadToken();
            }

            var streams = ImmutableArray<Stream>.Empty;
            if (token == Token.AdditionalStreams)
            {
                streams = ReadPackedStreams();
                token = ReadToken();
            }

            List<long> unpackSizes;
            List<Checksum?> checksums;

            ArchiveMetadata metadata;
            if (token == Token.MainStreams)
            {
                metadata = ReadMetadata(streams, true);
                token = ReadToken();
            }
            else
            {
                throw new NotImplementedException();
            }

            int? emptyStreamCount = null;

            if (token != Token.End)
            {
                if (token != Token.Files)
                    throw new InvalidDataException();

                var fileCount = ReadNumberAsInt32();

                for (;;)
                {
                    token = ReadToken();
                    if (token == Token.End)
                        break;

                    var recordSize = (long)ReadNumber();
                    if (recordSize < 0)
                        throw new InvalidDataException();

                    var oldOffset = GetCurrentOffset();

                    #region File Metadata

                    switch (token)
                    {
                        case Token.Name:
                            using (SelectStream(streams))
                            {
                                var reader = new MetadataStringReader(this, fileCount);
                                ReadNames(reader);
                                reader.Complete();
                            }
                            break;

                        case Token.WinAttributes:
                            {
                                var vector = ReadOptionalBitVector(fileCount);
                                using (SelectStream(streams))
                                {
                                    var reader = new MetadataAttributeReader(this, fileCount, vector);
                                    ReadAttributes(reader);
                                    reader.Complete();
                                }

                                break;
                            }

                        case Token.EmptyStream:
                            {
                                var emptyStreams = ReadRequiredBitVector(fileCount);
                                emptyStreamCount = emptyStreams.CountSetBits();

                                var reader = new MetadataBitReader(this, emptyStreams);
                                ReadEmptyStreamMarkers(reader);
                                reader.Complete();
                                break;
                            }

                        case Token.EmptyFile:
                            {
                                if (emptyStreamCount == null)
                                    throw new InvalidDataException();

                                var reader = new MetadataBitReader(this, ReadRequiredBitVector(emptyStreamCount.Value));
                                ReadEmptyFileMarkers(reader);
                                reader.Complete();
                                break;
                            }

                        case Token.Anti:
                            {
                                if (emptyStreamCount == null)
                                    throw new InvalidDataException();

                                var reader = new MetadataBitReader(this, ReadRequiredBitVector(emptyStreamCount.Value));
                                ReadRemovedFileMarkers(reader);
                                reader.Complete();
                                break;
                            }

                        case Token.StartPos:
                            {
                                var vector = ReadOptionalBitVector(fileCount);
                                using (SelectStream(streams))
                                {
                                    var reader = new MetadataNumberReader(this, fileCount, vector);
                                    ReadOffsets(reader);
                                    reader.Complete();
                                }

                                break;
                            }

                        case Token.CTime:
                            {
                                var vector = ReadOptionalBitVector(fileCount);
                                using (SelectStream(streams))
                                {
                                    var reader = new MetadataDateReader(this, fileCount, vector);
                                    ReadCTime(reader);
                                    reader.Complete();
                                }

                                break;
                            }

                        case Token.ATime:
                            {
                                var vector = ReadOptionalBitVector(fileCount);
                                using (SelectStream(streams))
                                {
                                    var reader = new MetadataDateReader(this, fileCount, vector);
                                    ReadATime(reader);
                                    reader.Complete();
                                }

                                break;
                            }

                        case Token.MTime:
                            {
                                var vector = ReadOptionalBitVector(fileCount);
                                using (SelectStream(streams))
                                {
                                    var reader = new MetadataDateReader(this, fileCount, vector);
                                    ReadMTime(reader);
                                    reader.Complete();
                                }

                                break;
                            }

                        case Token.Dummy:
                            // TODO: what does the reference implementation do here? just skip it? then we shouldn't throw an exception!
                            for (int i = 0; i < recordSize; i++)
                                if (ReadByte() != 0)
                                    throw new InvalidDataException();

                            break;

                        default:
                            // TODO: skip data
                            break;
                    }

                    #endregion

                    // Up until version 0.3 there was a bug which could emit invalid record sizes, but it didn't really matter.
                    // Starting from version 0.3 there have been extensions to the file format which require correct record sizes.
                    if (!(mMajorVersion == 0 && mMinorVersion < 3))
                    {
                        var newOffset = GetCurrentOffset();
                        if (newOffset - oldOffset != recordSize)
                            throw new InvalidDataException();
                    }
                }
            }

            return metadata;
        }

        private ImmutableArray<Stream> ReadPackedStreams()
        {
            var metadata = ReadMetadata(ImmutableArray<Stream>.Empty, false);
            var count = metadata.DecoderSections.Length;
            var streams = ImmutableArray.CreateBuilder<Stream>(count);

            for (int i = 0; i < count; i++)
                streams.Add(CreateCachedDecoderStream(metadata, i));

            return streams.MoveToImmutable();
        }

        private Stream CreateCachedDecoderStream(ArchiveMetadata metadata, int section)
        {
            // TODO: decode stream once then reuse the cached data
            throw new NotImplementedException();
        }

        private ArchiveMetadata ReadMetadata(ImmutableArray<Stream> streams, bool main)
        {
            var rawStreams = default(ImmutableArray<ArchiveFileSection>);
            var sections = default(ArchiveSectionMetadataBuilder[]);

            for (;;)
            {
                switch (ReadToken())
                {
                    case Token.PackInfo:
                        rawStreams = ReadRawStreamList();
                        break;

                    case Token.UnpackInfo:
                        sections = ReadSectionHeader(streams);
                        break;

                    case Token.SubStreamsInfo:
                        if (sections == null)
                            throw new InvalidDataException();

                        ReadSectionDetails(sections);
                        break;

                    case Token.End:
                        if (rawStreams.IsDefault || sections == null)
                            throw new InvalidDataException();

                        var sectionListBuilder = ImmutableArray.CreateBuilder<ArchiveDecoderSection>(sections.Length);

                        foreach (var section in sections)
                        {
                            var decoderListBuilder = ImmutableArray.CreateBuilder<DecoderMetadata>(section.Decoders.Length);

                            foreach (var decoder in section.Decoders)
                            {
                                decoderListBuilder.Add(new DecoderMetadata(
                                    decoder.Method,
                                    decoder.Settings,
                                    decoder.InputInfo.MoveToImmutable(),
                                    decoder.OutputInfo.MoveToImmutable()));
                            }

                            sectionListBuilder.Add(new ArchiveDecoderSection(
                                decoderListBuilder.MoveToImmutable(),
                                section.Checksum,
                                section.Subsections.ToImmutableArray()));
                        }

                        return new ArchiveMetadata(rawStreams, sectionListBuilder.MoveToImmutable());

                    default:
                        throw new InvalidDataException();
                }
            }
        }

        private ImmutableArray<ArchiveFileSection> ReadRawStreamList()
        {
            var rawStreamBaseOffset = ReadNumberAsInt64() + kHeaderLength;
            if (rawStreamBaseOffset < 0)
                throw new InvalidDataException();

            var rawStreamCount = ReadNumberAsInt32();

            SkipToToken(Token.Size);

            var rawStreamSizes = new long[rawStreamCount];
            for (int i = 0; i < rawStreamCount; i++)
                rawStreamSizes[i] = ReadNumberAsInt64();

            ImmutableArray<ArchiveFileSection>.Builder rawStreamListBuilder = null;

            for (;;)
            {
                var token = ReadToken();
                if (token == Token.CRC)
                {
                    var vector = ReadOptionalBitVector(rawStreamCount);
                    var rawStreamOffset = rawStreamBaseOffset;
                    rawStreamListBuilder = ImmutableArray.CreateBuilder<ArchiveFileSection>(rawStreamCount);

                    for (int i = 0; i < rawStreamCount; i++)
                    {
                        var length = rawStreamSizes[i];
                        rawStreamOffset += length;

                        if (vector[i])
                            rawStreamListBuilder.Add(new ArchiveFileSection(rawStreamOffset, length, new Checksum(ReadInt32())));
                        else
                            rawStreamListBuilder.Add(new ArchiveFileSection(rawStreamOffset, length, null));
                    }
                }
                else if (token == Token.End)
                {
                    if (rawStreamListBuilder == null)
                    {
                        var rawStreamOffset = rawStreamBaseOffset;
                        rawStreamListBuilder = ImmutableArray.CreateBuilder<ArchiveFileSection>(rawStreamCount);

                        for (int i = 0; i < rawStreamCount; i++)
                        {
                            var length = rawStreamSizes[i];
                            rawStreamOffset += length;

                            rawStreamListBuilder.Add(new ArchiveFileSection(rawStreamOffset, length, null));
                        }
                    }

                    return rawStreamListBuilder.MoveToImmutable();
                }
                else
                {
                    SkipDataBlock();
                }
            }
        }

        private ArchiveSectionMetadataBuilder[] ReadSectionHeader(ImmutableArray<Stream> streams)
        {
            SkipToToken(Token.Folder);

            var sectionCount = ReadNumberAsInt32();
            var sections = new ArchiveSectionMetadataBuilder[sectionCount];
            int rawStreamCount = 0;

            using (SelectStream(streams))
            {
                for (int i = 0; i < sectionCount; i++)
                {
                    var section = ReadSection(rawStreamCount);
                    rawStreamCount += section.RequiredRawInputStreamCount;
                    sections[i] = section;
                }
            }

            SkipToToken(Token.CodersUnpackSize);

            foreach (var section in sections)
            {
                long totalOutputLength = 0;

                foreach (var decoder in section.Decoders)
                {
                    var outputCount = decoder.OutputCount;
                    var outputBuilder = ImmutableArray.CreateBuilder<DecoderOutputMetadata>(outputCount);

                    for (int i = 0; i < outputCount; i++)
                    {
                        var length = ReadNumberAsInt64();
                        totalOutputLength += length;
                        outputBuilder.Add(new DecoderOutputMetadata(length));
                    }

                    decoder.OutputInfo = outputBuilder;
                }

                section.OutputLength += totalOutputLength;
            }

            for (;;)
            {
                var token = ReadToken();
                if (token == Token.End)
                    break;

                if (token == Token.CRC)
                {
                    var vector = ReadOptionalBitVector(sectionCount);

                    for (int i = 0; i < sectionCount; i++)
                    {
                        if (vector[i])
                            sections[i].Checksum = new Checksum(ReadInt32());
                        else
                            sections[i].Checksum = null;
                    }
                }
                else
                {
                    SkipDataBlock();
                }
            }

            return sections;
        }

        private void ReadSectionDetails(ArchiveSectionMetadataBuilder[] sections)
        {
            #region Counts

            bool hasStreamCounts = false;

            Token token;
            for (;;)
            {
                token = ReadToken();
                if (token == Token.End || token == Token.CRC || token == Token.Size)
                    break;

                if (token == Token.NumUnpackStream)
                {
                    hasStreamCounts = true;

                    foreach (var section in sections)
                        section.SubStreamCount = ReadNumberAsInt32();
                }
                else
                {
                    SkipDataBlock();
                }
            }

            if (!hasStreamCounts)
                foreach (var section in sections)
                    section.SubStreamCount = 1;

            #endregion

            #region Sizes

            foreach (var section in sections)
            {
                // v3.13 was broken and wrote empty sections
                // v4.07 added compat code to skip empty sections
                if (section.SubStreamCount.Value == 0)
                    continue;

                var remaining = section.OutputLength;
                var subsections = new DecodedStreamMetadata[section.SubStreamCount.Value];

                for (int i = 0; i < subsections.Length - 1; i++)
                {
                    if (token == Token.Size)
                    {
                        var size = ReadNumberAsInt64();
                        if (size == 0 || size >= remaining)
                            throw new InvalidDataException();

                        subsections[i] = new DecodedStreamMetadata(size, null);
                        remaining -= size;
                    }
                }

                if (remaining == 0)
                    throw new InvalidDataException();

                subsections[subsections.Length - 1] = new DecodedStreamMetadata(remaining, null);
                section.Subsections = subsections;
            }

            if (token == Token.Size)
                token = ReadToken();

            #endregion

            #region Checksums

            int requiredChecksumCount = 0;
            int totalChecksumCount = 0;

            ImmutableArray<Checksum?>.Builder checksums = null;

            foreach (var section in sections)
            {
                // If there is only one stream and we have a section checksum 7z doesn't store the checksum again.
                if (!(section.SubStreamCount == 1 && section.Checksum.HasValue))
                    requiredChecksumCount += section.SubStreamCount.Value;

                totalChecksumCount += section.SubStreamCount.Value;
            }

            for (;;)
            {
                if (token == Token.End)
                {
                    if (checksums == null)
                    {
                        checksums = ImmutableArray.CreateBuilder<Checksum?>(totalChecksumCount);
                        for (int i = 0; i < totalChecksumCount; i++)
                            checksums.Add(null);
                    }

                    break;
                }
                else if (token == Token.CRC)
                {
                    checksums = ImmutableArray.CreateBuilder<Checksum?>(totalChecksumCount);

                    var vector = ReadOptionalBitVector(requiredChecksumCount);
                    int requiredChecksumIndex = 0;

                    foreach (var section in sections)
                    {
                        if (section.SubStreamCount == 1 && section.Checksum.HasValue)
                        {
                            checksums.Add(section.Checksum.Value);
                        }
                        else
                        {
                            for (int i = 0; i < section.SubStreamCount; i++)
                            {
                                if (vector[requiredChecksumIndex++])
                                    checksums.Add(new Checksum(ReadInt32()));
                                else
                                    checksums.Add(null);
                            }
                        }
                    }

                    System.Diagnostics.Debug.Assert(requiredChecksumIndex == requiredChecksumCount);
                    System.Diagnostics.Debug.Assert(checksums.Count == totalChecksumCount);
                }
                else
                {
                    SkipDataBlock();
                }

                token = ReadToken();
            }

            #endregion
        }

        private ArchiveSectionMetadataBuilder ReadSection(int rawInputStreamIndex)
        {
            var section = new ArchiveSectionMetadataBuilder();

            int totalInputCount = 0;
            int totalOutputCount = 0;

            var decoderCount = ReadNumberAsInt32();
            section.Decoders = new DecoderMetadataBuilder[decoderCount];
            for (int i = 0; i < decoderCount; i++)
            {
                var decoder = ReadDecoder();
                totalInputCount += decoder.InputCount;
                totalOutputCount += decoder.OutputCount;
                section.Decoders[i] = decoder;
            }

            // One output is the final output, the others need to be wired up.
            for (int i = 1; i < totalOutputCount; i++)
            {
                int inputIndex = ReadNumberAsInt32();
                int inputDecoderIndex = 0;
                for (;;)
                {
                    if (inputDecoderIndex == decoderCount)
                        throw new InvalidDataException();

                    if (inputIndex < section.Decoders[inputDecoderIndex].InputCount)
                        break;

                    inputIndex -= section.Decoders[inputDecoderIndex].OutputCount;
                    inputDecoderIndex += 1;
                }

                int outputIndex = ReadNumberAsInt32();
                int outputDecoderIndex = 0;
                for (;;)
                {
                    if (outputDecoderIndex == decoderCount)
                        throw new InvalidDataException();

                    var outputCount = section.Decoders[outputDecoderIndex].OutputCount;
                    if (outputIndex < outputCount)
                        break;

                    outputIndex -= outputCount;
                    outputDecoderIndex += 1;
                }

                // Detect duplicate connections by checking for the placeholder.
                if (section.Decoders[inputDecoderIndex].InputInfo[inputIndex].StreamIndex != Int32.MaxValue)
                    throw new InvalidDataException();

                section.Decoders[inputDecoderIndex].InputInfo[inputIndex] = new DecoderInputMetadata(outputDecoderIndex, outputIndex);
            }

            // Outputs must be wired up to unique inputs. Inputs which are not wired to outputs must be wired to raw streams.
            // Note that negative overflow is not possible and positive overflow is ok in this calculation,
            // it will fail the range check and trigger the exception, which is the intended behavior.
            var requiredRawStreams = 1 + totalInputCount - totalOutputCount;
            if (requiredRawStreams <= 0)
                throw new InvalidDataException();

            section.RequiredRawInputStreamCount = requiredRawStreams;

            if (requiredRawStreams == 1)
            {
                bool connected = false;

                foreach (var decoder in section.Decoders)
                {
                    for (int i = 0; i < decoder.InputCount; i++)
                    {
                        if (decoder.InputInfo[i].StreamIndex == Int32.MaxValue)
                        {
                            if (connected)
                                throw new InvalidDataException();

                            connected = true;
                            decoder.InputInfo[i] = new DecoderInputMetadata(null, 0);
                        }
                    }
                }

                if (!connected)
                    throw new InvalidDataException();
            }
            else
            {
                for (int i = 0; i < requiredRawStreams; i++)
                {
                    int inputIndex = ReadNumberAsInt32();
                    int inputDecoderIndex = 0;
                    for (;;)
                    {
                        if (inputDecoderIndex == decoderCount)
                            throw new InvalidDataException();

                        var decoder = section.Decoders[inputDecoderIndex];
                        if (inputIndex < decoder.InputCount)
                            break;

                        inputIndex -= decoder.OutputCount;
                        inputDecoderIndex += 1;
                    }

                    var decoderInput = section.Decoders[inputDecoderIndex].InputInfo;
                    if (decoderInput[inputIndex].StreamIndex != Int32.MaxValue)
                        throw new InvalidDataException();

                    decoderInput[inputIndex] = new DecoderInputMetadata(null, rawInputStreamIndex + i);
                }
            }

            return section;
        }

        private DecoderMetadataBuilder ReadDecoder()
        {
            var decoder = new DecoderMetadataBuilder();

            var mainByte = ReadByte();

#warning ??? why did we check this in the old decoder ???
            if ((mainByte & 0x80) != 0)
                throw new NotImplementedException();

            var idLen = (mainByte & 0x0F);
            if (idLen > 4)
                throw new InvalidDataException(); // all known decoders use 4 bytes or less

            int id = 0;
            for (int i = idLen - 1; i >= 0; i--)
                id |= ReadByte() << (i * 8);

            decoder.Method = CompressionMethod.TryDecode(id);
            if (decoder.Method.IsUndefined)
                throw new InvalidDataException(); // unknown decoder

            if ((mainByte & 0x10) != 0)
            {
                decoder.InputCount = ReadNumberAsInt32();
                decoder.OutputCount = ReadNumberAsInt32();
            }
            else
            {
                decoder.InputCount = 1;
                decoder.OutputCount = 1;
            }

            decoder.Method.CheckInputOutputCount(decoder.InputCount, decoder.OutputCount);

            decoder.InputInfo = ImmutableArray.CreateBuilder<DecoderInputMetadata>(decoder.InputCount);

            // We need to fill the array because it is not guaranteed that inputs will be connected in order.
            // No valid decoder can have Int32.MaxValue inputs so we can use this as a placeholder value to detect bugs.
            var placeholder = new DecoderInputMetadata(Int32.MaxValue, Int32.MaxValue);
            for (int i = 0; i < decoder.InputCount; i++)
                decoder.InputInfo.Add(placeholder);

            if ((mainByte & 0x20) != 0)
            {
                var settingsLength = ReadNumberAsInt32();
                var settingsBuilder = ImmutableArray.CreateBuilder<byte>(settingsLength);

                for (int i = 0; i < settingsLength; i++)
                    settingsBuilder.Add(ReadByte());

                decoder.Settings = settingsBuilder.MoveToImmutable();
            }
            else
            {
                decoder.Settings = ImmutableArray<byte>.Empty;
            }

            return decoder;
        }

        private void ReadArchiveProperties()
        {
            //while (ReadToken() != MetadataToken.End)
            //    SkipData();
            throw new NotImplementedException();
        }

        private BitVector ReadRequiredBitVector(int count)
        {
            // TODO: this calculation could overflow for malformed data, should throw an InvalidDataException instead of ArgumentOutOfRangeException
            var buffer = new byte[(count + 7) >> 3];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = ReadByte();

            return new BitVector(count, buffer);
        }

        private BitVector ReadOptionalBitVector(int count)
        {
            var allTrue = ReadByte();
            if (allTrue != 0)
                return new BitVector(count, true);

            return ReadRequiredBitVector(count);
        }

        internal long ReadInt32Internal()
        {
            return ReadInt32();
        }

        internal long ReadInt64Internal()
        {
            return ReadInt64();
        }

        internal long ReadNumberInternal()
        {
            return ReadNumberAsInt64();
        }

        internal string ReadStringInternal()
        {
            return mScope.ReadString();
        }

        #endregion

        #region Private Implementation - Binary Reader

        private long GetCurrentOffset()
        {
            return mScope.GetCurrentOffset();
        }

        private byte ReadByte()
        {
            return mScope.ReadByte();
        }

        private int ReadInt32()
        {
            return (int)mScope.ReadUInt32();
        }

        private long ReadInt64()
        {
            return (long)mScope.ReadUInt64();
        }

        private ulong ReadNumber()
        {
            return mScope.ReadNumber();
        }

        private long ReadNumberAsInt64()
        {
            var value = (long)ReadNumber();
            if (value < 0)
                throw new InvalidDataException();

            return value;
        }

        private int ReadNumberAsInt32()
        {
            var value = ReadNumber();
            if (value > Int32.MaxValue)
                throw new InvalidDataException();

            return (int)value;
        }

        private StreamScope SelectStream(ImmutableArray<Stream> streams)
        {
            var switchStream = ReadByte();
            if (switchStream == 0)
                return null;

            var streamIndex = ReadNumberAsInt32();
            if (streamIndex < 0 || streamIndex >= streams.Length)
                throw new InvalidDataException();

            var stream = streams[streamIndex];
            var scope = new StreamScope(this);
            scope.SetSource(stream);
            return scope;
        }

        private Token ReadToken()
        {
            var token = ReadNumber();
            if (token > 25)
                return Token.Unknown;
            else
                return (Token)token;
        }

        private void SkipToToken(Token token)
        {
            while (ReadToken() != token)
                SkipDataBlock();
        }

        private void SkipDataBlock()
        {
            mScope.Skip(ReadNumberAsInt64());
        }

        private enum Token
        {
            #region Constants

            Unknown = -1,

            End = 0,
            Header = 1,
            ArchiveProperties = 2,
            AdditionalStreams = 3,
            MainStreams = 4,
            Files = 5,
            PackInfo = 6,
            UnpackInfo = 7,
            SubStreamsInfo = 8,
            Size = 9,
            CRC = 10,
            Folder = 11,
            CodersUnpackSize = 12,
            NumUnpackStream = 13,
            EmptyStream = 14,
            EmptyFile = 15,
            Anti = 16,
            Name = 17,
            CTime = 18,
            ATime = 19,
            MTime = 20,
            WinAttributes = 21,
            Comment = 22,
            PackedHeader = 23,
            StartPos = 24,
            Dummy = 25,

            #endregion
        }

        #endregion

        #region Private Implementation - Stream Scope

        private sealed class StreamScope : IDisposable
        {
            private ArchiveMetadataReader mReader;
            private StreamScope mOuterScope;
            private Stream mStream;
            private byte[] mBuffer = new byte[0x4000];
            private int mBufferOffset;
            private int mBufferEnding;

            public StreamScope(ArchiveMetadataReader reader)
            {
                mReader = reader;
                mOuterScope = reader.mScope;
                reader.mScope = this;
            }

            public void Dispose()
            {
                mReader.mScope = mOuterScope;
                mReader = null;
            }

            public void SetSource(Stream stream)
            {
                stream.Position = 0;
                mStream = stream;
            }

            private void EnsureBuffer(int size)
            {
                if (mBufferEnding - mBufferOffset < size)
                    PrefetchBuffer(size);
            }

            private void PrefetchBuffer(int size)
            {
                var buffer = (size <= mBuffer.Length) ? mBuffer : new byte[Math.Max(mBuffer.Length * 2, size)];

                if (mBufferOffset < mBufferEnding)
                    Buffer.BlockCopy(mBuffer, mBufferOffset, buffer, 0, mBufferEnding - mBufferOffset);

                mBufferEnding -= mBufferOffset;
                mBufferOffset = 0;
                mBuffer = buffer;

                while (mBufferEnding < size)
                {
                    var length = mStream.Read(mBuffer, mBufferEnding, mBuffer.Length - mBufferEnding);
                    if (length <= 0)
                        throw new EndOfStreamException();

                    mBufferEnding += length;
                }
            }

            public long GetCurrentOffset()
            {
                return mStream.Position - (mBufferEnding - mBufferOffset);
            }

            public byte ReadByte()
            {
                EnsureBuffer(1);
                return mBuffer[mBufferOffset++];
            }

            public uint ReadUInt32()
            {
                EnsureBuffer(4);
                var result = (uint)GetInt32(mBuffer, mBufferOffset);
                mBufferOffset += 4;
                return result;
            }

            public ulong ReadUInt64()
            {
                EnsureBuffer(8);
                var result = (ulong)GetInt64(mBuffer, mBufferOffset);
                mBufferOffset += 8;
                return result;
            }

            public ulong ReadNumber()
            {
                byte firstByte = ReadByte();
                byte mask = 0x80;
                ulong value = 0;

                for (int i = 0; i < 8; i++)
                {
                    if ((firstByte & mask) == 0)
                    {
                        ulong highPart = firstByte & (mask - 1u);
                        value += highPart << (i * 8);
                        return value;
                    }

                    EnsureBuffer(1);
                    value |= (ulong)ReadByte() << (8 * i);
                    mask >>= 1;
                }

                return value;
            }

            public string ReadString()
            {
                int length = 0;
                for (;;)
                {
                    EnsureBuffer(length + 2);
                    if (mBuffer[mBufferOffset + length] == 0 && mBuffer[mBufferOffset + length + 1] == 0)
                        break;

                    length += 2;
                }

                var result = Encoding.Unicode.GetString(mBuffer, mBufferOffset, length);
                mBufferOffset += length + 2;
                return result;
            }

            public void Skip(long size)
            {
                if (size < 0)
                    throw new InvalidDataException();

                if (size < mBufferEnding - mBufferOffset)
                {
                    mBufferOffset += (int)size;
                }
                else
                {
                    var offset = GetCurrentOffset();

                    if (size > mStream.Length - offset)
                        throw new InvalidDataException();

                    mBufferEnding = 0;
                    mBufferOffset = 0;

                    mStream.Seek(offset + size, SeekOrigin.Current);
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Reads only the header of the archive metadata and discards file specific information.
    /// </summary>
    public class ArchiveHeaderMetadataReader : ArchiveMetadataReader
    {
        public ArchiveMetadata ReadMetadata(Stream stream) => ReadMetadata(stream, null);

        public ArchiveMetadata ReadMetadata(Stream stream, Lazy<string> password)
        {
            return ReadMetadataCore(stream, password);
        }
    }
}
