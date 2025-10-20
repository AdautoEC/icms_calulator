using System.Collections.Generic;
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
        private readonly List<WaypointInfo> _waypoints;

        public RouteEditorWindow(ModelRow modelRow)
        {
            InitializeComponent();
            _modelRow = modelRow;
            _waypoints = modelRow.Waypoints;
            WaypointsGrid.ItemsSource = _waypoints;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveButton.IsEnabled = false;
            ProgressBar.Visibility = Visibility.Visible;

            var updatedWaypoints = (List<WaypointInfo>)WaypointsGrid.ItemsSource;

            var routeResult = await DistanceService.TryRouteLegsKmAsync(updatedWaypoints, true);

            if (routeResult.TotalKm.HasValue)
            {
                _modelRow.DistanciaPercorridaKm = routeResult.TotalKm;
                _modelRow.Roteiro = string.Join(" -> ", updatedWaypoints.Select(w => w.City));
                _modelRow.MapPath = RouteLogService.GenerateRouteMap(routeResult.Polyline, routeResult.Waypoints, new List<ModelRow> { _modelRow });
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
    }
}
