// Services/ParserMDFe.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CsvIntegratorApp.Services
{
    public sealed class MdfeHeader
    {
        // ide
        public string? NumeroMdf { get; set; }
        public string? Serie { get; set; }
        public string? Mod { get; set; }
        public string? CMdf { get; set; }
        public string? CDV { get; set; }
        public string? Modal { get; set; }
        public DateTime? DhEmi { get; set; }
        public DateTime? DhIniViagem { get; set; }
        public string? UFIni { get; set; }
        public string? UFFim { get; set; }

        // origem de carregamento (primeira e lista completa em MdfeParsed)
        public string? OrigemCidade { get; set; }
        public int? OrigemCodMun { get; set; }

        // emitente (útil como fallback)
        public string? EmitCnpj { get; set; }
        public string? EmitNome { get; set; }
        public string? EmitUF { get; set; }
        public string? EmitCidade { get; set; }

        // modal rodoviário
        public string? Placa { get; set; }
        public string? Renavam { get; set; }
        public string? TpRod { get; set; }
        public string? TpCar { get; set; }
        public string? VeicUF { get; set; }
        public string? CondutorNome { get; set; }
        public string? CondutorCPF { get; set; }
        public int? TaraKg { get; set; }
        public int? CapKg { get; set; }
        public int? CapM3 { get; set; }

        // totals
        public int? QtdeNFe { get; set; }
        public double? ValorCarga { get; set; }

        public List<string> UfsPercurso { get; set; } = new();
    }

    public sealed class MdfeParsed
    {
        public MdfeHeader Header { get; set; } = new MdfeHeader();

        // lista de origens
        public List<(string xMunCarrega, int cMunCarrega)> Origens { get; set; } = new();

        // chNFe -> (Cidade, UF, CodMun)
        public Dictionary<string, (string? Cidade, string? UF, int? CodMun)> DestinosPorChave
        { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ChavesNFe { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static class ParserMDFe
    {
        static readonly XNamespace ns = "http://www.portalfiscal.inf.br/mdfe";

        public static MdfeParsed Parse(string xmlPath)
        {
            var doc = XDocument.Load(xmlPath);

            var ide = doc.Descendants(ns + "ide").FirstOrDefault();
            var emit = doc.Descendants(ns + "emit").FirstOrDefault();
            var rodo = doc.Descendants(ns + "veicTracao").FirstOrDefault();

            var h = new MdfeHeader
            {
                NumeroMdf = ide?.Element(ns + "nMDF")?.Value,
                Serie = ide?.Element(ns + "serie")?.Value,
                Mod = ide?.Element(ns + "mod")?.Value,
                CMdf = ide?.Element(ns + "cMDF")?.Value,
                CDV = ide?.Element(ns + "cDV")?.Value,
                Modal = ide?.Element(ns + "modal")?.Value,
                UFIni = ide?.Element(ns + "UFIni")?.Value,
                UFFim = ide?.Element(ns + "UFFim")?.Value
            };

            if (DateTime.TryParse(ide?.Element(ns + "dhEmi")?.Value, out var d1)) h.DhEmi = d1;
            if (DateTime.TryParse(ide?.Element(ns + "dhIniViagem")?.Value, out var d2)) h.DhIniViagem = d2;

            // infMunCarrega (múltiplos)
            var origens = new List<(string, int)>();
            foreach (var c in doc.Descendants(ns + "infMunCarrega"))
            {
                var x = c.Element(ns + "xMunCarrega")?.Value;
                int.TryParse(c.Element(ns + "cMunCarrega")?.Value, out var cod);
                if (!string.IsNullOrWhiteSpace(x) && cod > 0) origens.Add((x!, cod));
            }
            if (origens.Count > 0)
            {
                h.OrigemCidade = origens[0].Item1;
                h.OrigemCodMun = origens[0].Item2;
            }

            // emit
            h.EmitCnpj = emit?.Element(ns + "CNPJ")?.Value;
            h.EmitNome = emit?.Element(ns + "xNome")?.Value;
            h.EmitUF = emit?.Element(ns + "enderEmit")?.Element(ns + "UF")?.Value;
            h.EmitCidade = emit?.Element(ns + "enderEmit")?.Element(ns + "xMun")?.Value;

            // veiculo
            h.Placa = rodo?.Element(ns + "placa")?.Value;
            h.Renavam = rodo?.Element(ns + "RENAVAM")?.Value;
            h.TpRod = rodo?.Element(ns + "tpRod")?.Value;
            h.TpCar = rodo?.Element(ns + "tpCar")?.Value;
            h.VeicUF = rodo?.Element(ns + "UF")?.Value;

            if (int.TryParse(rodo?.Element(ns + "tara")?.Value, out var tara)) h.TaraKg = tara;
            if (int.TryParse(rodo?.Element(ns + "capKG")?.Value, out var capkg)) h.CapKg = capkg;
            if (int.TryParse(rodo?.Element(ns + "capM3")?.Value, out var capm3)) h.CapM3 = capm3;

            var cond = rodo?.Element(ns + "condutor");
            h.CondutorNome = cond?.Element(ns + "xNome")?.Value;
            h.CondutorCPF = cond?.Element(ns + "CPF")?.Value;

            // percurso
            var ufs = doc.Descendants(ns + "UFPer").Select(x => x.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            if (ufs.Count > 0) h.UfsPercurso = ufs;

            // totals
            var tot = doc.Descendants(ns + "tot").FirstOrDefault();
            if (int.TryParse(tot?.Element(ns + "qNFe")?.Value, out var qnfe)) h.QtdeNFe = qnfe;
            if (double.TryParse((tot?.Element(ns + "vCarga")?.Value ?? "").Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var vc))
                h.ValorCarga = vc;

            var parsed = new MdfeParsed { Header = h, Origens = origens };

            // chaves soltas (por redundância)
            foreach (var ch in doc.Descendants(ns + "infNFe").Elements(ns + "chNFe"))
            {
                var val = Clean(ch.Value);
                if (!string.IsNullOrWhiteSpace(val)) parsed.ChavesNFe.Add(val);
            }

            // infMunDescarga: mapeia cada chNFe para (cidade, UF pelo IBGE, cod)
            foreach (var infMun in doc.Descendants(ns + "infMunDescarga"))
            {
                int? codMun = null;
                if (int.TryParse(infMun.Element(ns + "cMunDescarga")?.Value, out var cm)) codMun = cm;
                var xMun = infMun.Element(ns + "xMunDescarga")?.Value;
                var uf = UfFromCodMun(codMun);

                foreach (var ch in infMun.Elements(ns + "infNFe").Elements(ns + "chNFe"))
                {
                    var chave = Clean(ch.Value);
                    if (string.IsNullOrWhiteSpace(chave)) continue;

                    parsed.ChavesNFe.Add(chave);
                    parsed.DestinosPorChave[chave] = (xMun, uf, codMun);
                }
            }

            return parsed;
        }

        static string Clean(string s)
        {
            var t = new string(s.Where(char.IsLetterOrDigit).ToArray());
            return t.StartsWith("NFE", StringComparison.OrdinalIgnoreCase) ? t.Substring(3) : t;
        }

        static string? UfFromCodMun(int? codMun)
        {
            if (!codMun.HasValue) return null;
            var ufCod = codMun.Value / 100000;
            return ufCod switch
            {
                11 => "RO",
                12 => "AC",
                13 => "AM",
                14 => "RR",
                15 => "PA",
                16 => "AP",
                17 => "TO",
                21 => "MA",
                22 => "PI",
                23 => "CE",
                24 => "RN",
                25 => "PB",
                26 => "PE",
                27 => "AL",
                28 => "SE",
                29 => "BA",
                31 => "MG",
                32 => "ES",
                33 => "RJ",
                35 => "SP",
                41 => "PR",
                42 => "SC",
                43 => "RS",
                50 => "MS",
                51 => "MT",
                52 => "GO",
                53 => "DF",
                _ => null
            };
        }
    }
}
