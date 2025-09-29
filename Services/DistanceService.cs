// Services/DistanceService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CsvIntegratorApp.Services
{
    public sealed class RouteResult
    {
        public double? TotalKm { get; init; }
        public List<double> LegsKm { get; init; } = new();
        public string? Used { get; init; }         // "OSRM" ou "HAVERSINE"
        public string? Error { get; init; }
    }

    public static class DistanceService
    {
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // cache bobo de geocodificação para não estourar Nominatim
        static readonly Dictionary<string, (double lat, double lon)> _geoCache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Rota entre dois pontos "Cidade, UF" (com geocodificação) usando OSRM; fallback Haversine.
        /// </summary>
        public static async Task<double?> TryRouteKmAsync(string origemCidadeUf, string destinoCidadeUf)
        {
            var res = await TryRouteLegsKmAsync(new[] { origemCidadeUf, destinoCidadeUf }, closeLoop: false);
            return res.TotalKm;
        }

        /// <summary>
        /// Rota com múltiplos pontos "Cidade, UF" (ordem dada). Soma as pernas (legs) retornadas pela API.
        /// Se closeLoop=true, adiciona o primeiro ponto no fim para fechar o ciclo.
        /// </summary>
        public static async Task<RouteResult> TryRouteLegsKmAsync(IEnumerable<string> pontos, bool closeLoop)
        {
            var list = pontos.Where(s => !string.IsNullOrWhiteSpace(s)).Select(NormalizePlace).ToList();
            if (list.Count < 2) return new RouteResult { TotalKm = null, Used = "OSRM", Error = "Pontos insuficientes" };

            if (closeLoop && list.Count >= 2 && !string.Equals(list.First(), list.Last(), StringComparison.OrdinalIgnoreCase))
                list.Add(list.First());

            // Geocodifica todos
            var coords = new List<(double lat, double lon)>();
            foreach (var p in list)
            {
                var c = await GeocodeAsync(p);
                if (c is null)
                    return await FallbackHaversineAsync(list); // se qualquer ponto falhar, cai todo para Haversine
                coords.Add(c.Value);
            }

            // Monta URL OSRM: lon,lat;lon,lat;...
            var ci = CultureInfo.InvariantCulture;
            var coordStr = string.Join(";",
                coords.Select(c => $"{c.lon.ToString(ci)},{c.lat.ToString(ci)}"));

            var url = $"https://router.project-osrm.org/route/v1/driving/{coordStr}?overview=false&steps=false&annotations=false";
            try
            {
                var json = await http.GetStringAsync(url);

                // Tenta pegar "code":"Ok"
                if (!Regex.IsMatch(json, @"""code""\s*:\s*""Ok""", RegexOptions.IgnoreCase))
                    return await FallbackHaversineAsync(list);

                // Parseia legs[].distance (m)
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var routes = root.GetProperty("routes");
                if (routes.GetArrayLength() == 0)
                    return await FallbackHaversineAsync(list);

                var r0 = routes[0];

                var legsKm = new List<double>();
                if (r0.TryGetProperty("legs", out var legsElem) && legsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var leg in legsElem.EnumerateArray())
                    {
                        if (leg.TryGetProperty("distance", out var d) && d.TryGetDouble(out var meters))
                            legsKm.Add(Math.Round(meters / 1000.0, 1));
                    }
                }

                double totalKm;
                if (legsKm.Count > 0)
                {
                    totalKm = Math.Round(legsKm.Sum(), 1);
                }
                else
                {
                    // fallback para a distância total do route caso legs não venham
                    if (r0.TryGetProperty("distance", out var dTot) && dTot.TryGetDouble(out var mt))
                        totalKm = Math.Round(mt / 1000.0, 1);
                    else
                        return await FallbackHaversineAsync(list);
                }

                return new RouteResult
                {
                    TotalKm = totalKm,
                    LegsKm = legsKm,
                    Used = "OSRM",
                    Error = null
                };
            }
            catch
            {
                return await FallbackHaversineAsync(list);
            }
        }

        // ===== Helpers =====

        static string NormalizePlace(string p)
            => p.Contains("Brasil", StringComparison.OrdinalIgnoreCase) ? p.Trim() : (p + ", Brasil").Trim();

        static async Task<(double lat, double lon)?> GeocodeAsync(string place)
        {
            if (_geoCache.TryGetValue(place, out var cached)) return cached;

            try
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("CsvIntegratorApp/1.0 (Nominatim polite usage)");
                var url = $"https://nominatim.openstreetmap.org/search?format=json&q={Uri.EscapeDataString(place)}";
                var json = await http.GetStringAsync(url);

                var mlat = Regex.Match(json, @"""lat"":\s*""([^""]+)""");
                var mlon = Regex.Match(json, @"""lon"":\s*""([^""]+)""");
                var ci = CultureInfo.InvariantCulture;

                if (mlat.Success && mlon.Success
                    && double.TryParse(mlat.Groups[1].Value, NumberStyles.Any, ci, out var lat)
                    && double.TryParse(mlon.Groups[1].Value, NumberStyles.Any, ci, out var lon))
                {
                    _geoCache[place] = (lat, lon);
                    return (lat, lon);
                }
            }
            catch { }
            return null;
        }

        static async Task<RouteResult> FallbackHaversineAsync(List<string> list)
        {
            // geocodifica tudo; se ainda assim falhar, devolve nulo
            var coords = new List<(double lat, double lon)>();
            foreach (var p in list)
            {
                var c = await GeocodeAsync(p);
                if (c is null) return new RouteResult { TotalKm = null, Used = "HAVERSINE", Error = "Falha ao geocodificar" };
                coords.Add(c.Value);
            }

            var legs = new List<double>();
            for (int i = 1; i < coords.Count; i++)
            {
                legs.Add(HaversineKm(coords[i - 1].lat, coords[i - 1].lon, coords[i].lat, coords[i].lon));
            }

            return new RouteResult
            {
                TotalKm = Math.Round(legs.Sum(), 1),
                LegsKm = legs,
                Used = "HAVERSINE",
                Error = "OSRM indisponível; usando linha reta"
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
