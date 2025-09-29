using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsvIntegratorApp.Models;

namespace CsvIntegratorApp.Services
{
    public static class MergeService
    {
        /// <summary>
        /// Constrói linhas a partir do MDF-e e enriquece com itens da NFe (litros/valores/placa).
        /// Distância = soma das pernas por API (OSRM): Origem(MDF-e) -> Destino(MDF-e) [-> Origem].
        /// </summary>
        /// <param name="nfeItems">Pode ser null/vazio. Enriquecimento por chave da NF-e.</param>
        /// <param name="mdfe">Parser do MDF-e (obrigatório).</param>
        /// <param name="somarRetornoParaOrigem">Se true, adiciona a perna Destino->Origem ao total.</param>
        public static async Task<List<ModelRow>> MergeAsync(
            List<NfeParsedItem>? nfeItems,
            MdfeParsed mdfe,
            bool somarRetornoParaOrigem = true)
        {
            var rows = new List<ModelRow>();
            var h = mdfe.Header;

            // Origem textual (Cidade, UF) só do MDF-e
            var origemCidade = h.OrigemCidade ?? h.EmitCidade;  // ITAPORA
            var origemUF = h.UFIni ?? h.EmitUF;              // MS
            var origemStr = (!string.IsNullOrWhiteSpace(origemCidade) && !string.IsNullOrWhiteSpace(origemUF))
                               ? $"{ToTitle(origemCidade)}, {origemUF}"
                               : h.UFIni ?? h.UFFim;

            // Índice NFe por chave (para enriquecer litros/valores/placa)
            var porChave = (nfeItems ?? new List<NfeParsedItem>())
                .GroupBy(x => x.ChaveNFe ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Se o MDF-e não trouxe mapeamentos por chNFe, ainda assim criamos uma linha com o 1º destino
            if (mdfe.DestinosPorChave.Count == 0)
            {
                var r = BaseFromMdfe(h);
                // destino = primeiro infMunDescarga (se houver), senão UFFim
                var (destCidadeFallback, destUfFallback) = PrimeiroDestinoDoMdfe(mdfe) ?? (null, h.UFFim);
                var destinoStr = MontaCidadeUf(destCidadeFallback, destUfFallback);

                // Opcional: se vier ao menos uma NFe (combustível), use para litros/valores
                if (porChave.Count > 0)
                {
                    var n = porChave.Values.First();
                    EnriquecerComNfe(r, n);
                }

                // Alíquota/Crédito
                r.AliquotaCredito = GetAliquota(r.UFEmit, r.UFDest, h.UFIni, h.UFFim);
                r.ValorCredito = CalcCredito(r.ValorTotalCombustivel, r.AliquotaCredito);

                // Distância / Roteiro
                await PreencherDistanciaERoteiroAsync(r, origemStr, destinoStr, somarRetornoParaOrigem);

                rows.Add(r);
                return rows;
            }

            // Caminho principal: uma linha por chNFe do MDF-e (destino certo)
            foreach (var kv in mdfe.DestinosPorChave)
            {
                var chave = kv.Key;
                var (destCidadeMdfe, destUfMdfe, _) = kv.Value;

                // Destino SEMPRE do MDF-e
                var destinoStr = MontaCidadeUf(destCidadeMdfe, destUfMdfe) ?? h.UFFim;

                var r = BaseFromMdfe(h);
                r.ChaveNFe = chave;
                r.UFDest = destUfMdfe;
                r.CidadeDest = destCidadeMdfe;

                // Enriquecer com a NF-e somente em litros/valores/placa
                if (!string.IsNullOrWhiteSpace(chave) && porChave.TryGetValue(chave, out var n))
                {
                    EnriquecerComNfe(r, n);
                }
                else
                {
                    // Tenta enriquecer por "placa" e proximidade de data (caso a nota de combustível não esteja no MDF-e)
                    var nGuess = TentarMatchPorPlacaEData(porChave.Values, r.Placa, r.Data);
                    if (nGuess != null)
                        EnriquecerComNfe(r, nGuess);
                }

                // Nota de aquisição = própria NF-e (se houver)
                r.NFeAquisicaoNumero = r.NFeNumero;
                r.DataAquisicao = r.DataEmissao;

                // Alíquota/Crédito
                r.AliquotaCredito = GetAliquota(r.UFEmit, r.UFDest, h.UFIni, h.UFFim);
                r.ValorCredito = CalcCredito(r.ValorTotalCombustivel, r.AliquotaCredito);

                // Distância / Roteiro: MDF-e only
                await PreencherDistanciaERoteiroAsync(r, origemStr, destinoStr, somarRetornoParaOrigem);

                rows.Add(r);
            }

            return rows;
        }

        // =========================================================
        // Helpers de enriquecimento, normalização e cálculo de rota
        // =========================================================

        private static ModelRow BaseFromMdfe(MdfeHeader h)
        {
            return new ModelRow
            {
                // Veículo (MDF-e)
                Modelo = null, // MDF-e não traz "modelo" textual
                Tipo = MapTipo(h.TpRod, h.TpCar),
                Renavam = h.Renavam,
                Placa = h.Placa,

                // Trajeto base
                MdfeNumero = h.NumeroMdf,
                Data = h.DhIniViagem ?? h.DhEmi,

                // Apoio (UF/Cidade do emitente podem ajudar na alíquota)
                UFEmit = h.EmitUF,
                CidadeEmit = ToTitle(h.EmitCidade)
            };
        }

        private static void EnriquecerComNfe(ModelRow r, NfeParsedItem n)
        {
            r.NFeNumero = n.NumeroNFe;
            r.DataEmissao = n.DataEmissao;
            r.QuantidadeLitros = n.Quantidade;
            r.EspecieCombustivel = n.DescANP ?? n.DescricaoProduto;
            r.ValorUnitario = n.ValorUnitario;
            r.ValorTotalCombustivel = n.ValorTotal;

            // UF/Cidade da NFe NÃO dirigem a rota, mas ajudam na alíquota
            r.UFEmit = n.UFEmit ?? r.UFEmit;
            r.UFDest = r.UFDest ?? n.UFDest;      // só para decisão de alíquota
            r.CidadeEmit = r.CidadeEmit ?? ToTitle(n.CidadeEmit);
            r.CidadeDest = r.CidadeDest ?? ToTitle(n.CidadeDest);

            // Placa (caso MDF-e não traga)
            if (string.IsNullOrWhiteSpace(r.Placa) && !string.IsNullOrWhiteSpace(n.PlacaObservada))
                r.Placa = n.PlacaObservada;
        }

        private static NfeParsedItem? TentarMatchPorPlacaEData(IEnumerable<NfeParsedItem> nfe, string? placa, DateTime? dataRef)
        {
            if (string.IsNullOrWhiteSpace(placa) || !dataRef.HasValue) return null;
            // janela de ±3 dias
            var min = dataRef.Value.AddDays(-3);
            var max = dataRef.Value.AddDays(3);

            return nfe
                .Where(x => (x.PlacaObservada ?? "").Equals(placa, StringComparison.OrdinalIgnoreCase)
                         && x.DataEmissao.HasValue
                         && x.DataEmissao.Value >= min && x.DataEmissao.Value <= max)
                .OrderBy(x => Math.Abs((x.DataEmissao!.Value - dataRef.Value).TotalHours))
                .FirstOrDefault();
        }

        private static async Task PreencherDistanciaERoteiroAsync(
            ModelRow r,
            string? origemStr,
            string? destinoStr,
            bool somarRetornoParaOrigem)
        {
            if (string.IsNullOrWhiteSpace(origemStr) || string.IsNullOrWhiteSpace(destinoStr))
                return;

            var origem = origemStr!;
            var destino = destinoStr!;

            // Evitar Itaporã->Itaporã por erro de fonte (TXT/NFe)
            if (IsSamePlace(origem, destino))
            {
                // nesse caso não faz sentido somar retorno (seria 0)
                somarRetornoParaOrigem = false;
            }

            var pontos = new List<string> { origem, destino };
            if (somarRetornoParaOrigem)
                pontos.Add(origem);

            var route = await DistanceService.TryRouteLegsKmAsync(pontos, closeLoop: false);

            r.DistanciaPercorridaKm = route.TotalKm;
            r.Roteiro = string.Join(" / ", pontos.Select(p => p.ToUpper()));
        }

        /// <summary>
        /// 17% intraestadual; 7% interestadual. Usa UF da NFe se houver, senão UFIni/UFFim do MDF-e.
        /// </summary>
        private static double? GetAliquota(string? ufEmit, string? ufDest, string? ufIni, string? ufFim)
        {
            var a = ufEmit ?? ufIni;
            var b = ufDest ?? ufFim;
            if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ? 0.17 : 0.07;
            return 0.17;
        }

        private static double? CalcCredito(double? total, double? aliq)
            => (total.HasValue && aliq.HasValue) ? Math.Round(total.Value * aliq.Value, 2) : (double?)null;

        private static string? MapTipo(string? tpRod, string? tpCar)
        {
            string Rod(string? c) => c switch
            {
                "01" => "Ciclomotor",
                "02" => "Motocicleta",
                "03" => "Motoneta",
                "04" => "Quadriciclo",
                "05" => "Automóvel",
                "06" => "Caminhão Trator",
                "07" => "Caminhão",
                "08" => "Utilitário",
                _ => c ?? "-"
            };
            string Car(string? c) => c switch
            {
                "00" => "Não Aplicável",
                "01" => "Aberta",
                "02" => "Fechada/Baú",
                "03" => "Graneleiro",
                "04" => "Porta-Contêiner",
                "05" => "Sider",
                _ => c ?? "-"
            };
            if (string.IsNullOrWhiteSpace(tpRod) && string.IsNullOrWhiteSpace(tpCar)) return null;
            return $"Rodovia: {Rod(tpRod)} / Carroceria: {Car(tpCar)}";
        }

        private static (string? xMun, string? uf)? PrimeiroDestinoDoMdfe(MdfeParsed mdfe)
        {
            // pega o 1º item da tabela de destinos por chave
            if (mdfe.DestinosPorChave.Count == 0) return null;
            var first = mdfe.DestinosPorChave.First().Value;
            return (first.Cidade, first.UF);
        }

        private static string? MontaCidadeUf(string? cidade, string? uf)
        {
            if (!string.IsNullOrWhiteSpace(cidade) && !string.IsNullOrWhiteSpace(uf))
                return $"{ToTitle(cidade)}, {uf}";
            if (!string.IsNullOrWhiteSpace(uf))
                return uf;
            return null;
        }

        // ===== normalização de nomes (ITAPORA vs ITAPORÃ) =====

        private static bool IsSamePlace(string a, string b)
            => RemoveDiacritics(a).Trim().ToUpperInvariant()
             == RemoveDiacritics(b).Trim().ToUpperInvariant();

        private static string ToTitle(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s ?? "";
            s = s.ToLowerInvariant();
            var ti = CultureInfo.GetCultureInfo("pt-BR").TextInfo;
            return ti.ToTitleCase(s);
        }

        private static string RemoveDiacritics(string s)
        {
            var norm = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in norm)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
