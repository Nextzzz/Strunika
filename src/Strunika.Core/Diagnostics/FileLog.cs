using System.Collections.Concurrent;
using System.Text;

namespace Strunika.Core.Diagnostics;

/// <summary>
/// Dead-simple file logger: one daily file, thread-safe, and it must
/// NEVER crash the app — logging failures are swallowed by design.
/// <para>
/// A line of information is stamped where it is said and written by a worker:
/// the song page says some from inside a frame, and opening and appending to a
/// file there — behind a lock any other thread's line could be holding — is a
/// frame lost. An error is written before the call returns, with everything
/// said before it, since the next thing to happen may be the end of the app.
/// </para>
/// </summary>
public static class FileLog
{
    private static readonly object Lock = new();
    private static readonly ConcurrentQueue<string> Pending = new();
    private static int _draining;

    public static string Directory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Strunika", "logs");

    public static string CurrentFile =>
        Path.Combine(Directory, $"strunika-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message)
    {
        Pending.Enqueue(Line("INFO ", message));
        if (Interlocked.CompareExchange(ref _draining, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(_ => Drain());
    }

    public static void Error(string message, Exception? exception = null)
    {
        Pending.Enqueue(Line("ERROR", exception == null
            ? message
            : message + Environment.NewLine + exception));
        Flush();
    }

    /// <summary>Everything said so far is in the file when this returns.</summary>
    public static void Flush()
    {
        try
        {
            lock (Lock)
            {
                if (Pending.IsEmpty) return;
                var text = new StringBuilder();
                while (Pending.TryDequeue(out var line)) text.Append(line);
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(CurrentFile, text.ToString());
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static string Line(string level, string message) =>
        $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

    private static void Drain()
    {
        while (true)
        {
            Flush();
            Volatile.Write(ref _draining, 0);
            // A line said between the flush and the flag going down has nobody to write it.
            if (Pending.IsEmpty || Interlocked.CompareExchange(ref _draining, 1, 0) != 0) return;
        }
    }
}
