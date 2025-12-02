// Services/FuelAllocator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using CsvIntegratorApp.Services;

namespace CsvIntegratorApp.Services
{
    /// <summary>
    /// Allocates DIESEL liters from NFe items across trips (MDF-e) without exceeding item or note totals.
    /// - Tracks remaining liters per (ChaveNFe, NumeroItem).
    /// - Allows partial allocations of an item across multiple trips.
    /// - Filters to DIESEL-only items (by ANP family 8201 or description containing DIESEL).
    /// </summary>
    public class FuelAllocator
    {
        public record ItemKey(string? ChaveNFe, int? NumeroItem);

        public record Allocation(NfeParsedItem Item, double LitrosAlocados);

        // Remaining liters by item
        private readonly Dictionary<ItemKey, double> _remaining;

        // All diesel items by key
        private readonly Dictionary<ItemKey, NfeParsedItem> _items;

        public FuelAllocator(IEnumerable<NfeParsedItem> nfeItemsDiesel)
        {
            _items = nfeItemsDiesel
                .Where(IsDieselItem)
                .Where(i => i.Quantidade.HasValue && i.Quantidade.Value > 0)
                .ToDictionary(i => new ItemKey(i.ChaveNFe, i.NumeroItem), i => i);

            _remaining = _items.ToDictionary(kv => kv.Key, kv => kv.Value.Quantidade ?? 0.0);
        }

        public static bool IsDieselItem(NfeParsedItem n)
        {
            var anp = (n.ProdANP ?? "").Trim();
            var desc = (n.DescricaoProduto ?? "OLEO DIESEL").ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(anp) && anp.StartsWith("8201")) return true; // família diesel
            return desc.Contains("DIESEL");
        }

        /// <summary>
        /// Allocate up to alvoLitros from the remaining pool, allowing partial usage of items.
        /// Returns allocations in input order (older first) to mimic FIFO.
        /// </summary>
        public List<Allocation> Allocate(double? alvoLitros)
        {
            var result = new List<Allocation>();
            if (alvoLitros == null || alvoLitros <= 0) return result;

            double restante = alvoLitros.Value;
            const double eps = 1e-6;

            // FIFO by issue date then by note number/item
            var ordered = _items.Values
                .OrderBy(i => i.DataEmissao ?? DateTime.MaxValue)
                .ThenBy(i => i.NumeroNFe)
                .ThenBy(i => i.NumeroItem ?? 0)
                .Select(i => new ItemKey(i.ChaveNFe, i.NumeroItem))
                .ToList();

            foreach (var key in ordered)
            {
                if (restante <= eps) break;
                if (!_remaining.TryGetValue(key, out var saldo) || saldo <= eps) continue;

                var usar = Math.Min(restante, saldo);
                restante -= usar;
                _remaining[key] = saldo - usar;
                var item = _items[key];
                result.Add(new Allocation(item, Math.Round(usar, 6)));
            }

            return result;
        }

        /// <summary>
        /// Gets the remaining liters for a given item.
        /// </summary>
        public double RemainingForItem(string? chave, int? numeroItem)
        {
            return _remaining.TryGetValue(new ItemKey(chave, numeroItem), out var v) ? v : 0.0;
        }

        /// <summary>
        /// Total remaining liters across all diesel items.
        /// </summary>
        public double TotalRemainingLiters() => _remaining.Values.Sum();
    }
}