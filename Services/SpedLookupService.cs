using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CsvIntegratorApp.Services
{
    // Lê um CSV com colunas: chNFe;cidade;UF  (ponto e vírgula ou vírgula)
    // Exemplo:
    // chNFe;cidade;UF
    // 502008...4901000005892;Ponta Porã;MS
    public static class SpedLookupService
    {
        private static Dictionary<string, (string cidade, string uf)> _map =
            new(StringComparer.OrdinalIgnoreCase);

        public static void Load(string path)
        {
            _map.Clear();
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0) return;

            // detecta separador
            char sep = lines[0].Contains(';') ? ';' : ',';

            foreach (var raw in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var cols = raw.Split(sep);
                if (cols.Length < 3) continue;

                var ch = Clean(cols[0]);
                var cidade = cols[1].Trim();
                var uf = cols[2].Trim().ToUpperInvariant();

                if (string.IsNullOrWhiteSpace(ch)) continue;
                _map[ch] = (cidade, uf);
            }
        }

        public static bool TryGetDestino(string? chNFe, out (string cidade, string uf) dst)
        {
            dst = default;
            if (string.IsNullOrWhiteSpace(chNFe)) return false;
            return _map.TryGetValue(Clean(chNFe), out dst);
        }

        private static string Clean(string s)
        {
            var t = new string(s.Where(char.IsLetterOrDigit).ToArray());
            // chNFe costuma ter 44 dígitos; se vier prefixo "NFe...", removemos
            if (t.StartsWith("NFE", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(3);
            return t;
        }
    }
}

