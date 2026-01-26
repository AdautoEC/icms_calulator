using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CsvIntegratorApp.Models;
using CsvIntegratorApp;

namespace CsvIntegratorApp.Services
{
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
            var mdfeOutputKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var porChave = (nfeItems ?? new List<NfeParsedItem>())
                .GroupBy(x => x.ChaveNFe ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int totalMdfes = mdfes.Count;
            int processedCount = 0;

            var dieselItems = (nfeItems ?? new List<NfeParsedItem>())
                   .Where(FuelAllocator.IsDieselItem)
                   .ToList();

            var allocator = new FuelAllocator(dieselItems);
            var totaisDieselPorNfe = DieselTotalsService.BuildDieselTotals(nfeItems ?? new List<NfeParsedItem>());

            foreach (var mdfe in mdfes)
            {
                processedCount++;
                var percentage = 65 + (int)((double)processedCount / totalMdfes * 25);
                progress.Report(new ProgressReport { Percentage = percentage, StatusMessage = $"Calculando rota para MDF-e {processedCount}/{totalMdfes}..." });

                var h = mdfe.Header;

                // Hardcode origin to "Itaporã, MS" as requested by the user
                var origemCidade = "Itaporã";
                var origemUF = "MS";
                var origemStr = "Itaporã, MS";

                // We no longer need to check if origemStr is null or empty since it's hardcoded.
                // However, we can log if the MDF-e's original origin was different for auditing.
                var originalMdfeOrigemCidade = h.OrigemCidade ?? h.EmitCidade;
                var originalMdfeOrigemUF = h.UFIni ?? h.EmitUF;
                if (!string.Equals(origemCidade, ToTitle(originalMdfeOrigemCidade), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(origemUF, originalMdfeOrigemUF, StringComparison.OrdinalIgnoreCase))
                {
                    CalculationLogService.Log($"AVISO: Origem do MDF-e {h.NumeroMdf} (original: {ToTitle(originalMdfeOrigemCidade)}, {originalMdfeOrigemUF}) foi sobrescrita para Itaporã, MS.");
                }

                var waypoints = new List<WaypointInfo> { new WaypointInfo { Address = origemStr, City = ToTitle(origemCidade), State = origemUF, InvoiceNumber = "Origem" } };
                foreach (var kv in mdfe.DestinosPorChave)
                {
                    var chave = kv.Key;
                    var (destCidadeMdfe, destUfMdfe, _) = kv.Value;
                    if (SpedTxtLookupService.TryGetAddressInfoPorChave(chave, out var addrInfo))
                    {
                        var state = addrInfo.uf ?? destUfMdfe;
                        var addressParts = new[] { addrInfo.street, addrInfo.number, destCidadeMdfe, state };
                        var destinoStr = string.Join(", ", addressParts.Where(s => !string.IsNullOrWhiteSpace(s)));
                        waypoints.Add(new WaypointInfo { Address = destinoStr, City = ToTitle(destCidadeMdfe), State = state, InvoiceNumber = chave });
                    }
                }

                var routeResult = await DistanceService.TryRouteLegsKmAsync(waypoints, somarRetornoParaOrigem);

                // >>> CORREÇÃO: não gerar linhas com rota inválida ou incompleta
                if (!routeResult.TotalKm.HasValue || routeResult.TotalKm.Value <= 0)
                {
                    CalculationLogService.Log(
                        $"Ignorado MDF-e {h.Serie}/{h.NumeroMdf}: rota inválida ou distância não calculada.");
                    continue;
                }

                if (routeResult.Waypoints == null || routeResult.Waypoints.Count < 2)
                {
                    CalculationLogService.Log(
                        $"Ignorado MDF-e {h.Serie}/{h.NumeroMdf}: pontos insuficientes para cálculo de rota.");
                    continue;
                }


                // todas as chaves que o MDF-e lista
                var nfeKeysAll = mdfe.DestinosPorChave.Keys
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // mantém APENAS NF-e que aparecem no SPED como C100 de SAÍDA
                var nfeKeysSaida = nfeKeysAll
                    .Where(k => SpedTxtLookupService.IsSaidaNFe(k))
                    .ToList();

                var nfeKeysSemSped = nfeKeysAll
                    .Except(nfeKeysSaida, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (nfeKeysSemSped.Count > 0)
                {
                    CalculationLogService.Log(
                        $"MDF-e {mdfe.Header.Serie}/{mdfe.Header.NumeroMdf}: NF-e sem C100 de saida no SPED ({nfeKeysSemSped.Count}): {string.Join(", ", nfeKeysSemSped)}");
                }

                var (sumIsentas, sumSt, totalNfe, c190IsentasCount, c190StCount, c100Count) = GetC190BasesForKeys(nfeKeysSaida);
                double? proporcaoIsentas = null;
                double? proporcaoSt = null;
                if (totalNfe > 0m)
                {
                    proporcaoIsentas = (double)(sumIsentas / totalNfe);
                    proporcaoSt = (double)(sumSt / totalNfe);
                }

                // se não sobrou nada → MDF-e só com nota de ENTRADA → ignora
                if (nfeKeysSaida.Count == 0)
                {
                    CalculationLogService.Log(
                        $"Ignorado MDF-e {mdfe.Header.Serie}/{mdfe.Header.NumeroMdf}: nenhuma NF-e de saída encontrada no SPED.");
                    continue;
                }

                // usa apenas as notas de saída para data/numeração da CARGA
                var cargoMostRecent = SpedTxtLookupService.TryGetMostRecentC100DateForKeys(nfeKeysSaida);
                string? nfeNumeroCarga = null;
                var firstCargoNfeKey = nfeKeysSaida.FirstOrDefault();
                if (firstCargoNfeKey != null && porChave.TryGetValue(firstCargoNfeKey, out var item))
                {
                    nfeNumeroCarga = item.NumeroNFe;
                }

                else if (firstCargoNfeKey.Length == 44)
                {
                    try { nfeNumeroCarga = long.Parse(firstCargoNfeKey.Substring(25, 9)).ToString(); } catch { }
                }

                double? alvoLitros = routeResult.TotalKm.HasValue ? routeResult.TotalKm.Value / 3.0 : null;
                var allocations = allocator.Allocate(alvoLitros);

                if (allocations.Any())
                {
                    var litrosAlocados = allocations.Sum(a => a.LitrosAlocados);
                    var valorTotal = allocations.Sum(a => (a.Item.ValorUnitario ?? 0.0) * a.LitrosAlocados);
                    var creditoTotal = allocations.Sum(a =>
                    {
                        var qtd = a.Item.Quantidade ?? 0.0;
                        var prop = qtd > 0 ? a.LitrosAlocados / qtd : 0.0;
                        return (a.Item.Credito ?? 0.0) * prop;
                    });
                    double? valorUnitMedio = litrosAlocados > 0 ? (valorTotal / litrosAlocados) : (double?)null;
                    var numerosNfeAquisicao = string.Join(", ", allocations.Select(a => a.Item.NumeroNFe).Distinct());
                    var dataAquisicaoMax = allocations.Select(a => a.Item.DataEmissao).Where(d => d.HasValue).DefaultIfEmpty().Max();

                    var row = BaseFromMdfe(h);
                    // Use routeResult.Waypoints to ensure it includes the return segment
                    row.Waypoints = routeResult.Waypoints;

                    var sourceNfeKey = allocations.First().Item.ChaveNFe;
                    var totalNfeQuantity = dieselItems.Where(item => item.ChaveNFe == sourceNfeKey).Sum(item => item.Quantidade ?? 0.0);
                    row.QuantidadeLitros = totalNfeQuantity;

                    row.QuantidadeUsadaLitros = alvoLitros;
                    row.DistanciaPercorridaKm = routeResult.TotalKm;
                    // Use routeResult.Waypoints for Roteiro string
                    row.Roteiro = routeResult.TotalKm.HasValue
                        ? string.Join(" -> ", routeResult.Waypoints.Select(w => w.City).Where(c => !string.IsNullOrWhiteSpace(c)))
                        : $"Falha no cálculo da rota: {routeResult.Error}";
                    row.MapPath = RouteLogService.GenerateRouteMap(routeResult.Polyline, routeResult.Waypoints, new List<ModelRow>());

                    var especie = allocations.Select(a => a.Item.DescricaoProduto).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "ÓLEO DIESEL S-10 COMUM";
                    row.EspecieCombustivel = especie;

                    row.ValorTotalCombustivel = Math.Round(valorTotal, 2);
                    row.ValorUnitario = valorUnitMedio;
                    row.ValorCredito = Math.Round(creditoTotal, 2);

                    var itemBase = allocations.First().Item;
                    double aliquotaCredito;

                    // CFOP vem do SPED (C190 ou C170)
                    string? cfop = null;

                    if (SpedTxtLookupService.TryGetC190InfoPorChave(itemBase.ChaveNFe, out var c190) &&
                        c190 != null && c190.Any())
                    {
                        cfop = c190.First().cfop;
                    }
                    else if (SpedTxtLookupService.TryGetC170InfoPorChave(itemBase.ChaveNFe, out var c170) &&
                             c170 != null && c170.Any())
                    {
                        cfop = c170.First().cfop;
                    }

                    cfop ??= "";

                    // Regra ICMS
                    if (cfop.StartsWith("2")) // interestadual
                    {
                        var ufOrigem = itemBase.UFEmit?.ToUpperInvariant();

                        if (ufOrigem == "RS" || ufOrigem == "SC" || ufOrigem == "PR" ||
                            ufOrigem == "SP" || ufOrigem == "RJ" || ufOrigem == "MG")
                        {
                            aliquotaCredito = 0.07;
                        }
                        else
                        {
                            aliquotaCredito = 0.12;
                        }
                    }
                    else
                    {
                        // operação interna
                        aliquotaCredito = itemBase.Aliquota ?? 0.0;
                    }

                    row.AliquotaCredito = aliquotaCredito;

                    row.NFeAquisicaoNumero = numerosNfeAquisicao;
                    row.DataAquisicao = dataAquisicaoMax?.Date;
                    row.NFeCargaNumero = string.Join(", ", nfeKeysSaida.Select(key =>
                    {
                        if (key.Length >= 34 && long.TryParse(key.Substring(25, 9), out long nfeNum))
                            return nfeNum.ToString();
                        return key;
                    }).Where(s => !string.IsNullOrWhiteSpace(s)));
                    row.NFeNumero = nfeNumeroCarga;
                    row.DataEmissaoCarga = cargoMostRecent?.Date;
                    row.Vinculo = "Sim";

                    ApplyCreditoEstorno(
                        row,
                        proporcaoIsentas,
                        proporcaoSt,
                        sumIsentas,
                        sumSt,
                        totalNfe,
                        c190IsentasCount,
                        c190StCount,
                        c100Count,
                        h);

                    var outKey = BuildMdfeOutputKey(h);
                    if (mdfeOutputKeys.Add(outKey))
                        allModelRows.Add(row);
                    else
                        CalculationLogService.Log($"Ignorado MDF-e repetido na saída (com alocação): {outKey}");
                }
                else
                {
                    // MDF-e sem alocação de combustível
                    // Regra de negócio: permitido para ajuste manual posterior

                    var modelRow = BaseFromMdfe(h);

                    modelRow.Waypoints = routeResult.Waypoints;
                    modelRow.DistanciaPercorridaKm = routeResult.TotalKm;

                    modelRow.Roteiro = routeResult.TotalKm.HasValue
                        ? string.Join(" -> ", routeResult.Waypoints
                            .Select(w => w.City)
                            .Where(c => !string.IsNullOrWhiteSpace(c)))
                        : "Rota não calculada";

                    modelRow.MapPath = RouteLogService.GenerateRouteMap(
                        routeResult.Polyline,
                        routeResult.Waypoints,
                        new List<ModelRow>());

                    modelRow.Vinculo = "Não";

                    // 🔒 CAMPOS DE COMBUSTÍVEL DEVEM SER NULOS (edição manual)
                    modelRow.QuantidadeLitros = null;
                    modelRow.QuantidadeUsadaLitros = null;
                    modelRow.EspecieCombustivel = null;
                    modelRow.ValorTotalCombustivel = null;
                    modelRow.ValorUnitario = null;
                    modelRow.ValorCredito = null;
                    modelRow.NFeAquisicaoNumero = null;
                    modelRow.DataAquisicao = null;

                    // 🔒 CAMPOS DE CARGA (sempre presentes)
                    modelRow.NFeCargaNumero = string.Join(", ", nfeKeysSaida.Select(key =>
                    {
                        if (key.Length >= 34 && long.TryParse(key.Substring(25, 9), out long nfeNum))
                            return nfeNum.ToString();
                        return key;
                    }).Where(s => !string.IsNullOrWhiteSpace(s)));

                    modelRow.DataEmissaoCarga = cargoMostRecent?.Date;

                    ApplyCreditoEstorno(
                        modelRow,
                        proporcaoIsentas,
                        proporcaoSt,
                        sumIsentas,
                        sumSt,
                        totalNfe,
                        c190IsentasCount,
                        c190StCount,
                        c100Count,
                        h);

                    var outKey = BuildMdfeOutputKey(h);
                    if (mdfeOutputKeys.Add(outKey))
                        allModelRows.Add(modelRow);
                    else
                        CalculationLogService.Log(
                            $"Ignorado MDF-e repetido na saída (sem alocação): {outKey}");
                }
            }

            foreach (var dto in totaisDieselPorNfe)
            {
                var original = dieselItems.Where(i => string.Equals(i.ChaveNFe, dto.ChaveNFe, StringComparison.OrdinalIgnoreCase)).Sum(i => i.Quantidade ?? 0.0);
                var remaining = dieselItems.Where(i => string.Equals(i.ChaveNFe, dto.ChaveNFe, StringComparison.OrdinalIgnoreCase)).Sum(i => allocator.RemainingForItem(i.ChaveNFe, i.NumeroItem));
                var consumido = original - remaining;
                CalculationLogService.Log($"NF-e {dto.NumeroNFe} ({dto.ChaveNFe}): DIESEL Total={dto.LitrosDiesel:F3}L, Alocado={consumido:F3}L");
            }


            var dieselAllocationReport = BuildDieselAllocationReport(dieselItems, allocator);
            LogDieselAllocationReport(dieselAllocationReport);

            CalculationLogService.Log("Processo finalizado.");
            CalculationLogService.Save();
            return allModelRows;
        }

        public static void RecalculateFuelAllocations(List<ModelRow> rows, List<NfeParsedItem> allNfeItems)
        {
            var dieselItems = (allNfeItems ?? new List<NfeParsedItem>())
                   .Where(FuelAllocator.IsDieselItem)
                   .ToList();

            var allocator = new FuelAllocator(dieselItems);

            foreach (var row in rows.OrderBy(r => r.Data ?? DateTime.MinValue))
            {
                if (string.IsNullOrEmpty(row.MdfeNumero)) continue;

                double? alvoLitros = row.DistanciaPercorridaKm.HasValue ? row.DistanciaPercorridaKm.Value / 3.0 : null;
                var allocations = allocator.Allocate(alvoLitros);

                if (allocations.Any())
                {
                    var litrosAlocados = allocations.Sum(a => a.LitrosAlocados);
                    var valorTotal = allocations.Sum(a => (a.Item.ValorUnitario ?? 0.0) * a.LitrosAlocados);
                    var creditoTotal = allocations.Sum(a =>
                    {
                        var qtd = a.Item.Quantidade ?? 0.0;
                        var prop = qtd > 0 ? a.LitrosAlocados / qtd : 0.0;
                        return (a.Item.Credito ?? 0.0) * prop;
                    });
                    double? valorUnitMedio = litrosAlocados > 0 ? (valorTotal / litrosAlocados) : (double?)null;
                    var numerosNfeAquisicao = string.Join(", ", allocations.Select(a => a.Item.NumeroNFe).Distinct());
                    var dataAquisicaoMax = allocations.Select(a => a.Item.DataEmissao).Where(d => d.HasValue).DefaultIfEmpty().Max();
                    var especie = allocations.Select(a => a.Item.DescricaoProduto).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "ÓLEO DIESEL S-10 COMUM";

                    var sourceNfeKey = allocations.First().Item.ChaveNFe;
                    var totalNfeQuantity = dieselItems.Where(item => item.ChaveNFe == sourceNfeKey).Sum(item => item.Quantidade ?? 0.0);

                    row.QuantidadeLitros = totalNfeQuantity;
                    row.QuantidadeUsadaLitros = alvoLitros;
                    row.EspecieCombustivel = especie;
                    row.ValorTotalCombustivel = Math.Round(valorTotal, 2);
                    row.ValorUnitario = valorUnitMedio;
                    row.ValorCredito = Math.Round(creditoTotal, 2);
                    row.NFeAquisicaoNumero = numerosNfeAquisicao;
                    row.DataAquisicao = dataAquisicaoMax?.Date;
                    row.Vinculo = "Sim";
                    ApplyCreditoEstornoFromPercent(row);
                }
                else
                {
                    row.QuantidadeUsadaLitros = alvoLitros;
                    row.EspecieCombustivel = null;
                    row.QuantidadeLitros = null;
                    row.ValorTotalCombustivel = null;
                    row.ValorUnitario = null;
                    row.ValorCredito = null;
                    row.NFeAquisicaoNumero = null;
                    row.DataAquisicao = null;
                    row.Vinculo = "Não";
                    ApplyCreditoEstornoFromPercent(row);
                }
            }
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
                Data = h.DhIniViagem?.Date ?? h.DhEmi?.Date,
                UFEmit = h.EmitUF,
                CidadeEmit = ToTitle(h.EmitCidade),
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

        private static (decimal sumIsentas, decimal sumSt, decimal totalNfe, int c190IsentasCount, int c190StCount, int c100Count) GetC190BasesForKeys(IEnumerable<string> keys)
        {
            decimal sumIsentas = 0m;
            decimal sumSt = 0m;
            decimal totalNfe = 0m;
            int c190IsentasCount = 0;
            int c190StCount = 0;
            int c100Count = 0;

            foreach (var key in keys ?? Array.Empty<string>())
            {
                if (SpedTxtLookupService.TryGetC190InfoPorChave(key, out var c190Info) && c190Info != null)
                {
                    foreach (var c190 in c190Info)
                    {
                        var cst = NormalizeCst(c190.cst);
                        if (cst == "40" || cst == "41" || cst == "20")
                        {
                            if (c190.totalDocumento.HasValue)
                            {
                                sumIsentas += c190.totalDocumento.Value;
                                c190IsentasCount++;
                            }
                        }
                        else if (cst == "10" || cst == "60" || cst == "61")
                        {
                            if (c190.totalDocumento.HasValue)
                            {
                                sumSt += c190.totalDocumento.Value;
                                c190StCount++;
                            }
                        }
                    }
                }

                if (SpedTxtLookupService.TryGetC100ValorDocumentoPorChave(key, out var valorDocumento) &&
                    valorDocumento.HasValue)
                {
                    totalNfe += valorDocumento.Value;
                    c100Count++;
                }
            }

            return (sumIsentas, sumSt, totalNfe, c190IsentasCount, c190StCount, c100Count);
        }

        private static void ApplyCreditoEstorno(
            ModelRow row,
            double? proporcaoIsentas,
            double? proporcaoSt,
            decimal sumIsentas,
            decimal sumSt,
            decimal totalNfe,
            int c190IsentasCount,
            int c190StCount,
            int c100Count,
            MdfeHeader h)
        {
            if (!row.ValorCredito.HasValue || !proporcaoIsentas.HasValue || !proporcaoSt.HasValue)
            {
                row.ValorEstornoCredito = null;
                row.ValorCreditoLiquido = null;
                row.ValorEstornoCreditoSt = null;
                row.ValorCreditoLiquidoSt = null;

                if (row.ValorCredito.HasValue)
                {
                    CalculationLogService.Log(
                        $"MDF-e {h.Serie}/{h.NumeroMdf}: base insuficiente para estorno (C190_isentas={c190IsentasCount}, C190_ST={c190StCount}, C100={c100Count}, SomaIsentas={sumIsentas:F2}, SomaST={sumSt:F2}, TotalNFe={totalNfe:F2}).");
                }
                return;
            }

            var propIsentas = Math.Round(Math.Clamp(proporcaoIsentas.Value, 0.0, 1.0), 4);
            var propSt = Math.Round(Math.Clamp(proporcaoSt.Value, 0.0, 1.0), 4);
            row.PercentualCredito = propIsentas;
            row.PercentualCreditoSt = propSt;
            var creditoIntegral = row.ValorCredito.Value;

            var estornoIsentas = Math.Round(creditoIntegral * propIsentas, 2);
            var estornoSt = Math.Round(creditoIntegral * propSt, 2);
            var creditoLiquido = Math.Round(creditoIntegral - estornoIsentas, 2);
            var creditoLiquidoSt = Math.Round(creditoIntegral - estornoIsentas - estornoSt, 2);

            row.ValorEstornoCredito = estornoIsentas;
            row.ValorCreditoLiquido = creditoLiquido;
            row.ValorEstornoCreditoSt = estornoSt;
            row.ValorCreditoLiquidoSt = creditoLiquidoSt;

            CalculationLogService.Log(
                $"MDF-e {h.Serie}/{h.NumeroMdf}: SomaIsentas={sumIsentas:F2}, SomaST={sumSt:F2}, TotalNFe={totalNfe:F2}, ProporcaoIsentas={propIsentas:P2}, ProporcaoST={propSt:P2}, CreditoIntegral={creditoIntegral:F2}, EstornoIsentas={estornoIsentas:F2}, EstornoST={estornoSt:F2}, CreditoLiquido={creditoLiquidoSt:F2}.");
        }

        private static void ApplyCreditoEstornoFromPercent(ModelRow row)
        {
            if (!row.ValorCredito.HasValue ||
                !row.PercentualCredito.HasValue ||
                !row.PercentualCreditoSt.HasValue)
            {
                row.ValorEstornoCredito = null;
                row.ValorCreditoLiquido = null;
                row.ValorEstornoCreditoSt = null;
                row.ValorCreditoLiquidoSt = null;
                return;
            }

            var propIsentas = Math.Round(Math.Clamp(row.PercentualCredito.Value, 0.0, 1.0), 4);
            var propSt = Math.Round(Math.Clamp(row.PercentualCreditoSt.Value, 0.0, 1.0), 4);
            var creditoIntegral = row.ValorCredito.Value;

            row.ValorEstornoCredito = Math.Round(creditoIntegral * propIsentas, 2);
            row.ValorEstornoCreditoSt = Math.Round(creditoIntegral * propSt, 2);
            row.ValorCreditoLiquido = Math.Round(creditoIntegral - row.ValorEstornoCredito.Value, 2);
            row.ValorCreditoLiquidoSt = Math.Round(creditoIntegral - row.ValorEstornoCredito.Value - row.ValorEstornoCreditoSt.Value, 2);
        }

        private sealed record DieselAllocationInfo(
            string? ChaveNFe,
            string? NumeroNFe,
            DateTime? DataEmissao,
            double LitrosTotal,
            double LitrosAlocados,
            double LitrosRestantes);

        private static List<DieselAllocationInfo> BuildDieselAllocationReport(List<NfeParsedItem> dieselItems, FuelAllocator allocator)
        {
            return (dieselItems ?? new List<NfeParsedItem>())
                .Where(FuelAllocator.IsDieselItem)
                .GroupBy(i => i.ChaveNFe ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var litrosTotal = g.Sum(i => i.Quantidade ?? 0.0);
                    var litrosRestantes = g.Sum(i => allocator.RemainingForItem(i.ChaveNFe, i.NumeroItem));
                    var litrosAlocados = litrosTotal - litrosRestantes;
                    var head = g.OrderByDescending(i => i.DataEmissao ?? DateTime.MinValue).FirstOrDefault();

                    return new DieselAllocationInfo(
                        ChaveNFe: head?.ChaveNFe ?? g.Key,
                        NumeroNFe: head?.NumeroNFe,
                        DataEmissao: head?.DataEmissao,
                        LitrosTotal: Math.Round(litrosTotal, 6),
                        LitrosAlocados: Math.Round(litrosAlocados, 6),
                        LitrosRestantes: Math.Round(litrosRestantes, 6));
                })
                .OrderBy(d => d.DataEmissao ?? DateTime.MinValue)
                .ToList();
        }

        private static void LogDieselAllocationReport(List<DieselAllocationInfo> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                CalculationLogService.Log("Relatorio de alocacao de diesel: sem entradas.");
                return;
            }

            var pt = CultureInfo.GetCultureInfo("pt-BR");
            CalculationLogService.Log("Relatorio de alocacao de diesel:");
            foreach (var e in entries)
            {
                var status = e.LitrosAlocados > 0 ? "Usada" : "Nao Usada";
                var data = e.DataEmissao?.ToString("dd/MM/yyyy") ?? "";
                CalculationLogService.Log(
                    $"NF-e {e.NumeroNFe} ({e.ChaveNFe}): Data={data}, Total={e.LitrosTotal.ToString("N6", pt)}L, Alocado={e.LitrosAlocados.ToString("N6", pt)}L, Restante={e.LitrosRestantes.ToString("N6", pt)}L, Status={status}.");
            }
        }

        private static string NormalizeCst(string? cst)
        {
            if (string.IsNullOrWhiteSpace(cst)) return "";
            var trimmed = cst.Trim().TrimStart('0');
            return string.IsNullOrEmpty(trimmed) ? "0" : trimmed;
        }
    }
}
