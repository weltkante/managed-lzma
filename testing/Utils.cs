using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedLzma.LZMA
{
    public abstract class TestBase
    {
        public TestContext TestContext { get; set; }

        #region Additional test attributes
        //
        // You can use the following additional attributes as you write your tests:
        //
        // Use ClassInitialize to run code before running the first test in the class
        // [ClassInitialize()]
        // public static void MyClassInitialize(TestContext testContext) { }
        //
        // Use ClassCleanup to run code after all tests in a class have run
        // [ClassCleanup()]
        // public static void MyClassCleanup() { }
        //
        // Use TestInitialize to run code before running each test 
        // [TestInitialize()]
        // public void MyTestInitialize() { }
        //
        // Use TestCleanup to run code after each test has run
        // [TestCleanup()]
        // public void MyTestCleanup() { }
        //
        #endregion

        protected void Test(TestSettings t)
        {
            TestRunner.RunTest(t);
        }

        protected void TestPack(int seed, int len, int runlen = 5, bool v2 = false)
        {
            Test(new TestSettings
            {
                Seed = seed,
                DatLen = len,
                RunLen = runlen,
                UseV2 = v2,
                WriteEndMark = 1,
                NumThreads = 0,
            });
        }
    }
}
