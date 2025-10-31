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
    /// <summary>
    /// Represents the result of a route calculation, including total distance, individual leg distances, 
    /// the service used, any errors, the polyline of the route, and the waypoints.
    /// </summary>
    public sealed class RouteResult
    {
        /// <summary>
        /// Gets the total distance of the route in kilometers.
        /// </summary>
        public double? TotalKm { get; init; }
        /// <summary>
        /// Gets a list of distances for each leg of the route in kilometers.
        /// </summary>
        public List<double> LegsKm { get; init; } = new();
        /// <summary>
        /// Gets the name of the service used for route calculation (e.g., "OpenRouteService", "Haversine").
        /// </summary>
        public string? Used { get; init; }
        /// <summary>
        /// Gets any error message that occurred during route calculation.
        /// </summary>
        public string? Error { get; init; }
        /// <summary>
        /// Gets the polyline coordinates representing the route.
        /// </summary>
        public List<List<double>> Polyline { get; init; } = new();
        /// <summary>
        /// Gets the list of waypoints used for the route calculation.
        /// </summary>
        public List<WaypointInfo> Waypoints { get; init; } = new();
    }

    /// <summary>
    /// Provides services for calculating distances and routing using OpenRouteService API or Haversine formula as a fallback.
    /// </summary>
    public static class DistanceService
    {
        private static OpenRouteServiceClient? _orsClient;
        private static string? _apiKeyCache;

        /// <summary>
        /// Retrieves the OpenRouteService API key. 
        /// Currently, the API key is hardcoded, which is a security risk.
        /// </summary>
        /// <returns>The API key as a string.</returns>
        private static string? GetApiKey()
        {
            if (_apiKeyCache != null) return _apiKeyCache;
            // WARNING: API Key is hardcoded. This is a security risk.
            _apiKeyCache = "eyJvcmciOiI1YjNjZTM1OTc4NTExMTAwMDFjZjYyNDgiLCJpZCI6IjA1Y2ZmNTZkOThjNzQ4ZDg5ZGNmNWNmMzhmOTBiNjQzIiwiaCI6Im11cm11cjY0In0=";
            return _apiKeyCache;
        }

        /// <summary>
        /// Gets an instance of the <see cref="OpenRouteServiceClient"/>, initializing it with the API key if necessary.
        /// </summary>
        /// <returns>An <see cref="OpenRouteServiceClient"/> instance, or null if the API key is not configured.</returns>
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

        /// <summary>
        /// Attempts to calculate the route distance in kilometers between a list of waypoints.
        /// It uses the OpenRouteService API, with chunking for long routes, and falls back to Haversine calculation if the API fails.
        /// </summary>
        /// <param name="waypoints">A list of <see cref="WaypointInfo"/> representing the points of the route.</param>
        /// <param name="closeLoop">If set to <c>true</c>, the route will attempt to close the loop by adding the first waypoint at the end.</param>
        /// <returns>A <see cref="RouteResult"/> containing the total distance, leg distances, and any errors.</returns>
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
                if (string.IsNullOrWhiteSpace(waypoint.Address))
                {
                    warnings.Add("Endereço de waypoint vazio ignorado.");
                    continue;
                }

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

            var validWaypoints = waypoints.Where(w => w.Coordinates.HasValue && (w.Coordinates.Value.Lat != 0 || w.Coordinates.Value.Lon != 0)).ToList();

            if (validWaypoints.Count < 2) return new RouteResult { Error = "Pontos geocodificados insuficientes.", Waypoints = validWaypoints };

            if (closeLoop && validWaypoints.Count >= 2)
            {
                var first = validWaypoints.First();
                var last = validWaypoints.Last();
                if (first.Coordinates.HasValue && last.Coordinates.HasValue && (first.Coordinates.Value.Lat != last.Coordinates.Value.Lat || first.Coordinates.Value.Lon != last.Coordinates.Value.Lon))
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

        /// <summary>
        /// Makes a chunked route request to the OpenRouteService API for routes with many waypoints.
        /// This method splits long routes into smaller chunks to comply with API limits.
        /// </summary>
        /// <param name="client">The <see cref="OpenRouteServiceClient"/> instance.</param>
        /// <param name="allWaypoints">The complete list of waypoints for the route.</param>
        /// <param name="initialWarnings">A list of initial warnings to be included in the result.</param>
        /// <returns>A <see cref="RouteResult"/> containing the aggregated route information.</returns>
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
                        if (chunkWaypoints[j].Coordinates.HasValue && chunkWaypoints[j+1].Coordinates.HasValue)
                        {
                            var legKm = HaversineKm(chunkWaypoints[j].Coordinates.Value.Lat, chunkWaypoints[j].Coordinates.Value.Lon, chunkWaypoints[j+1].Coordinates.Value.Lat, chunkWaypoints[j+1].Coordinates.Value.Lon);
                            totalKm += legKm;
                            allLegs.Add(legKm);
                            if (j == 0 && !fullPolyline.Any()) fullPolyline.Add(new List<double> { chunkWaypoints[j].Coordinates.Value.Lat, chunkWaypoints[j].Coordinates.Value.Lon });
                            fullPolyline.Add(new List<double> { chunkWaypoints[j+1].Coordinates.Value.Lat, chunkWaypoints[j+1].Coordinates.Value.Lon });
                        }
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

        /// <summary>
        /// Makes a single route request to the OpenRouteService API.
        /// </summary>
        /// <param name="client">The <see cref="OpenRouteServiceClient"/> instance.</param>
        /// <param name="waypoints">The list of waypoints for the route.</param>
        /// <param name="warnings">A list of warnings to be included in the result.</param>
        /// <returns>A <see cref="RouteResult"/> containing the route information from the API or a Haversine fallback.</returns>
        private static async Task<RouteResult> MakeSingleRouteRequestAsync(OpenRouteServiceClient client, List<WaypointInfo> waypoints, List<string> warnings)
        {
            var validCoords = waypoints.Select(w => w.Coordinates).Where(c => c.HasValue).Select(c => c.Value).ToList();
            if (validCoords.Count < 2)
            {
                return new RouteResult 
                {
                    Used = "Haversine (fallback)",
                    Error = "Não há pontos válidos suficientes para traçar uma rota.",
                    Waypoints = waypoints
                };
            }

            var requestModel = new DirectionsRequest { Coordinates = validCoords.Select(c => new[] { c.Lon, c.Lat }).ToList() };
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
            for(int i = 0; i < validCoords.Count - 1; i++)
            {
                var legKm = HaversineKm(validCoords[i].Lat, validCoords[i].Lon, validCoords[i+1].Lat, validCoords[i+1].Lon);
                totalKm += legKm;
                fallbackLegs.Add(legKm);
                if (i == 0) fallbackPolyline.Add(new List<double> { validCoords[i].Lat, validCoords[i].Lon });
                fallbackPolyline.Add(new List<double> { validCoords[i+1].Lat, validCoords[i+1].Lon });
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

        /// <summary>
        /// Calculates the distance between two geographical points using the Haversine formula.
        /// </summary>
        /// <param name="lat1">Latitude of the first point.</param>
        /// <param name="lon1">Longitude of the first point.</param>
        /// <param name="lat2">Latitude of the second point.</param>
        /// <param name="lon2">Longitude of the second point.</param>
        /// <returns>The distance in kilometers, rounded to one decimal place.</returns>
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