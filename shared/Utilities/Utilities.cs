using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedLzma
{
    /// <summary>
    /// This exception is only thrown from "impossible" situations.
    /// If it is ever observed this indicates a bug in the library.
    /// </summary>
#if !BUILD_PORTABLE
    [Serializable]
#endif
    internal sealed class InternalFailureException : InvalidOperationException
    {
        public InternalFailureException()
        {
#if DEBUG
            if (System.Diagnostics.Debugger.IsAttached)
                System.Diagnostics.Debugger.Break();
#endif
        }

#if !BUILD_PORTABLE
        private InternalFailureException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// Controls how the completion of <see cref="IStreamReader"/> and <see cref="IStreamWriter"/> methods work.
    /// </summary>
    public enum StreamMode
    {
        /// <summary>
        /// Wait until the provided buffer section has been completely processed.
        /// </summary>
        Complete,

        /// <summary>
        /// Return after processing any amount of data from the provided buffer section.
        /// </summary>
        Partial,
    }

    public interface IStreamReader
    {
        /// <summary>Requests to read data into the provided buffer.</summary>
        /// <param name="buffer">The buffer into which data is read. Cannot be null.</param>
        /// <param name="offset">The offset at which data is written.</param>
        /// <param name="length">The amount of data to read. Must be greater than zero.</param>
        /// <param name="mode">Determines the response if the buffer cannot be filled.</param>
        /// <returns>A task which completes when the read completes. Returns the number of bytes written.</returns>
        Task<int> ReadAsync(byte[] buffer, int offset, int length, StreamMode mode);
    }

    public interface IStreamWriter
    {
        Task<int> WriteAsync(byte[] buffer, int offset, int length, StreamMode mode);
        Task CompleteAsync();
    }

    internal static class Utilities
    {
#if NET_45
        internal static Task CompletedTask => Task.FromResult<object>(null);
#else
        internal static Task CompletedTask => Task.CompletedTask;
#endif

        internal static void ClearBuffer<T>(ref T[] buffer)
        {
            if (buffer != null)
            {
                Array.Clear(buffer, 0, buffer.Length);
                buffer = null;
            }
        }

        internal static void CheckStreamArguments(byte[] buffer, int offset, int length, StreamMode mode)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            // Since length cannot be zero we also know offset cannot be the buffer length.
            if (offset < 0 || offset >= buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            // Length cannot be zero.
            if (length <= 0 || length > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(length));

            if (mode != StreamMode.Complete && mode != StreamMode.Partial)
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        [System.Diagnostics.Conditional("DEBUG")]
        internal static void DebugCheckStreamArguments(byte[] buffer, int offset, int length, StreamMode mode)
        {
            System.Diagnostics.Debug.Assert(buffer != null);
            System.Diagnostics.Debug.Assert(0 <= offset && offset < buffer.Length);
            System.Diagnostics.Debug.Assert(0 < length && length <= buffer.Length - offset);
            System.Diagnostics.Debug.Assert(mode == StreamMode.Complete || mode == StreamMode.Partial);
        }
    }

    internal struct AsyncTaskCompletionSource<T>
    {
        public static AsyncTaskCompletionSource<T> Create()
        {
#if NET_45
            return new AsyncTaskCompletionSource<T>(new TaskCompletionSource<T>());
#else
            return new AsyncTaskCompletionSource<T>(new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously));
#endif
        }

        private readonly TaskCompletionSource<T> mCompletionSource;

        private AsyncTaskCompletionSource(TaskCompletionSource<T> cps)
        {
            mCompletionSource = cps;
        }

        public Task<T> Task => mCompletionSource.Task;

        public void SetCanceled()
        {
#if NET_45
            var cps = mCompletionSource;
            System.Threading.Tasks.Task.Run(delegate { cps.SetCanceled(); });
#else
            mCompletionSource.SetCanceled();
#endif
        }

        public void SetResult(T result)
        {
#if NET_45
            var cps = mCompletionSource;
            System.Threading.Tasks.Task.Run(delegate { cps.SetResult(result); });
#else
            mCompletionSource.SetResult(result);
#endif
        }
    }
}

#if BUILD_PORTABLE && NET_45
namespace System.IO
{
    /// <summary>
    /// Provides attributes for files and directories.
    /// </summary>
    [Flags]
    public enum FileAttributes
    {
        /// <summary>
        /// The file is read-only.
        /// </summary>
        ReadOnly = 1,

        /// <summary>
        /// The file is hidden, and thus is not included in an ordinary directory listing.
        /// </summary>
        Hidden = 2,

        /// <summary>
        /// The file is a system file. That is, the file is part of the operating system
        /// or is used exclusively by the operating system.
        /// </summary>
        System = 4,

        /// <summary>
        /// The file is a directory.
        /// </summary>
        Directory = 16,

        /// <summary>
        /// The file is a candidate for backup or removal.
        /// </summary>
        Archive = 32,

        /// <summary>
        /// Reserved for future use.
        /// </summary>
        Device = 64,

        /// <summary>
        /// The file is a standard file that has no special attributes. This attribute is
        /// valid only if it is used alone.
        /// </summary>
        Normal = 128,

        /// <summary>
        /// The file is temporary. A temporary file contains data that is needed while an
        /// application is executing but is not needed after the application is finished.
        /// File systems try to keep all the data in memory for quicker access rather than
        /// flushing the data back to mass storage. A temporary file should be deleted by
        /// the application as soon as it is no longer needed.
        /// </summary>
        Temporary = 256,

        /// <summary>
        /// The file is a sparse file. Sparse files are typically large files whose data
        /// consists of mostly zeros.
        /// </summary>
        SparseFile = 512,

        /// <summary>
        /// The file contains a reparse point, which is a block of user-defined data associated
        /// with a file or a directory.
        /// </summary>
        ReparsePoint = 1024,

        /// <summary>
        /// The file is compressed.
        /// </summary>
        Compressed = 2048,

        /// <summary>
        /// The file is offline. The data of the file is not immediately available.
        /// </summary>
        Offline = 4096,

        /// <summary>
        /// The file will not be indexed by the operating system's content indexing service.
        /// </summary>
        NotContentIndexed = 8192,

        /// <summary>
        /// The file or directory is encrypted. For a file, this means that all data in the
        /// file is encrypted. For a directory, this means that encryption is the default
        /// for newly created files and directories.
        /// </summary>
        Encrypted = 16384,

        /// <summary>
        /// The file or directory includes data integrity support. When this value is applied
        /// to a file, all data streams in the file have integrity support. When this value
        /// is applied to a directory, all new files and subdirectories within that directory,
        /// by default, include integrity support.
        /// </summary>
        IntegrityStream = 32768,

        /// <summary>
        /// The file or directory is excluded from the data integrity scan. When this value
        /// is applied to a directory, by default, all new files and subdirectories within
        /// that directory are excluded from data integrity.
        /// </summary>
        NoScrubData = 131072,
    }
}
#endif
