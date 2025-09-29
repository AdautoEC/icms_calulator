using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using CsvIntegratorApp.Models;
using CsvIntegratorApp.Services;

namespace CsvIntegratorApp
{
    public partial class ImportWizardWindow : Window
    {
        private List<ModelRow> _rows = new();

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

                var mdfe = ParserMDFe.Parse(MdfePath.Text);

                List<NfeParsedItem>? nfeItems = null;
                if (System.IO.File.Exists(NfePath.Text))
                {
                    var parsed = ParserNFe.Parse(NfePath.Text);
                    // Se quiser restringir a combustível:
                    // parsed = parsed.Where(i => i.IsCombustivel).ToList();
                    nfeItems = parsed;
                }

                var merged = await MergeService.MergeAsync(nfeItems, mdfe, somarRetornoParaOrigem: true);

                _rows = merged;
                PreviewGrid.ItemsSource = null;
                PreviewGrid.ItemsSource = _rows;

                StatusText.Text = $"Pré-preenchido: {_rows.Count} linha(s).";
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenEditor_Click(object sender, RoutedEventArgs e)
        {
            var win = new ModelEditorWindow();
            win.ShowDialog();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_rows == null || _rows.Count == 0)
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

                    foreach (var r in _rows)
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
    }
}
