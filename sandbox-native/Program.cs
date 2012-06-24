using System;
using System.Linq;

namespace ManagedLzma.LZMA
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Server.Main("-ipc", "sandbox-native");
        }
    }
}
