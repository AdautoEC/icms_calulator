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
using ClosedXML.Excel;

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

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentRows == null || _currentRows.Count == 0)
                {
                    MessageBox.Show(this, "Nada para exportar. Faça o pré-preenchimento primeiro.", "Aviso");
                    return;
                }

                var sfd = new SaveFileDialog { Filter = "Excel Macro-Enabled Workbook (*.xlsm)|*.xlsm", FileName = "demonstrativo.xlsm" };
                if (sfd.ShowDialog() == true)
                {
                    using var workbook = new XLWorkbook();

                    CreateDemonstrativoWorksheet(workbook);
                    CreateNotaAquisicaoWorksheet(workbook, _currentRows);

                    workbook.SaveAs(sfd.FileName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateDemonstrativoWorksheet(IXLWorkbook workbook)
        {
            var worksheet = workbook.Worksheets.Add("Demonstrativo");

            // Page Setup
            worksheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;

            // Column Widths
            worksheet.Column("A").Width = 9.0;
            worksheet.Column("B").Width = 8.75;
            worksheet.Column("C").Width = 12.625;
            worksheet.Column("D").Width = 9.0;
            // E is default
            worksheet.Column("F").Width = 10.0;
            worksheet.Column("G").Width = 9.375;
            worksheet.Column("H").Width = 16.25;
            worksheet.Column("I").Width = 24.125;
            worksheet.Column("J").Width = 7.125;
            worksheet.Column("K").Width = 19.125;
            worksheet.Column("L").Width = 9.875;
            worksheet.Column("M").Width = 12.875;
            worksheet.Column("N").Width = 13.875;
            worksheet.Column("O").Width = 13.25;
            worksheet.Column("P").Width = 13.125;
            worksheet.Column("Q").Width = 20.0;
            worksheet.Column("R").Width = 13.75;

            // Row Heights
            worksheet.Row(2).Height = 25.5;
            worksheet.Row(3).Height = 38.25;
            worksheet.Row(4).Height = 38.25;
            worksheet.Row(5).Height = 25.5;
            worksheet.Row(6).Height = 191.25;

            // Merged Cells
            worksheet.Range("B5:C5").Merge();
            worksheet.Range("B6:C6").Merge();
            worksheet.Range("B2:E2").Merge();
            worksheet.Range("F2:I2").Merge();
            worksheet.Range("J2:K2").Merge();
            worksheet.Range("L2:M2").Merge();
            worksheet.Range("N2:O2").Merge();
            worksheet.Range("Q2:R2").Merge();
            worksheet.Range("B1:E1").Merge();
            worksheet.Range("F1:K1").Merge();
            worksheet.Range("L1:M1").Merge();
            worksheet.Range("N1:O1").Merge();
            worksheet.Range("Q1:R1").Merge();

            // Header Style
            var headerStyle = worksheet.Range("A1:R4").Style;
            headerStyle.Font.FontName = "Cambria";
            headerStyle.Font.FontSize = 10;
            headerStyle.Font.Bold = false;
            headerStyle.Font.Italic = false;
            headerStyle.Font.FontColor = XLColor.FromTheme(XLThemeColor.Text1);
            headerStyle.Fill.PatternType = XLFillPatternValues.Solid;
            headerStyle.Fill.BackgroundColor = XLColor.FromArgb(0x00, 0x20, 0x60);
            headerStyle.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerStyle.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerStyle.Alignment.WrapText = true;

            // Column Headers and specific styles
            worksheet.Cell("B1").Value = "Art. 62-B § 3º I";
            worksheet.Cell("B1").Style.Font.Bold = true;
            worksheet.Cell("F1").Value = "Art. 62-B § 3º II";
            worksheet.Cell("F1").Style.Font.Bold = true;
            worksheet.Cell("L1").Value = "Art. 62-B § 3º IV";
            worksheet.Cell("L1").Style.Font.Bold = true;
            worksheet.Cell("N1").Value = "Art. 62-B § 3º V";
            worksheet.Cell("N1").Style.Font.Bold = true;
            worksheet.Cell("P1").Value = "Art. 62-B § 3º VI";
            worksheet.Cell("P1").Style.Font.Bold = true;
            worksheet.Cell("Q1").Value = "Art. 62-B § 3º VII";
            worksheet.Cell("Q1").Style.Font.Bold = true;

            // Data alignment
            worksheet.Range("A5:R6").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range("A5:R6").Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        private void CreateNotaAquisicaoWorksheet(IXLWorkbook workbook, List<ModelRow> rows)
        {
            var worksheet = workbook.Worksheets.Add("Nota de Aquisição Combustível");

            // Column Widths
            worksheet.Column("A").Width = 7.125;
            worksheet.Column("B").Width = 13.25;
            worksheet.Column("C").Width = 24.875;
            worksheet.Column("D").Width = 13.75;
            worksheet.Column("E").Width = 9.875;
            worksheet.Column("F").Width = 12.0;
            worksheet.Column("G").Width = 8.125;
            worksheet.Column("H").Width = 23.25;
            worksheet.Column("I").Width = 8.25;
            worksheet.Column("J").Width = 16.25;
            worksheet.Column("K").Width = 12.25;
            worksheet.Column("L").Width = 9.375;
            worksheet.Column("M").Width = 13.375;
            worksheet.Column("N").Width = 9.0;
            worksheet.Column("Q").Width = 9.75;
            worksheet.Column("R").Width = 9.625;

            // Row Heights
            worksheet.Row(9).Height = 30.75;
            worksheet.Row(10).Height = 38.25;

            // Merged Cells
            worksheet.Range("B6:M6").Merge();
            worksheet.Range("E7:F7").Merge();

            // Header Style (Row 7)
            var headerRange = worksheet.Range("A7:R7");
            headerRange.Style.Font.FontName = "Cambria";
            headerRange.Style.Font.FontSize = 10;
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Font.FontColor = XLColor.FromTheme(XLThemeColor.Text1);
            headerRange.Style.Fill.PatternType = XLFillPatternValues.Solid;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(0x00, 0x20, 0x60);
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRange.Style.Alignment.WrapText = true;

            // Column Headers
            worksheet.Cell("B7").Value = "Data de emissão";
            worksheet.Cell("C7").Value = "Data de Entrada";
            worksheet.Cell("D7").Value = "N° Nota Fiscal";
            worksheet.Cell("E7").Value = "Fornecedor";
            worksheet.Cell("G7").Value = "Endereço ";
            worksheet.Cell("H7").Value = "Produto";
            worksheet.Cell("I7").Value = "Categoria";
            worksheet.Cell("J7").Value = "Quantidade (Litros)";
            worksheet.Cell("K7").Value = "Valor Unitário";
            worksheet.Cell("L7").Value = "Valor total";
            worksheet.Cell("M7").Value = "Chave de Acesso";

            // Data Population
            int currentRow = 8;
            foreach (var r in rows)
            {
                // Mapping data - making educated guesses
                worksheet.Cell(currentRow, 2).Value = r.DataAquisicao; // Data de emissão
                worksheet.Cell(currentRow, 3).Value = r.Data; // Data de Entrada
                worksheet.Cell(currentRow, 4).Value = r.NFeAquisicaoNumero; // N° Nota Fiscal
                // E, F, G - Fornecedor, Endereço - No data in ModelRow
                worksheet.Cell(currentRow, 8).Value = r.EspecieCombustivel; // Produto
                // I - Categoria - No data in ModelRow
                worksheet.Cell(currentRow, 10).Value = r.QuantidadeLitros;
                worksheet.Cell(currentRow, 11).Value = r.ValorUnitario;
                worksheet.Cell(currentRow, 12).Value = r.ValorTotalCombustivel;
                worksheet.Cell(currentRow, 13).Value = r.NFeAquisicaoNumero; // Chave de Acesso

                // Apply number formats
                worksheet.Cell(currentRow, 10).Style.NumberFormat.Format = "_-* #,##0.00_-;\\-* #,##0.00_-;_-* \"-\"??_-;_-@_";
                worksheet.Cell(currentRow, 11).Style.NumberFormat.Format = "_-* #,##0.0000_-;\\-* #,##0.0000_-;_-* \"-\"??_-;_-@_";
                worksheet.Cell(currentRow, 12).Style.NumberFormat.Format = "_-* #,##0.00_-;\\-* #,##0.00_-;_-* \"-\"??_-;_-@_";
                worksheet.Cell(currentRow, 13).Style.NumberFormat.Format = "@";

                currentRow++;
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
