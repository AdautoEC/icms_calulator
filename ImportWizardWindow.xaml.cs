using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using CsvIntegratorApp.Models;
using CsvIntegratorApp.Services;
using CsvIntegratorApp.Services.Sped;

namespace CsvIntegratorApp
{
    public partial class ImportWizardWindow : Window
    {
        private List<ModelRow> _currentRows = new();

        public ImportWizardWindow()
        {
            InitializeComponent();
        }

        private void PickNfe_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "NFe XML (*.xml)|*.xml" };
            if (dlg.ShowDialog() == true) NfePath.Text = dlg.FileName;
        }

        private void PickMdfe_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "MDFe XML (*.xml)|*.xml" };
            if (dlg.ShowDialog() == true) MdfePath.Text = dlg.FileName;
        }

        private void PickTxt_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "TXT (*.txt)|*.txt" };
            if (dlg.ShowDialog() == true) TxtPath.Text = dlg.FileName;
        }

        private async void PreFill_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!System.IO.File.Exists(MdfePath.Text))
                {
                    MessageBox.Show(this, "Selecione um arquivo MDF-e.", "Aviso");
                    return;
                }

                StatusText.Text = "Processando... aguarde.";
                ViewRouteButton.IsEnabled = false;

                if (System.IO.File.Exists(TxtPath.Text))
                {
                    SpedTxtLookupService.LoadTxt(TxtPath.Text);
                }

                var mdfe = ParserMDFe.Parse(MdfePath.Text);

                List<NfeParsedItem>? nfeItems = null;
                if (System.IO.File.Exists(NfePath.Text))
                {
                    var parsed = ParserNFe.Parse(NfePath.Text);
                    nfeItems = parsed;
                }

                var merged = await MergeService.MergeAsync(nfeItems, mdfe, true);

                _currentRows = merged;
                PreviewGrid.ItemsSource = null;
                PreviewGrid.ItemsSource = _currentRows;

                StatusText.Text = $"Pré-preenchido: {_currentRows.Count} linha(s).";

                if (!string.IsNullOrEmpty(RouteLogService.LastGeneratedMapPath))
                {
                    ViewRouteButton.IsEnabled = true;
                }
                ViewLogButton.IsEnabled = true; // Sempre habilita o log
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                CalculationLogService.Log($"ERRO INESPERADO: {ex.Message}");
                CalculationLogService.Save();
                ViewLogButton.IsEnabled = true; // Habilita o log mesmo em caso de erro
            }
        }

        private void OpenEditor_Click(object sender, RoutedEventArgs e)
        {
            var editor = new ModelEditorWindow(_currentRows);
            editor.Owner = this;
            editor.ShowDialog();
        }

        private void OpenVehicleEditor_Click(object sender, RoutedEventArgs e)
        {
            var vehicleEditor = new VehicleEditorWindow();
            vehicleEditor.Owner = this;
            vehicleEditor.ShowDialog();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentRows == null || _currentRows.Count == 0)
                {
                    MessageBox.Show(this, "Nada para exportar. Faça o pré-preenchimento primeiro.", "Aviso");
                    return;
                }

                var sfd = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "consolidado.csv" };
                if (sfd.ShowDialog() == true)
                {
                    const string SEP = ";";
                    var ci = System.Globalization.CultureInfo.InvariantCulture;

                    string Esc(string? t) => t is null ? "" : "\"" + t.Replace("\"", "\"\"") + "\"";
                    string Dat(DateTime? d) => d.HasValue ? d.Value.ToString("s", ci) : "";
                    string Num(double? v) => v.HasValue ? v.Value.ToString(ci) : "";

                    using var sw = new StreamWriter(sfd.FileName, false, Encoding.UTF8);

                    sw.WriteLine(string.Join(SEP, new[]
                    {
                "Modelo","Tipo","Renavam","Placa",
                "MDF-e","Data","Roteiro","Distância Percorrida (KM)",
                "N° NF-e","Data de Emissão","Quantidade (LT)","Espécie do Combustivel",
                "Valor unitário","Valor Total do Combustivel","Valor do Crédito a ser utilizado (17%)",
                "N° NF-e (Aquisição)","Data de Aquisição"
            }));

                    foreach (var r in _currentRows)
                    {
                        var campos = new[]
                        {
                    Esc(r.Modelo),
                    Esc(r.Tipo),
                    Esc(r.Renavam),
                    Esc(r.Placa),

                    Esc(r.MdfeNumero),
                    Esc(Dat(r.Data)),
                    Esc(r.Roteiro),
                    Num(r.DistanciaPercorridaKm),

                    Esc(r.NFeNumero),
                    Esc(Dat(r.DataEmissao)),
                    Num(r.QuantidadeLitros),
                    Esc(r.EspecieCombustivel),

                    Num(r.ValorUnitario),
                    Num(r.ValorTotalCombustivel),
                    Num(r.ValorCredito),

                    Esc(r.NFeAquisicaoNumero),
                    Esc(Dat(r.DataAquisicao))
                };
                        sw.WriteLine(string.Join(SEP, campos));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ViewLog_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(CalculationLogService.LogFilePath))
            {
                MessageBox.Show("O arquivo de log não foi encontrado.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(CalculationLogService.LogFilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir o arquivo de log: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ViewRoute_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(RouteLogService.LastGeneratedMapPath) || !File.Exists(RouteLogService.LastGeneratedMapPath))
            {
                MessageBox.Show("O arquivo do mapa de rota não foi encontrado ou não pôde ser gerado.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Abre o arquivo HTML no navegador padrão
                Process.Start(new ProcessStartInfo(RouteLogService.LastGeneratedMapPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir o mapa: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
