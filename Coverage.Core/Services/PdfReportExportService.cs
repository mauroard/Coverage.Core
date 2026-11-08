using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Coverage.Core.Models;

namespace Coverage.Core.Services;

public sealed class PdfReportExportService
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double Margin = 44;
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");
    private static readonly Encoding Ascii = Encoding.ASCII;

    public byte[] Generate(PdfReportExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Coverage is null && request.StaticAnalysis is null)
        {
            throw new PdfReportExportException("Non ci sono risultati da esportare. Esegui prima un'analisi coverage o statica.");
        }

        var document = new PdfDocumentWriter();
        document.AddParagraph("Report di analisi", 20, bold: true, color: PdfColor.Navy);
        document.AddParagraph($"Progetto: {request.ProjectName}", 11, bold: true);
        document.AddParagraph($"Tipo: {request.ProjectType}");
        document.AddParagraph($"Percorso: {request.ApplicationPath}", 9);
        document.AddParagraph($"Generato il {DateTime.Now.ToString("dd MMMM yyyy, HH:mm", ItalianCulture)}", 9, color: PdfColor.Muted);
        document.AddParagraph("Il report include tutti i rilievi disponibili, indipendentemente dai filtri attivi nell'interfaccia.", 8, color: PdfColor.Muted);
        document.AddSpacer(8);

        if (request.Coverage is not null)
        {
            AddCoverageSection(
                document,
                request.Coverage,
                request.QualityReport,
                request.MinimumOverallCoverage,
                request.MinimumFileCoverage,
                request.FailOnThresholdViolation);
        }
        else
        {
            document.AddSectionHeading("Coverage");
            document.AddParagraph("Analisi coverage non eseguita.");
        }

        if (request.StaticAnalysis is not null)
        {
            AddStaticAnalysisSection(document, request.StaticAnalysis);
        }
        else
        {
            document.AddSectionHeading("Analisi statica");
            document.AddParagraph("Analisi statica non eseguita.");
        }

        return document.Build();
    }

    private static void AddCoverageSection(
        PdfDocumentWriter document,
        CoverageAnalysisResult coverage,
        CoverageQualityReport? qualityReport,
        decimal? minimumOverallCoverage,
        decimal? minimumFileCoverage,
        bool failOnThresholdViolation)
    {
        document.AddSectionHeading("Coverage");
        document.AddParagraph($"Copertura complessiva: {FormatCoverage(coverage)}", 12, bold: true);
        document.AddParagraph($"Linee coperte: {coverage.CoveredLines.ToString("N0", ItalianCulture)} su {coverage.TotalLines.ToString("N0", ItalianCulture)}");
        document.AddParagraph($"File con dati: {coverage.FilesWithCoverage} su {coverage.TotalFiles}");

        if (minimumOverallCoverage.HasValue || minimumFileCoverage.HasValue)
        {
            var gateStatus = qualityReport is null
                ? "non valutato"
                : qualityReport.IsFailure
                    ? "NON SUPERATO"
                    : qualityReport.HasFindings
                        ? "AVVISI"
                        : qualityReport.HasPassed
                            ? "SUPERATO"
                            : "non valutato";
            document.AddParagraph($"Quality gate: {gateStatus}", 10, bold: true);
            document.AddParagraph($"Soglia complessiva: {FormatThreshold(minimumOverallCoverage)}; soglia per file: {FormatThreshold(minimumFileCoverage)}");
            document.AddParagraph($"Violazioni come esito negativo: {(failOnThresholdViolation ? "sì" : "no")}");

            if (qualityReport is { HasFindings: true })
            {
                document.AddParagraph("Dettagli quality gate", 10, bold: true);
                AddBulletList(document, qualityReport.Findings, PdfColor.Muted);
            }
        }
        else
        {
            document.AddParagraph("Quality gate: disattivato.");
        }

        if (coverage.Warnings.Count > 0)
        {
            document.AddParagraph("Avvisi coverage", 10, bold: true);
            AddBulletList(document, coverage.Warnings, PdfColor.Warning);
        }

        document.AddParagraph($"File analizzati ({coverage.TotalFiles})", 10, bold: true);
        foreach (var file in coverage.Files)
        {
            var percentage = file.HasCoverageData
                ? $"{file.CoveragePercentage.ToString("0.0", ItalianCulture)}%"
                : "Nessun dato";
            var lines = file.HasCoverageData
                ? $"{file.CoveredLines.ToString("N0", ItalianCulture)}/{file.TotalLines.ToString("N0", ItalianCulture)} linee"
                : "Non misurato";
            document.AddParagraph($"{file.RelativePath}  |  {percentage}  |  {lines}", 9);
        }
    }

    private static void AddStaticAnalysisSection(PdfDocumentWriter document, StaticAnalysisResult staticAnalysis)
    {
        document.AddSectionHeading("Analisi statica");
        var errors = staticAnalysis.Findings.Count(finding => finding.Severity == StaticAnalysisSeverity.Error);
        var warnings = staticAnalysis.Findings.Count(finding => finding.Severity == StaticAnalysisSeverity.Warning);
        var information = staticAnalysis.Findings.Count(finding => finding.Severity == StaticAnalysisSeverity.Info);
        document.AddParagraph($"Rilievi: {staticAnalysis.Findings.Count} totali — {errors} errori, {warnings} avvisi, {information} informazioni");

        if (staticAnalysis.Warnings.Count > 0)
        {
            document.AddParagraph("Avvisi dell'analisi", 10, bold: true);
            AddBulletList(document, staticAnalysis.Warnings, PdfColor.Warning);
        }

        if (staticAnalysis.Findings.Count == 0)
        {
            document.AddParagraph("Nessun rilievo statico trovato.");
            return;
        }

        document.AddParagraph("Rilievi", 10, bold: true);
        foreach (var finding in staticAnalysis.Findings)
        {
            var color = finding.Severity switch
            {
                StaticAnalysisSeverity.Error => PdfColor.Error,
                StaticAnalysisSeverity.Warning => PdfColor.Warning,
                _ => PdfColor.Muted,
            };
            document.AddParagraph(
                $"{finding.SeverityLabel}  |  {finding.RuleId}  |  {finding.RelativePath}  |  {finding.LocationText}",
                9,
                bold: true,
                color: color);
            document.AddParagraph(finding.Message, 9, indent: 12);
            document.AddSpacer(3);
        }
    }

    private static void AddBulletList(PdfDocumentWriter document, IEnumerable<string> values, PdfColor color)
    {
        foreach (var value in values)
        {
            document.AddParagraph($"- {value}", 9, indent: 8, color: color);
        }
    }

    private static string FormatCoverage(CoverageAnalysisResult coverage) => coverage.TotalLines == 0
        ? "Nessun dato"
        : $"{coverage.CoveragePercentage.ToString("0.0", ItalianCulture)}%";

    private static string FormatThreshold(decimal? threshold) => threshold.HasValue
        ? $"{threshold.Value.ToString("0.##", ItalianCulture)}%"
        : "disattivata";

    private readonly record struct PdfColor(double Red, double Green, double Blue)
    {
        public static PdfColor Navy { get; } = new(0.09, 0.20, 0.31);
        public static PdfColor Muted { get; } = new(0.38, 0.42, 0.48);
        public static PdfColor Warning { get; } = new(0.48, 0.31, 0.02);
        public static PdfColor Error { get; } = new(0.60, 0.12, 0.12);
        public static PdfColor LightRule { get; } = new(0.84, 0.87, 0.91);
    }

    private sealed class PdfDocumentWriter
    {
        private const double BottomMargin = 50;
        private const double FooterHeight = 20;
        private const double LineSpacing = 3;
        private readonly List<StringBuilder> _pages = new();
        private StringBuilder _currentPage = null!;
        private double _cursorY;

        public PdfDocumentWriter() => StartPage();

        public void AddParagraph(string text, double fontSize = 10, bool bold = false, double indent = 0, PdfColor? color = null)
        {
            var lineHeight = fontSize + LineSpacing;
            var maxCharacters = Math.Max(16, (int)((PageWidth - Margin * 2 - indent) / (fontSize * 0.52)));
            foreach (var line in WrapText(text, maxCharacters))
            {
                EnsureSpace(lineHeight);
                AddText(line, Margin + indent, _cursorY, fontSize, bold, color ?? PdfColor.Navy);
                _cursorY -= lineHeight;
            }
        }

        public void AddSectionHeading(string heading)
        {
            EnsureSpace(32);
            AddText(heading, Margin, _cursorY, 14, bold: true, PdfColor.Navy);
            _cursorY -= 7;
            AddRule(_cursorY);
            _cursorY -= 13;
        }

        public void AddSpacer(double height) => _cursorY -= height;

        public byte[] Build()
        {
            for (var index = 0; index < _pages.Count; index++)
            {
                AddTextTo(_pages[index], $"Pagina {index + 1} di {_pages.Count}", PageWidth - Margin - 70, 28, 8, bold: false, PdfColor.Muted);
                AddRuleTo(_pages[index], Margin, 42, PageWidth - Margin, PdfColor.LightRule);
            }

            return BuildPdf();
        }

        private void StartPage()
        {
            _currentPage = new StringBuilder();
            _pages.Add(_currentPage);
            AddText("Coverage Inspector", Margin, PageHeight - 30, 8, bold: true, PdfColor.Muted);
            AddRule(PageHeight - 40);
            _cursorY = PageHeight - 64;
        }

        private void EnsureSpace(double height)
        {
            if (_cursorY - height < BottomMargin + FooterHeight)
            {
                StartPage();
            }
        }

        private void AddText(string text, double x, double y, double fontSize, bool bold, PdfColor color) =>
            AddTextTo(_currentPage, text, x, y, fontSize, bold, color);

        private static void AddTextTo(StringBuilder page, string text, double x, double y, double fontSize, bool bold, PdfColor color)
        {
            page.Append("BT /")
                .Append(bold ? "F2" : "F1")
                .Append(' ')
                .Append(FormatNumber(fontSize))
                .Append(" Tf ")
                .Append(FormatNumber(color.Red)).Append(' ')
                .Append(FormatNumber(color.Green)).Append(' ')
                .Append(FormatNumber(color.Blue))
                .Append(" rg 1 0 0 1 ")
                .Append(FormatNumber(x)).Append(' ')
                .Append(FormatNumber(y))
                .Append(" Tm ")
                .Append(ToPdfLiteral(text))
                .AppendLine(" Tj ET");
        }

        private void AddRule(double y) => AddRuleTo(_currentPage, Margin, y, PageWidth - Margin, PdfColor.LightRule);

        private static void AddRuleTo(StringBuilder page, double x1, double y, double x2, PdfColor color)
        {
            page.Append(FormatNumber(color.Red)).Append(' ')
                .Append(FormatNumber(color.Green)).Append(' ')
                .Append(FormatNumber(color.Blue))
                .Append(" RG 0.7 w ")
                .Append(FormatNumber(x1)).Append(' ')
                .Append(FormatNumber(y)).Append(" m ")
                .Append(FormatNumber(x2)).Append(' ')
                .Append(FormatNumber(y)).AppendLine(" l S");
        }

        private byte[] BuildPdf()
        {
            var pageCount = _pages.Count;
            var objectCount = 4 + pageCount * 2;
            var objects = new byte[objectCount + 1][];
            objects[1] = Ascii.GetBytes("<< /Type /Catalog /Pages 2 0 R >>");
            objects[2] = Ascii.GetBytes($"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, pageCount).Select(index => $"{PageObjectNumber(index).ToString(CultureInfo.InvariantCulture)} 0 R"))}] /Count {pageCount.ToString(CultureInfo.InvariantCulture)} >>");
            objects[3] = Ascii.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            objects[4] = Ascii.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

            for (var index = 0; index < pageCount; index++)
            {
                var pageObject = PageObjectNumber(index);
                var contentObject = pageObject + 1;
                var content = Ascii.GetBytes(_pages[index].ToString());
                objects[pageObject] = Ascii.GetBytes(
                    $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {FormatNumber(PageWidth)} {FormatNumber(PageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObject.ToString(CultureInfo.InvariantCulture)} 0 R >>");
                objects[contentObject] = Combine(
                    Ascii.GetBytes($"<< /Length {content.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n"),
                    content,
                    Ascii.GetBytes("\nendstream"));
            }

            using var output = new MemoryStream();
            WriteAscii(output, "%PDF-1.4\n");
            var offsets = new long[objects.Length];
            for (var objectNumber = 1; objectNumber < objects.Length; objectNumber++)
            {
                offsets[objectNumber] = output.Position;
                WriteAscii(output, $"{objectNumber.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
                output.Write(objects[objectNumber]);
                WriteAscii(output, "\nendobj\n");
            }

            var crossReferenceOffset = output.Position;
            WriteAscii(output, $"xref\n0 {objects.Length.ToString(CultureInfo.InvariantCulture)}\n0000000000 65535 f \n");
            for (var objectNumber = 1; objectNumber < offsets.Length; objectNumber++)
            {
                WriteAscii(output, $"{offsets[objectNumber].ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
            }

            WriteAscii(output,
                $"trailer\n<< /Size {objects.Length.ToString(CultureInfo.InvariantCulture)} /Root 1 0 R >>\nstartxref\n{crossReferenceOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
            return output.ToArray();
        }

        private static int PageObjectNumber(int pageIndex) => 5 + pageIndex * 2;

        private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static IEnumerable<string> WrapText(string text, int maxCharacters)
        {
            var normalizedText = (text ?? string.Empty).Normalize(NormalizationForm.FormC)
                .Replace("\t", "    ", StringComparison.Ordinal)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

            foreach (var sourceLine in normalizedText.Split('\n'))
            {
                if (sourceLine.Length == 0)
                {
                    yield return string.Empty;
                    continue;
                }

                var line = new StringBuilder();
                foreach (var word in sourceLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.Length > maxCharacters)
                    {
                        if (line.Length > 0)
                        {
                            yield return line.ToString();
                            line.Clear();
                        }

                        for (var offset = 0; offset < word.Length; offset += maxCharacters)
                        {
                            var length = Math.Min(maxCharacters, word.Length - offset);
                            var chunk = word.Substring(offset, length);
                            if (length == maxCharacters && offset + length < word.Length)
                            {
                                yield return chunk;
                            }
                            else
                            {
                                line.Append(chunk);
                            }
                        }

                        continue;
                    }

                    if (line.Length > 0 && line.Length + word.Length + 1 > maxCharacters)
                    {
                        yield return line.ToString();
                        line.Clear();
                    }

                    if (line.Length > 0)
                    {
                        line.Append(' ');
                    }

                    line.Append(word);
                }

                if (line.Length > 0)
                {
                    yield return line.ToString();
                }
            }
        }

        private static string ToPdfLiteral(string text)
        {
            var builder = new StringBuilder(text.Length + 2).Append('(');
            foreach (var character in text)
            {
                var value = ToWinAnsiByte(character);
                if (value is (byte)'(' or (byte)')' or (byte)'\\')
                {
                    builder.Append('\\').Append((char)value);
                }
                else if (value < 0x20 || value >= 0x7F)
                {
                    builder.Append('\\').Append(Convert.ToString(value, 8).PadLeft(3, '0'));
                }
                else
                {
                    builder.Append((char)value);
                }
            }

            return builder.Append(')').ToString();
        }

        private static byte ToWinAnsiByte(char character)
        {
            if (character >= '\u00A0' && character <= '\u00FF')
            {
                return (byte)character;
            }

            return character switch
            {
                >= ' ' and <= '~' => (byte)character,
                '\u20AC' => 0x80,
                '\u201A' => 0x82,
                '\u0192' => 0x83,
                '\u201E' => 0x84,
                '\u2026' => 0x85,
                '\u2020' => 0x86,
                '\u2021' => 0x87,
                '\u02C6' => 0x88,
                '\u2030' => 0x89,
                '\u0160' => 0x8A,
                '\u2039' => 0x8B,
                '\u0152' => 0x8C,
                '\u017D' => 0x8E,
                '\u2018' => 0x91,
                '\u2019' => 0x92,
                '\u201C' => 0x93,
                '\u201D' => 0x94,
                '\u2022' => 0x95,
                '\u2013' => 0x96,
                '\u2014' => 0x97,
                '\u02DC' => 0x98,
                '\u2122' => 0x99,
                '\u0161' => 0x9A,
                '\u203A' => 0x9B,
                '\u0153' => 0x9C,
                '\u017E' => 0x9E,
                '\u0178' => 0x9F,
                _ => character <= '\u001F' || char.IsControl(character) ? (byte)' ' : (byte)'?',
            };
        }

        private static byte[] Combine(byte[] first, byte[] second, byte[] third)
        {
            var combined = new byte[first.Length + second.Length + third.Length];
            Buffer.BlockCopy(first, 0, combined, 0, first.Length);
            Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
            Buffer.BlockCopy(third, 0, combined, first.Length + second.Length, third.Length);
            return combined;
        }

        private static void WriteAscii(Stream stream, string value)
        {
            var bytes = Ascii.GetBytes(value);
            stream.Write(bytes);
        }
    }
}
