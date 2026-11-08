using System;

namespace Coverage.Core.Services;

public sealed class StaticAnalysisException : Exception
{
    public StaticAnalysisException(string message)
        : base(message)
    {
    }

    public StaticAnalysisException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
