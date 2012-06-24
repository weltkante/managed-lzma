using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedLzma.LZMA
{
	[TestClass]
	public class UnitTestFastMode: TestBase
	{
        [TestMethod]
        public void TestMethodMtBT2_1()
        {
            Test(new TestSettings {
                Seed = 64,
                DatLen = 64,
                RunLen = 5,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT2_2()
        {
            Test(new TestSettings {
                Seed = 128,
                DatLen = 128,
                RunLen = 5,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT2_3()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1024,
                RunLen = 5,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT2_4()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 32768,
                RunLen = 256,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT2_5()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1048576,
                RunLen = 1024,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 2,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT3_6()
        {
            Test(new TestSettings {
                Seed = 64,
                DatLen = 64,
                RunLen = 5,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT3_7()
        {
            Test(new TestSettings {
                Seed = 128,
                DatLen = 128,
                RunLen = 5,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT3_8()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1024,
                RunLen = 5,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT3_9()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 32768,
                RunLen = 256,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT3_10()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1048576,
                RunLen = 1024,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 3,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT4_11()
        {
            Test(new TestSettings {
                Seed = 64,
                DatLen = 64,
                RunLen = 5,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT4_12()
        {
            Test(new TestSettings {
                Seed = 128,
                DatLen = 128,
                RunLen = 5,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT4_13()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1024,
                RunLen = 5,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT4_14()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 32768,
                RunLen = 256,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

        [TestMethod]
        public void TestMethodMtBT4_15()
        {
            Test(new TestSettings {
                Seed = 1024,
                DatLen = 1048576,
                RunLen = 1024,
                UseV2 = true,
                Algo = 0,
                BTMode = 1,
                NumHashBytes = 4,
                WriteEndMark = 1,
                NumThreads = 2,
            });
        }

	}
}
