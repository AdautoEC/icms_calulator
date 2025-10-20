using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
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
            _vehicles = new ObservableCollection<VehicleInfo>(VehicleService.GetVehicles());
            VehicleGrid.ItemsSource = _vehicles;
        }

        private void AddVehicle_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = new AddVehicleWindow { Owner = this };
            if (addWindow.ShowDialog() == true)
            {
                if (addWindow.NewVehicle != null)
                {
                    _vehicles.Add(addWindow.NewVehicle);
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            UpdateVehicleServiceAndSave();
            MessageBox.Show("Frota de veículos salva com sucesso!", "Salvo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ImportJson_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dlg.FileName);
                    var importedVehicles = JsonSerializer.Deserialize<ObservableCollection<VehicleInfo>>(json);

                    if (importedVehicles != null)
                    {
                        int importCount = 0;
                        foreach (var vehicle in importedVehicles)
                        {
                            _vehicles.Add(vehicle);
                            importCount++;
                        }

                        UpdateVehicleServiceAndSave();

                        MessageBox.Show($"{importCount} veículos importados e adicionados à frota com sucesso!", "Importação Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Falha ao importar o arquivo JSON: {ex.Message}", "Erro de Importação", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportJson_Click(object sender, RoutedEventArgs e)
        {
            if (_vehicles.Count == 0)
            {
                MessageBox.Show("Não há veículos para exportar.", "Nenhum Veículo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                FileName = "frota.json",
                Filter = "JSON Files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(_vehicles, options);
                    File.WriteAllText(dlg.FileName, json);

                    MessageBox.Show("Arquivo JSON da frota exportado com sucesso!", "Exportação Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Falha ao exportar o arquivo JSON: {ex.Message}", "Erro de Exportação", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void UpdateVehicleServiceAndSave()
        {
            var vehicleList = _vehicles.ToList();
            VehicleService.GetVehicles().Clear();
            foreach (var v in vehicleList)
            {
                VehicleService.GetVehicles().Add(v);
            }
            VehicleService.SaveVehicles();
        }
    }
}