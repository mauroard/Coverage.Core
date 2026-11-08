using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Coverage.Core.Services;

internal sealed class CoverageReportReader
{
    private readonly string _applicationRoot;
    private readonly StringComparer _pathComparer;
    private readonly StringComparison _pathComparison;
    private readonly Dictionary<string, string> _sourceFiles;
    private readonly Dictionary<string, Dictionary<int, bool>> _coverage;

    public CoverageReportReader(string applicationRoot, IEnumerable<string> sourceFiles)
    {
        _applicationRoot = Path.GetFullPath(applicationRoot);
        _pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        _sourceFiles = new Dictionary<string, string>(_pathComparer);
        _coverage = new Dictionary<string, Dictionary<int, bool>>(_pathComparer);

        foreach (var sourceFile in sourceFiles)
        {
            var fullPath = Path.GetFullPath(sourceFile);
            _sourceFiles[fullPath] = fullPath;
        }
    }

    public void ReadCobertura(string reportPath)
    {
        var report = XDocument.Load(reportPath);
        var reportDirectory = Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? _applicationRoot;
        var sourceRoots = report
            .Descendants()
            .Where(element => element.Name.LocalName == "source")
            .Select(element => element.Value.Trim())
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .ToArray();

        foreach (var classElement in report.Descendants().Where(element => element.Name.LocalName == "class"))
        {
            var fileName = (string?)classElement.Attribute("filename");
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            var sourceFile = ResolveSourceFile(fileName, reportDirectory, sourceRoots);
            if (sourceFile is null)
            {
                continue;
            }

            var linesElement = classElement.Elements().FirstOrDefault(element => element.Name.LocalName == "lines");
            if (linesElement is null)
            {
                continue;
            }

            foreach (var lineElement in linesElement.Elements().Where(element => element.Name.LocalName == "line"))
            {
                if (!int.TryParse((string?)lineElement.Attribute("number"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNumber)
                    || !int.TryParse((string?)lineElement.Attribute("hits"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hits))
                {
                    continue;
                }

                AddLine(sourceFile, lineNumber, hits > 0);
            }
        }
    }

    public void ReadLcov(string reportPath)
    {
        var reportDirectory = Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? _applicationRoot;
        string? sourceFile = null;
        var lines = new Dictionary<int, bool>();

        void StoreRecord()
        {
            if (sourceFile is not null)
            {
                foreach (var (lineNumber, isCovered) in lines)
                {
                    AddLine(sourceFile, lineNumber, isCovered);
                }
            }

            sourceFile = null;
            lines.Clear();
        }

        foreach (var line in File.ReadLines(reportPath))
        {
            if (line.StartsWith("SF:", StringComparison.Ordinal))
            {
                StoreRecord();
                sourceFile = ResolveSourceFile(line[3..].Trim(), reportDirectory, Array.Empty<string>());
            }
            else if (line.StartsWith("DA:", StringComparison.Ordinal) && sourceFile is not null)
            {
                var values = line[3..].Split(',', 3);
                if (values.Length >= 2
                    && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNumber)
                    && int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hits))
                {
                    lines[lineNumber] = lines.TryGetValue(lineNumber, out var wasCovered)
                        ? wasCovered || hits > 0
                        : hits > 0;
                }
            }
            else if (line.Trim().Equals("end_of_record", StringComparison.Ordinal))
            {
                StoreRecord();
            }
        }

        StoreRecord();
    }

    public (int CoveredLines, int TotalLines) GetCoverage(string sourceFile)
    {
        if (!_coverage.TryGetValue(Path.GetFullPath(sourceFile), out var lines))
        {
            return (0, 0);
        }

        return (lines.Values.Count(isCovered => isCovered), lines.Count);
    }

    private void AddLine(string sourceFile, int lineNumber, bool isCovered)
    {
        if (!_coverage.TryGetValue(sourceFile, out var lines))
        {
            lines = new Dictionary<int, bool>();
            _coverage.Add(sourceFile, lines);
        }

        lines[lineNumber] = lines.TryGetValue(lineNumber, out var wasCovered)
            ? wasCovered || isCovered
            : isCovered;
    }

    private string? ResolveSourceFile(string reportFileName, string reportDirectory, IReadOnlyList<string> sourceRoots)
    {
        var normalizedFileName = reportFileName.Trim();
        if (normalizedFileName.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(normalizedFileName, UriKind.Absolute, out var fileUri)
            && fileUri.IsFile)
        {
            normalizedFileName = fileUri.LocalPath;
        }

        var pathValue = normalizedFileName
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var candidates = new List<string>();

        if (Path.IsPathRooted(pathValue))
        {
            candidates.Add(pathValue);
        }
        else
        {
            candidates.Add(Path.Combine(_applicationRoot, pathValue));
            candidates.Add(Path.Combine(reportDirectory, pathValue));

            foreach (var sourceRoot in sourceRoots)
            {
                var normalizedRoot = sourceRoot
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                if (!Path.IsPathRooted(normalizedRoot))
                {
                    normalizedRoot = Path.Combine(reportDirectory, normalizedRoot);
                }

                candidates.Add(Path.Combine(normalizedRoot, pathValue));
            }
        }

        foreach (var candidate in candidates)
        {
            if (TryNormalizePath(candidate, out var fullPath)
                && _sourceFiles.TryGetValue(fullPath, out var matchedSource))
            {
                return matchedSource;
            }
        }

        var suffix = normalizedFileName.Replace('\\', '/').TrimStart('/');
        while (suffix.StartsWith("./", StringComparison.Ordinal))
        {
            suffix = suffix[2..];
        }

        if (suffix.Length == 0)
        {
            return null;
        }

        var suffixPattern = "/" + suffix;
        var suffixMatches = _sourceFiles.Values
            .Where(source => source.Replace('\\', '/').EndsWith(suffixPattern, _pathComparison))
            .Distinct(_pathComparer)
            .Take(2)
            .ToArray();

        return suffixMatches.Length == 1 ? suffixMatches[0] : null;
    }

    private static bool TryNormalizePath(string path, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (ArgumentException)
        {
            fullPath = string.Empty;
            return false;
        }
        catch (NotSupportedException)
        {
            fullPath = string.Empty;
            return false;
        }
        catch (PathTooLongException)
        {
            fullPath = string.Empty;
            return false;
        }
    }
}
