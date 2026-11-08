namespace Coverage.Core.Models;

public enum StaticAnalysisSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record StaticAnalysisSeverityOption(StaticAnalysisSeverity? Value, string Label);
