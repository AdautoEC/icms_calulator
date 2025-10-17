using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CsvIntegratorApp.Models;

namespace CsvIntegratorApp.Services
{
    public static class RouteLogService
    {
        public static string? LastGeneratedMapPath { get; private set; }

        public static void GenerateRouteMap(List<List<double>>? polyline, List<WaypointInfo>? waypoints, List<ModelRow> modelRows, string fileName = "rota.html")
        {
            if (polyline == null || polyline.Count < 2)
            {
                LastGeneratedMapPath = null;
                return;
            }

            var ci = CultureInfo.InvariantCulture;
            var pointsJs = string.Join(",", polyline.Select(p => $"[{p[0].ToString(ci)}, {p[1].ToString(ci)}]"));

            var waypointsJs = "[]";
            if (waypoints != null && waypoints.Any())
            {
                var waypointsData = waypoints.Select(w =>
                {
                    var c190Rows = modelRows.Where(r => r.ChaveNFe == w.InvoiceNumber).ToList();
                    var c190Data = c190Rows.Select(r => new {
                        cst = r.Cst,
                        cfop = r.Cfop,
                        valorIcms = r.ValorIcms,
                        baseIcms = r.BaseIcms,
                        totalDocumento = r.TotalDocumento
                    }).ToList();

                    return new
                    {
                        lat = w.Coordinates.Lat,
                        lon = w.Coordinates.Lon,
                        address = w.Address,
                        invoice = w.InvoiceNumber,
                        c190Data = c190Data
                    };
                }).ToList();
                waypointsJs = JsonSerializer.Serialize(waypointsData);
            }

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

            html.AppendLine($"    var waypoints = {waypointsJs};");
            html.AppendLine("    for (var i = 0; i < waypoints.length; i++) {");
            html.AppendLine("        var waypoint = waypoints[i];");
            html.AppendLine("        var marker = L.marker([waypoint.lat, waypoint.lon]).addTo(map);");
            html.AppendLine("        var popupContent = '<b>' + waypoint.invoice + '</b><br>' + waypoint.address;");
            html.AppendLine("        if (waypoint.c190Data && waypoint.c190Data.length > 0) {");
            html.AppendLine("            popupContent += '<hr>';");
            html.AppendLine("            for (var j = 0; j < waypoint.c190Data.length; j++) {");
            html.AppendLine("                var c190 = waypoint.c190Data[j];");
            html.AppendLine("                if (c190.cst) { popupContent += '<br>CST: ' + c190.cst; }");
            html.AppendLine("                if (c190.cfop) { popupContent += '<br>CFOP: ' + c190.cfop; }");
            html.AppendLine("                if (c190.valorIcms) { popupContent += '<br>Valor ICMS: ' + c190.valorIcms; }");
            html.AppendLine("                if (c190.baseIcms) { popupContent += '<br>Base ICMS: ' + c190.baseIcms; }");
            html.AppendLine("                if (c190.totalDocumento) { popupContent += '<br>Total Documento: ' + c190.totalDocumento; }");
            html.AppendLine("                if (j < waypoint.c190Data.length - 1) { popupContent += '<hr>'; }");
            html.AppendLine("            }");
            html.AppendLine("        }");
            html.AppendLine("        marker.bindPopup(popupContent);");
            html.AppendLine("    }");

            html.AppendLine("</script>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            var filePath = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllText(filePath, html.ToString());
            LastGeneratedMapPath = filePath;
        }
    }
}