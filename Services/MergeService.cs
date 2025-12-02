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

                // todas as chaves que o MDF-e lista
                var nfeKeysAll = mdfe.DestinosPorChave.Keys
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // mantém APENAS NF-e que aparecem no SPED como C100 de SAÍDA
                var nfeKeysSaida = nfeKeysAll
                    .Where(k => SpedTxtLookupService.IsSaidaNFe(k))
                    .ToList();

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
                    row.AliquotaCredito = allocations.First().Item.Aliquota; // Assign aliquot from the first allocated item
                    row.NFeAquisicaoNumero = numerosNfeAquisicao;
                    row.DataAquisicao = dataAquisicaoMax?.ToString("dd/MM/yyyy");
                    row.NFeCargaNumero = string.Join(", ", nfeKeysSaida.Select(key =>
                    {
                        if (key.Length >= 34 && long.TryParse(key.Substring(25, 9), out long nfeNum))
                            return nfeNum.ToString();
                        return key;
                    }).Where(s => !string.IsNullOrWhiteSpace(s)));
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
                    var modelRow = BaseFromMdfe(h);
                    // Use routeResult.Waypoints to ensure it includes the return segment
                    modelRow.Waypoints = routeResult.Waypoints;
                    modelRow.DistanciaPercorridaKm = routeResult.TotalKm;
                    // Use routeResult.Waypoints for Roteiro string
                    modelRow.Roteiro = routeResult.TotalKm.HasValue
                        ? string.Join(" -> ", routeResult.Waypoints.Select(w => w.City).Where(c => !string.IsNullOrWhiteSpace(c)))
                        : $"Falha no cálculo da rota: {routeResult.Error}";
                    modelRow.MapPath = RouteLogService.GenerateRouteMap(routeResult.Polyline, routeResult.Waypoints, new List<ModelRow>());
                    modelRow.Vinculo = "Não";
                    modelRow.NFeCargaNumero = string.Join(", ", nfeKeysSaida.Select(key =>
                    {
                        if (key.Length >= 34 && long.TryParse(key.Substring(25, 9), out long nfeNum))
                            return nfeNum.ToString();
                        return key;
                    }).Where(s => !string.IsNullOrWhiteSpace(s)));

                    modelRow.DataEmissaoCarga = cargoMostRecent?.ToString("dd/MM/yyyy");
                    modelRow.QuantidadeUsadaLitros = alvoLitros;

                    var outKey = BuildMdfeOutputKey(h);
                    if (mdfeOutputKeys.Add(outKey))
                        allModelRows.Add(modelRow);
                    else
                        CalculationLogService.Log($"Ignorado MDF-e repetido na saída (sem alocação): {outKey}");
                }
            }

            foreach (var dto in totaisDieselPorNfe)
            {
                var original = dieselItems.Where(i => string.Equals(i.ChaveNFe, dto.ChaveNFe, StringComparison.OrdinalIgnoreCase)).Sum(i => i.Quantidade ?? 0.0);
                var remaining = dieselItems.Where(i => string.Equals(i.ChaveNFe, dto.ChaveNFe, StringComparison.OrdinalIgnoreCase)).Sum(i => allocator.RemainingForItem(i.ChaveNFe, i.NumeroItem));
                var consumido = original - remaining;
                CalculationLogService.Log($"NF-e {dto.NumeroNFe} ({dto.ChaveNFe}): DIESEL Total={dto.LitrosDiesel:F3}L, Alocado={consumido:F3}L");
            }

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

        public static void RecalculateFuelAllocations(List<ModelRow> rows, List<NfeParsedItem> allNfeItems)
        {
            var dieselItems = (allNfeItems ?? new List<NfeParsedItem>())
                   .Where(FuelAllocator.IsDieselItem)
                   .ToList();

            var allocator = new FuelAllocator(dieselItems);

            foreach (var row in rows.OrderBy(r => r.Data))
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
                    row.DataAquisicao = dataAquisicaoMax?.ToString("dd/MM/yyyy");
                    row.Vinculo = "Sim";
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
                Data = h.DhIniViagem?.ToString("dd/MM/yyyy") ?? h.DhEmi?.ToString("dd/MM/yyyy"),
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
                DataEmissao = n.DataEmissao?.ToString("dd/MM/yyyy"),
                QuantidadeLitros = n.Quantidade,
                EspecieCombustivel = n.DescricaoProduto ?? "OLEO DIESEL",
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
