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
            CalculationLogService.Clear();
            CalculationLogService.Log("Iniciando processo de merge e cálculo de rota.");

            var h = mdfe.Header;

            // Origem textual (Cidade, UF) só do MDF-e
            var origemCidade = h.OrigemCidade ?? h.EmitCidade;
            var origemUF = h.UFIni ?? h.EmitUF;
            var origemStr = (!string.IsNullOrWhiteSpace(origemCidade) && !string.IsNullOrWhiteSpace(origemUF))
                               ? $"{ToTitle(origemCidade)}, {origemUF}"
                               : h.UFIni ?? h.UFFim;

            if (string.IsNullOrWhiteSpace(origemStr))
            {
                CalculationLogService.Log("ERRO: Origem da viagem não pôde ser determinada a partir do MDF-e.");
                CalculationLogService.Save();
                return new List<ModelRow>();
            }
            CalculationLogService.Log($"Origem definida como: {origemStr}");

            // Índice NFe por chave (para enriquecer litros/valores/placa)
            var porChave = (nfeItems ?? new List<NfeParsedItem>())
                .GroupBy(x => x.ChaveNFe ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var allChaves = mdfe.DestinosPorChave.Keys.ToList();
            var destinationPoints = new List<string>();

            CalculationLogService.Log("Coletando pontos de destino...");
            // Coleta todos os pontos de destino únicos
            foreach (var kv in mdfe.DestinosPorChave)
            {
                var chave = kv.Key;
                var (destCidadeMdfe, destUfMdfe, _) = kv.Value;

                string destinoStr;
                if (SpedTxtLookupService.TryGetAddressInfoPorChave(chave, out var addrInfo))
                {
                    var addressParts = new[] { addrInfo.street, addrInfo.number, destCidadeMdfe, addrInfo.uf ?? destUfMdfe };
                    destinoStr = string.Join(", ", addressParts.Where(s => !string.IsNullOrWhiteSpace(s)));
                    CalculationLogService.Log($"Destino para NFe {chave} encontrado no SPED: {destinoStr}");
                }
                else
                {
                    destinoStr = MontaCidadeUf(destCidadeMdfe, destUfMdfe) ?? h.UFFim ?? "";
                    CalculationLogService.Log($"Destino para NFe {chave} usando dados do MDF-e: {destinoStr}");
                }
                destinationPoints.Add(destinoStr);
            }

            // Se não houver destinos no MDF-e, usa o fallback
            if (destinationPoints.Count == 0)
            {
                var (destCidadeFallback, destUfFallback) = PrimeiroDestinoDoMdfe(mdfe) ?? (null, h.UFFim);
                var destinoStr = MontaCidadeUf(destCidadeFallback, destUfFallback);
                if (!string.IsNullOrWhiteSpace(destinoStr))
                {
                    destinationPoints.Add(destinoStr);
                    CalculationLogService.Log($"Nenhum destino no MDF-e. Usando fallback: {destinoStr}");
                }
            }

            var finalRow = BaseFromMdfe(h);

            // Agrega dados de todas as NF-e de combustível associadas
            double totalLitros = 0;
            double totalValorCombustivel = 0;
            var combustivelNFe = porChave.Values.Where(n => n.IsCombustivel).ToList();
            
            if (combustivelNFe.Any())
            {
                var nfePrincipal = TentarMatchPorPlacaEData(combustivelNFe, h.Placa, h.DhIniViagem ?? h.DhEmi) ?? combustivelNFe.First();
                EnriquecerComNfe(finalRow, nfePrincipal);
                
                totalLitros = combustivelNFe.Sum(n => n.Quantidade ?? 0);
                totalValorCombustivel = combustivelNFe.Sum(n => n.ValorTotal ?? 0);

                finalRow.QuantidadeLitros = totalLitros > 0 ? totalLitros : null;
                finalRow.ValorTotalCombustivel = totalValorCombustivel > 0 ? totalValorCombustivel : null;
                CalculationLogService.Log($"Encontradas {combustivelNFe.Count} NF-e de combustível. Total: {totalLitros} L, R$ {totalValorCombustivel}.");
            }

            // Constrói a rota completa
            var waypoints = new List<string> { origemStr };
            waypoints.AddRange(destinationPoints.Distinct(StringComparer.OrdinalIgnoreCase));

            if (somarRetornoParaOrigem && waypoints.Count > 1)
            {
                waypoints.Add(origemStr);
            }

            CalculationLogService.Log($"Enviando {waypoints.Count} pontos para cálculo de rota: {string.Join(" | ", waypoints)}");
            var routeResult = await DistanceService.TryRouteLegsKmAsync(waypoints, closeLoop: false);
            CalculationLogService.Log($"Resultado da API: Distancia={routeResult.TotalKm}km, Usado='{routeResult.Used}', Erro='{routeResult.Error}'");

            RouteLogService.GenerateRouteMap(routeResult.Coordinates);

            if (routeResult.TotalKm.HasValue && routeResult.Used == "OSRM")
            {
                finalRow.DistanciaPercorridaKm = routeResult.TotalKm;
                finalRow.Roteiro = "Rota Calculada com Sucesso";
                CalculationLogService.Log("Rota calculada com sucesso.");
            }
            else
            {
                finalRow.DistanciaPercorridaKm = null;
                finalRow.Roteiro = $"Falha no cálculo da rota: {routeResult.Error}";
                CalculationLogService.Log($"Falha no cálculo da rota. Motivo: {routeResult.Error}");
            }

            finalRow.ChaveNFe = string.Join(", ", allChaves.Distinct());
            finalRow.UFDest = h.UFFim;
            finalRow.CidadeDest = waypoints.LastOrDefault()?.Split(',')[0].Trim();

            finalRow.AliquotaCredito = GetAliquota(finalRow.UFEmit, finalRow.UFDest, h.UFIni, h.UFFim);
            finalRow.ValorCredito = CalcCredito(finalRow.ValorTotalCombustivel, finalRow.AliquotaCredito);

            CalculationLogService.Log("Processo finalizado.");
            CalculationLogService.Save();
            return new List<ModelRow> { finalRow };
        }

        // =========================================================
        // Helpers de enriquecimento, normalização e cálculo de rota
        // =========================================================

        private static ModelRow BaseFromMdfe(MdfeHeader h)
        {
            // Tenta buscar o tipo do veículo no nosso "banco de dados" local primeiro
            var tipoVeiculo = VehicleService.GetVehicleType(h.Placa, h.Renavam) 
                              ?? MapTipo(h.TpRod, h.TpCar); // Fallback para o mapeamento padrão

            return new ModelRow
            {
                // Veículo (MDF-e)
                Modelo = null, // MDF-e não traz "modelo" textual
                Tipo = tipoVeiculo,
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
