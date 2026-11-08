namespace Coverage.Core.Models;

public sealed class CoverageFileResult
{
    public required string RelativePath { get; init; }

    public required string Language { get; init; }

    public required string Extension { get; init; }

    public int CoveredLines { get; init; }

    public int TotalLines { get; init; }

    public bool HasCoverageData => TotalLines > 0;

    public double CoveragePercentage => TotalLines == 0
        ? 0
        : (double)CoveredLines / TotalLines * 100;

    public string CoverageText => HasCoverageData
        ? $"{CoveragePercentage:0.0}%"
        : "Nessun dato";

    public string LinesText => HasCoverageData
        ? $"{CoveredLines}/{TotalLines} linee"
        : "Non misurato";
}
