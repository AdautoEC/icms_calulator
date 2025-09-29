// Services/ParserNFe.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CsvIntegratorApp.Services
{
    public class NfeParsedItem
    {
        // Cabeçalho
        public string? ChaveNFe { get; set; }
        public string? NumeroNFe { get; set; }
        public string? Serie { get; set; }
        public DateTime? DataEmissao { get; set; }

        // Emitente
        public string? EmitCNPJ { get; set; }
        public string? EmitNome { get; set; }
        public string? UFEmit { get; set; }
        public string? CidadeEmit { get; set; }
        public int? CMunEmit { get; set; }

        // Destinatário
        public string? DestCNPJ { get; set; }
        public string? DestNome { get; set; }
        public string? UFDest { get; set; }
        public string? CidadeDest { get; set; }
        public int? CMunDest { get; set; }

        // Item
        public int? NumeroItem { get; set; }
        public string? CodigoProduto { get; set; }
        public string? DescricaoProduto { get; set; }
        public string? NCM { get; set; }
        public string? CFOP { get; set; }
        public string? Unidade { get; set; }
        public double? Quantidade { get; set; }
        public double? ValorUnitario { get; set; }
        public double? ValorTotal { get; set; }

        // Combustível
        public string? ProdANP { get; set; }
        public string? DescANP { get; set; }
        public string? UFConsumo { get; set; }

        // Extras
        public string? PlacaObservada { get; set; }
        public bool IsCombustivel { get; set; }
    }

    public static class ParserNFe
    {
        public static List<NfeParsedItem> Parse(string xmlPath)
        {
            XDocument doc = XDocument.Load(xmlPath);
            XNamespace ns = "http://www.portalfiscal.inf.br/nfe";

            string? chave = doc.Descendants(ns + "infNFe").Attributes("Id").Select(a => a.Value).FirstOrDefault();
            if (!string.IsNullOrEmpty(chave) && chave.StartsWith("NFe", StringComparison.OrdinalIgnoreCase))
                chave = chave.Substring(3);
            if (string.IsNullOrEmpty(chave))
                chave = doc.Descendants(ns + "chNFe").Select(x => x.Value).FirstOrDefault();

            var ide = doc.Descendants(ns + "ide").FirstOrDefault();
            string? nNF = ide?.Element(ns + "nNF")?.Value;
            string? serie = ide?.Element(ns + "serie")?.Value;

            DateTime? dhEmi = null;
            var dhEmiStr = ide?.Element(ns + "dhEmi")?.Value;
            if (DateTime.TryParse(dhEmiStr, out var dtE)) dhEmi = dtE;

            var emit = doc.Descendants(ns + "emit").FirstOrDefault();
            string? emitCNPJ = emit?.Element(ns + "CNPJ")?.Value;
            string? emitNome = emit?.Element(ns + "xNome")?.Value;
            string? ufEmit = emit?.Element(ns + "enderEmit")?.Element(ns + "UF")?.Value;
            string? xMunEmit = emit?.Element(ns + "enderEmit")?.Element(ns + "xMun")?.Value;
            int? cMunEmit = TryInt(emit?.Element(ns + "enderEmit")?.Element(ns + "cMun")?.Value);

            var dest = doc.Descendants(ns + "dest").FirstOrDefault();
            string? destCNPJ = dest?.Element(ns + "CNPJ")?.Value;
            string? destNome = dest?.Element(ns + "xNome")?.Value;
            string? ufDest = dest?.Element(ns + "enderDest")?.Element(ns + "UF")?.Value;
            string? xMunDest = dest?.Element(ns + "enderDest")?.Element(ns + "xMun")?.Value;
            int? cMunDest = TryInt(dest?.Element(ns + "enderDest")?.Element(ns + "cMun")?.Value);

            string? infCpl = doc.Descendants(ns + "infCpl").Select(x => x.Value).FirstOrDefault();
            string? placaInf = TryFindPlaca(infCpl);

            var list = new List<NfeParsedItem>();
            foreach (var det in doc.Descendants(ns + "det"))
            {
                int? nItem = TryInt(det.Attribute("nItem")?.Value);
                var prod = det.Element(ns + "prod");

                string? cProd = prod?.Element(ns + "cProd")?.Value;
                string? xProd = prod?.Element(ns + "xProd")?.Value;
                string? ncm = prod?.Element(ns + "NCM")?.Value;
                string? cfop = prod?.Element(ns + "CFOP")?.Value;
                string? uCom = prod?.Element(ns + "uCom")?.Value;

                double? qCom = TryD(prod?.Element(ns + "qCom")?.Value);
                double? vUn = TryD(prod?.Element(ns + "vUnCom")?.Value);
                double? vProd = TryD(prod?.Element(ns + "vProd")?.Value);

                var comb = prod?.Element(ns + "comb");
                string? cProdANP = comb?.Element(ns + "cProdANP")?.Value;
                string? descANP = comb?.Element(ns + "descANP")?.Value;
                string? ufCons = comb?.Element(ns + "UFCons")?.Value;

                bool isComb = !string.IsNullOrWhiteSpace(cProdANP)
                              || (!string.IsNullOrWhiteSpace(ncm) && ncm.StartsWith("2710"))
                              || (xProd ?? "").ToUpperInvariant().Contains("DIESEL")
                              || (xProd ?? "").ToUpperInvariant().Contains("GASOL")
                              || (xProd ?? "").ToUpperInvariant().Contains("ETANOL");

                list.Add(new NfeParsedItem
                {
                    ChaveNFe = chave,
                    NumeroNFe = nNF,
                    Serie = serie,
                    DataEmissao = dhEmi,

                    EmitCNPJ = emitCNPJ,
                    EmitNome = emitNome,
                    UFEmit = ufEmit,
                    CidadeEmit = xMunEmit,
                    CMunEmit = cMunEmit,

                    DestCNPJ = destCNPJ,
                    DestNome = destNome,
                    UFDest = ufDest,
                    CidadeDest = xMunDest,
                    CMunDest = cMunDest,

                    NumeroItem = nItem,
                    CodigoProduto = cProd,
                    DescricaoProduto = xProd,
                    NCM = ncm,
                    CFOP = cfop,
                    Unidade = uCom,
                    Quantidade = qCom,
                    ValorUnitario = vUn,
                    ValorTotal = vProd,

                    ProdANP = cProdANP,
                    DescANP = descANP ?? xProd,
                    UFConsumo = ufCons,

                    PlacaObservada = placaInf,
                    IsCombustivel = isComb
                });
            }

            if (list.Count == 0)
            {
                list.Add(new NfeParsedItem
                {
                    ChaveNFe = chave,
                    NumeroNFe = nNF,
                    Serie = serie,
                    DataEmissao = dhEmi,
                    EmitCNPJ = emitCNPJ,
                    EmitNome = emitNome,
                    UFEmit = ufEmit,
                    CidadeEmit = xMunEmit,
                    CMunEmit = cMunEmit,
                    DestCNPJ = destCNPJ,
                    DestNome = destNome,
                    UFDest = ufDest,
                    CidadeDest = xMunDest,
                    CMunDest = cMunDest,
                    PlacaObservada = placaInf,
                    IsCombustivel = false
                });
            }

            return list;
        }

        static int? TryInt(string? s)
            => int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

        static double? TryD(string? s)
            => double.TryParse((s ?? "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

        static string? TryFindPlaca(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var rx = new Regex(@"\b([A-Z]{3}-?\d[A-Z0-9]\d{2})\b", RegexOptions.IgnoreCase);
            var m = rx.Match(text);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant().Replace("-", "") : null;
        }
    }
}
