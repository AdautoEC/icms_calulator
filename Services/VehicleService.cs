// Services/VehicleService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CsvIntegratorApp.Models;

namespace CsvIntegratorApp.Services
{
    public static class VehicleService
    {
        private static readonly string AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CsvIntegratorApp");

        private static readonly string DbPath = Path.Combine(AppDir, "vehicles.json");

        private static List<VehicleInfo> _vehicles = new();
        private static bool _loaded = false;

        public static List<VehicleInfo> GetVehicles()
        {
            if (!_loaded) LoadVehicles();
            return _vehicles;
        }

        public static void LoadVehicles()
        {
            try
            {
                if (File.Exists(DbPath))
                {
                    var json = File.ReadAllText(DbPath);
                    _vehicles = JsonSerializer.Deserialize<List<VehicleInfo>>(json) ?? new List<VehicleInfo>();
                }
                else
                {
                    _vehicles = new List<VehicleInfo>();
                }
            }
            catch
            {
                _vehicles = new List<VehicleInfo>();
            }
            _loaded = true;
        }

        public static void SaveVehicles()
        {
            Directory.CreateDirectory(AppDir);
            var json = JsonSerializer.Serialize(_vehicles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DbPath, json);
        }

        public static string? GetVehicleType(string? placa, string? renavam)
        {
            if (string.IsNullOrWhiteSpace(placa) || string.IsNullOrWhiteSpace(renavam)) return null;
            if (!_loaded) LoadVehicles();

            return _vehicles.FirstOrDefault(v => 
                v.Placa?.Equals(placa, StringComparison.OrdinalIgnoreCase) == true && 
                v.Renavam?.Equals(renavam, StringComparison.OrdinalIgnoreCase) == true)?.Tipo;
        }
    }
}
