using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CsvIntegratorApp.Models.OpenRouteService
{
    public class DirectionsResponse
    {
        [JsonPropertyName("features")]
        public List<RouteFeature> Features { get; set; }
    }

    public class RouteFeature
    {
        [JsonPropertyName("properties")]
        public RouteProperties Properties { get; set; }

        [JsonPropertyName("geometry")]
        public RouteGeometry Geometry { get; set; }
    }

    public class RouteGeometry
    {
        [JsonPropertyName("coordinates")]
        public List<List<double>> Coordinates { get; set; }
    }

    public class RouteProperties
    {
        [JsonPropertyName("segments")]
        public List<RouteSegment> Segments { get; set; }

        [JsonPropertyName("summary")]
        public RouteSummary Summary { get; set; }
    }

    public class RouteSegment
    {
        [JsonPropertyName("steps")]
        public List<RouteStep> Steps { get; set; }
    }

    public class RouteStep
    {
        [JsonPropertyName("distance")]
        public double Distance { get; set; }
    }

    public class RouteSummary
    {
        [JsonPropertyName("distance")]
        public double Distance { get; set; } // in meters
    }
}
