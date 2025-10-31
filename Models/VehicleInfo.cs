// Models/VehicleInfo.cs
namespace CsvIntegratorApp.Models
{
    /// <summary>
    /// Represents information about a vehicle.
    /// </summary>
    public class VehicleInfo
    {
        /// <summary>
        /// Gets or sets the license plate of the vehicle.
        /// </summary>
        public string? Placa { get; set; }
        /// <summary>
        /// Gets or sets the Renavam (National Register of Motor Vehicles) code of the vehicle.
        /// </summary>
        public string? Renavam { get; set; }
        /// <summary>
        /// Gets or sets the model of the vehicle.
        /// </summary>
        public string? Modelo { get; set; }
        /// <summary>
        /// Gets or sets the type of the vehicle.
        /// </summary>
        public string? Tipo { get; set; }
    }
}
