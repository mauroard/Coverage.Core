using System;
using System.Collections.Generic;

namespace Coverage.Core.Models;

public sealed class CoverageQualityReport
{
    public required bool HasThresholds { get; init; }

    public required bool IsFailure { get; init; }

    public required IReadOnlyList<string> Findings { get; init; }

    public bool HasFindings => Findings.Count > 0;

    public bool HasPassed => HasThresholds && !HasFindings;

    public string FindingsText => string.Join(Environment.NewLine, Findings);
}
