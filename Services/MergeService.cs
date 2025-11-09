using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsvIntegratorApp.Models;
using CsvIntegratorApp;

namespace CsvIntegratorApp.Services
{
    /// <summary>
    /// Provides services for merging data from NFe, MDFe, and SPED files, 
    /// calculating routes, and enriching <see cref="ModelRow"/> objects.
    /// </summary>
    public static class MergeService
    {
        /// <summary>
        /// Merges data from NFe, MDFe, and SPED files, calculates routes, and populates a list of <see cref="ModelRow"/>.
        /// </summary>
        public static async Task<List<ModelRow>> MergeAsync(
            List<NfeParsedItem>? nfeItems,
            List<MdfeParsed> mdfes,
            IProgress<ProgressReport> progress,
            bool somarRetornoParaOrigem = true)
        {
            CalculationLogService.Clear();
            CalculationLogService.Log("Iniciando processo de merge e cálculo de rota.");

            var allModelRows = new List<ModelRow>();

            // Evita a mesma MDF-e aparecer mais de uma vez na saída
            var mdfeOutputKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Índice por chave (para recuperar dados do XML quando existir)
            var porChave = (nfeItems ?? new List<NfeParsedItem>())
                .GroupBy(x => x.ChaveNFe ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int totalMdfes = mdfes.Count;
            int processedCount = 0;

            var dieselItems = (nfeItems ?? new List<NfeParsedItem>())
                   .Where(FuelAllocator.IsDieselItem)
                   .ToList();

            var allocator = new FuelAllocator(dieselItems);

            // Totais por NF-e (DIESEL) — auditoria/log
            var totaisDieselPorNfe = DieselTotalsService.BuildDieselTotals(nfeItems ?? new List<NfeParsedItem>());

            foreach (var mdfe in mdfes)
            {
                processedCount++;
                var percentage = 65 + (int)((double)processedCount / totalMdfes * 25); // 65% -> 90%
                progress.Report(new ProgressReport { Percentage = percentage, StatusMessage = $"Calculando rota para MDF-e {processedCount}/{totalMdfes}..." });

                var h = mdfe.Header;

                // Origem
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

                    var outKeyErr = BuildMdfeOutputKey(h);
                    if (mdfeOutputKeys.Add(outKeyErr))
                        allModelRows.Add(mdfeRow);

                    continue;
                }

                // Waypoints: origem + destinos (tentando enriquecer com endereço do SPED)
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

                // Chaves da carga
                var nfeKeys = mdfe.DestinosPorChave.Keys.Distinct().ToList();
                var cargoMostRecent = SpedTxtLookupService.TryGetMostRecentC100DateForKeys(nfeKeys);

                // Extrai um número de NF-e (da carga) para exibição
                string? nfeNumeroCarga = null;
                var firstCargoNfeKey = nfeKeys.FirstOrDefault();
                if (firstCargoNfeKey != null)
                {
                    if (porChave.TryGetValue(firstCargoNfeKey, out var cargoNfe))
                    {
                        nfeNumeroCarga = cargoNfe.NumeroNFe;
                    }
                    else if (firstCargoNfeKey.Length == 44)
                    {
                        try { nfeNumeroCarga = long.Parse(firstCargoNfeKey.Substring(25, 9)).ToString(); } catch { }
                    }
                }

                // Alvo de litros estimado pela rota
                double? alvoLitros = routeResult.TotalKm.HasValue ? routeResult.TotalKm.Value / 3.0 : null;

                // Alocações parciais do pool de DIESEL (controla saldo por item)
                var allocations = allocator.Allocate(alvoLitros);

                if (allocations.Any())
                {
                    // === CONSOLIDA TODAS AS ALOCAÇÕES DA MESMA MDF-e EM UMA ÚNICA LINHA ===
                    var litrosTot = allocations.Sum(a => a.LitrosAlocados);
                    var valorTotal = allocations.Sum(a => (a.Item.ValorUnitario ?? 0.0) * a.LitrosAlocados);
                    var creditoTotal = allocations.Sum(a =>
                    {
                        var qtd = a.Item.Quantidade ?? 0.0;
                        var prop = qtd > 0 ? a.LitrosAlocados / qtd : 0.0;
                        return (a.Item.Credito ?? 0.0) * prop;
                    });
                    double? valorUnitMedio = litrosTot > 0 ? (valorTotal / litrosTot) : (double?)null;

                    var numerosNfeAquisicao = string.Join(", ", allocations.Select(a => a.Item.NumeroNFe).Distinct());
                    var dataAquisicaoMax = allocations.Select(a => a.Item.DataEmissao).Where(d => d.HasValue).DefaultIfEmpty().Max();

                    // Monta a linha única da MDF-e
                    var row = BaseFromMdfe(h);
                    row.Waypoints = waypoints;

                    if (routeResult.TotalKm.HasValue)
                        row.QuantidadeEstimadaLitros = routeResult.TotalKm.Value / 3.0;

                    row.DistanciaPercorridaKm = routeResult.TotalKm;
                    row.Roteiro = routeResult.TotalKm.HasValue
                        ? string.Join(" -> ", waypoints.Select(w => w.City).Where(c => !string.IsNullOrWhiteSpace(c)))
                        : $"Falha no cálculo da rota: {routeResult.Error}";
                    row.MapPath = RouteLogService.GenerateRouteMap(routeResult.Polyline, routeResult.Waypoints, new List<ModelRow>());

                    // Espécie do combustível (a partir das alocações)
                    var especie =
                        allocations.Select(a => a.Item.DescANP).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ??
                        allocations.Select(a => a.Item.DescricaoProduto).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ??
                        "ÓLEO DIESEL S-10 COMUM";
                    row.EspecieCombustivel = especie;

                    // Consolidados
                    row.QuantidadeLitros = Math.Round(litrosTot, 6);
                    row.ValorTotalCombustivel = Math.Round(valorTotal, 2);
                    row.ValorUnitario = valorUnitMedio;
                    row.ValorCredito = Math.Round(creditoTotal, 2);

                    // Dados de aquisição/carga
                    row.NFeAquisicaoNumero = numerosNfeAquisicao;
                    row.DataAquisicao = dataAquisicaoMax;

                    row.NFeCargaNumero = string.Join(", ",
                        nfeKeys.Select(key =>
                        {
                            if (key.Length >= 34 && long.TryParse(key.Substring(25, 9), out long nfeNum))
                                return nfeNum.ToString();
                            return key;
                        }).Where(s => !string.IsNullOrWhiteSpace(s))
                    );
                    row.NFeNumero = nfeNumeroCarga;
                    row.DataEmissaoCarga = cargoMostRecent?.ToString("dd/MM/yyyy");

                    row.Vinculo = "Sim";

                    var outKey = BuildMdfeOutputKey(h);
                    if (mdfeOutputKeys.Add(outKey))
                        allModelRows.Add(row);
                    else
                        CalculationLogService.Log($"Ignorado MDF-e repetido na saída (com alocação): {outKey}");
                }
                else
                {
                    // SEM alocação possível -> linha só da viagem (Vinculo = "Não")
                    var modelRow = BaseFromMdfe(h);
                    modelRow.Waypoints = waypoints;
                    modelRow.DistanciaPercorridaKm = routeResult.TotalKm;
                    modelRow.Roteiro = routeResult.TotalKm.HasValue
                        ? string.Join(" -> ", waypoints.Select(w => w.City).Where(c => !string.IsNullOrWhiteSpace(c)))
                        : $"Falha no cálculo da rota: {routeResult.Error}";
                    modelRow.MapPath = RouteLogService.GenerateRouteMap(routeResult.Polyline, routeResult.Waypoints, new List<ModelRow>());
                    modelRow.Vinculo = "Não";
                    modelRow.NFeCargaNumero = string.Join(", ",
                        nfeKeys.Select(key =>
                        {
                            if (key.Length >= 34 && long.TryParse(key.Substring(25, 9), out long nfeNum))
                                return nfeNum.ToString();
                            return key;
                        }).Where(s => !string.IsNullOrWhiteSpace(s))
                    );
                    modelRow.DataEmissaoCarga = cargoMostRecent?.ToString("dd/MM/yyyy");

                    if (routeResult.TotalKm.HasValue)
                        modelRow.QuantidadeEstimadaLitros = routeResult.TotalKm.Value / 3.0;

                    var outKey = BuildMdfeOutputKey(h);
                    if (mdfeOutputKeys.Add(outKey))
                        allModelRows.Add(modelRow);
                    else
                        CalculationLogService.Log($"Ignorado MDF-e repetido na saída (sem alocação): {outKey}");
                }
            }

            // Auditoria: quanto de cada NF-e foi consumido
            foreach (var dto in totaisDieselPorNfe)
            {
                var original = dieselItems.Where(i => string.Equals(i.ChaveNFe, dto.ChaveNFe, StringComparison.OrdinalIgnoreCase))
                                          .Sum(i => i.Quantidade ?? 0.0);
                var remaining = dieselItems.Where(i => string.Equals(i.ChaveNFe, dto.ChaveNFe, StringComparison.OrdinalIgnoreCase))
                                           .Sum(i => allocator.RemainingForItem(i.ChaveNFe, i.NumeroItem));
                var consumido = original - remaining;
                CalculationLogService.Log($"NF-e {dto.NumeroNFe} ({dto.ChaveNFe}): DIESEL Total={dto.LitrosDiesel:F3}L, Alocado={consumido:F3}L");
            }

            // Adiciona linhas de NFe de DIESEL não utilizadas (somente itens totalmente não alocados)
            foreach (var item in dieselItems)
            {
                var original = item.Quantidade ?? 0.0;
                var rem = allocator.RemainingForItem(item.ChaveNFe, item.NumeroItem);
                if (original > 0 && Math.Abs(rem - original) < 1e-6)
                {
                    var nfeRow = BaseFromNfe(item);
                    nfeRow.Vinculo = "Não";
                    allModelRows.Add(nfeRow);
                }
            }

            CalculationLogService.Log("Processo finalizado.");
            CalculationLogService.Save();
            return allModelRows;
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
                AliquotaCredito = n.Aliquota,
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

        private static string BuildMdfeOutputKey(MdfeHeader h)
            => $"{h.EmitCnpj}|{h.Serie}|{h.NumeroMdf}|{h.Placa}";
    }
}
