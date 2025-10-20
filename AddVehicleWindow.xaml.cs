using System.Windows;
using CsvIntegratorApp.Models;

namespace CsvIntegratorApp
{
    public partial class AddVehicleWindow : Window
    {
        public VehicleInfo? NewVehicle { get; private set; }

        public AddVehicleWindow()
        {
            InitializeComponent();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PlacaTextBox.Text))
            {
                MessageBox.Show("O campo 'Placa' é obrigatório.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            NewVehicle = new VehicleInfo
            {
                Placa = PlacaTextBox.Text,
                Renavam = RenavamTextBox.Text,
                Modelo = ModeloTextBox.Text,
                Tipo = TipoTextBox.Text
            };

            this.DialogResult = true;
            this.Close();
        }
    }
}
