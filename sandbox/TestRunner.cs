using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ManagedLzma.Testing;
using ManagedCoder = ManagedLzma.LZMA.Helper;
using ManagedCoder2 = ManagedLzma.LZMA.Helper2;
using NativeCoder = ManagedLzma.LZMA.Reference.Native.Helper;
using NativeCoder2 = ManagedLzma.LZMA.Reference.Native.Helper2;

namespace ManagedLzma.LZMA
{
    [Serializable]
    public class TestSettings : SharedSettings
    {
        public int Seed;
        public int DatLen;
        public int RunLen;

        public TestSettings() { }
        public TestSettings(TestSettings other)
            : base(other)
        {
            this.Seed = other.Seed;
            this.DatLen = other.DatLen;
            this.RunLen = other.RunLen;
        }
    }

    public static class TestRunner
    {
        private static IHelper CreateHelper(Guid id, bool managed, SharedSettings s)
        {
            if (managed)
            {
                if (s.UseV2)
                    return new ManagedCoder2(id);
                else
                    return new ManagedCoder(id);
            }
            else
            {
                if (s.UseV2)
                    return new NativeCoder2(id);
                else
                    return new NativeCoder(id);
            }
        }

        private static void GenData(int seed, int runlen, byte[] buffer)
        {
            var rnd = new Random(seed);
            byte val = (byte)rnd.Next(256);
            for (int i = 0; i < buffer.Length; i++)
            {
                if (rnd.Next(runlen) == 0)
                    val = (byte)rnd.Next(32);

                buffer[i] = val;
            }
        }

        private static TestSettings Pack(Guid id, bool managed, TestSettings s)
        {
            s.Dst = new PZ(new byte[s.DatLen * 2]);
            s.Src = new PZ(new byte[s.DatLen]);

            GenData(s.Seed, s.RunLen, s.Src.Buffer);

            using (var coder = CreateHelper(id, managed, s))
                coder.LzmaCompress(s);

            return s;
        }

        private static TestSettings Unpack(Guid id, bool managed, TestSettings t)
        {
            var s = new TestSettings(t)
            {
                Dst = new PZ(new byte[t.Src.Length]),
                Src = t.Dst,
                Enc = t.Enc,
            };

            using (var coder = CreateHelper(id, managed, s))
                coder.LzmaUncompress(s);

            if (s.WrittenSize != t.Src.Length)
                throw new InvalidOperationException();

            if (s.UsedSize != t.WrittenSize)
                throw new InvalidOperationException();

            return s;
        }

        public static void RunPackTest(ref TestSettings settings)
        {
            TestSettings r1 = null;
            TestSettings r2 = null;

            using (var nativeManager = new LocalManager())
            {
                Guid id = Guid.NewGuid();

                var nativeResult = nativeManager.BeginInvoke(typeof(TestRunner), "Pack", id, false, settings);

                using (var managedManager = new InprocManager())
                    r2 = (TestSettings)managedManager.Invoke(typeof(TestRunner), "Pack", id, true, settings);

                r1 = (TestSettings)nativeManager.EndInvoke(nativeResult);
            }

            if (r1.WrittenSize != r2.WrittenSize)
                throw new InvalidOperationException();

            for (int i = 0; i < r1.WrittenSize; i++)
                if (r1.Dst[i] != r2.Dst[i])
                    throw new InvalidOperationException();

            if (r1.Enc.Length != r2.Enc.Length)
                throw new InvalidOperationException();

            for (int i = 0; i < r1.Enc.Length; i++)
                if (r1.Enc[i] != r2.Enc[i])
                    throw new InvalidOperationException();

            settings = r1;
        }

        public static void RunUnpackTest(ref TestSettings settings)
        {
            TestSettings r1 = null;
            TestSettings r2 = null;

            using (var nativeManager = new LocalManager())
            {
                Guid id = Guid.NewGuid();

                var nativeResult = nativeManager.BeginInvoke(typeof(TestRunner), "Unpack", id, false, settings);

                using (var managedManager = new InprocManager())
                    r2 = (TestSettings)managedManager.Invoke(typeof(TestRunner), "Unpack", id, true, settings);

                r1 = (TestSettings)nativeManager.EndInvoke(nativeResult);
            }

            if (r1.WrittenSize != r2.WrittenSize)
                throw new InvalidOperationException();

            if (r1.UsedSize != r2.UsedSize)
                throw new InvalidOperationException();

            for (int i = 0; i < r1.WrittenSize; i++)
                if (r1.Dst[i] != r2.Dst[i])
                    throw new InvalidOperationException();

            settings = r1;
        }

        private static void RunTestInternal(TestSettings test)
        {
            RunPackTest(ref test);

            GC.Collect();
            GC.WaitForPendingFinalizers();

            RunUnpackTest(ref test);
        }

        public static void RunTest(TestSettings test)
        {
            try
            {
                RunTestInternal(test);
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }
}
