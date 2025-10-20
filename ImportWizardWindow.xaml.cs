using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CsvIntegratorApp.Models;
using CsvIntegratorApp.Services;
using CsvIntegratorApp.Services.Sped;
using ClosedXML.Excel;

namespace CsvIntegratorApp
{
    // Classe para reportar o progresso da UI
    public class ProgressReport
    {
        public int Percentage { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }

    public partial class ImportWizardWindow : Window
    {
        private List<ModelRow> _currentRows = new();
        private List<NfeParsedItem> _allNfeItems = new();
        private readonly HashSet<string> _processedMdfeKeys = new HashSet<string>();

        public ImportWizardWindow()
        {
            InitializeComponent();
        }

        private void PickNfe_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "NFe XML (*.xml)|*.xml", Multiselect = true };
            if (dlg.ShowDialog() == true) NfePath.Text = string.Join(";", dlg.FileNames);
        }

        private void PickMdfe_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "MDFe XML (*.xml)|*.xml", Multiselect = true };
            if (dlg.ShowDialog() == true) MdfePath.Text = string.Join(";", dlg.FileNames);
        }

        private void PickTxt_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "TXT (*.txt)|*.txt", Multiselect = true };
            if (dlg.ShowDialog() == true) TxtPath.Text = string.Join(";", dlg.FileNames);
        }

        private async void PreFill_Click(object sender, RoutedEventArgs e)
        {
            var mdfeFiles = MdfePath.Text.Split(';').Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).ToList();
            if (!mdfeFiles.Any())
            {
                MessageBox.Show(this, "Selecione pelo menos um arquivo MDF-e.", "Aviso");
                return;
            }

            SetUiProcessingState(true);

            var progress = new Progress<ProgressReport>(report =>
            {
                StatusText.Text = report.StatusMessage;
                LoadingIndicator.Value = report.Percentage;
                ProgressPercentageText.Text = $"{report.Percentage}%";
            });

            try
            {
                var txtFiles = TxtPath.Text.Split(';').Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).ToList();
                var nfeFiles = NfePath.Text.Split(';').Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).ToList();

                var (success, mergedRows) = await Task.Run(() => 
                    ProcessFilesInBackground(txtFiles, nfeFiles, mdfeFiles, progress)
                );

                if (success)
                {
                    _currentRows = mergedRows;
                    PreviewGrid.ItemsSource = null;
                    PreviewGrid.ItemsSource = _currentRows;
                    StatusText.Text = $"Processamento concluído: {_currentRows.Count} linha(s) gerada(s).";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erro Inesperado", MessageBoxButton.OK, MessageBoxImage.Error);
                CalculationLogService.Log($"ERRO FATAL: {ex.Message}");
                CalculationLogService.Save();
            }
            finally
            {
                SetUiProcessingState(false);
                ViewLogButton.IsEnabled = true;
            }
        }

        private (bool success, List<ModelRow> mergedRows) ProcessFilesInBackground(
            List<string> txtFiles, List<string> nfeFiles, List<string> mdfeFiles, IProgress<ProgressReport> progress)
        {
            try
            {
                progress.Report(new ProgressReport { Percentage = 5, StatusMessage = "Iniciando leitura de arquivos SPED..." });
                if (txtFiles.Any()) SpedTxtLookupService.LoadTxt(txtFiles);
                progress.Report(new ProgressReport { Percentage = 20, StatusMessage = "Arquivos SPED carregados." });

                progress.Report(new ProgressReport { Percentage = 25, StatusMessage = "Lendo arquivos NFe de combustível..." });
                if (nfeFiles.Any())
                {
                    _allNfeItems.Clear();
                    foreach (var nfeFile in nfeFiles) _allNfeItems.AddRange(ParserNFe.Parse(nfeFile));
                }
                progress.Report(new ProgressReport { Percentage = 40, StatusMessage = "NFes de combustível carregadas." });

                progress.Report(new ProgressReport { Percentage = 45, StatusMessage = "Lendo arquivos MDFe..." });
                var mdfes = new List<MdfeParsed>();
                foreach (var mdfeFile in mdfeFiles)
                {
                    var mdfe = ParserMDFe.Parse(mdfeFile);
                    var mdfeKey = $"{mdfe.Header.EmitCnpj}-{mdfe.Header.Serie}-{mdfe.Header.NumeroMdf}";
                    if (_processedMdfeKeys.Contains(mdfeKey))
                    {
                        CalculationLogService.Log($"AVISO: O MDF-e número {mdfe.Header.NumeroMdf} (chave: {mdfeKey}) já foi processado e será ignorado.");
                        continue;
                    }
                    mdfes.Add(mdfe);
                    _processedMdfeKeys.Add(mdfeKey);
                }
                progress.Report(new ProgressReport { Percentage = 60, StatusMessage = "MDFes carregados." });

                progress.Report(new ProgressReport { Percentage = 65, StatusMessage = "Iniciando cruzamento de dados e cálculo de rotas..." });
                var mergedRows = MergeService.MergeAsync(_allNfeItems, mdfes, true).Result;
                progress.Report(new ProgressReport { Percentage = 90, StatusMessage = "Cruzamento e cálculo de rotas concluídos." });

                progress.Report(new ProgressReport { Percentage = 100, StatusMessage = "Finalizando..." });

                return (true, mergedRows);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erro durante o processamento", MessageBoxButton.OK, MessageBoxImage.Error);
                CalculationLogService.Log($"ERRO: {ex.Message}");
                CalculationLogService.Save();
                progress.Report(new ProgressReport { Percentage = 0, StatusMessage = $"Erro: {ex.Message}" });
                return (false, new List<ModelRow>());
            }
        }

        private void SetUiProcessingState(bool isProcessing)
        {
            PreFillButton.IsEnabled = !isProcessing;
            ExportExcelButton.IsEnabled = !isProcessing;
            ExportConferenciaButton.IsEnabled = !isProcessing;
            OpenVehicleEditorButton.IsEnabled = !isProcessing;
            ImportJsonButton.IsEnabled = !isProcessing;
            SaveJsonButton.IsEnabled = !isProcessing;

            LoadingIndicator.Visibility = isProcessing ? Visibility.Visible : Visibility.Collapsed;
            ProgressPercentageText.Visibility = isProcessing ? Visibility.Visible : Visibility.Collapsed;

            if (!isProcessing)
            {
                LoadingIndicator.Value = 0;
                ProgressPercentageText.Text = "0%";
            }
        }

        private void ImportJson_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "JSON de Cálculo (*.json)|*.json", DefaultExt = ".json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dlg.FileName);
                    var rows = JsonSerializer.Deserialize<List<ModelRow>>(json);
                    if (rows != null)
                    {
                        _currentRows = rows;
                        PreviewGrid.ItemsSource = null;
                        PreviewGrid.ItemsSource = _currentRows;
                        StatusText.Text = $"Dados importados de {Path.GetFileName(dlg.FileName)}. {_currentRows.Count} linhas carregadas.";
                    }
                    else
                    {
                        MessageBox.Show(this, "O arquivo JSON está vazio ou em formato inválido.", "Erro de Importação", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Falha ao importar o arquivo JSON: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveJson_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRows == null || _currentRows.Count == 0)
            {
                MessageBox.Show(this, "Não há dados para salvar. Processe os arquivos primeiro.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog { Filter = "JSON de Cálculo (*.json)|*.json", DefaultExt = ".json", FileName = "calculo.json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(_currentRows, options);
                    File.WriteAllText(dlg.FileName, json);
                    StatusText.Text = $"Dados salvos em {Path.GetFileName(dlg.FileName)}.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Falha ao salvar o arquivo JSON: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenVehicleEditor_Click(object sender, RoutedEventArgs e)
        {
            var vehicleEditor = new VehicleEditorWindow();
            vehicleEditor.Owner = this;
            vehicleEditor.ShowDialog();
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentRows == null || _currentRows.Count == 0)
                {
                    MessageBox.Show(this, "Nada para exportar. Faça o processamento primeiro.", "Aviso");
                    return;
                }

                var sfd = new SaveFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx", FileName = "demonstrativo.xlsx" };
                if (sfd.ShowDialog() == true)
                {
                    using var workbook = new XLWorkbook("modelo_para_exportar.xlsx");

                    PopulateDemonstrativoWorksheet(workbook.Worksheet("Demonstrativo"), _currentRows);
                    PopulateNotaAquisicaoWorksheet(workbook.Worksheet("Nota de Aquisição Combustível"), _currentRows);

                    workbook.SaveAs(sfd.FileName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportConferencia_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, "Esta funcionalidade está temporariamente desabilitada.", "Aviso");
        }

        private void PopulateDemonstrativoWorksheet(IXLWorksheet worksheet, List<ModelRow> rows)
        {
            // ... (código de preenchimento da planilha, sem alterações)
        }

        private void PopulateNotaAquisicaoWorksheet(IXLWorksheet worksheet, List<ModelRow> rows)
        {
            // ... (código de preenchimento da planilha, sem alterações)
        }

        private void CreateConferenciaC190Worksheet(IXLWorkbook workbook, List<ModelRow> rows)
        {
            // ... (código de preenchimento da planilha, sem alterações)
        }

        private void ViewMap_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var mapPath = button?.CommandParameter as string;

            if (string.IsNullOrEmpty(mapPath) || !File.Exists(mapPath))
            {
                MessageBox.Show("O arquivo de mapa para esta rota não foi encontrado.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(mapPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir o mapa: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }
}