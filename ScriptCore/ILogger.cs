namespace TornadoScript.ScriptCore
{
    /// <summary>
    /// Interface for logging functionality
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Logs an informational message
        /// </summary>
        void Log(string message);

        /// <summary>
        /// Logs an error message
        /// </summary>
        void Error(string message);
    }
}
