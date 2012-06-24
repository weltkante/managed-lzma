using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedLzma.LZMA
{
    [TestClass]
    public class UnitTest2 : TestBase
    {
        [TestMethod]
        public void TestMethod64()
        {
            TestPack(64, 64, 5, true);
        }

        [TestMethod]
        public void TestMethod128()
        {
            TestPack(128, 128, 5, true);
        }

        [TestMethod]
        public void TestMethod1K()
        {
            TestPack(1024, 1 << 10, 5, true);
        }

        [TestMethod]
        public void TestMethod32K()
        {
            TestPack(1024, 1 << 15, 5, true);
        }

        //[TestMethod]
        //public void TestMethod1M()
        //{
        //    TestPack(1024, 1 << 20, 1024, true);
        //}

        //[TestMethod]
        //public void TestMethod10M()
        //{
        //    TestPack(1024, 10 << 20, 1024, true);
        //}
    }
}
