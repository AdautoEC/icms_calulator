// Services/CalculationLogService.cs
using System;
using System.IO;
using System.Text;

namespace CsvIntegratorApp.Services
{
    public static class CalculationLogService
    {
        private static readonly StringBuilder _logBuilder = new StringBuilder();
        
        public static string LogFilePath { get; private set; }

        static CalculationLogService()
        {
            LogFilePath = Path.Combine(Path.GetTempPath(), "calculation_log.txt");
        }

        public static void Clear()
        {
            _logBuilder.Clear();
        }

        public static void Log(string message)
        {
            _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

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
