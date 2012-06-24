using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ManagedLzma.LZMA.Master
{
    public static partial class LZMA
    {
        /* #define SHOW_STAT */
        /* #define SHOW_STAT2 */
        /* #define SHOW_DEBUG_INFO */
        /* #define PROTOTYPE */

        [System.Diagnostics.Conditional("SHOW_DEBUG_INFO")]
        internal static void DebugPrint(string format, params object[] args)
        {
            System.Diagnostics.Debug.Write(String.Format(format, args));
        }

        internal static void Print(string format, params object[] args)
        {
            System.Diagnostics.Debug.Write(String.Format(format, args));
        }
    }
}
