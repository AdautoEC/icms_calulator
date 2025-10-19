using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CsvIntegratorApp.Models;
using CsvIntegratorApp.Services;
using CsvIntegratorApp.Services.Sped;
using ClosedXML.Excel;

namespace CsvIntegratorApp
{
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
            try
            {
                var mdfeFiles = MdfePath.Text.Split(';').Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).ToList();
                if (!mdfeFiles.Any())
                {
                    MessageBox.Show(this, "Selecione pelo menos um arquivo MDF-e.", "Aviso");
                    return;
                }

                StatusText.Text = "Processando... aguarde.";

                var txtFiles = TxtPath.Text.Split(';').Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).ToList();
                if (txtFiles.Any())
                {
                    SpedTxtLookupService.LoadTxt(txtFiles);
                }

                var nfeFiles = NfePath.Text.Split(';').Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).ToList();
                if (nfeFiles.Any())
                {
                    _allNfeItems.Clear();
                    foreach (var nfeFile in nfeFiles)
                    {
                        _allNfeItems.AddRange(ParserNFe.Parse(nfeFile));
                    }
                }

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

                _currentRows = await MergeService.MergeAsync(_allNfeItems, mdfes, true);
                PreviewGrid.ItemsSource = null;
                PreviewGrid.ItemsSource = _currentRows;

                StatusText.Text = $"Pré-preenchido: {_currentRows.Count} linha(s).";

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

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentRows == null || _currentRows.Count == 0)
                {
                    MessageBox.Show(this, "Nada para exportar. Faça o pré-preenchimento primeiro.", "Aviso");
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
            worksheet.Cell("M1").Value = "Art. 62-B § 3º V";
            worksheet.Cell("O1").Value = "Art. 62-B § 3º VI";
            worksheet.Cell("P1").Value = "Art. 62-B § 3º VII";

            // Headers - Row 2
            worksheet.Cell("A2").Value = "Veículo Utilizado";
            worksheet.Cell("E2").Value = "Trajeto";
            worksheet.Cell("I2").Value = "Carga";
            worksheet.Cell("K2").Value = "Combustível";
            worksheet.Cell("M2").Value = "Valor do Combustivel";
            worksheet.Cell("O2").Value = "Crédito a ser apropriado";
            worksheet.Cell("P2").Value = "Nota de Aquisição do Combustível";

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
            worksheet.Cell("K3").Value = "Quantidade (LT)";
            worksheet.Cell("L3").Value = "Espécie do Combustivel";
            worksheet.Cell("M3").Value = "Valor unitário";
            worksheet.Cell("N3").Value = "Valor Total do Combustivel";
            worksheet.Cell("O3").Value = "Valor do Crédito a ser utilizado (17%)";
            worksheet.Cell("P3").Value = "N° NF-e";
            worksheet.Cell("Q3").Value = "Data de Aquisição";

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
                worksheet.Cell(currentRow, 7).Value = row.Roteiro;
                worksheet.Cell(currentRow, 8).Value = row.DistanciaPercorridaKm;
                worksheet.Cell(currentRow, 9).Value = row.NFeNumero;
                worksheet.Cell(currentRow, 10).Value = row.DataEmissao;
                worksheet.Cell(currentRow, 11).Value = row.QuantidadeLitros;
                worksheet.Cell(currentRow, 12).Value = row.EspecieCombustivel;
                worksheet.Cell(currentRow, 13).Value = row.ValorUnitario;
                worksheet.Cell(currentRow, 14).Value = row.ValorTotalCombustivel;
                worksheet.Cell(currentRow, 15).Value = row.ValorCredito;
                worksheet.Cell(currentRow, 16).Value = row.NFeAquisicaoNumero;
                worksheet.Cell(currentRow, 17).Value = row.DataAquisicao;
                currentRow++;
            }

            if (rows.Any())
            {
                var range = worksheet.Range(4, 1, currentRow - 1, 17);
                range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                range.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            }
        }

        private void PopulateNotaAquisicaoWorksheet(IXLWorksheet worksheet, List<ModelRow> rows)
        {
            var dieselNFeItems = _allNfeItems
                .Where(n => n.IsCombustivel && (n.DescricaoProduto ?? "").ToUpperInvariant().Contains("DIESEL"))
                .GroupBy(n => n.ChaveNFe)
                .Select(g => g.First())
                .ToList();

            var dieselRows = dieselNFeItems.Select(n => new ModelRow
            {
                DataEmissao = n.DataEmissao,
                NFeNumero = n.NumeroNFe,
                FornecedorCnpj = n.EmitCNPJ,
                FornecedorNome = n.EmitNome,
                FornecedorEndereco = $"{n.EmitStreet}, {n.EmitNumber} - {n.EmitNeighborhood}, {n.CidadeEmit} - {n.UFEmit}",
                EspecieCombustivel = n.DescANP ?? n.DescricaoProduto,
                QuantidadeLitros = n.Quantidade,
                ValorUnitario = n.ValorUnitario,
                ValorTotalCombustivel = n.ValorTotal,
                ChaveNFe = n.ChaveNFe
            }).ToList();

            // Summary rows for Diesel
            worksheet.Cell("C3").Value = dieselRows.Sum(r => r.QuantidadeLitros);
            worksheet.Cell("D3").Value = dieselRows.Sum(r => r.ValorTotalCombustivel);

            // Headers
            worksheet.Cell("A6").Value = "Demonstrativo de Aquisição de Combustivel (Diesel)";
            worksheet.Cell("A7").Value = "Data de emissão";
            worksheet.Cell("B7").Value = "Data de Entrada";
            worksheet.Cell("C7").Value = "N° Nota Fiscal";
            worksheet.Cell("D7").Value = "Fornecedor";
            worksheet.Cell("G7").Value = "Endereço";
            worksheet.Cell("H7").Value = "Produto";
            worksheet.Cell("I7").Value = "Categoria";
            worksheet.Cell("J7").Value = "Quantidade (Litros)";
            worksheet.Cell("K7").Value = "Valor Unitário";
            worksheet.Cell("L7").Value = "Valor total";
            worksheet.Cell("M7").Value = "Chave de Acesso";

            worksheet.Cell("D8").Value = "CNPJ";
            worksheet.Cell("E8").Value = "Razão Social";

            // Data
            int currentRow = 9;
            foreach (var row in dieselRows)
            {
                worksheet.Cell(currentRow, 1).Value = row.DataEmissao;
                worksheet.Cell(currentRow, 2).Value = row.Data; // This might be null, as it comes from MDFe
                worksheet.Cell(currentRow, 3).Value = row.NFeNumero;
                worksheet.Cell(currentRow, 4).Value = row.FornecedorCnpj;
                worksheet.Cell(currentRow, 5).Value = row.FornecedorNome;
                worksheet.Cell(currentRow, 7).Value = row.FornecedorEndereco;
                worksheet.Cell(currentRow, 8).Value = row.EspecieCombustivel;
                // Categoria is not in ModelRow
                worksheet.Cell(currentRow, 10).Value = row.QuantidadeLitros;
                worksheet.Cell(currentRow, 11).Value = row.ValorUnitario;
                worksheet.Cell(currentRow, 12).Value = row.ValorTotalCombustivel;
                worksheet.Cell(currentRow, 13).Value = row.ChaveNFe;
                currentRow++;
            }

            if (dieselRows.Any())
            {
                var range = worksheet.Range(9, 1, currentRow - 1, 13);
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


    }
}
