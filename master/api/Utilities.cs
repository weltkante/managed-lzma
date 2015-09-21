using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManagedLzma
{
    public interface IPasswordProvider
    {
        string GetPassword();
    }

    public enum ReadMode
    {
        /// <summary>
        /// Wait until the provided buffer has been completely filed.
        /// </summary>
        FillBuffer,

        /// <summary>
        /// Return as soon as something has been written to the buffer, even if the buffer could not be filled completely.
        /// </summary>
        ReturnEarly,
    }
}
