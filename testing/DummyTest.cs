using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

// These tests only exist to generate coverage.

namespace ManagedLzma.LZMA
{
    using LZMA = Master.LZMA;

    [TestClass]
    public class DummyTest : TestBase
    {
        [TestMethod]
        public void TestSRes()
        {
            LZMA.SRes res = LZMA.SZ_OK;
            res.GetHashCode(); // coverage
            Assert.IsTrue(res.Equals(res));
            Assert.IsTrue(res.Equals((object)res));
            Assert.IsFalse(res.Equals(null));
        }
    }
}
