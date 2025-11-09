using CsvIntegratorApp.Models;
using CsvIntegratorApp.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace CsvIntegratorApp
{
    public partial class ModelEditorWindow : Window
    {
        private List<ModelRow> _rows = new();

        public ModelEditorWindow(List<ModelRow> rowsToEdit)
        {
            InitializeComponent();
            _rows.Clear();
            if (rowsToEdit != null)
            {
                _rows.AddRange(rowsToEdit);
            }
            EditorGrid.ItemsSource = _rows;
            EditorStatus.Text = $"Editando {_rows.Count} linha(s).";
        }

        private void ImportModel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Modelo JSON (*.json)|*.json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dlg.FileName);
                    var importedRows = System.Text.Json.JsonSerializer.Deserialize<List<ModelRow>>(json);
                    _rows.Clear();
                    if (importedRows != null)
                    {
                        _rows.AddRange(importedRows);
                    }
                    EditorGrid.ItemsSource = null;
                    EditorGrid.ItemsSource = _rows;
                    EditorStatus.Text = $"Importado: {_rows.Count} linha(s).";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Erro ao importar", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveModel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "Modelo JSON (*.json)|*.json", FileName = "modelo.json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(_rows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dlg.FileName, json);
                    ModelService.SaveLocal(_rows);
                    EditorStatus.Text = "Modelo salvo.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Erro ao salvar", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void AddRow_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var sel = EditorGrid?.SelectedItem as ModelRow;
            double? aliq = sel?.AliquotaCredito ?? 0.17;

            var nova = new ModelRow
            {
                AliquotaCredito = aliq
            };

            _rows.Add(nova);
            EditorGrid.ItemsSource = null;
            EditorGrid.ItemsSource = _rows;
        }

        private void RemoveRow_Click(object sender, RoutedEventArgs e)
        {
            var sel = EditorGrid.SelectedItem as ModelRow;
            if (sel != null)
            {
                _rows.Remove(sel);
                EditorGrid.ItemsSource = null;
                EditorGrid.ItemsSource = _rows;
            }
        }

        private void EditorGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row?.Item is ModelRow r)
            {
                r.ValorCredito = (r.ValorTotalCombustivel.HasValue && r.AliquotaCredito.HasValue)
                    ? Math.Round(r.ValorTotalCombustivel.Value * r.AliquotaCredito.Value, 2)
                    : (double?)null;
            }
        }

        private void EditorGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column is DataGridTextColumn textColumn)
            {
                // Distancia percorrida: sem casas decimais
                if (e.PropertyName == "DistanciaPercorridaKm")
                {
                    textColumn.Binding.StringFormat = "{0:F0}";
                }
                // Datas: remover horas
                else if (e.PropertyName == "Data" || e.PropertyName == "DataEmissao" || e.PropertyName == "DataAquisicao")
                {
                    textColumn.Binding.StringFormat = "{0:d}";
                }
                // Litros e valores monetários: 2 casas decimais
                else if (e.PropertyName == "QuantidadeLitros" || e.PropertyName == "QuantidadeEstimadaLitros" ||
                         e.PropertyName == "ValorUnitario" || e.PropertyName == "ValorTotalCombustivel" ||
                         e.PropertyName == "ValorCredito")
                {
                    textColumn.Binding.StringFormat = "{0:F2}";
                }
            }
        }
    }
}
