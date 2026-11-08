namespace Coverage.Core.Models;

public enum CoverageProjectKind
{
    Microservice,
    FrontendMvc,
}

public sealed record CoverageProjectKindOption(CoverageProjectKind Value, string Label)
{
    public override string ToString() => Label;
}
