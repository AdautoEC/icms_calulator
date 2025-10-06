using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvIntegratorApp.Models.OpenRouteService;
using CsvIntegratorApp.Services.ApiClients;

namespace CsvIntegratorApp.Services
{
    public sealed class RouteResult
    {
        public double? TotalKm { get; init; }
        public List<double> LegsKm { get; init; } = new();
        public string? Used { get; init; }
        public string? Error { get; init; }
        public List<(double lat, double lon)> Coordinates { get; init; } = new();
    }

    public struct GeoPoint
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    public static class DistanceService
    {
        private static OpenRouteServiceClient? _orsClient;
        private static string? _apiKeyCache;

        private static string? GetApiKey()
        {
            if (_apiKeyCache != null) return _apiKeyCache;
            try
            {
                var keyPath = "C:\\Users\\User\\Documents\\icms\\ors_api_key.txt";
                if (File.Exists(keyPath))
                {
                    var key = File.ReadAllText(keyPath).Trim();
                    if (!string.IsNullOrWhiteSpace(key) && key.Length > 40)
                    {
                        _apiKeyCache = key;
                        return _apiKeyCache;
                    }
                }
                CalculationLogService.Log("AVISO: Arquivo 'ors_api_key.txt' não encontrado ou com chave inválida.");
                _apiKeyCache = "";
                return null;
            }
            catch (Exception ex)
            {
                CalculationLogService.Log($"ERRO: Falha ao ler a chave da API. {ex.Message}");
                return null;
            }
        }

        private static OpenRouteServiceClient? GetClient()
        {
            if (_orsClient != null) return _orsClient;

            var apiKey = GetApiKey();
            if (!string.IsNullOrEmpty(apiKey))
            {
                _orsClient = new OpenRouteServiceClient(apiKey);
                return _orsClient;
            }
            return null;
        }

        public static async Task<RouteResult> TryRouteLegsKmAsync(IEnumerable<string> pontos, bool closeLoop)
        {
            var client = GetClient();
            if (client == null)
            {
                return new RouteResult { Error = "Chave da API não configurada. Adicione a chave ao arquivo 'ors_api_key.txt'." };
            }

            var list = pontos.Where(s => !string.IsNullOrWhiteSpace(s)).Select(NormalizePlace).ToList();
            if (list.Count < 2) return new RouteResult { Error = "Pontos insuficientes" };

            if (closeLoop && list.Count >= 2 && !string.Equals(list.First(), list.Last(), StringComparison.OrdinalIgnoreCase))
                list.Add(list.First());

            var coords = new List<GeoPoint>();
            var warnings = new List<string>();
            foreach (var p in list)
            {
                var c = await client.GeocodeAsync(p);
                if (c is null || (c.Value.Lat == 0 && c.Value.Lon == 0))
                {
                    warnings.Add($"Falha ao geocodificar o ponto '{p}'. Ponto ignorado.");
                }
                else
                {
                    coords.Add(c.Value);
                }
            }

            if (coords.Count < 2) return new RouteResult { Error = "Pontos geocodificados insuficientes.", Used = "OpenRouteService" };

            const int chunkSize = 69;
            if (coords.Count <= chunkSize + 1)
            {
                return await MakeSingleRouteRequestAsync(client, coords, warnings);
            }
            else
            {
                return await MakeChunkedRouteRequestAsync(client, coords, warnings);
            }
        }

        private static async Task<RouteResult> MakeChunkedRouteRequestAsync(OpenRouteServiceClient client, List<GeoPoint> allCoords, List<string> initialWarnings)
        {
            CalculationLogService.Log($"INFO: Rota com {allCoords.Count} pontos excede o limite. A requisição será dividida.");
            
            double totalKm = 0;
            var allWarnings = new List<string>(initialWarnings);

            const int chunkSize = 69;
            for (int i = 0; i < allCoords.Count - 1; i += chunkSize)
            {
                var chunkEndIndex = Math.Min(i + chunkSize, allCoords.Count - 1);
                var chunkCoords = allCoords.GetRange(i, chunkEndIndex - i + 1);
                
                var chunkResult = await MakeSingleRouteRequestAsync(client, chunkCoords, new List<string>());

                if (chunkResult.TotalKm.HasValue)
                {
                    totalKm += chunkResult.TotalKm.Value;
                    if (!string.IsNullOrEmpty(chunkResult.Error)) allWarnings.Add(chunkResult.Error);
                }
                else
                {
                    var errorMsg = $"Falha no trecho da rota a partir do ponto {i}. Usando linha reta. Erro: {chunkResult.Error}";
                    allWarnings.Add(errorMsg);
                    for (int j = 0; j < chunkCoords.Count - 1; j++) { totalKm += HaversineKm(chunkCoords[j].Lat, chunkCoords[j].Lon, chunkCoords[j+1].Lat, chunkCoords[j+1].Lon); }
                }
            }

            return new RouteResult
            {
                TotalKm = Math.Round(totalKm, 1),
                Used = "OpenRouteService (chunked)",
                Error = allWarnings.Any() ? string.Join("; ", allWarnings) : null,
                Coordinates = allCoords.Select(p => (p.Lat, p.Lon)).ToList()
            };
        }

        private static async Task<RouteResult> MakeSingleRouteRequestAsync(OpenRouteServiceClient client, List<GeoPoint> coords, List<string> warnings)
        {
            var requestModel = new DirectionsRequest { Coordinates = coords.Select(c => new[] { c.Lon, c.Lat }).ToList() };
            var responseModel = await client.GetDirectionsAsync(requestModel);

            if (responseModel?.Features?.FirstOrDefault()?.Properties?.Summary is RouteSummary summary)
            {
                return new RouteResult
                {
                    TotalKm = Math.Round(summary.Distance / 1000.0, 1),
                    Used = "OpenRouteService",
                    Error = warnings.Any() ? string.Join("; ", warnings) : null,
                    Coordinates = coords.Select(p => (p.Lat, p.Lon)).ToList()
                };
            }
            
            return new RouteResult { TotalKm = null, Error = "Não foi possível obter a rota da API." };
        }

        // ===== Helpers =====
        static string NormalizePlace(string p) => p.Contains("Brasil", StringComparison.OrdinalIgnoreCase) ? p.Trim() : (p + ", Brasil").Trim();
        public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            lat1 *= Math.PI / 180.0;
            lat2 *= Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return Math.Round(R * c, 1);
        }
    }
}