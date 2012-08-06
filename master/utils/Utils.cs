using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Compat
{
    public static class String
    {
        public static string Join(string separator, IEnumerable<string> fragments)
        {
#if NET_20
            StringBuilder sb = new StringBuilder();
            using(var iter = fragments.GetEnumerator())
            {
                if(iter.MoveNext())
                {
                    sb.Append(iter.Current);
                    while(iter.MoveNext())
                    {
                        sb.Append(separator);
                        sb.Append(iter.Current);
                    }
                }
            }
            return sb.ToString();
#else
            return System.String.Join(separator, fragments);
#endif
        }
    }
}

namespace ManagedLzma.LZMA
{
    public interface IBufferPool
    {
        P<byte> Allocate(int size);
        void Release(P<byte> buffer);
    }

    internal static class CUtils
    {
        public static void memcpy(P<byte> dst, P<byte> src, long size)
        {
            memcpy(dst, src, checked((int)size));
        }

        public static void memcpy(P<byte> dst, P<byte> src, int size)
        {
            if (dst.mBuffer == src.mBuffer && src.mOffset < dst.mOffset + size && dst.mOffset < src.mOffset + size)
            {
                System.Diagnostics.Debugger.Break();
                throw new InvalidOperationException("memcpy cannot handle overlapping regions correctly");
            }

            Buffer.BlockCopy(src.mBuffer, src.mOffset, dst.mBuffer, dst.mOffset, size);
        }

        public static void memmove<T>(P<T> dst, P<T> src, uint size)
        {
            memmove(dst, src, (long)size);
        }

        public static void memmove<T>(P<T> dst, P<T> src, long size)
        {
            if (dst.mBuffer == src.mBuffer && dst.mOffset > src.mOffset && dst.mOffset < src.mOffset + size)
            {
                // destination would overwrite source so we need to copy backwards
                for(long i = size - 1; i >= 0; i--)
                    dst[i] = src[i];
            }
            else
            {
                for(uint i = 0; i < size; i++)
                    dst[i] = src[i];
            }
        }

        public static T[] Init<T>(int sz1, Func<T> init)
        {
            T[] buffer = new T[sz1];
            for (int i = 0; i < sz1; i++)
                buffer[i] = init();
            return buffer;
        }

        public static T[][] Init<T>(int sz1, int sz2)
        {
            T[][] buffer = new T[sz1][];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = new T[sz2];
            return buffer;
        }

        [System.Diagnostics.DebuggerHidden]
        public static void Assert(bool expr)
        {
            if (!expr)
            {
                if (System.Diagnostics.Debugger.IsAttached)
                    System.Diagnostics.Debugger.Break();

                throw new InvalidOperationException("Assertion failed.");
            }
        }
    }
}
