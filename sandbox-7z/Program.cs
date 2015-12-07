using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ManagedLzma.LZMA.Master.SevenZip;

namespace sandbox_7z
{
    static class Program
    {
        class Password : master._7zip.Legacy.IPasswordProvider
        {
            string _pw;

            public Password(string pw)
            {
                _pw = pw;
            }

            string master._7zip.Legacy.IPasswordProvider.CryptoGetTextPassword()
            {
                return _pw;
            }
        }

        [STAThread]
        static void Main()
        {
            Directory.CreateDirectory("_test");

            bool writeArchive = true;
            bool useOldWriter = false;
            bool readArchive = true;
            bool useOldReader = false;

            if (writeArchive)
            {
                if (useOldWriter)
                {
                    using (var stream = new FileStream(@"_test\test.7z", FileMode.Create, FileAccess.ReadWrite, FileShare.Delete))
                    using (var encryption = new AESEncryptionProvider("test"))
                    using (var encoder = new ArchiveWriter.Lzma2Encoder(null))
                    {
                        var writer = new ArchiveWriter(stream);
                        writer.DefaultEncryptionProvider = encryption;
                        writer.ConnectEncoder(encoder);
                        string path = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                        var directory = new DirectoryInfo(path);
                        foreach (string filename in Directory.EnumerateFiles(path))
                            writer.WriteFile(directory, new FileInfo(filename));
                        writer.WriteFinalHeader();
                    }
                }
                else
                {
                    Task.Run(async delegate {
                        using (var archiveStream = new FileStream(@"_test\test.7z", FileMode.Create, FileAccess.ReadWrite, FileShare.Delete))
                        using (var archiveWriter = ManagedLzma.SevenZip.Writer.ArchiveWriter.Create(archiveStream, false))
                        {
                            var encoder = new ManagedLzma.SevenZip.Writer.EncoderDefinition();
                            ManagedLzma.SevenZip.Writer.EncoderNodeDefinition node1 = null;
                            ManagedLzma.SevenZip.Writer.EncoderNodeDefinition node2 = null;
                            //node1 = encoder.CreateEncoder(ManagedLzma.SevenZip.Encoders.CopyEncoderSettings.Instance);
                            //node1 = encoder.CreateEncoder(new ManagedLzma.SevenZip.Encoders.LzmaEncoderSettings(new ManagedLzma.LZMA.EncoderSettings()));
                            //node1 = encoder.CreateEncoder(new ManagedLzma.SevenZip.Encoders.Lzma2EncoderSettings(new ManagedLzma.LZMA2.EncoderSettings()));
                            node2 = encoder.CreateEncoder(new ManagedLzma.SevenZip.Writer.AesEncoderSettings(ManagedLzma.PasswordStorage.Create("test")));
                            if (node1 != null && node2 != null)
                            {
                                encoder.Connect(encoder.GetContentSource(), node1.GetInput(0));
                                encoder.Connect(node1.GetOutput(0), node2.GetInput(0));
                                encoder.Connect(node2.GetOutput(0), encoder.CreateStorageSink());
                            }
                            else
                            {
                                encoder.Connect(encoder.GetContentSource(), (node1 ?? node2).GetInput(0));
                                encoder.Connect((node1 ?? node2).GetOutput(0), encoder.CreateStorageSink());
                            }
                            encoder.Complete();

                            var metadata = new ManagedLzma.SevenZip.Writer.ArchiveMetadataRecorder();

                            var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(Program).Assembly.Location));

                            bool useDistinctEncoders = false;

                            if (useDistinctEncoders)
                            {
                                foreach (var file in directory.EnumerateFiles())
                                {
                                    using (var session = archiveWriter.BeginEncoding(encoder, true))
                                    {
                                        using (var fileStream = file.OpenRead())
                                        {
                                            var result = await session.AppendStream(fileStream, true);
                                            metadata.AppendFile(file.Name, result.Length, result.Checksum, file.Attributes, file.CreationTimeUtc, file.LastWriteTimeUtc, file.LastAccessTimeUtc);
                                        }

                                        // TODO: ensure that everything still aborts properly if we don't call complete
                                        await session.Complete();
                                    }
                                }
                            }
                            else
                            {
                                using (var session = archiveWriter.BeginEncoding(encoder, true))
                                {
                                    foreach (var file in directory.EnumerateFiles())
                                    {
                                        using (var fileStream = file.OpenRead())
                                        {
                                            var result = await session.AppendStream(fileStream, true);
                                            metadata.AppendFile(file.Name, result.Length, result.Checksum, file.Attributes, file.CreationTimeUtc, file.LastWriteTimeUtc, file.LastAccessTimeUtc);
                                        }
                                    }

                                    // TODO: ensure that everything still aborts properly if we don't call complete
                                    await session.Complete();
                                }
                            }

                            await archiveWriter.WriteMetadata(metadata);
                            await archiveWriter.WriteHeader();
                        }
                    }).GetAwaiter().GetResult();
                }
            }

            if (readArchive)
            {
                if (useOldReader)
                {
                    var pass = new Password("test");
                    var db = new master._7zip.Legacy.CArchiveDatabaseEx();
                    var x = new master._7zip.Legacy.ArchiveReader();
                    x.Open(new FileStream(@"_test\test.7z", FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
                    x.ReadDatabase(db, pass);
                    db.Fill();
                    x.Extract(db, null, pass);
                }
                else
                {
                    var file = new FileStream(@"_test\test.7z", FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    var mdReader = new ManagedLzma.SevenZip.FileModel.ArchiveFileModelMetadataReader();
                    var mdModel = mdReader.ReadMetadata(file);
                    var dsReader = new ManagedLzma.SevenZip.Reader.DecodedSectionReader(file, mdModel.Metadata, 0, ManagedLzma.PasswordStorage.Create("test"));
                    int k = 0;
                    while (dsReader.CurrentStreamIndex < dsReader.StreamCount)
                    {
                        var substream = dsReader.OpenStream();
                        using (var outstream = new FileStream(@"_test\output" + (++k), FileMode.Create, FileAccess.ReadWrite, FileShare.Delete))
                            substream.CopyTo(outstream);
                        dsReader.NextStream();
                    }
                }
            }
        }
    }
}
