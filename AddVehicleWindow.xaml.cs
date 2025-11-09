using System.Windows;
using CsvIntegratorApp.Models;

namespace CsvIntegratorApp
{
    public partial class AddVehicleWindow : Window
    {
        public VehicleInfo? NewVehicle { get; private set; }
        private VehicleInfo? _originalVehicle;

        public AddVehicleWindow()
        {
            InitializeComponent();
            this.Title = "Adicionar Novo Veículo";
        }

        public AddVehicleWindow(VehicleInfo vehicleToEdit) : this()
        {
            this.Title = "Editar Veículo";
            _originalVehicle = vehicleToEdit;
            PlacaTextBox.Text = vehicleToEdit.Placa;
            RenavamTextBox.Text = vehicleToEdit.Renavam;
            ModeloTextBox.Text = vehicleToEdit.Modelo;
            TipoTextBox.Text = vehicleToEdit.Tipo;
            // Change button content for editing
            var addButton = (this.FindName("AddButton") as System.Windows.Controls.Button);
            if (addButton != null)
            {
                addButton.Content = "Salvar";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PlacaTextBox.Text))
            {
                MessageBox.Show("O campo 'Placa' é obrigatório.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_originalVehicle != null)
            {
                // Update existing vehicle
                _originalVehicle.Placa = PlacaTextBox.Text;
                _originalVehicle.Renavam = RenavamTextBox.Text;
                _originalVehicle.Modelo = ModeloTextBox.Text;
                _originalVehicle.Tipo = TipoTextBox.Text;
                NewVehicle = _originalVehicle; // Set NewVehicle to the updated original vehicle
            }
            else
            {
                // Create new vehicle
                NewVehicle = new VehicleInfo
                {
                    Placa = PlacaTextBox.Text,
                    Renavam = RenavamTextBox.Text,
                    Modelo = ModeloTextBox.Text,
                    Tipo = TipoTextBox.Text
                };
            }

            this.DialogResult = true;
            this.Close();
        }
    }
}
