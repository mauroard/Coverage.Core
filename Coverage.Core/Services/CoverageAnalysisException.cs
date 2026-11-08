using System;

namespace Coverage.Core.Services;

public sealed class CoverageAnalysisException : Exception
{
    public CoverageAnalysisException(string message)
        : base(message)
    {
    }

    public CoverageAnalysisException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
