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
                    using (var stream = new FileStream(@"_test\test.7z", FileMode.Create, FileAccess.ReadWrite, FileShare.Delete))
                    using (var writer = ManagedLzma.SevenZip.ArchiveWriter.Create(stream))
                    {
                        var encoder = new ManagedLzma.SevenZip.EncoderDefinition();
                        var lzma2 = encoder.CreateEncoder(new ManagedLzma.SevenZip.Encoders.Lzma2EncoderSettings(new ManagedLzma.LZMA2.EncoderSettings()));
                        var crypt = encoder.CreateEncoder(new ManagedLzma.SevenZip.Encoders.AesEncoderSettings(ManagedLzma.PasswordStorage.Create("test")));
                        encoder.Connect(encoder.GetContentSource(), lzma2.GetInput(0));
                        encoder.Connect(lzma2.GetOutput(0), crypt.GetInput(0));
                        encoder.Connect(crypt.GetOutput(0), encoder.CreateStorageSink());
                        encoder.Complete();

                        using (var session = writer.BeginEncoding(encoder))
                        {
                            var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(Program).Assembly.Location));
                            foreach (var file in directory.EnumerateFiles())
                                session.AppendFile(file, directory);
                        }

                        writer.WriteMetadata();
                        writer.WriteHeader();
                    }
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
                    var dsReader = new ManagedLzma.SevenZip.DecodedSectionReader(file, mdModel.Metadata, 0, ManagedLzma.PasswordStorage.Create("test"));
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
