namespace Coverage.Core.Models;

public sealed record PdfReportExportRequest
{
    public required string ProjectName { get; init; }

    public required string ApplicationPath { get; init; }

    public required string ProjectType { get; init; }

    public CoverageAnalysisResult? Coverage { get; init; }

    public CoverageQualityReport? QualityReport { get; init; }

    public StaticAnalysisResult? StaticAnalysis { get; init; }

    public decimal? MinimumOverallCoverage { get; init; }

    public decimal? MinimumFileCoverage { get; init; }

    public bool FailOnThresholdViolation { get; init; }
}
