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
        public static async Task<List<ModelRow>> MergeAsync(
            List<NfeParsedItem>? nfeItems,
            List<MdfeParsed> mdfes,
            bool somarRetornoParaOrigem = true)
        {
            CalculationLogService.Clear();
            CalculationLogService.Log("Iniciando processo de merge e cálculo de rota.");

            var allModelRows = new List<ModelRow>();
            var unmatchedFuelNfes = nfeItems?.Where(n => n.IsCombustivel && (n.DescricaoProduto ?? "").ToUpperInvariant().Contains("DIESEL")).ToList() ?? new List<NfeParsedItem>();
            var porChave = (nfeItems ?? new List<NfeParsedItem>())
                .GroupBy(x => x.ChaveNFe ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var mdfe in mdfes)
            {
                var h = mdfe.Header;

                var origemCidade = h.OrigemCidade ?? h.EmitCidade;
                var origemUF = h.UFIni ?? h.EmitUF;
                var origemStr = (!string.IsNullOrWhiteSpace(origemCidade) && !string.IsNullOrWhiteSpace(origemUF))
                                   ? $"{ToTitle(origemCidade)}, {origemUF}"
                                   : h.UFIni ?? h.UFFim;

                if (string.IsNullOrWhiteSpace(origemStr))
                {
                    CalculationLogService.Log($"ERRO: Origem da viagem para o MDF-e {h.NumeroMdf} não pôde ser determinada.");
                    var mdfeRow = BaseFromMdfe(h);
                    mdfeRow.Vinculo = "Não";
                    allModelRows.Add(mdfeRow); // Add MDFe with empty fuel info
                    continue;
                }

                var waypoints = new List<WaypointInfo> { new WaypointInfo { Address = origemStr, City = ToTitle(origemCidade), InvoiceNumber = "Origem" } };
                foreach (var kv in mdfe.DestinosPorChave)
                {
                    var chave = kv.Key;
                    var (destCidadeMdfe, destUfMdfe, _) = kv.Value;
                    if (SpedTxtLookupService.TryGetAddressInfoPorChave(chave, out var addrInfo))
                    {
                        var addressParts = new[] { addrInfo.street, addrInfo.number, destCidadeMdfe, addrInfo.uf ?? destUfMdfe };
                        var destinoStr = string.Join(", ", addressParts.Where(s => !string.IsNullOrWhiteSpace(s)));
                        waypoints.Add(new WaypointInfo { Address = destinoStr, City = ToTitle(destCidadeMdfe), InvoiceNumber = chave });
                    }
                }

                var routeResult = await DistanceService.TryRouteLegsKmAsync(waypoints, somarRetornoParaOrigem);

                var modelRow = BaseFromMdfe(h);
                modelRow.Waypoints = waypoints;

                if (routeResult.TotalKm.HasValue)
                {
                    modelRow.QuantidadeEstimadaLitros = routeResult.TotalKm.Value / 3.0; // Assumed efficiency
                }

                var bestNfeMatch = TryFindBestNfeMatch(unmatchedFuelNfes, h, routeResult);

                if (bestNfeMatch != null)
                {
                    EnriquecerComNfe(modelRow, bestNfeMatch);
                    unmatchedFuelNfes.Remove(bestNfeMatch);
                    modelRow.Vinculo = "Sim";
                    CalculationLogService.Log($"MDF-e {h.NumeroMdf} vinculado à NF-e {bestNfeMatch.NumeroNFe}.");
                }
                else
                {
                    modelRow.Vinculo = "Não";
                    CalculationLogService.Log($"Nenhuma NF-e de combustível correspondente encontrada para o MDF-e {h.NumeroMdf}.");
                }

                var nfeKeys = mdfe.DestinosPorChave.Keys.Distinct().ToList();
                modelRow.NFeCargaNumero = string.Join(", ", nfeKeys);

                var firstCargoNfeKey = nfeKeys.FirstOrDefault();
                if (firstCargoNfeKey != null)
                {
                    if (porChave.TryGetValue(firstCargoNfeKey, out var cargoNfe))
                    {
                        modelRow.NFeNumero = cargoNfe.NumeroNFe;
                        modelRow.DataEmissao = cargoNfe.DataEmissao;
                    }
                    else
                    {
                        if (firstCargoNfeKey.Length == 44)
                        {
                            try { modelRow.NFeNumero = long.Parse(firstCargoNfeKey.Substring(25, 9)).ToString(); } catch { }
                        }

                        if (SpedTxtLookupService.TryGetC100DataPorChave(firstCargoNfeKey, out var dt))
                        {
                            modelRow.DataEmissao = dt;
                        }
                    }
                }

                modelRow.DataEmissaoCarga = string.Join(", ", nfeKeys.Select(k => SpedTxtLookupService.TryGetC100DataPorChave(k, out var dt) ? dt?.ToString("g") : "").Where(n => !string.IsNullOrEmpty(n)).Distinct());

                modelRow.DistanciaPercorridaKm = routeResult.TotalKm;
                modelRow.Roteiro = routeResult.TotalKm.HasValue ? string.Join(" -> ", waypoints.Select(w => w.City).Where(c => !string.IsNullOrWhiteSpace(c))) : $"Falha no cálculo da rota: {routeResult.Error}";
                modelRow.MapPath = RouteLogService.GenerateRouteMap(routeResult.Polyline, routeResult.Waypoints, new List<ModelRow>());

                allModelRows.Add(modelRow);
            }

            // Add unmatched fuel NFes
            foreach (var nfe in unmatchedFuelNfes)
            {
                var nfeRow = BaseFromNfe(nfe);
                nfeRow.Vinculo = "Não";
                allModelRows.Add(nfeRow);
            }

            CalculationLogService.Log("Processo finalizado.");
            CalculationLogService.Save();
            return allModelRows;
        }

        private static NfeParsedItem? TryFindBestNfeMatch(List<NfeParsedItem> fuelNfes, MdfeHeader mdfeHeader, RouteResult routeResult)
        {
            if (!fuelNfes.Any()) return null;

            var scoredNfes = new List<(NfeParsedItem nfe, int score)>();

            CalculationLogService.Log($"--- Iniciando busca por NF-e para o MDF-e {mdfeHeader.NumeroMdf} ---");

            foreach (var nfe in fuelNfes)
            {
                int score = 0;
                var log = new StringBuilder($"Analisando NF-e {nfe.NumeroNFe}: ");

                // Rule 1: Route passes through NFe generation location
                var nfeLocation = $"{nfe.CidadeEmit}, {nfe.UFEmit}";
                if (routeResult.Waypoints.Any(w => w.Address.Contains(nfe.CidadeEmit ?? "")))
                {
                    score += 10;
                    log.Append("[Local OK] ");
                }

                // Rule 2: Emission dates are close
                if (nfe.DataEmissao.HasValue && mdfeHeader.DhIniViagem.HasValue)
                {
                    var dateDiff = Math.Abs((nfe.DataEmissao.Value - mdfeHeader.DhIniViagem.Value).TotalDays);
                    if (dateDiff <= 1)
                    {
                        score += 20;
                        log.Append("[Data OK (<=1d)] ");
                    }
                    else if (dateDiff <= 3)
                    {
                        score += 10;
                        log.Append("[Data OK (<=3d)] ");
                    }
                }

                // Rule 3: Fuel quantity is close
                if (routeResult.TotalKm.HasValue && nfe.Quantidade.HasValue)
                {
                    var estimatedConsumption = routeResult.TotalKm.Value / 3.0; // Assumed efficiency
                    var quantityDiff = Math.Abs(estimatedConsumption - nfe.Quantidade.Value) / estimatedConsumption;
                    if (quantityDiff <= 0.1)
                    {
                        score += 20;
                        log.Append("[Qtd OK (<=10%)] ");
                    }
                    else if (quantityDiff <= 0.2)
                    {
                        score += 10;
                        log.Append("[Qtd OK (<=20%)] ");
                    }
                }

                // Rule 4: License plate match
                if (!string.IsNullOrWhiteSpace(nfe.PlacaObservada) && nfe.PlacaObservada.Equals(mdfeHeader.Placa, StringComparison.OrdinalIgnoreCase))
                {
                    score += 30;
                    log.Append("[Placa OK] ");
                }

                // Rule 5: Fuel values are close (10% margin)
                if (routeResult.TotalKm.HasValue && nfe.ValorTotal.HasValue && nfe.ValorUnitario.HasValue)
                {
                    var estimatedConsumption = routeResult.TotalKm.Value / 3.0;
                    var estimatedCost = estimatedConsumption * nfe.ValorUnitario.Value;
                    var valueDiff = Math.Abs(estimatedCost - nfe.ValorTotal.Value) / estimatedCost;
                    if (valueDiff <= 0.1)
                    {
                        score += 15;
                        log.Append("[Valor OK (<=10%)] ");
                    }
                }

                if (score > 0)
                {
                    log.Append($"=> PONTUAÇÃO FINAL: {score}");
                    CalculationLogService.Log(log.ToString());
                    scoredNfes.Add((nfe, score));
                }
            }

            if (!scoredNfes.Any())
            {
                CalculationLogService.Log("Nenhuma NF-e pontuou.");
                return null;
            }

            var bestMatch = scoredNfes.OrderByDescending(s => s.score).First();
            CalculationLogService.Log($"--- MELHOR MATCH: NF-e {bestMatch.nfe.NumeroNFe} com {bestMatch.score} pontos ---");
            return bestMatch.nfe;
        }

                private static ModelRow BaseFromMdfe(MdfeHeader h)
                {
                    var vehicleInfo = VehicleService.GetVehicleInfo(h.Placa, h.Renavam); 
                    var tipoVeiculo = vehicleInfo?.Tipo ?? MapTipo(h.TpRod, h.TpCar); 
        
                    return new ModelRow
                    {
                        Modelo = vehicleInfo?.Modelo, 
                        Tipo = tipoVeiculo,
                        Renavam = h.Renavam,
                        Placa = h.Placa,
                        MdfeNumero = h.NumeroMdf,
                        Data = h.DhIniViagem ?? h.DhEmi,
                        UFEmit = h.EmitUF,
                        CidadeEmit = ToTitle(h.EmitCidade),
                        Vinculo = "Não"
                    };
                }
        private static ModelRow BaseFromNfe(NfeParsedItem n)
        {
            return new ModelRow
            {
                NFeNumero = n.NumeroNFe,
                DataEmissao = n.DataEmissao,
                QuantidadeLitros = n.Quantidade,
                EspecieCombustivel = n.DescANP ?? n.DescricaoProduto ?? $"sem descrição:<{n.CodigoProduto}>",
                ValorUnitario = n.ValorUnitario,
                ValorTotalCombustivel = n.ValorTotal,
                ValorCredito = n.Credito,
                UFEmit = n.UFEmit,
                UFDest = n.UFDest,
                CidadeEmit = ToTitle(n.CidadeEmit),
                CidadeDest = ToTitle(n.CidadeDest),
                FornecedorCnpj = n.EmitCNPJ,
                FornecedorNome = n.EmitNome,
                FornecedorEndereco = $"{n.EmitStreet}, {n.EmitNumber} - {n.EmitNeighborhood}, {n.CidadeEmit} - {n.UFEmit}",
                Placa = n.PlacaObservada,
                Vinculo = "Não"
            };
        }

        private static void EnriquecerComNfe(ModelRow r, NfeParsedItem n)
        {
            r.QuantidadeLitros = n.Quantidade;
            r.EspecieCombustivel = n.DescANP ?? n.DescricaoProduto ?? $"sem descrição:<{n.CodigoProduto}>";
            r.ValorUnitario = n.ValorUnitario;
            r.ValorTotalCombustivel = n.ValorTotal;
            r.ValorCredito = n.Credito;
            r.NFeAquisicaoNumero = n.NumeroNFe;
            r.DataAquisicao = n.DataEmissao;
        }

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

        private static string ToTitle(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s ?? "";
            s = s.ToLowerInvariant();
            var ti = CultureInfo.GetCultureInfo("pt-BR").TextInfo;
            return ti.ToTitleCase(s);
        }
    }
}
