using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CsvIntegratorApp.Models.OpenRouteService
{
    public class DirectionsRequest
    {
        [JsonPropertyName("coordinates")]
        public List<double[]>? Coordinates { get; set; }
    }
}
