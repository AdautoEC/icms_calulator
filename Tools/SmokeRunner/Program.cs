using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvIntegratorApp;
using CsvIntegratorApp.Services;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: SmokeRunner <folder>");
            return 1;
        }

        var folder = args[0];
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"Folder not found: {folder}");
            return 1;
        }

        var culture = new CultureInfo("pt-BR");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        var txtFiles = Directory.GetFiles(folder, "*.txt");
        var xmlFiles = Directory.GetFiles(folder, "*.xml");
        var mdfeFiles = xmlFiles
            .Where(f => Path.GetFileName(f).Contains("mdfe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var nfeFiles = xmlFiles.Except(mdfeFiles).ToList();

        if (txtFiles.Length == 0) Console.WriteLine("WARN: no SPED TXT found.");
        if (!mdfeFiles.Any()) Console.WriteLine("WARN: no MDFe XML found.");
        if (!nfeFiles.Any()) Console.WriteLine("WARN: no NFe XML found.");

        SpedTxtLookupService.LoadTxt(txtFiles.ToList());
        var nfeItems = nfeFiles.SelectMany(ParserNFe.Parse).ToList();
        var mdfes = mdfeFiles.Select(ParserMDFe.Parse).ToList();

        var progress = new Progress<ProgressReport>(report =>
        {
            Console.WriteLine($"{report.Percentage}% {report.StatusMessage}");
        });

        var rows = await MergeService.MergeAsync(nfeItems, mdfes, progress, true);
        CalculationLogService.Save();

        Console.WriteLine($"Rows generated: {rows.Count}");
        Console.WriteLine($"Log file: {CalculationLogService.LogFilePath}");
        return 0;
    }
}
