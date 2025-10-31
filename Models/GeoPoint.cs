namespace CsvIntegratorApp.Models
{
    /// <summary>
    /// Represents a geographical point with latitude and longitude coordinates.
    /// </summary>
    public struct GeoPoint
    {
        /// <summary>
        /// Gets or sets the latitude of the geographical point.
        /// </summary>
        public double Lat { get; set; }
        /// <summary>
        /// Gets or sets the longitude of the geographical point.
        /// </summary>
        public double Lon { get; set; }
    }
}
