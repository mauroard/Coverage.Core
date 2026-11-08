using System.Collections.Generic;
using System.Linq;

namespace Coverage.Core.Models;

public sealed class CoverageAnalysisResult
{
    public required CoverageProjectKind ProjectKind { get; init; }

    public required IReadOnlyList<CoverageFileResult> Files { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required string ReportDirectory { get; init; }

    public int TotalFiles => Files.Count;

    public int FilesWithCoverage => Files.Count(file => file.HasCoverageData);

    public int CoveredLines => Files.Sum(file => file.CoveredLines);

    public int TotalLines => Files.Sum(file => file.TotalLines);

    public double CoveragePercentage => TotalLines == 0
        ? 0
        : (double)CoveredLines / TotalLines * 100;

    public string CoverageLabel => TotalLines == 0
        ? "Nessun dato"
        : $"{CoveragePercentage:0.0}%";

    public string SummaryText => TotalLines == 0
        ? $"Nessuna linea misurata su {TotalFiles} file sorgente."
        : $"{CoveredLines:N0} linee coperte su {TotalLines:N0}; dati disponibili per {FilesWithCoverage} file su {TotalFiles}.";
}
