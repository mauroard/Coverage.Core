using System;

namespace Coverage.Core.Services;

public sealed class PdfReportExportException : Exception
{
    public PdfReportExportException(string message)
        : base(message)
    {
    }
}
