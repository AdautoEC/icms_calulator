// VehicleEditorWindow.xaml.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CsvIntegratorApp.Models;
using CsvIntegratorApp.Services;

namespace CsvIntegratorApp
{
    public partial class VehicleEditorWindow : Window
    {
        private readonly ObservableCollection<VehicleInfo> _vehicles;

        public VehicleEditorWindow()
        {
            InitializeComponent();
            // Carrega os veículos e cria uma coleção observável para a UI atualizar automaticamente
            _vehicles = new ObservableCollection<VehicleInfo>(VehicleService.GetVehicles());
            VehicleGrid.ItemsSource = _vehicles;
        }

        private void RemoveRow_Click(object sender, RoutedEventArgs e)
        {
            if (VehicleGrid.SelectedItem is VehicleInfo selectedVehicle)
            {
                _vehicles.Remove(selectedVehicle);
            }
            else
            {
                MessageBox.Show("Por favor, selecione um veículo para remover.", "Nenhum Veículo Selecionado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            // Atualiza a lista de serviço com os dados da UI e salva
            var vehicleList = _vehicles.ToList();
            VehicleService.GetVehicles().Clear();
            vehicleList.ForEach(v => VehicleService.GetVehicles().Add(v));
            VehicleService.SaveVehicles();
            
            MessageBox.Show("Veículos salvos com sucesso!", "Salvo", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }
    }
}
