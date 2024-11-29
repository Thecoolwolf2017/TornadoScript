using System;
using System.IO;
using System.Text;
using GTA;

namespace TornadoScript.ScriptMain.Utility
{
    public static class ErrorLogger
    {
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TornadoScript");
        private static readonly string ErrorLogPath = Path.Combine(LogDirectory, "error.log");
        private static readonly object LogLock = new object();

        static ErrorLogger()
        {
            try
            {
                if (!Directory.Exists(LogDirectory))
                    Directory.CreateDirectory(LogDirectory);
            }
            catch { }
        }

        public static void LogError(Exception ex, string context, bool notify = true)
        {
            try
            {
                lock (LogLock)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"\n=== ERROR in {context} at {DateTime.Now} ===");
                    sb.AppendLine($"Error: {ex.Message}");
                    sb.AppendLine($"Stack Trace: {ex.StackTrace}");

                    if (ex.InnerException != null)
                    {
                        sb.AppendLine("Inner Exception:");
                        sb.AppendLine($"Error: {ex.InnerException.Message}");
                        sb.AppendLine($"Stack Trace: {ex.InnerException.StackTrace}");
                    }

                    File.AppendAllText(ErrorLogPath, sb.ToString());

                    // Log to console
                    Logger.Error($"Error in {context}: {ex.Message}");

                    // Show notification if requested
                    if (notify)
                    {
                        GTA.UI.Notification.PostTicker($"Error in {context}: {ex.Message}", false);
                    }
                }
            }
            catch { } // Fail silently if we can't log
        }

        public static void LogError(string message, string context, bool notify = true)
        {
            try
            {
                lock (LogLock)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"\n=== ERROR in {context} at {DateTime.Now} ===");
                    sb.AppendLine($"Error: {message}");

                    File.AppendAllText(ErrorLogPath, sb.ToString());

                    // Log to console
                    Logger.Error($"Error in {context}: {message}");

                    // Show notification if requested
                    if (notify)
                    {
                        GTA.UI.Notification.PostTicker($"Error in {context}: {message}", false);
                    }
                }
            }
            catch { } // Fail silently if we can't log
        }
    }
}
