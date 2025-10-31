// Services/CalculationLogService.cs
using System;
using System.IO;
using System.Text;

namespace CsvIntegratorApp.Services
{
    /// <summary>
    /// Provides logging functionality for calculation processes, storing messages in a temporary file.
    /// </summary>
    public static class CalculationLogService
    {
        private static readonly StringBuilder _logBuilder = new StringBuilder();
        
        /// <summary>
        /// Gets the full path to the log file.
        /// </summary>
        public static string LogFilePath { get; private set; }

        /// <summary>
        /// Initializes the <see cref="CalculationLogService"/> class, setting up the log file path.
        /// </summary>
        static CalculationLogService()
        {
            LogFilePath = Path.Combine(Path.GetTempPath(), "calculation_log.txt");
        }

        /// <summary>
        /// Clears all accumulated log messages.
        /// </summary>
        public static void Clear()
        {
            _logBuilder.Clear();
        }

        /// <summary>
        /// Logs a message with a timestamp.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Log(string message)
        {
            _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        /// <summary>
        /// Saves all accumulated log messages to the log file.
        /// Errors during saving are ignored to prevent application crashes.
        /// </summary>
        public static void Save()
        {
            try
            {
                File.WriteAllText(LogFilePath, _logBuilder.ToString());
            }
            catch
            {
                // Ignora erros de escrita no log para não travar a aplicação
            }
        }
    }
}
