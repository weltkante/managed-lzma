using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedLzma.LZMA
{
    [TestClass]
    public class UnitTest4: TestBase
    {
        [TestMethod]
        public void TestMethod64()
        {
            Test(new TestSettings
            {
                Seed = 64,
                DatLen = 64,
                RunLen = 5,
                Variant = 1,
                UseV2 = false,
                BTMode = 0,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethod128()
        {
            Test(new TestSettings
            {
                Seed = 128,
                DatLen = 128,
                RunLen = 5,
                Variant = 1,
                UseV2 = false,
                BTMode = 0,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethod1K()
        {
            TestPack(1024, 1 << 10);
            Test(new TestSettings
            {
                Seed = 1024,
                DatLen = 1 << 10,
                RunLen = 5,
                Variant = 1,
                UseV2 = false,
                BTMode = 0,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethod32K()
        {
            Test(new TestSettings
            {
                Seed = 1024,
                DatLen = 1 << 15,
                RunLen = 5,
                Variant = 1,
                UseV2 = false,
                BTMode = 0,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }
    }
}
