// Models/ModelRow.cs
namespace CsvIntegratorApp.Models
{
    public class ModelRow
    {
        // Veículo Utilizado (Art. 62-B § 3º I)
        public string? Modelo { get; set; }      // editável; MDFe não traz "modelo"
        public string? Tipo { get; set; }        // mapeado de tpRod/tpCar
        public string? Renavam { get; set; }
        public string? Placa { get; set; }

        // Trajeto (Art. 62-B § 3º II)
        public string? MdfeNumero { get; set; }
        public System.DateTime? Data { get; set; }        // dhIniViagem (fallback dhEmi)
        public string? Roteiro { get; set; }              // ORIGEM / DESTINO [/ ORIGEM]
        public double? DistanciaPercorridaKm { get; set; }

        // Carga / NF-e
        public string? NFeNumero { get; set; }
        public System.DateTime? DataEmissao { get; set; }

        // Combustível
        public double? QuantidadeLitros { get; set; }
        public string? EspecieCombustivel { get; set; }   // DescANP/xProd

        // Valores
        public double? ValorUnitario { get; set; }
        public double? ValorTotalCombustivel { get; set; }

        // Crédito a ser apropriado
        public double? AliquotaCredito { get; set; }      // 0.17 intra / 0.07 inter
        public double? ValorCredito { get; set; }         // Total × Alíquota

        // Nota de Aquisição do Combustível
        public string? NFeAquisicaoNumero { get; set; }
        public System.DateTime? DataAquisicao { get; set; }

        // Apoio / auditoria
        public string? ChaveNFe { get; set; }
        public string? UFEmit { get; set; }
        public string? UFDest { get; set; }
        public string? CidadeEmit { get; set; }
        public string? CidadeDest { get; set; }

        // C190
        public string? Cst { get; set; }
        public string? Cfop { get; set; }
        public decimal? ValorIcms { get; set; }
        public decimal? BaseIcms { get; set; }
        public decimal? TotalDocumento { get; set; }

        // Address
        public string? Street { get; set; }
        public string? Number { get; set; }
        public string? Neighborhood { get; set; }

        // Fornecedor
        public string? FornecedorCnpj { get; set; }
        public string? FornecedorNome { get; set; }
        public string? FornecedorEndereco { get; set; }
    }
}
