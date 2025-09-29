using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CsvIntegratorApp.Services
{
    public static class SpedTxtLookupService
    {
        // chNFe -> COD_PART
        private static readonly Dictionary<string, string> _mapChaveParaPart = new(StringComparer.OrdinalIgnoreCase);

        // COD_PART -> (COD_MUN, END opcional, NOME opcional)
        private static readonly Dictionary<string, (int? codMun, string? end, string? nome)> _mapPartes =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;

        public static void LoadTxt(string path)
        {
            _mapChaveParaPart.Clear();
            _mapPartes.Clear();
            _loaded = false;

            if (!File.Exists(path)) return;

            foreach (var raw in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (raw[0] != '|' || raw.Length < 3) continue;

                var cols = raw.Split('|'); // [0] vazio, [1]=registro
                if (cols.Length < 2) continue;

                var reg = cols[1];

                if (reg == "C100")
                {
                    // |C100|ind_oper|ind_emit|cod_part|cod_mod|cod_sit|ser|num_doc|chv_nfe|...
                    string? codPart = cols.Length > 4 ? cols[4] : null;

                    // procura a primeira coluna com 44 dígitos como chave
                    string? ch = cols.FirstOrDefault(c => c != null && c.Length == 44 && c.All(char.IsDigit));

                    if (!string.IsNullOrWhiteSpace(ch) && !string.IsNullOrWhiteSpace(codPart))
                    {
                        _mapChaveParaPart[Clean(ch)] = codPart.Trim();
                    }
                }
                else if (reg == "0150")
                {
                    // |0150|cod_part|nome|cod_pais|cnpj|cpf|ie|cod_mun|suframa|end|num|compl|bairro|
                    string? codPart = cols.Length > 2 ? cols[2] : null;

                    int? codMun = null;
                    if (cols.Length > 8 && int.TryParse(cols[8], out var cm)) codMun = cm;

                    string? end = cols.Length > 10 ? cols[10] : null;
                    string? nome = cols.Length > 3 ? cols[3] : null;

                    if (!string.IsNullOrWhiteSpace(codPart))
                    {
                        _mapPartes[codPart.Trim()] = (codMun, end, nome);
                    }
                }
            }

            _loaded = true;
        }

        public static bool TryGetDestinoPorChave(string? chNFe, out (string? cidade, string? uf) dest)
        {
            dest = default;
            if (!_loaded || string.IsNullOrWhiteSpace(chNFe)) return false;

            var ch = Clean(chNFe);
            if (!_mapChaveParaPart.TryGetValue(ch, out var codPart)) return false;

            if (_mapPartes.TryGetValue(codPart, out var info))
            {
                var uf = UfFromCodMun(info.codMun);
                // cidade normalmente exige tabela IBGE → deixamos nula;
                // o merge combinará "cidade da NFe" + "UF do SPED" se precisar.
                dest = (null, uf);
                return uf != null;
            }

            return false;
        }

        private static string Clean(string s)
        {
            var t = new string(s.Where(char.IsLetterOrDigit).ToArray());
            if (t.StartsWith("NFE", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(3);
            return t;
        }

        // mapeia os dois primeiros dígitos do COD_MUN (IBGE) para UF
        private static string? UfFromCodMun(int? codMun)
        {
            if (!codMun.HasValue) return null;
            var ufCod = codMun.Value / 100000; // 2 primeiros dígitos

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

