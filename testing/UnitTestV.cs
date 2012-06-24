using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedLzma.LZMA
{
	[TestClass]
	public class UnitTestV: TestBase
	{
        [TestMethod]
        public void TestMethodBT2_1()
        {
            Test(new TestSettings {
                Seed = 64,
                DatLen = 64,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT2_2()
        {
            Test(new TestSettings {
                Seed = 128,
                DatLen = 128,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT2_3()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1024,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT2_4()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 32768,
                RunLen = 256,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT2_5()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1048576,
                RunLen = 1024,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT3_6()
        {
            Test(new TestSettings {
                Seed = 64,
                DatLen = 64,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT3_7()
        {
            Test(new TestSettings {
                Seed = 128,
                DatLen = 128,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT3_8()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1024,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT3_9()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 32768,
                RunLen = 256,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT3_10()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1048576,
                RunLen = 1024,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT4_11()
        {
            Test(new TestSettings {
                Seed = 64,
                DatLen = 64,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT4_12()
        {
            Test(new TestSettings {
                Seed = 128,
                DatLen = 128,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT4_13()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1024,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT4_14()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 32768,
                RunLen = 256,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

        [TestMethod]
        public void TestMethodBT4_15()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1048576,
                RunLen = 1024,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }

	}
}
