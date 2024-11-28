using System;
using System.Diagnostics;
using System.Reflection;
using System.IO;

namespace TornadoScript.ScriptCore
{
    /// <summary>
    /// Static logger class that allows direct logging of anything to a text file
    /// </summary>
    public static class Logger
    {
        private static readonly string DefaultLogPath = "TornadoScript.log";
        private static string _logPath;
        private static bool _initialized;

        public static string LogPath
        {
            get => _logPath ?? DefaultLogPath;
            set => _logPath = value;
        }

        public enum LogLevel
        {
            Trace,
            Information,
            Warning,
            Error
        }

        static Logger()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;
            
            try
            {
                // Ensure directory exists
                string directory = Path.GetDirectoryName(Path.GetFullPath(LogPath));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write header to log file
                string header = $"=== TornadoScript Log Started at {DateTime.Now} ===\n" +
                              $"Version: {Assembly.GetExecutingAssembly().GetName().Version}\n" +
                              $"OS: {Environment.OSVersion}\n" +
                              "=====================================\n";
                
                File.AppendAllText(LogPath, header);
                _initialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize logger: {ex.Message}");
            }
        }

        public static void Log(string format, params object[] args)
        {
            Log(LogLevel.Information, format, args);
        }

        public static void Log(LogLevel level, string format, params object[] args)
        {
            try
            {
                string message = string.Format(format, args);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logEntry = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";

                File.AppendAllText(LogPath, logEntry);

                // Also output to debug console for debugging purposes
                System.Diagnostics.Debug.WriteLine(logEntry);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write to log: {ex.Message}");
            }
        }

        public static void Debug(string format, params object[] args) => Log(LogLevel.Trace, format, args);
        public static void Info(string format, params object[] args) => Log(LogLevel.Information, format, args);
        public static void Warning(string format, params object[] args) => Log(LogLevel.Warning, format, args);
        public static void Error(string format, params object[] args) => Log(LogLevel.Error, format, args);
    }
}
