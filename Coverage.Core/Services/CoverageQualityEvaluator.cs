using System;
using System.Collections.Generic;
using System.Linq;
using Coverage.Core.Models;

namespace Coverage.Core.Services;

public static class CoverageQualityEvaluator
{
    public static CoverageQualityReport Evaluate(
        CoverageAnalysisResult analysis,
        decimal? minimumOverallCoverage,
        decimal? minimumFileCoverage,
        bool failOnViolation)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ValidateThreshold(minimumOverallCoverage, nameof(minimumOverallCoverage));
        ValidateThreshold(minimumFileCoverage, nameof(minimumFileCoverage));

        var findings = new List<string>();
        if (minimumOverallCoverage is decimal overallMinimum)
        {
            if (analysis.TotalLines == 0)
            {
                findings.Add($"Coverage complessiva non disponibile: impossibile verificare la soglia minima del {overallMinimum:0.##}%.");
            }
            else
            {
                var overallCoverage = CalculatePercentage(analysis.CoveredLines, analysis.TotalLines);
                if (overallCoverage < overallMinimum)
                {
                    findings.Add($"Coverage complessiva {overallCoverage:0.###}% ({analysis.CoveredLines}/{analysis.TotalLines} linee) inferiore alla soglia minima del {overallMinimum:0.##}%.");
                }
            }
        }

        if (minimumFileCoverage is decimal fileMinimum)
        {
            var measuredFiles = analysis.Files.Where(file => file.HasCoverageData).ToArray();
            if (measuredFiles.Length == 0)
            {
                findings.Add($"Coverage per file non disponibile: impossibile verificare la soglia minima del {fileMinimum:0.##}%.");
            }
            else
            {
                foreach (var file in measuredFiles)
                {
                    var fileCoverage = CalculatePercentage(file.CoveredLines, file.TotalLines);
                    if (fileCoverage < fileMinimum)
                    {
                        findings.Add($"{file.RelativePath}: coverage {fileCoverage:0.###}% ({file.CoveredLines}/{file.TotalLines} linee) inferiore alla soglia minima per file del {fileMinimum:0.##}%.");
                    }
                }
            }
        }

        var hasThresholds = minimumOverallCoverage.HasValue || minimumFileCoverage.HasValue;
        return new CoverageQualityReport
        {
            HasThresholds = hasThresholds,
            IsFailure = hasThresholds && failOnViolation && findings.Count > 0,
            Findings = findings.ToArray(),
        };
    }

    private static decimal CalculatePercentage(int coveredLines, int totalLines) =>
        (decimal)coveredLines / totalLines * 100m;

    private static void ValidateThreshold(decimal? threshold, string parameterName)
    {
        if (threshold.HasValue && (threshold.Value < 0m || threshold.Value > 100m))
        {
            throw new ArgumentOutOfRangeException(parameterName, threshold, "La soglia deve essere compresa tra 0 e 100.");
        }
    }
}
