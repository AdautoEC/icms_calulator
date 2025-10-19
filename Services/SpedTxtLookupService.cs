using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;

namespace CsvIntegratorApp.Services
{
    public static class SpedTxtLookupService
    {
        // chNFe -> COD_PART
        private static readonly Dictionary<string, string> _mapChaveParaPart = new(StringComparer.OrdinalIgnoreCase);

        // COD_PART -> (COD_MUN, street, number, neighborhood, NOME)
        private static readonly Dictionary<string, (int? codMun, string? street, string? number, string? neighborhood, string? nome)> _mapPartes =
            new(StringComparer.OrdinalIgnoreCase);

        // chNFe -> Data Emissão (C100)
        private static readonly Dictionary<string, DateTime?> _mapChaveParaDataEmissao = new(StringComparer.OrdinalIgnoreCase);

        // chNFe -> C190 data
        private static readonly Dictionary<string, List<(string? cst, string? cfop, decimal? valorIcms, decimal? baseIcms, decimal? totalDocumento)>> _mapChaveParaC190 = new(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;

        public static void LoadTxt(List<string> paths)
        {
            _mapChaveParaPart.Clear();
            _mapPartes.Clear();
            _mapChaveParaC190.Clear();
            _loaded = false;

            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;

                string? lastChNFe = null;

                foreach (var raw in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    if (raw[0] != '|' || raw.Length < 3) continue;

                    var cols = raw.Split('|'); // [0] vazio, [1]=registro
                    if (cols.Length < 2) continue;

                    var reg = cols[1];

                    if (reg == "C100")
                    {
                        // |C100|ind_oper|ind_emit|cod_part|cod_mod|cod_sit|ser|num_doc|chv_nfe|dt_doc|...
                        string? codPart = cols.Length > 4 ? cols[4] : null;
                        string? dtDocStr = cols.Length > 10 ? cols[10] : null;

                        // procura a primeira coluna com 44 dígitos como chave
                        string? ch = cols.FirstOrDefault(c => c != null && c.Length == 44 && c.All(char.IsDigit));

                        if (!string.IsNullOrWhiteSpace(ch))
                        {
                            lastChNFe = Clean(ch);
                            if (!string.IsNullOrWhiteSpace(codPart))
                            {
                                _mapChaveParaPart[lastChNFe] = codPart.Trim();
                            }

                            if (DateTime.TryParseExact(dtDocStr, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtDoc))
                            {
                                _mapChaveParaDataEmissao[lastChNFe] = dtDoc;
                            }
                        }
                    }
                    else if (reg == "C190")
                    {
                        if (lastChNFe != null)
                        {
                            // |C190|CST|CFOP|ALIQ_ICMS|VL_OPR|VL_BC_ICMS|VL_ICMS|...
                            string? cst = cols.Length > 2 ? cols[2] : null;
                            string? cfop = cols.Length > 3 ? cols[3] : null;
                            decimal? totalDocumento = cols.Length > 5 && decimal.TryParse(cols[5], out var val) ? val : null;
                            decimal? baseIcms = cols.Length > 6 && decimal.TryParse(cols[6], out var val2) ? val2 : null;
                            decimal? valorIcms = cols.Length > 7 && decimal.TryParse(cols[7], out var val3) ? val3 : null;

                            if (!_mapChaveParaC190.ContainsKey(lastChNFe))
                            {
                                _mapChaveParaC190[lastChNFe] = new List<(string? cst, string? cfop, decimal? valorIcms, decimal? baseIcms, decimal? totalDocumento)>();
                            }
                            _mapChaveParaC190[lastChNFe].Add((cst, cfop, valorIcms, baseIcms, totalDocumento));
                        }
                    }
                    else if (reg == "0150")
                    {
                        // |0150|cod_part|nome|cod_pais|cnpj|cpf|ie|cod_mun|suframa|end|num|compl|bairro|
                        string? codPart = cols.Length > 2 ? cols[2] : null;
                        if (string.IsNullOrWhiteSpace(codPart)) continue;

                        int? codMun = null;
                        if (cols.Length > 8 && int.TryParse(cols[8], out var cm)) codMun = cm;

                        string? nome = cols.Length > 3 ? cols[3] : null;
                        string? street = cols.Length > 10 ? cols[10] : null;
                        string? number = cols.Length > 11 ? cols[11] : null;
                        string? neighborhood = cols.Length > 13 ? cols[13] : null;

                        _mapPartes[codPart.Trim()] = (codMun, street, number, neighborhood, nome);
                    }
                }
            }

            _loaded = true;
        }

        public static bool TryGetAddressInfoPorChave(string? chNFe, out (string? street, string? number, string? neighborhood, string? uf) addressInfo)
        {
            addressInfo = default;
            if (!_loaded || string.IsNullOrWhiteSpace(chNFe)) return false;

            var ch = Clean(chNFe);
            if (!_mapChaveParaPart.TryGetValue(ch, out var codPart)) return false;

            if (_mapPartes.TryGetValue(codPart, out var info))
            {
                var uf = UfFromCodMun(info.codMun);
                addressInfo = (info.street, info.number, info.neighborhood, uf);
                return true;
            }

            return false;
        }

        public static bool TryGetC190InfoPorChave(string? chNFe, out List<(string? cst, string? cfop, decimal? valorIcms, decimal? baseIcms, decimal? totalDocumento)> c190Info)
        {
            c190Info = null;
            if (!_loaded || string.IsNullOrWhiteSpace(chNFe)) return false;

            var ch = Clean(chNFe);
            return _mapChaveParaC190.TryGetValue(ch, out c190Info);
        }

        public static bool TryGetC100DataPorChave(string? chNFe, out DateTime? dataEmissao)
        {
            dataEmissao = null;
            if (!_loaded || string.IsNullOrWhiteSpace(chNFe)) return false;

            var ch = Clean(chNFe);
            return _mapChaveParaDataEmissao.TryGetValue(ch, out dataEmissao);
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

