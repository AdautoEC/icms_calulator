using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CsvIntegratorApp.Services.Sped
{
    public sealed class SpedParticipant
    {
        public string CodPart { get; set; } = "";
        public string? Nome { get; set; }
        public string? CodMunIbge { get; set; } // 7 dígitos (IBGE)
        public string? Endereco { get; set; }
    }

    public sealed class SpedNFeDoc
    {
        public string ChaveNFe { get; set; } = "";
        public string? CodPart { get; set; }
        public DateTime? DtDoc { get; set; }
        public string? NumDoc { get; set; }
    }

    public sealed class SpedEfdData
    {
        public Dictionary<string, SpedParticipant> ParticipantsByCodPart { get; } = new();
        public Dictionary<string, SpedNFeDoc> NfeByChave { get; } = new();
    }

    public static class SpedEfdTxtReader
    {
        public static SpedEfdData Parse(string path)
        {
            var data = new SpedEfdData();
            using var sr = new StreamReader(path);

            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var f = line.Split('|');
                if (f.Length < 2) continue;

                var reg = Get(f, 1);

                if (reg == "0150")
                {
                    // 0150: 02 COD_PART, 03 NOME, 08 COD_MUN (IBGE), 10 END
                    var codPart = Get(f, 2);
                    if (string.IsNullOrEmpty(codPart)) continue;

                    data.ParticipantsByCodPart[codPart] = new SpedParticipant
                    {
                        CodPart = codPart,
                        Nome = Get(f, 3),
                        CodMunIbge = Get(f, 8),
                        Endereco = Get(f, 10)
                    };
                }
                else if (reg == "C100")
                {
                    // C100 (modelo 55): 04 COD_PART, 05 COD_MOD, 08 NUM_DOC, 09 CHV_NFE, 10 DT_DOC
                    var codMod = Get(f, 5);
                    if (codMod != "55") continue; // só NF-e

                    var chave = Get(f, 9);
                    if (string.IsNullOrWhiteSpace(chave) || chave.Length != 44) continue;

                    data.NfeByChave[chave] = new SpedNFeDoc
                    {
                        ChaveNFe = chave,
                        CodPart = Get(f, 4),
                        NumDoc = Get(f, 8),
                        DtDoc = ParseData(Get(f, 10))
                    };
                }
            }
            return data;
        }

        private static string Get(string[] arr, int idx)
            => (idx >= 0 && idx < arr.Length) ? arr[idx] : "";

        private static DateTime? ParseData(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (DateTime.TryParseExact(s, "ddMMyyyy", CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out var dt))
                return dt;
            return null;
        }
    }
}
