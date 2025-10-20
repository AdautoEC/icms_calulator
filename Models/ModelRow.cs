// Models/ModelRow.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace CsvIntegratorApp.Models
{
    public class ModelRow : INotifyPropertyChanged
    {
        private string? _modelo;
        private string? _tipo;
        private string? _renavam;
        private string? _placa;
        private string? _mdfeNumero;
        private System.DateTime? _data;
        private double? _distanciaPercorridaKm;
        private string? _nFeNumero;
        private System.DateTime? _dataEmissao;
        private string? _nFeCargaNumero;
        private string? _dataEmissaoCarga;
        private double? _quantidadeLitros;
        private double? _quantidadeEstimadaLitros;
        private string? _especieCombustivel;
        private double? _valorUnitario;
        private double? _valorTotalCombustivel;
        private double? _valorCredito;
        private string? _nFeAquisicaoNumero;
        private System.DateTime? _dataAquisicao;
        private string? _vinculo;

        public bool IsInitialized { get; set; }

        public string? Modelo { get => _modelo; set { if (SetField(ref _modelo, value) && IsInitialized) { Modelo_IsEdited = true; OnPropertyChanged(nameof(Modelo_IsEdited)); } } }
        public bool Modelo_IsEdited { get; private set; }
        public string? Tipo { get => _tipo; set { if (SetField(ref _tipo, value) && IsInitialized) { Tipo_IsEdited = true; OnPropertyChanged(nameof(Tipo_IsEdited)); } } }
        public bool Tipo_IsEdited { get; private set; }
        public string? Renavam { get => _renavam; set { if (SetField(ref _renavam, value) && IsInitialized) { Renavam_IsEdited = true; OnPropertyChanged(nameof(Renavam_IsEdited)); } } }
        public bool Renavam_IsEdited { get; private set; }
        public string? Placa { get => _placa; set { if (SetField(ref _placa, value) && IsInitialized) { Placa_IsEdited = true; OnPropertyChanged(nameof(Placa_IsEdited)); } } }
        public bool Placa_IsEdited { get; private set; }

        public string? MdfeNumero { get => _mdfeNumero; set { if (SetField(ref _mdfeNumero, value) && IsInitialized) { MdfeNumero_IsEdited = true; OnPropertyChanged(nameof(MdfeNumero_IsEdited)); } } }
        public bool MdfeNumero_IsEdited { get; private set; }
        public System.DateTime? Data { get => _data; set { if (SetField(ref _data, value) && IsInitialized) { Data_IsEdited = true; OnPropertyChanged(nameof(Data_IsEdited)); } } }
        public bool Data_IsEdited { get; private set; }
        public string? Roteiro { get; set; }
        public double? DistanciaPercorridaKm { get => _distanciaPercorridaKm; set { if (SetField(ref _distanciaPercorridaKm, value) && IsInitialized) { DistanciaPercorridaKm_IsEdited = true; OnPropertyChanged(nameof(DistanciaPercorridaKm_IsEdited)); } } }
        public bool DistanciaPercorridaKm_IsEdited { get; private set; }

        public string? NFeNumero { get => _nFeNumero; set { if (SetField(ref _nFeNumero, value) && IsInitialized) { NFeNumero_IsEdited = true; OnPropertyChanged(nameof(NFeNumero_IsEdited)); } } }
        public bool NFeNumero_IsEdited { get; private set; }
        public System.DateTime? DataEmissao { get => _dataEmissao; set { if (SetField(ref _dataEmissao, value) && IsInitialized) { DataEmissao_IsEdited = true; OnPropertyChanged(nameof(DataEmissao_IsEdited)); } } }
        public bool DataEmissao_IsEdited { get; private set; }
        public string? NFeCargaNumero { get => _nFeCargaNumero; set { if (SetField(ref _nFeCargaNumero, value) && IsInitialized) { NFeCargaNumero_IsEdited = true; OnPropertyChanged(nameof(NFeCargaNumero_IsEdited)); } } }
        public bool NFeCargaNumero_IsEdited { get; private set; }
        public string? DataEmissaoCarga { get => _dataEmissaoCarga; set { if (SetField(ref _dataEmissaoCarga, value) && IsInitialized) { DataEmissaoCarga_IsEdited = true; OnPropertyChanged(nameof(DataEmissaoCarga_IsEdited)); } } }
        public bool DataEmissaoCarga_IsEdited { get; private set; }

        public double? QuantidadeLitros { get => _quantidadeLitros; set { if (SetField(ref _quantidadeLitros, value) && IsInitialized) { QuantidadeLitros_IsEdited = true; OnPropertyChanged(nameof(QuantidadeLitros_IsEdited)); } } }
        public bool QuantidadeLitros_IsEdited { get; private set; }
        public double? QuantidadeEstimadaLitros { get => _quantidadeEstimadaLitros; set { if (SetField(ref _quantidadeEstimadaLitros, value) && IsInitialized) { QuantidadeEstimadaLitros_IsEdited = true; OnPropertyChanged(nameof(QuantidadeEstimadaLitros_IsEdited)); } } }
        public bool QuantidadeEstimadaLitros_IsEdited { get; private set; }
        public string? EspecieCombustivel { get => _especieCombustivel; set { if (SetField(ref _especieCombustivel, value) && IsInitialized) { EspecieCombustivel_IsEdited = true; OnPropertyChanged(nameof(EspecieCombustivel_IsEdited)); } } }
        public bool EspecieCombustivel_IsEdited { get; private set; }

        public double? ValorUnitario { get => _valorUnitario; set { if (SetField(ref _valorUnitario, value) && IsInitialized) { ValorUnitario_IsEdited = true; OnPropertyChanged(nameof(ValorUnitario_IsEdited)); } } }
        public bool ValorUnitario_IsEdited { get; private set; }
        public double? ValorTotalCombustivel { get => _valorTotalCombustivel; set { if (SetField(ref _valorTotalCombustivel, value) && IsInitialized) { ValorTotalCombustivel_IsEdited = true; OnPropertyChanged(nameof(ValorTotalCombustivel_IsEdited)); } } }
        public bool ValorTotalCombustivel_IsEdited { get; private set; }

        public double? AliquotaCredito { get; set; }
        public double? ValorCredito { get => _valorCredito; set { if (SetField(ref _valorCredito, value) && IsInitialized) { ValorCredito_IsEdited = true; OnPropertyChanged(nameof(ValorCredito_IsEdited)); } } }
        public bool ValorCredito_IsEdited { get; private set; }

        public string? NFeAquisicaoNumero { get => _nFeAquisicaoNumero; set { if (SetField(ref _nFeAquisicaoNumero, value) && IsInitialized) { NFeAquisicaoNumero_IsEdited = true; OnPropertyChanged(nameof(NFeAquisicaoNumero_IsEdited)); } } }
        public bool NFeAquisicaoNumero_IsEdited { get; private set; }
        public System.DateTime? DataAquisicao { get => _dataAquisicao; set { if (SetField(ref _dataAquisicao, value) && IsInitialized) { DataAquisicao_IsEdited = true; OnPropertyChanged(nameof(DataAquisicao_IsEdited)); } } }
        public bool DataAquisicao_IsEdited { get; private set; }

        public string? ChaveNFe { get; set; }
        public string? UFEmit { get; set; }
        public string? UFDest { get; set; }
        public string? CidadeEmit { get; set; }
        public string? CidadeDest { get; set; }

        public string? Cst { get; set; }
        public string? Cfop { get; set; }
        public decimal? ValorIcms { get; set; }
        public decimal? BaseIcms { get; set; }
        public decimal? TotalDocumento { get; set; }

        public string? Street { get; set; }
        public string? Number { get; set; }
        public string? Neighborhood { get; set; }

        public string? FornecedorCnpj { get; set; }
        public string? FornecedorNome { get; set; }
        public string? FornecedorEndereco { get; set; }
        public string? MapPath { get; set; }
        public string? Vinculo { get => _vinculo; set { if (SetField(ref _vinculo, value) && IsInitialized) { Vinculo_IsEdited = true; OnPropertyChanged(nameof(Vinculo_IsEdited)); } } }
        public bool Vinculo_IsEdited { get; private set; }

        public List<WaypointInfo> Waypoints { get; set; } = new List<WaypointInfo>();

        public bool IsComplete => Vinculo == "Sim" && DistanciaPercorridaKm.HasValue;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}