using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Threading;

namespace ManagedLzma.LZMA
{
    internal static class Program
    {
        public static void RunSandbox()
        {
            TestRunner.RunTest(new TestSettings
            {
                Seed = 64,
                DatLen = 64,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 2,
            });

            //TestRunner.RunTest(new TestSettings {
            //    Seed = 1024,
            //    DatLen = 10 << 10,
            //    RunLen = 1024,
            //    UseV2 = true,
            //    Variant = 1,

            //    WriteEndMark = 1,
            //    NumThreads = 0,
            //});

            //TestRunner.RunTest(new TestSettings {
            //    Seed = 1024,
            //    DatLen = 10 << 10,
            //    RunLen = 1024,
            //    UseV2 = true,

            //    WriteEndMark = 1,
            //    NumThreads = 0,
            //});

            //TestRunner.RunTest(new TestSettings {
            //    Seed = 1024,
            //    DatLen = 10 << 15,
            //    RunLen = 256,
            //    UseV2 = true,
            //    WriteEndMark = 1,
            //    BTMode = 1,
            //    NumHashBytes = 4,

            //    NumThreads = 4,
            //    NumTotalThreads = 4,
            //    NumBlockThreads = 2,
            //});
        }
    }
}
