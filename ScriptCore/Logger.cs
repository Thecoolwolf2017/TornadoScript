using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace TornadoScript.ScriptCore
{
    public enum LogLevel
    {
        Trace,
        Debug,
        Information,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Static logger class that allows direct logging of anything to a text file
    /// </summary>
    public static class Logger
    {
        private static bool _initialized;
        private static readonly object _lock = new object();
        private static string _logDirectory;
        private static string _mainLogPath;
        private static string _errorLogPath;
        private static readonly int MaxLogSizeBytes = 5 * 1024 * 1024; // 5MB

        static Logger()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;

            try
            {
                // Set up log directory - ensure consistent location in GTA V scripts folder
                string gtaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "GTA V");
                _logDirectory = Path.Combine(gtaPath, "scripts", "TornadoScript");
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                // Set up log files
                _mainLogPath = Path.Combine(_logDirectory, "tornado.log");
                _errorLogPath = Path.Combine(_logDirectory, "error.log");

                // Rotate logs if they're too big
                RotateLogIfNeeded(_mainLogPath);
                RotateLogIfNeeded(_errorLogPath);

                // Write header to main log
                string header =
                    $"=== TornadoScript Log Started at {DateTime.Now} ===\n" +
                    $"Version: {Assembly.GetExecutingAssembly().GetName().Version}\n" +
                    $"OS: {Environment.OSVersion}\n" +
                    "=====================================\n";

                File.AppendAllText(_mainLogPath, header);
                _initialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize logger: {ex.Message}");
            }
        }

        private static void RotateLogIfNeeded(string logPath)
        {
            if (!File.Exists(logPath)) return;

            try
            {
                var fileInfo = new FileInfo(logPath);
                if (fileInfo.Length > MaxLogSizeBytes)
                {
                    string backupPath = logPath + ".old";
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                    File.Move(logPath, backupPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to rotate log file: {ex.Message}");
            }
        }

        public static void Log(LogLevel level, string format, params object[] args)
        {
            if (!_initialized)
            {
                Initialize();
            }

            try
            {
                string message = args.Length > 0 ? string.Format(format, args) : format;
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logEntry = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";

                lock (_lock)
                {
                    // Write to appropriate log file based on level
                    string targetPath = (level == LogLevel.Error || level == LogLevel.Critical)
                        ? _errorLogPath
                        : _mainLogPath;

                    File.AppendAllText(targetPath, logEntry);

                    // Also output to debug console
                    System.Diagnostics.Debug.WriteLine(logEntry);

                    // Rotate log if needed
                    if (level != LogLevel.Error && level != LogLevel.Critical)
                    {
                        RotateLogIfNeeded(_mainLogPath);
                    }
                    else
                    {
                        RotateLogIfNeeded(_errorLogPath);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write to log: {ex.Message}");
            }
        }

        public static void LogException(Exception ex, string context = null)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(context))
            {
                sb.AppendLine($"Context: {context}");
            }
            sb.AppendLine($"Exception: {ex.GetType().Name}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"Stack Trace: {ex.StackTrace}");

            if (ex.InnerException != null)
            {
                sb.AppendLine("Inner Exception:");
                sb.AppendLine($"Type: {ex.InnerException.GetType().Name}");
                sb.AppendLine($"Message: {ex.InnerException.Message}");
                sb.AppendLine($"Stack Trace: {ex.InnerException.StackTrace}");
            }

            Log(LogLevel.Error, sb.ToString());
        }

        public static void Log(string format, params object[] args)
        {
            Log(LogLevel.Information, format, args);
        }

        public static void Debug(string format, params object[] args) => Log(LogLevel.Debug, format, args);
        public static void Info(string format, params object[] args) => Log(LogLevel.Information, format, args);
        public static void Warning(string format, params object[] args) => Log(LogLevel.Warning, format, args);
        public static void Error(string format, params object[] args) => Log(LogLevel.Error, format, args);
        public static void Critical(string format, params object[] args) => Log(LogLevel.Critical, format, args);
    }
}
