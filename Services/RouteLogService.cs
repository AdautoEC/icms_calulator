// Services/RouteLogService.cs
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CsvIntegratorApp.Services
{
    public static class RouteLogService
    {
        public static string? LastGeneratedMapPath { get; private set; }

        public static void GenerateRouteMap(List<(double lat, double lon)>? coordinates, string fileName = "rota.html")
        {
            if (coordinates == null || coordinates.Count < 2)
            {
                LastGeneratedMapPath = null;
                return;
            }

            var ci = CultureInfo.InvariantCulture;
            var pointsJs = string.Join(",", coordinates.Select(c => $"[{c.lat.ToString(ci)}, {c.lon.ToString(ci)}]"));

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("    <title>Visualização de Rota</title>");
            html.AppendLine("    <meta charset=\"utf-8\" />");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine("    <link rel=\"stylesheet\" href=\"https://unpkg.com/leaflet@1.7.1/dist/leaflet.css\" />");
            html.AppendLine("    <script src=\"https://unpkg.com/leaflet@1.7.1/dist/leaflet.js\"></script>");
            html.AppendLine("    <style>html, body, #map { height: 100%; margin: 0; padding: 0; }</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("<div id=\"map\"></div>");
            html.AppendLine("<script>");
            html.AppendLine("    var map = L.map('map');");
            html.AppendLine("    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {");
            html.AppendLine("        attribution: '&copy; <a href=\"https://www.openstreetmap.org/copyright\">OpenStreetMap</a> contributors'");
            html.AppendLine("    }).addTo(map);");
            html.AppendLine($"    var points = [{pointsJs}];");
            html.AppendLine("    var polyline = L.polyline(points, {color: 'blue'}).addTo(map);");
            html.AppendLine("    map.fitBounds(polyline.getBounds().pad(0.1));");
            // Marcadores
            html.AppendLine("    L.marker(points[0]).addTo(map).bindPopup('<b>Origem</b>');");
            html.AppendLine("    L.marker(points[points.length - 1]).addTo(map).bindPopup('<b>Destino Final</b>');");
            html.AppendLine("</script>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllText(filePath, html.ToString());
            LastGeneratedMapPath = filePath;
        }
    }
}
