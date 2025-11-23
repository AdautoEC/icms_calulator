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
                    foreach (var row in _currentRows)
                    {
                        row.IsInitialized = true;
                    }
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
                var mergedRows = MergeService.MergeAsync(_allNfeItems, mdfes, progress, true).Result;
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
                        foreach (var row in _currentRows)
                        {
                            row.IsInitialized = true;
                        }
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
            // Headers - Row 1
            worksheet.Cell("A1").Value = "Art. 62-B § 3º I";
            worksheet.Cell("E1").Value = "Art. 62-B § 3º II";
            worksheet.Cell("K1").Value = "Art. 62-B § 3º IV";
            worksheet.Cell("N1").Value = "Art. 62-B § 3º V";
            worksheet.Cell("P1").Value = "Art. 62-B § 3º VI";
            worksheet.Cell("Q1").Value = "Art. 62-B § 3º VII";

            // Headers - Row 2
            worksheet.Cell("A2").Value = "Veículo Utilizado";
            worksheet.Cell("E2").Value = "Trajeto";
            worksheet.Cell("I2").Value = "Carga";
            worksheet.Cell("K2").Value = "Combustível";
            worksheet.Cell("N2").Value = "Valor do Combustivel";
            worksheet.Cell("P2").Value = "Crédito a ser apropriado";
            worksheet.Cell("Q2").Value = "Nota de Aquisição do Combustível";

            // Headers - Row 3
            worksheet.Cell("A3").Value = "Modelo";
            worksheet.Cell("B3").Value = "Tipo";
            worksheet.Cell("C3").Value = "Renavam";
            worksheet.Cell("D3").Value = "Placa";
            worksheet.Cell("E3").Value = "MDF-e";
            worksheet.Cell("F3").Value = "Data";
            worksheet.Cell("G3").Value = "Roteiro";
            worksheet.Cell("H3").Value = "Distância Percorrida (KM)";
            worksheet.Cell("I3").Value = "N° NF-e";
            worksheet.Cell("J3").Value = "Data de Emissão";
            worksheet.Cell("K3").Value = "Quantidade Usada (LT)";
            worksheet.Cell("L3").Value = "Qtd Total NFe (LT)";
            worksheet.Cell("M3").Value = "Espécie do Combustivel";
            worksheet.Cell("N3").Value = "Valor unitário";
            worksheet.Cell("O3").Value = "Valor Total do Combustivel";
            worksheet.Cell("P3").Value = "Valor do Crédito a ser utilizado";
            worksheet.Cell("Q3").Value = "N° NF-e";
            worksheet.Cell("R3").Value = "Data de Aquisição";

            // Data
            int currentRow = 4;
            foreach (var row in rows)
            {
                worksheet.Cell(currentRow, 1).Value = row.Modelo;
                worksheet.Cell(currentRow, 2).Value = row.Tipo;
                worksheet.Cell(currentRow, 3).Value = row.Renavam;
                worksheet.Cell(currentRow, 4).Value = row.Placa;
                worksheet.Cell(currentRow, 5).Value = row.MdfeNumero;
                worksheet.Cell(currentRow, 6).Value = row.Data;
                worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "dd/MM/yyyy";
                worksheet.Cell(currentRow, 7).Value = row.Roteiro;
                worksheet.Cell(currentRow, 8).Value = row.DistanciaPercorridaKm;
                worksheet.Cell(currentRow, 8).Style.NumberFormat.Format = "0";
                worksheet.Cell(currentRow, 9).Value = row.NFeCargaNumero;
                worksheet.Cell(currentRow, 10).Value = row.DataEmissaoCarga;
                worksheet.Cell(currentRow, 10).Style.NumberFormat.Format = "dd/MM/yyyy";
                worksheet.Cell(currentRow, 11).Value = row.QuantidadeUsadaLitros;
                worksheet.Cell(currentRow, 11).Style.NumberFormat.Format = "0.0000";
                worksheet.Cell(currentRow, 12).Value = row.QuantidadeLitros;
                worksheet.Cell(currentRow, 12).Style.NumberFormat.Format = "0.0000";
                worksheet.Cell(currentRow, 13).Value = row.EspecieCombustivel;
                worksheet.Cell(currentRow, 14).Value = row.ValorUnitario;
                worksheet.Cell(currentRow, 14).Style.NumberFormat.Format = "0.0000";
                worksheet.Cell(currentRow, 15).Value = row.ValorTotalCombustivel;
                worksheet.Cell(currentRow, 15).Style.NumberFormat.Format = "0.00";
                worksheet.Cell(currentRow, 16).Value = row.ValorCredito;
                worksheet.Cell(currentRow, 16).Style.NumberFormat.Format = "0.00";
                worksheet.Cell(currentRow, 17).Value = row.NFeAquisicaoNumero;
                worksheet.Cell(currentRow, 18).Value = row.DataAquisicao;
                worksheet.Cell(currentRow, 18).Style.NumberFormat.Format = "dd/MM/yyyy";
                currentRow++;
            }

            if (rows.Any())
            {
                var range = worksheet.Range(4, 1, currentRow - 1, 18);
                range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                range.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            }
        }

        private void PopulateNotaAquisicaoWorksheet(IXLWorksheet worksheet, List<ModelRow> rows)
        {
            var dieselRows = (_allNfeItems ?? new List<NfeParsedItem>())
                .Where(FuelAllocator.IsDieselItem) // ANP 8201 ou descrição contendo "DIESEL"
                .Select(i =>
                {
                    var litros = i.Quantidade ?? 0.0;
                    var unit = i.ValorUnitario ?? 0.0;
                    var total = unit * litros;

                    return new ModelRow
                    {
                        DataEmissao = i.DataEmissao,
                        NFeNumero = i.NumeroNFe,
                        FornecedorCnpj = i.EmitCNPJ,
                        FornecedorNome = i.EmitNome,
                        FornecedorEndereco = $"{i.EmitStreet}, {i.EmitNumber} - {i.EmitNeighborhood}, {i.CidadeEmit} - {i.UFEmit}",
                        EspecieCombustivel = i.DescANP ?? i.DescricaoProduto ?? "DIESEL",

                        // <<< uma linha por ITEM de DIESEL
                        QuantidadeLitros = Math.Round(litros, 6),
                        ValorUnitario = unit,
                        ValorTotalCombustivel = Math.Round(total, 2),

                        ChaveNFe = i.ChaveNFe
                    };
                })
                .OrderBy(r => r.DataEmissao ?? DateTime.MinValue)
                .ToList();


            // Summary rows for Diesel
            worksheet.Cell("C3").Value = dieselRows.Sum(r => r.QuantidadeLitros);
            worksheet.Cell("D3").Value = dieselRows.Sum(r => r.ValorTotalCombustivel);

            // Headers
            worksheet.Cell("A6").Value = "Demonstrativo de Aquisição de Combustivel (Diesel)";
            worksheet.Cell("A7").Value = "Data de emissão";
            worksheet.Cell("B7").Value = "Data de Entrada";
            worksheet.Cell("C7").Value = "N° Nota Fiscal";
            worksheet.Cell("D7").Value = "Fornecedor";
            worksheet.Cell("F7").Value = "Endereço";
            worksheet.Cell("G7").Value = "Produto";
            worksheet.Cell("H7").Value = "Categoria";
            worksheet.Cell("I7").Value = "Quantidade (Litros)";
            worksheet.Cell("J7").Value = "Valor Unitário";
            worksheet.Cell("K7").Value = "Valor total";

            worksheet.Cell("D8").Value = "CNPJ";
            worksheet.Cell("E8").Value = "Razão Social";

            // Data
            int currentRow = 9;
            foreach (var row in dieselRows)
            {
                // Tenta obter a data de entrada do C100 usando a chave da NFe
                DateTime? dataEntrada = null;
                if (SpedTxtLookupService.TryGetC100DataPorChave(row.ChaveNFe, out var dtEntrada))
                {
                    dataEntrada = dtEntrada;
                }

                worksheet.Cell(currentRow, 1).Value = row.DataEmissao;
                worksheet.Cell(currentRow, 1).Style.NumberFormat.Format = "dd/MM/yyyy";
                worksheet.Cell(currentRow, 2).Value = dataEntrada; // Usa a data de entrada do C100
                worksheet.Cell(currentRow, 2).Style.NumberFormat.Format = "dd/MM/yyyy";
                worksheet.Cell(currentRow, 3).Value = row.NFeNumero;
                worksheet.Cell(currentRow, 4).Value = row.FornecedorCnpj;
                worksheet.Cell(currentRow, 5).Value = row.FornecedorNome;
                worksheet.Cell(currentRow, 6).Value = row.FornecedorEndereco;
                worksheet.Cell(currentRow, 7).Value = row.EspecieCombustivel;
                worksheet.Cell(currentRow, 8).Value = "Diesel";
                worksheet.Cell(currentRow, 9).Value = row.QuantidadeLitros;
                worksheet.Cell(currentRow, 9).Style.NumberFormat.Format = "0.0000";
                worksheet.Cell(currentRow, 10).Value = row.ValorUnitario;
                worksheet.Cell(currentRow, 10).Style.NumberFormat.Format = "0.0000";
                worksheet.Cell(currentRow, 11).Value = row.ValorTotalCombustivel;
                worksheet.Cell(currentRow, 11).Style.NumberFormat.Format = "0.00";
                worksheet.Cell(currentRow, 12).Value = row.ChaveNFe;
                currentRow++;
            }

            if (dieselRows.Any())
            {
                var range = worksheet.Range(9, 1, currentRow - 1, 12);
                range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                range.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            }
        }

        private void CreateConferenciaC190Worksheet(IXLWorkbook workbook, List<ModelRow> rows)
        {
            var worksheet = workbook.Worksheets.Add("ConferenciaC190");

            // Headers
            worksheet.Cell(1, 1).Value = "ChaveNFe";
            worksheet.Cell(1, 2).Value = "CST";
            worksheet.Cell(1, 3).Value = "CFOP";
            worksheet.Cell(1, 4).Value = "ValorIcms";
            worksheet.Cell(1, 5).Value = "BaseIcms";
            worksheet.Cell(1, 6).Value = "TotalDocumento";
            worksheet.Cell(1, 7).Value = "Rua";
            worksheet.Cell(1, 8).Value = "Numero";
            worksheet.Cell(1, 9).Value = "Bairro";
            worksheet.Cell(1, 10).Value = "UF";

            // Data
            int currentRow = 2;
            foreach (var r in rows)
            {
                worksheet.Cell(currentRow, 1).Value = r.ChaveNFe;
                worksheet.Cell(currentRow, 2).Value = r.Cst;
                worksheet.Cell(currentRow, 3).Value = r.Cfop;
                worksheet.Cell(currentRow, 4).Value = r.ValorIcms;
                worksheet.Cell(currentRow, 5).Value = r.BaseIcms;
                worksheet.Cell(currentRow, 6).Value = r.TotalDocumento;
                worksheet.Cell(currentRow, 7).Value = r.Street;
                worksheet.Cell(currentRow, 8).Value = r.Number;
                worksheet.Cell(currentRow, 9).Value = r.Neighborhood;
                worksheet.Cell(currentRow, 10).Value = r.UFDest;

                currentRow++;
            }

            if (rows.Any())
            {
                var range = worksheet.Range(1, 1, currentRow - 1, 10);
                range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                range.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            }

            worksheet.Columns().AdjustToContents();
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

        private void AdjustRoute_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var modelRow = button?.CommandParameter as ModelRow;

            if (modelRow != null)
            {
                var routeEditor = new RouteEditorWindow(modelRow);
                routeEditor.Owner = this;
                if (routeEditor.ShowDialog() == true)
                {
                    // A route was changed, which affects the entire fuel allocation pool.
                    // We need to recalculate all allocations.
                    StatusText.Text = "Recalculando alocação de combustível após ajuste de rota...";
                    MergeService.RecalculateFuelAllocations(_currentRows, _allNfeItems);
                    
                    // Refresh the grid to show updated values for all rows
                    PreviewGrid.ItemsSource = null;
                    PreviewGrid.ItemsSource = _currentRows;
                    StatusText.Text = "Alocação de combustível recalculada.";
                }
            }
        }
    }
}