using CsvIntegratorApp.Models.DTO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsvIntegratorApp.Services
{
    public static class DieselTotalsService
    {
        public static List<DieselTotalDto> BuildDieselTotals(IEnumerable<NfeParsedItem> nfeItems)
        {
            var diesel = nfeItems.Where(FuelAllocator.IsDieselItem);

            return diesel
                .GroupBy(i => i.ChaveNFe ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var litros = g.Sum(i => i.Quantidade ?? 0.0);
                    var valor = g.Sum(i => (i.ValorUnitario ?? 0.0) * (i.Quantidade ?? 0.0));
                    double? vMed = litros > 0 ? valor / litros : (double?)null;

                    // pega o item mais “recente” só pra preencher número/data
                    var head = g.OrderByDescending(i => i.DataEmissao ?? DateTime.MinValue).FirstOrDefault();

                    return new DieselTotalDto(
                        ChaveNFe: head?.ChaveNFe ?? g.Key,
                        NumeroNFe: head?.NumeroNFe,
                        DataEmissao: head?.DataEmissao,
                        LitrosDiesel: Math.Round(litros, 6),
                        ValorTotalDiesel: Math.Round(valor, 2),
                        ValorUnitMedio: vMed,
                        ItensDiesel: g.Count()
                    );
                })
                .OrderBy(d => d.DataEmissao ?? DateTime.MinValue)
                .ToList();
        }

        // (Opcional) CSV simples pra exportar a “Aba Nota de Aquisição (Combustível)”
        public static string ToCsv(IEnumerable<DieselTotalDto> totais)
        {
            var sb = new StringBuilder();
            var pt = CultureInfo.GetCultureInfo("pt-BR");
            sb.AppendLine("Chave;Numero;DataEmissao;LitrosDiesel;ValorTotalDiesel;ValorUnitMedio;ItensDiesel");

            foreach (var t in totais)
            {
                sb.Append(t.ChaveNFe).Append(';')
                  .Append(t.NumeroNFe).Append(';')
                  .Append(t.DataEmissao?.ToString("dd/MM/yyyy") ?? "").Append(';')
                  .Append(t.LitrosDiesel.ToString("N6", pt)).Append(';')
                  .Append(t.ValorTotalDiesel.ToString("N2", pt)).Append(';')
                  .Append((t.ValorUnitMedio ?? 0).ToString("N4", pt)).Append(';')
                  .Append(t.ItensDiesel.ToString(pt)).AppendLine();
            }
            return sb.ToString();
        }
    }
}
