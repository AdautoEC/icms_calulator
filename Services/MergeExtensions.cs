// Services/MergeExtensions.cs
using System;

namespace CsvIntegratorApp.Services
{
    public static class MergeExtensions
    {
        /// <summary>
        /// Enriches a ModelRow with a partial allocation from an NFe item.
        /// Quantities and monetary values are proportional to the allocated liters.
        /// </summary>
        public static void EnriquecerComNfeParcial(CsvIntegratorApp.Models.ModelRow r, CsvIntegratorApp.Services.NfeParsedItem n, double litrosAlocados)
        {
            // Proporção (evitar divisão por zero)
            var qtdTotal = n.Quantidade ?? 0.0;
            var proporcao = qtdTotal > 0 ? litrosAlocados / qtdTotal : 0.0;

            r.QuantidadeLitros = litrosAlocados;
            r.EspecieCombustivel = n.DescricaoProduto ?? $"sem descrição:<{n.CodigoProduto}>";

            r.ValorUnitario = n.ValorUnitario;
            r.ValorTotalCombustivel = (n.ValorUnitario ?? 0.0) * litrosAlocados;

            r.AliquotaCredito = n.Aliquota;
            r.ValorCredito = (n.Credito ?? 0.0) * proporcao;

            r.NFeAquisicaoNumero = n.NumeroNFe;
            r.DataAquisicao = n.DataEmissao?.ToString("dd/MM/yyyy");
        }
    }
}