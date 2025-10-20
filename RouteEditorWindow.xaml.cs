using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CsvIntegratorApp.Models;
using CsvIntegratorApp.Services;

namespace CsvIntegratorApp
{
    public partial class RouteEditorWindow : Window
    {
        private readonly ModelRow _modelRow;
        private readonly ObservableCollection<WaypointInfo> _waypoints;

        public RouteEditorWindow(ModelRow modelRow)
        {
            InitializeComponent();
            _modelRow = modelRow;
            _waypoints = new ObservableCollection<WaypointInfo>(modelRow.Waypoints);
            WaypointsGrid.ItemsSource = _waypoints;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveButton.IsEnabled = false;
            ProgressBar.Visibility = Visibility.Visible;

            var updatedWaypoints = _waypoints.ToList();

            var routeResult = await DistanceService.TryRouteLegsKmAsync(updatedWaypoints, true);

            if (routeResult.TotalKm.HasValue)
            {
                _modelRow.DistanciaPercorridaKm = routeResult.TotalKm;
                _modelRow.Roteiro = string.Join(" -> ", updatedWaypoints.Select(w => w.City));
                _modelRow.MapPath = RouteLogService.GenerateRouteMap(routeResult.Polyline, routeResult.Waypoints, new List<ModelRow> { _modelRow });
                _modelRow.Waypoints = updatedWaypoints; // Atualiza a lista original
            }
            else
            {
                MessageBox.Show(this, $"Falha ao recalcular a rota: {routeResult.Error}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            SaveButton.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;

            DialogResult = true;
            Close();
        }

        private void AddAddressButton_Click(object sender, RoutedEventArgs e)
        {
            _waypoints.Add(new WaypointInfo { Address = "Novo Endereço" });
        }

        private void RemoveAddressButton_Click(object sender, RoutedEventArgs e)
        {
            if (WaypointsGrid.SelectedItem is WaypointInfo selectedWaypoint)
            {
                _waypoints.Remove(selectedWaypoint);
            }
            else
            {
                MessageBox.Show(this, "Selecione um endereço para remover.", "Nenhum Endereço Selecionado", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
