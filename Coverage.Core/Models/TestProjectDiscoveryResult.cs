namespace Coverage.Core.Models;

public sealed record TestProjectDiscoveryResult(
    string? ProjectPath,
    string Message,
    string? ApplicationProjectPath = null)
{
    public bool IsFound => !string.IsNullOrWhiteSpace(ProjectPath);
}
