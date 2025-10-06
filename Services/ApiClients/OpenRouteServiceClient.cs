using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CsvIntegratorApp.Models.OpenRouteService;
using CsvIntegratorApp.Models;

namespace CsvIntegratorApp.Services.ApiClients
{
    public class OpenRouteServiceClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        // --- Cache de geocodificação ---
        private static readonly string AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CsvIntegratorApp");
        private static readonly string GeoCachePath = Path.Combine(AppDir, "geocache.json");
        private static bool _geoCacheLoaded = false;
        private static readonly Dictionary<string, GeoPoint> _geoCache = new(StringComparer.OrdinalIgnoreCase);

        public OpenRouteServiceClient(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) 
            {
                throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
            }

            _apiKey = apiKey;
            _httpClient = new HttpClient { BaseAddress = new Uri("https://api.openrouteservice.org") };
            _httpClient.Timeout = TimeSpan.FromSeconds(90);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, application/geo+json, application/gpx+xml, img/png; charset=utf-8");
            
            LoadGeoCache();
        }

        private static void LoadGeoCache()
        {
            if (_geoCacheLoaded) return;
            try
            {
                if (File.Exists(GeoCachePath))
                {
                    var json = File.ReadAllText(GeoCachePath);
                    var loadedCache = JsonSerializer.Deserialize<Dictionary<string, GeoPoint>>(json);
                    if (loadedCache != null)
                    {
                        _geoCache.Clear();
                        foreach(var item in loadedCache) { _geoCache.Add(item.Key, item.Value); }
                        CalculationLogService.Log($"INFO: Cache de geolocalização carregado com {_geoCache.Count} entradas.");
                    }
                }
            }
            catch (Exception ex) { CalculationLogService.Log($"AVISO: Não foi possível carregar o cache de geolocalização. {ex.Message}"); }
            _geoCacheLoaded = true;
        }

        private static void SaveGeoCache()
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_geoCache, options);
                File.WriteAllText(GeoCachePath, json);
            }
            catch (Exception ex) { CalculationLogService.Log($"AVISO: Não foi possível salvar o cache de geolocalização. {ex.Message}"); }
        }

        public async Task<GeoPoint?> GeocodeAsync(string place)
        {
            if (_geoCache.TryGetValue(place, out var cached) && (cached.Lat != 0 || cached.Lon != 0))
            {
                return cached;
            }

            try
            {
                var url = $"/geocode/search?api_key={_apiKey}&text={Uri.EscapeDataString(place)}";
                var response = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                
                if (doc.RootElement.TryGetProperty("features", out var features) && features.GetArrayLength() > 0)
                {
                    if (features[0].TryGetProperty("geometry", out var geometry) && geometry.TryGetProperty("coordinates", out var coords))
                    {
                        var point = new GeoPoint { Lon = coords[0].GetDouble(), Lat = coords[1].GetDouble() };
                        _geoCache[place] = point;
                        SaveGeoCache();
                        return point;
                    }
                }
            }
            catch (Exception ex) { CalculationLogService.Log($"ERRO: Exceção ao geocodificar '{place}'. {ex.Message}"); }
            return null;
        }

        public async Task<DirectionsResponse?> GetDirectionsAsync(DirectionsRequest request)
        {
            try
            {
                var jsonRequest = JsonSerializer.Serialize(request);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/v2/directions/driving-car/geojson", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    CalculationLogService.Log($"ERRO API (ORS): Código {response.StatusCode}. Resposta: {errorContent}");
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<DirectionsResponse>(jsonResponse);
            }
            catch (Exception ex)
            {
                CalculationLogService.Log($"ERRO: Exceção ao chamar a API de direções do ORS. {ex.Message}");
                return null;
            }
        }
    }
}