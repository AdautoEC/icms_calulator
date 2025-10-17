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
        private MdfeParsed _mdfe;

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

                _mdfe = ParserMDFe.Parse(MdfePath.Text);

                List<NfeParsedItem>? nfeItems = null;
                if (System.IO.File.Exists(NfePath.Text))
                {
                    var parsed = ParserNFe.Parse(NfePath.Text);
                    nfeItems = parsed;
                }

                var merged = await MergeService.MergeAsync(nfeItems, _mdfe, true);

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
            try
            {
                if (_mdfe == null)
                {
                    MessageBox.Show(this, "Nada para exportar. Faça o pré-preenchimento primeiro.", "Aviso");
                    return;
                }

                var sfd = new SaveFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx", FileName = "conferencia.xlsx" };
                if (sfd.ShowDialog() == true)
                {
                    var detailedRows = new List<ModelRow>();
                    foreach (var kv in _mdfe.DestinosPorChave)
                    {
                        var chave = kv.Key;
                        if (SpedTxtLookupService.TryGetC190InfoPorChave(chave, out var c190InfoList))
                        {
                            SpedTxtLookupService.TryGetAddressInfoPorChave(chave, out var addrInfo);
                            foreach (var c190Info in c190InfoList)
                            {
                                var row = new ModelRow
                                {
                                    ChaveNFe = chave,
                                    Cst = c190Info.cst,
                                    Cfop = c190Info.cfop,
                                    ValorIcms = c190Info.valorIcms,
                                    BaseIcms = c190Info.baseIcms,
                                    TotalDocumento = c190Info.totalDocumento,
                                    Street = addrInfo.street,
                                    Number = addrInfo.number,
                                    Neighborhood = addrInfo.neighborhood,
                                    UFDest = addrInfo.uf
                                };
                                detailedRows.Add(row);
                            }
                        }
                    }

                    using var workbook = new XLWorkbook();
                    CreateConferenciaC190Worksheet(workbook, detailedRows);
                    workbook.SaveAs(sfd.FileName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PopulateDemonstrativoWorksheet(IXLWorksheet worksheet, List<ModelRow> rows)
        {
            var row = rows.FirstOrDefault();
            if (row == null) return;

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

            // Data - Row 4
            worksheet.Cell("A4").Value = row.Modelo;
            worksheet.Cell("B4").Value = row.Tipo;
            worksheet.Cell("C4").Value = row.Renavam;
            worksheet.Cell("D4").Value = row.Placa;
            worksheet.Cell("E4").Value = row.MdfeNumero;
            worksheet.Cell("F4").Value = row.Data;
            worksheet.Cell("G4").Value = row.Roteiro;
            worksheet.Cell("H4").Value = row.DistanciaPercorridaKm;
            worksheet.Cell("I4").Value = row.NFeNumero;
            worksheet.Cell("J4").Value = row.DataEmissao;
            worksheet.Cell("K4").Value = row.QuantidadeLitros;
            worksheet.Cell("L4").Value = row.EspecieCombustivel;
            worksheet.Cell("M4").Value = row.ValorUnitario;
            worksheet.Cell("N4").Value = row.ValorTotalCombustivel;
            worksheet.Cell("O4").Value = row.ValorCredito;
            worksheet.Cell("P4").Value = row.NFeAquisicaoNumero;
            worksheet.Cell("Q4").Value = row.DataAquisicao;
        }

        private void PopulateNotaAquisicaoWorksheet(IXLWorksheet worksheet, List<ModelRow> rows)
        {
            var row = rows.FirstOrDefault();
            if (row == null) return;

            // Populate summary rows
            if (row.EspecieCombustivel?.ToLower().Contains("etanol") == true)
            {
                worksheet.Cell("C2").Value = row.QuantidadeLitros;
                worksheet.Cell("D2").Value = row.ValorTotalCombustivel;
            }
            else if (row.EspecieCombustivel?.ToLower().Contains("diesel") == true)
            {
                worksheet.Cell("C3").Value = row.QuantidadeLitros;
                worksheet.Cell("D3").Value = row.ValorTotalCombustivel;
            }
            else if (row.EspecieCombustivel?.ToLower().Contains("gasolina") == true)
            {
                worksheet.Cell("C4").Value = row.QuantidadeLitros;
                worksheet.Cell("D4").Value = row.ValorTotalCombustivel;
            }

            // Headers
            worksheet.Cell("A6").Value = "Demonstrativo de Aquisição de Combustivel";
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

            worksheet.Cell("D8").Value = "CNPJ";
            worksheet.Cell("E8").Value = "Razão Social";

            // Data
            worksheet.Cell("A9").Value = row.DataEmissao;
            worksheet.Cell("B9").Value = row.Data;
            worksheet.Cell("C9").Value = row.NFeNumero;
            worksheet.Cell("D9").Value = row.FornecedorCnpj;
            worksheet.Cell("E9").Value = row.FornecedorNome;
            worksheet.Cell("G9").Value = row.FornecedorEndereco;
            worksheet.Cell("H9").Value = row.EspecieCombustivel;
            // Categoria is not in ModelRow
            worksheet.Cell("J9").Value = row.QuantidadeLitros;
            worksheet.Cell("K9").Value = row.ValorUnitario;
            worksheet.Cell("L9").Value = row.ValorTotalCombustivel;

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

            worksheet.Columns().AdjustToContents();
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
