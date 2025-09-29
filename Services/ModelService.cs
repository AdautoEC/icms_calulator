using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CsvIntegratorApp.Models;

namespace CsvIntegratorApp.Services
{
    public static class ModelService
    {
        private static readonly string AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CsvIntegratorApp");

        private static readonly string DefaultJsonPath = Path.Combine(AppDir, "modelo.local.json");

        public static void SaveLocal(List<ModelRow> rows)
        {
            Directory.CreateDirectory(AppDir);
            var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DefaultJsonPath, json);
        }

        public static List<ModelRow> LoadLocal()
        {
            try
            {
                if (File.Exists(DefaultJsonPath))
                {
                    var json = File.ReadAllText(DefaultJsonPath);
                    var rows = JsonSerializer.Deserialize<List<ModelRow>>(json);
                    return rows ?? new List<ModelRow>();
                }
            }
            catch { /* ignore */ }
            return new List<ModelRow>();
        }
    }
}
