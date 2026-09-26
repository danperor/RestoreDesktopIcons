using System.IO;
using System.Text;

namespace RestoreDesktopIcons.Services;

/// <summary>
/// 统一应用日志记录器：支持异常堆栈持久化、级别划分、线程安全和自动轮转
/// </summary>
public static class AppLogger
{
    private static readonly object LockObj = new();
    public static string LogFilePath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
    private const long MaxLogSizeBytes = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// 控制台输出开关：CLI 命令行执行时开启，以便在终端中实时查看进度
    /// </summary>
    public static bool ConsoleOutputEnabled { get; set; } = false;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public static void Debug(string message)
    {
#if DEBUG
        Write("DEBUG", message, null);
#endif
    }

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [T{Environment.CurrentManagedThreadId:D2}] {message}");

            if (ex != null)
            {
                sb.AppendLine();
                sb.Append($"  Exception: {ex.GetType().FullName}: {ex.Message}");
                if (!string.IsNullOrEmpty(ex.StackTrace))
                {
                    sb.AppendLine();
                    sb.Append($"  StackTrace:\n{ex.StackTrace}");
                }

                var inner = ex.InnerException;
                while (inner != null)
                {
                    sb.AppendLine();
                    sb.Append($"  InnerException: {inner.GetType().FullName}: {inner.Message}");
                    if (!string.IsNullOrEmpty(inner.StackTrace))
                    {
                        sb.AppendLine();
                        sb.Append($"  InnerStackTrace:\n{inner.StackTrace}");
                    }
                    inner = inner.InnerException;
                }
            }

            sb.AppendLine();
            string entry = sb.ToString();

            // 1. 发送至调试器
            System.Diagnostics.Trace.Write(entry);

            // 2. 控制台实时打印 (CLI 模式或控制台已附加时)
            if (ConsoleOutputEnabled)
            {
                try
                {
                    Console.Write(entry);
                }
                catch { }
            }

            // 3. 线程安全写入磁盘文件
            lock (LockObj)
            {
                RotateIfNeeded();
                File.AppendAllText(LogFilePath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志记录系统自身不得抛出未捕获异常破坏调用方业务
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                var fi = new FileInfo(LogFilePath);
                if (fi.Length > MaxLogSizeBytes)
                {
                    string oldLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.old.log");
                    if (File.Exists(oldLog))
                    {
                        File.Delete(oldLog);
                    }
                    File.Move(LogFilePath, oldLog);
                }
            }
        }
        catch
        {
            // 忽略轮转异常
        }
    }
}
