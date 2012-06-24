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
        class Password: master._7zip.Legacy.IPasswordProvider
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
            ManagedLzma.LZMA.SyncTrace.Enable = false;

            Directory.CreateDirectory("_test");

            using(var stream = new FileStream(@"_test\test.7z", FileMode.Create, FileAccess.ReadWrite, FileShare.Delete))
            {
                var writer = new ArchiveWriter(stream);
                writer.InitializeLzma2Encoder();
                string path = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                var directory = new DirectoryInfo(path);
                foreach(string filename in Directory.EnumerateFiles(path))
                    writer.WriteFile(directory, new FileInfo(filename));
                writer.WriteFinalHeader();
            }

            {
                var db = new master._7zip.Legacy.CArchiveDatabaseEx();
                var x = new master._7zip.Legacy.ArchiveReader();
                x.Open(new FileStream(@"_test\test.7z", FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
                x.ReadDatabase(db, null);
                db.Fill();
                x.Extract(db, null, null);
            }
        }
    }
}
