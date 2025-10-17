using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvIntegratorApp.Models;
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
        public List<List<double>> Polyline { get; init; } = new();
        public List<WaypointInfo> Waypoints { get; init; } = new();
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

        public static async Task<RouteResult> TryRouteLegsKmAsync(List<WaypointInfo> waypoints, bool closeLoop)
        {
            var client = GetClient();
            if (client == null)
            {
                return new RouteResult { Error = "Chave da API não configurada. Adicione a chave ao arquivo 'ors_api_key.txt'." };
            }

            if (waypoints.Count < 2) return new RouteResult { Error = "Pontos insuficientes" };

            var warnings = new List<string>();
            foreach (var waypoint in waypoints)
            {
                var c = await client.GeocodeAsync(waypoint.Address);
                if (c is null || (c.Value.Lat == 0 && c.Value.Lon == 0))
                {
                    warnings.Add($"Falha ao geocodificar o ponto '{waypoint.Address}'. Ponto ignorado.");
                }
                else
                {
                    waypoint.Coordinates = c.Value;
                }
            }

            var validWaypoints = waypoints.Where(w => w.Coordinates.Lat != 0 || w.Coordinates.Lon != 0).ToList();

            if (validWaypoints.Count < 2) return new RouteResult { Error = "Pontos geocodificados insuficientes.", Waypoints = validWaypoints };

            if (closeLoop && validWaypoints.Count >= 2)
            {
                var first = validWaypoints.First();
                var last = validWaypoints.Last();
                if (first.Coordinates.Lat != last.Coordinates.Lat || first.Coordinates.Lon != last.Coordinates.Lon)
                {
                    validWaypoints.Add(first);
                }
            }

            const int chunkSize = 69;
            if (validWaypoints.Count <= chunkSize + 1)
            {
                return await MakeSingleRouteRequestAsync(client, validWaypoints, warnings);
            }
            else
            {
                return await MakeChunkedRouteRequestAsync(client, validWaypoints, warnings);
            }
        }

        private static async Task<RouteResult> MakeChunkedRouteRequestAsync(OpenRouteServiceClient client, List<WaypointInfo> allWaypoints, List<string> initialWarnings)
        {
            CalculationLogService.Log($"INFO: Rota com {allWaypoints.Count} pontos excede o limite. A requisição será dividida.");
            
            double totalKm = 0;
            var allWarnings = new List<string>(initialWarnings);
            var fullPolyline = new List<List<double>>();
            var allLegs = new List<double>();

            const int chunkSize = 69;
            for (int i = 0; i < allWaypoints.Count - 1; i += chunkSize)
            {
                var chunkEndIndex = Math.Min(i + chunkSize, allWaypoints.Count - 1);
                var chunkWaypoints = allWaypoints.GetRange(i, chunkEndIndex - i + 1);
                
                var chunkResult = await MakeSingleRouteRequestAsync(client, chunkWaypoints, new List<string>());

                if (chunkResult.TotalKm.HasValue)
                {
                    totalKm += chunkResult.TotalKm.Value;
                    allLegs.AddRange(chunkResult.LegsKm);
                    if (chunkResult.Polyline.Any())
                    {
                        fullPolyline.AddRange(fullPolyline.Any() ? chunkResult.Polyline.Skip(1) : chunkResult.Polyline);
                    }
                    if (!string.IsNullOrEmpty(chunkResult.Error)) allWarnings.Add(chunkResult.Error);
                }
                else
                {
                    var errorMsg = $"Falha no trecho da rota a partir do ponto {i}. Usando linha reta. Erro: {chunkResult.Error}";
                    allWarnings.Add(errorMsg);
                    for (int j = 0; j < chunkWaypoints.Count - 1; j++) 
                    { 
                        var legKm = HaversineKm(chunkWaypoints[j].Coordinates.Lat, chunkWaypoints[j].Coordinates.Lon, chunkWaypoints[j+1].Coordinates.Lat, chunkWaypoints[j+1].Coordinates.Lon);
                        totalKm += legKm;
                        allLegs.Add(legKm);
                        if (j == 0 && !fullPolyline.Any()) fullPolyline.Add(new List<double> { chunkWaypoints[j].Coordinates.Lat, chunkWaypoints[j].Coordinates.Lon });
                        fullPolyline.Add(new List<double> { chunkWaypoints[j+1].Coordinates.Lat, chunkWaypoints[j+1].Coordinates.Lon });
                    }
                }
            }

            return new RouteResult
            {
                TotalKm = Math.Round(totalKm, 1),
                LegsKm = allLegs,
                Used = "OpenRouteService (chunked)",
                Error = allWarnings.Any() ? string.Join("; ", allWarnings) : null,
                Polyline = fullPolyline,
                Waypoints = allWaypoints
            };
        }

        private static async Task<RouteResult> MakeSingleRouteRequestAsync(OpenRouteServiceClient client, List<WaypointInfo> waypoints, List<string> warnings)
        {
            var coords = waypoints.Select(w => w.Coordinates).ToList();
            var requestModel = new DirectionsRequest { Coordinates = coords.Select(c => new[] { c.Lon, c.Lat }).ToList() };
            var responseModel = await client.GetDirectionsAsync(requestModel);

            var feature = responseModel?.Features?.FirstOrDefault();
            if (feature?.Properties?.Summary is RouteSummary summary)
            {
                var polyline = new List<List<double>>();
                if (feature.Geometry?.Coordinates != null)
                {
                    polyline = feature.Geometry.Coordinates.Select(p => new List<double> { p[1], p[0] }).ToList();
                }

                var legs = new List<double>();
                if (feature.Properties?.Segments != null)
                {
                    foreach (var segment in feature.Properties.Segments)
                    {
                        double segmentDistance = 0;
                        if (segment.Steps != null)
                        {
                            foreach (var step in segment.Steps)
                            {
                                segmentDistance += step.Distance;
                            }
                        }
                        legs.Add(Math.Round(segmentDistance / 1000.0, 1));
                    }
                }

                return new RouteResult
                {
                    TotalKm = Math.Round(summary.Distance / 1000.0, 1),
                    LegsKm = legs,
                    Used = "OpenRouteService",
                    Error = warnings.Any() ? string.Join("; ", warnings) : null,
                    Polyline = polyline,
                    Waypoints = waypoints
                };
            }
            
            double totalKm = 0;
            var fallbackPolyline = new List<List<double>>();
            var fallbackLegs = new List<double>();
            for(int i = 0; i < coords.Count - 1; i++)
            {
                var legKm = HaversineKm(coords[i].Lat, coords[i].Lon, coords[i+1].Lat, coords[i+1].Lon);
                totalKm += legKm;
                fallbackLegs.Add(legKm);
                if (i == 0) fallbackPolyline.Add(new List<double> { coords[i].Lat, coords[i].Lon });
                fallbackPolyline.Add(new List<double> { coords[i+1].Lat, coords[i+1].Lon });
            }

            return new RouteResult 
            {
                TotalKm = totalKm,
                LegsKm = fallbackLegs,
                Used = "Haversine (fallback)",
                Error = "Não foi possível obter a rota da API. Usando cálculo de linha reta.",
                Polyline = fallbackPolyline,
                Waypoints = waypoints
            };
        }

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