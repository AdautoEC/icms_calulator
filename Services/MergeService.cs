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
            MdfeParsed mdfe,
            bool somarRetornoParaOrigem = true)
        {
            CalculationLogService.Clear();
            CalculationLogService.Log("Iniciando processo de merge e cálculo de rota.");

            var h = mdfe.Header;

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

            var porChave = (nfeItems ?? new List<NfeParsedItem>())
                .GroupBy(x => x.ChaveNFe ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var waypoints = new List<WaypointInfo> { new WaypointInfo { Address = origemStr, InvoiceNumber = "Origem" } };
            var finalRow = BaseFromMdfe(h);

            decimal? totalValorIcms = 0;
            decimal? totalBaseIcms = 0;
            decimal? totalTotalDocumento = 0;

            var mapModelRows = new List<ModelRow>();

            CalculationLogService.Log("Coletando pontos de destino a partir do SPED EFD...");
            foreach (var kv in mdfe.DestinosPorChave)
            {
                var chave = kv.Key;
                var (destCidadeMdfe, destUfMdfe, _) = kv.Value;

                if (SpedTxtLookupService.TryGetAddressInfoPorChave(chave, out var addrInfo))
                {
                    var addressParts = new[] { addrInfo.street, addrInfo.number, destCidadeMdfe, addrInfo.uf ?? destUfMdfe };
                    var destinoStr = string.Join(", ", addressParts.Where(s => !string.IsNullOrWhiteSpace(s)));
                    
                    waypoints.Add(new WaypointInfo { Address = destinoStr, InvoiceNumber = chave });
                    CalculationLogService.Log($"Destino para NFe {chave} encontrado no SPED: {destinoStr}");
                }
                else
                {
                    CalculationLogService.Log($"AVISO: A chave NFe {chave} do MDF-e não foi encontrada no SPED EFD. Este destino será ignorado no cálculo da rota.");
                }

                if (SpedTxtLookupService.TryGetC190InfoPorChave(chave, out var c190InfoList))
                {
                    foreach (var c190Info in c190InfoList)
                    {
                        if (finalRow.Cst == null)
                        {
                            finalRow.Cst = c190Info.cst;
                        }
                        if (finalRow.Cfop == null)
                        {
                            finalRow.Cfop = c190Info.cfop;
                        }
                        totalValorIcms += c190Info.valorIcms ?? 0;
                        totalBaseIcms += c190Info.baseIcms ?? 0;
                        totalTotalDocumento += c190Info.totalDocumento ?? 0;

                        var mapModelRow = new ModelRow { ChaveNFe = chave };
                        mapModelRow.Cst = c190Info.cst;
                        mapModelRow.Cfop = c190Info.cfop;
                        mapModelRow.ValorIcms = c190Info.valorIcms;
                        mapModelRow.BaseIcms = c190Info.baseIcms;
                        mapModelRow.TotalDocumento = c190Info.totalDocumento;
                        mapModelRows.Add(mapModelRow);
                    }
                }
            }

            finalRow.ValorIcms = totalValorIcms;
            finalRow.BaseIcms = totalBaseIcms;
            finalRow.TotalDocumento = totalTotalDocumento;

            if (waypoints.Count == 1) // Only origin
            {
                var (destCidadeFallback, destUfFallback) = PrimeiroDestinoDoMdfe(mdfe) ?? (null, h.UFFim);
                var destinoStr = MontaCidadeUf(destCidadeFallback, destUfFallback);
                if (!string.IsNullOrWhiteSpace(destinoStr))
                {
                    waypoints.Add(new WaypointInfo { Address = destinoStr, InvoiceNumber = "Destino Fallback" });
                    CalculationLogService.Log($"Nenhum destino no MDF-e. Usando fallback: {destinoStr}");
                }
            }

            var routeResult = await DistanceService.TryRouteLegsKmAsync(waypoints, somarRetornoParaOrigem);
            CalculationLogService.Log($"Resultado da API: Distancia={routeResult.TotalKm}km, Usado='{routeResult.Used}', Erro='{routeResult.Error}'");

            CalculationLogService.Log("Gerando mapa da rota...");
            RouteLogService.GenerateRouteMap(routeResult.Polyline, routeResult.Waypoints, mapModelRows);

            if (routeResult.TotalKm.HasValue)
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

            finalRow.ChaveNFe = string.Join(", ", mdfe.DestinosPorChave.Keys.Distinct());
            finalRow.UFDest = h.UFFim;
            finalRow.CidadeDest = waypoints.LastOrDefault()?.Address.Split(',')[0].Trim();

            // Handle fuel
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

            finalRow.AliquotaCredito = GetAliquota(finalRow.UFEmit, finalRow.UFDest, h.UFIni, h.UFFim);
            finalRow.ValorCredito = CalcCredito(finalRow.ValorTotalCombustivel, finalRow.AliquotaCredito);

            CalculationLogService.Log("Processo finalizado.");
            CalculationLogService.Save();
            return new List<ModelRow> { finalRow };
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
            r.UFEmit = n.UFEmit ?? r.UFEmit;
            r.UFDest = r.UFDest ?? n.UFDest;      
            r.CidadeEmit = r.CidadeEmit ?? ToTitle(n.CidadeEmit);
            r.CidadeDest = r.CidadeDest ?? ToTitle(n.CidadeDest);
            r.FornecedorCnpj = n.EmitCNPJ;
            r.FornecedorNome = n.EmitNome;
            r.FornecedorEndereco = $"{n.EmitStreet}, {n.EmitNumber} - {n.EmitNeighborhood}, {n.CidadeEmit} - {n.UFEmit}";

            if (string.IsNullOrWhiteSpace(r.Placa) && !string.IsNullOrWhiteSpace(n.PlacaObservada))
                r.Placa = n.PlacaObservada;
        }

        private static NfeParsedItem? TentarMatchPorPlacaEData(IEnumerable<NfeParsedItem> nfe, string? placa, DateTime? dataRef)
        {
            if (string.IsNullOrWhiteSpace(placa) || !dataRef.HasValue) return null;
            var min = dataRef.Value.AddDays(-3);
            var max = dataRef.Value.AddDays(3);

            return nfe
                .Where(x => (x.PlacaObservada ?? "").Equals(placa, StringComparison.OrdinalIgnoreCase)
                         && x.DataEmissao.HasValue
                         && x.DataEmissao.Value >= min && x.DataEmissao.Value <= max)
                .OrderBy(x => Math.Abs((x.DataEmissao!.Value - dataRef.Value).TotalHours))
                .FirstOrDefault();
        }

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

        private static string ToTitle(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s ?? "";
            s = s.ToLowerInvariant();
            var ti = CultureInfo.GetCultureInfo("pt-BR").TextInfo;
            return ti.ToTitleCase(s);
        }
    }
}
