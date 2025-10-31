namespace CsvIntegratorApp.Models
{
    /// <summary>
    /// Represents a single row of processed data, combining information from MDF-e, NF-e, and SPED files.
    /// Implements <see cref="INotifyPropertyChanged"/> to support UI updates.
    /// </summary>
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

        /// <summary>
        /// Gets or sets a value indicating whether the row has been initialized.
        /// </summary>
        public bool IsInitialized { get; set; }

        /// <summary>
        /// Gets or sets the vehicle model.
        /// </summary>
        public string? Modelo { get => _modelo; set { if (SetField(ref _modelo, value) && IsInitialized) { Modelo_IsEdited = true; OnPropertyChanged(nameof(Modelo_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="Modelo"/> property has been edited.
        /// </summary>
        public bool Modelo_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the vehicle type.
        /// </summary>
        public string? Tipo { get => _tipo; set { if (SetField(ref _tipo, value) && IsInitialized) { Tipo_IsEdited = true; OnPropertyChanged(nameof(Tipo_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="Tipo"/> property has been edited.
        /// </summary>
        public bool Tipo_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the Renavam (National Register of Motor Vehicles) code of the vehicle.
        /// </summary>
        public string? Renavam { get => _renavam; set { if (SetField(ref _renavam, value) && IsInitialized) { Renavam_IsEdited = true; OnPropertyChanged(nameof(Renavam_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="Renavam"/> property has been edited.
        /// </summary>
        public bool Renavam_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the license plate of the vehicle.
        /// </summary>
        public string? Placa { get => _placa; set { if (SetField(ref _placa, value) && IsInitialized) { Placa_IsEdited = true; OnPropertyChanged(nameof(Placa_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="Placa"/> property has been edited.
        /// </summary>
        public bool Placa_IsEdited { get; private set; }

        /// <summary>
        /// Gets or sets the MDF-e (Manifesto Eletrônico de Documentos Fiscais) number.
        /// </summary>
        public string? MdfeNumero { get => _mdfeNumero; set { if (SetField(ref _mdfeNumero, value) && IsInitialized) { MdfeNumero_IsEdited = true; OnPropertyChanged(nameof(MdfeNumero_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="MdfeNumero"/> property has been edited.
        /// </summary>
        public bool MdfeNumero_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the date associated with the MDF-e (e.g., start of travel).
        /// </summary>
        public System.DateTime? Data { get => _data; set { if (SetField(ref _data, value) && IsInitialized) { Data_IsEdited = true; OnPropertyChanged(nameof(Data_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="Data"/> property has been edited.
        /// </summary>
        public bool Data_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the route description.
        /// </summary>
        public string? Roteiro { get; set; }
        /// <summary>
        /// Gets or sets the distance traveled in kilometers.
        /// </summary>
        public double? DistanciaPercorridaKm { get => _distanciaPercorridaKm; set { if (SetField(ref _distanciaPercorridaKm, value) && IsInitialized) { DistanciaPercorridaKm_IsEdited = true; OnPropertyChanged(nameof(DistanciaPercorridaKm_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="DistanciaPercorridaKm"/> property has been edited.
        /// </summary>
        public bool DistanciaPercorridaKm_IsEdited { get; private set; }

        /// <summary>
        /// Gets or sets the NF-e (Nota Fiscal Eletrônica) number of the cargo.
        /// </summary>
        public string? NFeNumero { get => _nFeNumero; set { if (SetField(ref _nFeNumero, value) && IsInitialized) { NFeNumero_IsEdited = true; OnPropertyChanged(nameof(NFeNumero_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="NFeNumero"/> property has been edited.
        /// </summary>
        public bool NFeNumero_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the emission date of the NF-e (cargo).
        /// </summary>
        public System.DateTime? DataEmissao { get => _dataEmissao; set { if (SetField(ref _dataEmissao, value) && IsInitialized) { DataEmissao_IsEdited = true; OnPropertyChanged(nameof(DataEmissao_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="DataEmissao"/> property has been edited.
        /// </summary>
        public bool DataEmissao_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the NF-e (Nota Fiscal Eletrônica) number(s) associated with the cargo.
        /// </summary>
        public string? NFeCargaNumero { get => _nFeCargaNumero; set { if (SetField(ref _nFeCargaNumero, value) && IsInitialized) { NFeCargaNumero_IsEdited = true; OnPropertyChanged(nameof(NFeCargaNumero_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="NFeCargaNumero"/> property has been edited.
        /// </summary>
        public bool NFeCargaNumero_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the emission date of the cargo NF-e as recorded in SPED.
        /// </summary>
        public string? DataEmissaoCarga { get => _dataEmissaoCarga; set { if (SetField(ref _dataEmissaoCarga, value) && IsInitialized) { DataEmissaoCarga_IsEdited = true; OnPropertyChanged(nameof(DataEmissaoCarga_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="DataEmissaoCarga"/> property has been edited.
        /// </summary>
        public bool DataEmissaoCarga_IsEdited { get; private set; }

        /// <summary>
        /// Gets or sets the quantity of liters (e.g., fuel).
        /// </summary>
        public double? QuantidadeLitros { get => _quantidadeLitros; set { if (SetField(ref _quantidadeLitros, value) && IsInitialized) { QuantidadeLitros_IsEdited = true; OnPropertyChanged(nameof(QuantidadeLitros_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="QuantidadeLitros"/> property has been edited.
        /// </summary>
        public bool QuantidadeLitros_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the estimated quantity of liters (e.g., fuel).
        /// </summary>
        public double? QuantidadeEstimadaLitros { get => _quantidadeEstimadaLitros; set { if (SetField(ref _quantidadeEstimadaLitros, value) && IsInitialized) { QuantidadeEstimadaLitros_IsEdited = true; OnPropertyChanged(nameof(QuantidadeEstimadaLitros_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="QuantidadeEstimadaLitros"/> property has been edited.
        /// </summary>
        public bool QuantidadeEstimadaLitros_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the type of fuel.
        /// </summary>
        public string? EspecieCombustivel { get => _especieCombustivel; set { if (SetField(ref _especieCombustivel, value) && IsInitialized) { EspecieCombustivel_IsEdited = true; OnPropertyChanged(nameof(EspecieCombustivel_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="EspecieCombustivel"/> property has been edited.
        /// </summary>
        public bool EspecieCombustivel_IsEdited { get; private set; }

        /// <summary>
        /// Gets or sets the unit value (e.g., price per liter).
        /// </summary>
        public double? ValorUnitario { get => _valorUnitario; set { if (SetField(ref _valorUnitario, value) && IsInitialized) { ValorUnitario_IsEdited = true; OnPropertyChanged(nameof(ValorUnitario_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="ValorUnitario"/> property has been edited.
        /// </summary>
        public bool ValorUnitario_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the total value of the fuel.
        /// </summary>
        public double? ValorTotalCombustivel { get => _valorTotalCombustivel; set { if (SetField(ref _valorTotalCombustivel, value) && IsInitialized) { ValorTotalCombustivel_IsEdited = true; OnPropertyChanged(nameof(ValorTotalCombustivel_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="ValorTotalCombustivel"/> property has been edited.
        /// </summary>
        public bool ValorTotalCombustivel_IsEdited { get; private set; }

        /// <summary>
        /// Gets or sets the credit aliquot (e.g., ICMS).
        /// </summary>
        public double? AliquotaCredito { get; set; }
        /// <summary>
        /// Gets or sets the credit value to be appropriated.
        /// </summary>
        public double? ValorCredito { get => _valorCredito; set { if (SetField(ref _valorCredito, value) && IsInitialized) { ValorCredito_IsEdited = true; OnPropertyChanged(nameof(ValorCredito_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="ValorCredito"/> property has been edited.
        /// </summary>
        public bool ValorCredito_IsEdited { get; private set; }

        /// <summary>
        /// Gets or sets the NF-e number of the fuel acquisition.
        /// </summary>
        public string? NFeAquisicaoNumero { get => _nFeAquisicaoNumero; set { if (SetField(ref _nFeAquisicaoNumero, value) && IsInitialized) { NFeAquisicaoNumero_IsEdited = true; OnPropertyChanged(nameof(NFeAquisicaoNumero_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="NFeAquisicaoNumero"/> property has been edited.
        /// </summary>
        public bool NFeAquisicaoNumero_IsEdited { get; private set; }
        /// <summary>
        /// Gets or sets the acquisition date of the fuel.
        /// </summary>
        public System.DateTime? DataAquisicao { get => _dataAquisicao; set { if (SetField(ref _dataAquisicao, value) && IsInitialized) { DataAquisicao_IsEdited = true; OnPropertyChanged(nameof(DataAquisicao_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="DataAquisicao"/> property has been edited.
        /// </summary>
        public bool DataAquisicao_IsEdited { get; private set; }

        /// <summary>
        /// Gets or sets the full NF-e access key.
        /// </summary>
        public string? ChaveNFe { get; set; }
        /// <summary>
        /// Gets or sets the UF (Unidade Federativa) of the emitter.
        /// </summary>
        public string? UFEmit { get; set; }
        /// <summary>
        /// Gets or sets the UF (Unidade Federativa) of the destination.
        /// </summary>
        public string? UFDest { get; set; }
        /// <summary>
        /// Gets or sets the city of the emitter.
        /// </summary>
        public string? CidadeEmit { get; set; }
        /// <summary>
        /// Gets or sets the city of the destination.
        /// </summary>
        public string? CidadeDest { get; set; }

        /// <summary>
        /// Gets or sets the CST (Código de Situação Tributária).
        /// </summary>
        public string? Cst { get; set; }
        /// <summary>
        /// Gets or sets the CFOP (Código Fiscal de Operações e Prestações).
        /// </summary>
        public string? Cfop { get; set; }
        /// <summary>
        /// Gets or sets the ICMS value.
        /// </summary>
        public decimal? ValorIcms { get; set; }
        /// <summary>
        /// Gets or sets the ICMS base value.
        /// </summary>
        public decimal? BaseIcms { get; set; }
        /// <summary>
        /// Gets or sets the total document value.
        /// </summary>
        public decimal? TotalDocumento { get; set; }

        /// <summary>
        /// Gets or sets the street name.
        /// </summary>
        public string? Street { get; set; }
        /// <summary>
        /// Gets or sets the street number.
        /// </summary>
        public string? Number { get; set; }
        /// <summary>
        /// Gets or sets the neighborhood.
        /// </summary>
        public string? Neighborhood { get; set; }

        /// <summary>
        /// Gets or sets the CNPJ of the supplier.
        /// </summary>
        public string? FornecedorCnpj { get; set; }
        /// <summary>
        /// Gets or sets the name of the supplier.
        /// </summary>
        public string? FornecedorNome { get; set; }
        /// <summary>
        /// Gets or sets the address of the supplier.
        /// </summary>
        public string? FornecedorEndereco { get; set; }
        /// <summary>
        /// Gets or sets the path to the generated map file.
        /// </summary>
        public string? MapPath { get; set; }
        /// <summary>
        /// Gets or sets the linkage status (e.g., "Sim" for linked, "Não" for not linked).
        /// </summary>
        public string? Vinculo { get => _vinculo; set { if (SetField(ref _vinculo, value) && IsInitialized) { Vinculo_IsEdited = true; OnPropertyChanged(nameof(Vinculo_IsEdited)); } } }
        /// <summary>
        /// Gets a value indicating whether the <see cref="Vinculo"/> property has been edited.
        /// </summary>
        public bool Vinculo_IsEdited { get; private set; }

        /// <summary>
        /// Gets or sets the list of waypoints for the route.
        /// </summary>
        public List<WaypointInfo> Waypoints { get; set; } = new List<WaypointInfo>();

        /// <summary>
        /// Gets a value indicating whether the current row is considered complete.
        /// A row is complete if it has a "Sim" Vinculo and a valid DistanciaPercorridaKm.
        /// </summary>
        public bool IsComplete => Vinculo == "Sim" && DistanciaPercorridaKm.HasValue;

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Sets the field and raises the <see cref="PropertyChanged"/> event if the value has changed.
        /// </summary>
        /// <typeparam name="T">The type of the field.</typeparam>
        /// <param name="field">The field to set.</param>
        /// <param name="value">The new value.</param>
        /// <param name="propertyName">The name of the property that changed.</param>
        /// <returns><c>true</c> if the value was changed, <c>false</c> otherwise.</returns>
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}