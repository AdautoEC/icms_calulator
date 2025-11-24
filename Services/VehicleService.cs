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

        public static VehicleInfo? GetVehicleInfo(string? placa, string? renavam)
        {
            if (string.IsNullOrWhiteSpace(placa)) return null; // Placa is always required

            if (!_loaded) LoadVehicles();

            // Try to match by both Placa and Renavam if Renavam is provided
            if (!string.IsNullOrWhiteSpace(renavam))
            {
                var vehicle = _vehicles.FirstOrDefault(v =>
                    v.Placa?.Equals(placa, StringComparison.OrdinalIgnoreCase) == true &&
                    v.Renavam?.Equals(renavam, StringComparison.OrdinalIgnoreCase) == true);
                if (vehicle != null) return vehicle;
            }

            // If Renavam is not provided, or no match was found with Renavam, try to match by Placa alone
            var foundVehicle = _vehicles.FirstOrDefault(v =>
                v.Placa?.Equals(placa, StringComparison.OrdinalIgnoreCase) == true);

            if (foundVehicle == null)
            {
                CalculationLogService.Log($"Veículo com Placa '{placa}' e Renavam '{renavam}' não encontrado em vehicles.json.");
            }

            return foundVehicle;
        }
    }
}
