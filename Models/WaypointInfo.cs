namespace CsvIntegratorApp.Models
{
    /// <summary>
    /// Represents a waypoint in a route, including its address, associated invoice number, city, and geographical coordinates.
    /// </summary>
    public class WaypointInfo
    {
        /// <summary>
        /// Gets or sets the address of the waypoint.
        /// </summary>
        public string? Address { get; set; }
        /// <summary>
        /// Gets or sets the invoice number associated with this waypoint.
        /// </summary>
        public string? InvoiceNumber { get; set; }
        /// <summary>
        /// Gets or sets the city of the waypoint.
        /// </summary>
        public string? City { get; set; }
        /// <summary>
        /// Gets or sets the geographical coordinates (latitude and longitude) of the waypoint.
        /// </summary>
        public GeoPoint? Coordinates { get; set; }
    }
}
