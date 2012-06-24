using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedLzma.LZMA
{
	[TestClass]
	public class UnitTestM2: TestBase
	{
        [TestMethod]
        public void Thread4_MtBT2_1()
        {
            Test(new TestSettings {
                Seed = 64,
                DatLen = 64,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

        [TestMethod]
        public void Thread4_MtBT2_2()
        {
            Test(new TestSettings {
                Seed = 128,
                DatLen = 128,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

        [TestMethod]
        public void Thread4_MtBT2_3()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1024,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

        [TestMethod]
        public void Thread4_MtBT2_4()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 32768,
                RunLen = 256,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

        [TestMethod]
        public void Thread4_MtBT3_5()
        {
            Test(new TestSettings {
                Seed = 64,
                DatLen = 64,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

        [TestMethod]
        public void Thread4_MtBT3_6()
        {
            Test(new TestSettings {
                Seed = 128,
                DatLen = 128,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

        [TestMethod]
        public void Thread4_MtBT3_7()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1024,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

        [TestMethod]
        public void Thread4_MtBT3_8()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 32768,
                RunLen = 256,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

        [TestMethod]
        public void Thread4_MtBT4_9()
        {
            Test(new TestSettings {
                Seed = 64,
                DatLen = 64,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

        [TestMethod]
        public void Thread4_MtBT4_10()
        {
            Test(new TestSettings {
                Seed = 128,
                DatLen = 128,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

        [TestMethod]
        public void Thread4_MtBT4_11()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1024,
                RunLen = 5,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

        [TestMethod]
        public void Thread4_MtBT4_12()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 32768,
                RunLen = 256,
                UseV2 = true,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 4,
                NumBlockThreads = 2,
                NumTotalThreads = 4,
            });
        }

	}
}
