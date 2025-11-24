namespace CsvIntegratorApp.Models
{
    public class WaypointInfo
    {
        public string? Address { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public GeoPoint? Coordinates { get; set; }
        public bool IsOutsideBrazil { get; set; }
    }
}
