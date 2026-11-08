using System.Collections.Generic;

namespace Coverage.Core.Models;

public sealed class StaticAnalysisResult
{
    public required IReadOnlyList<StaticAnalysisFinding> Findings { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}
