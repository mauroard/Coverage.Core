namespace Coverage.Core.Models;

public sealed record StaticAnalysisFinding
{
    public required StaticAnalysisSeverity Severity { get; init; }

    public required string RuleId { get; init; }

    public required string RelativePath { get; init; }

    public required int Line { get; init; }

    public required int Column { get; init; }

    public required string Message { get; init; }

    public string SeverityLabel => Severity switch
    {
        StaticAnalysisSeverity.Error => "Errore",
        StaticAnalysisSeverity.Warning => "Avviso",
        _ => "Informazione",
    };

    public string LocationText => Line > 0
        ? Column > 0 ? $"Riga {Line}, colonna {Column}" : $"Riga {Line}"
        : "Posizione non disponibile";
}
