using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsvIntegratorApp.Models.DTO
{
    public record DieselTotalDto(
        string ChaveNFe,
        string? NumeroNFe,
        DateTime? DataEmissao,
        double LitrosDiesel,
        double ValorTotalDiesel,
        double? ValorUnitMedio,
        int ItensDiesel
    );

}
