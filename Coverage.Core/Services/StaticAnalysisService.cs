using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coverage.Core.Models;

namespace Coverage.Core.Services;

public sealed class StaticAnalysisService
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", "dist", "build", "coverage", "TestResults", ".next", ".angular"
    };

    private static readonly Regex BuildDiagnosticPattern = new(
        @"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>error|warning|info)\s+(?<rule>[A-Z]+\d+)\s*:\s*(?<message>.*?)(?:\s+\[[^\]]+\])?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task<StaticAnalysisResult> AnalyzeAsync(
        string applicationPath,
        CoverageProjectKind projectKind,
        IProgress<string>? progress = null)
    {
        try
        {
            var applicationRoot = ResolveApplicationRoot(applicationPath);
            var findings = new List<StaticAnalysisFinding>();
            var warnings = new List<string>();
            var projects = FindApplicationProjects(applicationPath, applicationRoot);

            if (projects.Count == 0)
            {
                warnings.Add("Nessun progetto .NET applicativo individuato: l'analisi C# non è stata eseguita.");
            }

            foreach (var project in projects)
            {
                progress?.Report($"Analisi statica .NET: {Path.GetFileName(project)}...");
                ExternalProcessResult processResult;
                try
                {
                    processResult = await RunProcessAsync(
                        "dotnet",
                        ["build", project, "--nologo", "--verbosity", "minimal", "-p:EnableNETAnalyzers=true", "-p:AnalysisLevel=latest"],
                        Path.GetDirectoryName(project)!,
                        "dotnet build",
                        progress);
                }
                catch (StaticAnalysisException exception)
                {
                    warnings.Add($"Analisi statica .NET per '{Path.GetFileName(project)}' non eseguita: {exception.Message}");
                    continue;
                }

                var projectFindings = ParseBuildDiagnostics(
                    CombineOutput(processResult),
                    applicationRoot,
                    Path.GetDirectoryName(project)!);
                findings.AddRange(projectFindings);

                if (processResult.ExitCode != 0 && !projectFindings.Any(finding => finding.Severity == StaticAnalysisSeverity.Error))
                {
                    warnings.Add($"dotnet build per '{Path.GetFileName(project)}' è terminato con codice {processResult.ExitCode}; non è stato possibile associare gli errori a file e righe.{Environment.NewLine}{GetLastOutput(processResult)}");
                }
            }

            if (projectKind == CoverageProjectKind.FrontendMvc)
            {
                var lintPackages = FindEslintPackages(applicationRoot, warnings);

                foreach (var packageDirectory in lintPackages)
                {
                    progress?.Report($"Analisi statica ESLint in {Path.GetRelativePath(applicationRoot, packageDirectory)}...");
                    var npmInvocation = CreateNpmInvocation();
                    ExternalProcessResult processResult;
                    try
                    {
                        processResult = await RunProcessAsync(
                            npmInvocation.FileName,
                            npmInvocation.Arguments,
                            packageDirectory,
                            npmInvocation.DisplayName,
                            progress);
                    }
                    catch (StaticAnalysisException exception)
                    {
                        warnings.Add($"{npmInvocation.DisplayName} in '{Path.GetRelativePath(applicationRoot, packageDirectory)}' non eseguito: {exception.Message}");
                        continue;
                    }

                    IReadOnlyList<StaticAnalysisFinding> packageFindings;
                    try
                    {
                        packageFindings = ParseEslintDiagnostics(
                            processResult.StandardOutput,
                            packageDirectory,
                            applicationRoot);
                    }
                    catch (StaticAnalysisException exception)
                    {
                        warnings.Add($"Output ESLint non analizzabile in '{Path.GetRelativePath(applicationRoot, packageDirectory)}': {exception.Message}{Environment.NewLine}{GetLastOutput(processResult)}");
                        continue;
                    }
                    catch (JsonException exception)
                    {
                        warnings.Add($"Report JSON ESLint non valido in '{Path.GetRelativePath(applicationRoot, packageDirectory)}': {exception.Message}");
                        continue;
                    }

                    findings.AddRange(packageFindings);
                    if (processResult.ExitCode != 0 && packageFindings.Count == 0)
                    {
                        warnings.Add($"{npmInvocation.DisplayName} in '{Path.GetRelativePath(applicationRoot, packageDirectory)}' è terminato con codice {processResult.ExitCode}, senza diagnostici analizzabili.{Environment.NewLine}{GetLastOutput(processResult)}");
                    }
                }
            }

            return new StaticAnalysisResult
            {
                Findings = findings
                    .Distinct()
                    .OrderByDescending(finding => finding.Severity)
                    .ThenBy(finding => finding.RelativePath, PathComparer())
                    .ThenBy(finding => finding.Line)
                    .ToArray(),
                Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            };
        }
        catch (StaticAnalysisException)
        {
            throw;
        }
        catch (CoverageAnalysisException exception)
        {
            throw new StaticAnalysisException(exception.Message, exception);
        }
        catch (IOException exception)
        {
            throw new StaticAnalysisException("Impossibile leggere un file o una cartella necessari all'analisi statica.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new StaticAnalysisException("Accesso negato durante l'analisi statica.", exception);
        }
        catch (JsonException exception)
        {
            throw new StaticAnalysisException("Un file package.json o il report JSON di ESLint non è valido.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new StaticAnalysisException("Uno dei percorsi indicati per l'analisi statica non è valido.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new StaticAnalysisException("Il sistema non supporta uno dei percorsi o dei comandi richiesti per l'analisi statica.", exception);
        }
    }

    private static string ResolveApplicationRoot(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new StaticAnalysisException("Seleziona prima la cartella del progetto applicativo.");
        }

        var path = input.Trim().Trim('"');
        if (Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        if (File.Exists(path) && Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(Path.GetFullPath(path))!;
        }

        throw new StaticAnalysisException("Il percorso dell'applicazione deve indicare una cartella esistente o un file .csproj esistente.");
    }

    private static IReadOnlyList<string> FindApplicationProjects(string applicationPath, string applicationRoot)
    {
        var path = applicationPath.Trim().Trim('"');
        if (File.Exists(path) && Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return [Path.GetFullPath(path)];
        }

        return ProjectFileEnumerator.Enumerate(applicationRoot, ExcludedDirectories)
            .Where(project => Path.GetExtension(project).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(project => !TestProjectDiscoveryService.IsTestProject(project))
            .Select(Path.GetFullPath)
            .Distinct(PathComparer())
            .OrderBy(project => project, PathComparer())
            .ToArray();
    }

    private static List<string> FindEslintPackages(string applicationRoot, ICollection<string> warnings)
    {
        var packages = new List<string>();

        foreach (var packageJson in ProjectFileEnumerator.Enumerate(applicationRoot, ExcludedDirectories)
                     .Where(path => Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase)))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("scripts", out var scripts)
                || scripts.ValueKind != JsonValueKind.Object
                || !scripts.TryGetProperty("lint", out var lintScript)
                || lintScript.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(lintScript.GetString()))
            {
                continue;
            }

            var script = lintScript.GetString()!;
            var packageDirectory = Path.GetDirectoryName(packageJson)!;
            if (!script.Contains("eslint", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Lo script npm 'lint' in '{Path.GetRelativePath(applicationRoot, packageDirectory)}' non invoca ESLint e non è stato eseguito.");
                continue;
            }

            if (script.Contains("&&", StringComparison.Ordinal)
                || script.Contains("||", StringComparison.Ordinal)
                || script.Contains(';'))
            {
                warnings.Add($"Lo script npm 'lint' in '{Path.GetRelativePath(applicationRoot, packageDirectory)}' contiene più comandi ed è stato saltato perché non è possibile applicare il formato JSON in modo sicuro.");
                continue;
            }

            if (script.Contains("--fix", StringComparison.OrdinalIgnoreCase)
                || script.Contains("--write", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Lo script npm 'lint' in '{Path.GetRelativePath(applicationRoot, packageDirectory)}' può modificare i file (--fix/--write) ed è stato saltato.");
                continue;
            }

            packages.Add(packageDirectory);
        }

        if (packages.Count == 0)
        {
            warnings.Add("Nessuno script npm 'lint' compatibile trovato: l'analisi ESLint non è stata eseguita.");
        }

        return packages.Distinct(PathComparer()).OrderBy(path => path, PathComparer()).ToList();
    }

    private static IReadOnlyList<StaticAnalysisFinding> ParseBuildDiagnostics(
        string output,
        string applicationRoot,
        string projectDirectory)
    {
        var findings = new List<StaticAnalysisFinding>();
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = BuildDiagnosticPattern.Match(line.Trim());
            if (!match.Success
                || !int.TryParse(match.Groups["line"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNumber)
                || !int.TryParse(match.Groups["column"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var columnNumber))
            {
                continue;
            }

            var reportedPath = match.Groups["file"].Value.Trim();
            var fullPath = Path.GetFullPath(Path.IsPathRooted(reportedPath)
                ? reportedPath
                : Path.Combine(projectDirectory, reportedPath));
            if (!TryGetRelativePath(applicationRoot, fullPath, out var relativePath)
                || IsExcludedPath(relativePath)
                || !IsSupportedDiagnosticFile(relativePath))
            {
                continue;
            }

            findings.Add(new StaticAnalysisFinding
            {
                Severity = ParseSeverity(match.Groups["severity"].Value),
                RuleId = match.Groups["rule"].Value,
                RelativePath = relativePath,
                Line = lineNumber,
                Column = columnNumber,
                Message = match.Groups["message"].Value.Trim(),
            });
        }

        return findings;
    }

    private static IReadOnlyList<StaticAnalysisFinding> ParseEslintDiagnostics(
        string output,
        string packageDirectory,
        string applicationRoot)
    {
        var jsonStart = output.IndexOf('[');
        var jsonEnd = output.LastIndexOf(']');
        if (jsonStart < 0 || jsonEnd < jsonStart)
        {
            throw new StaticAnalysisException("ESLint non ha prodotto un report JSON leggibile.");
        }

        using var document = JsonDocument.Parse(output[jsonStart..(jsonEnd + 1)]);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new StaticAnalysisException("Il report JSON prodotto da ESLint non è un elenco di file.");
        }

        var findings = new List<StaticAnalysisFinding>();
        foreach (var fileResult in document.RootElement.EnumerateArray())
        {
            if (fileResult.ValueKind != JsonValueKind.Object
                || !fileResult.TryGetProperty("filePath", out var filePathElement)
                || filePathElement.ValueKind != JsonValueKind.String
                || !fileResult.TryGetProperty("messages", out var messages)
                || messages.ValueKind != JsonValueKind.Array)
            {
                throw new StaticAnalysisException("Il report JSON prodotto da ESLint non contiene i campi filePath e messages previsti.");
            }

            var reportedPath = filePathElement.GetString()!;
            var fullPath = Path.GetFullPath(Path.IsPathRooted(reportedPath)
                ? reportedPath
                : Path.Combine(packageDirectory, reportedPath));
            if (!TryGetRelativePath(applicationRoot, fullPath, out var relativePath)
                || IsExcludedPath(relativePath))
            {
                continue;
            }

            foreach (var message in messages.EnumerateArray())
            {
                if (message.ValueKind != JsonValueKind.Object)
                {
                    throw new StaticAnalysisException("Il report JSON prodotto da ESLint contiene un messaggio non valido.");
                }

                var severityNumber = message.TryGetProperty("severity", out var severityElement)
                    && severityElement.ValueKind == JsonValueKind.Number
                    && severityElement.TryGetInt32(out var parsedSeverity)
                    ? parsedSeverity
                    : 1;
                var lineNumber = GetJsonInteger(message, "line");
                var columnNumber = GetJsonInteger(message, "column");
                var ruleId = message.TryGetProperty("ruleId", out var ruleElement)
                    && ruleElement.ValueKind == JsonValueKind.String
                    ? ruleElement.GetString()
                    : null;
                var text = message.TryGetProperty("message", out var messageElement)
                    && messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString()
                    : null;

                findings.Add(new StaticAnalysisFinding
                {
                    Severity = severityNumber >= 2 ? StaticAnalysisSeverity.Error
                        : severityNumber == 1 ? StaticAnalysisSeverity.Warning
                        : StaticAnalysisSeverity.Info,
                    RuleId = string.IsNullOrWhiteSpace(ruleId) ? "ESLINT" : ruleId,
                    RelativePath = relativePath,
                    Line = lineNumber,
                    Column = columnNumber,
                    Message = text ?? string.Empty,
                });
            }
        }

        return findings;
    }

    private static bool TryGetRelativePath(string applicationRoot, string fullPath, out string relativePath)
    {
        relativePath = Path.GetRelativePath(applicationRoot, fullPath);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", PathComparison())
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison())
            || relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, PathComparison()))
        {
            relativePath = string.Empty;
            return false;
        }

        relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        return true;
    }

    private static bool IsExcludedPath(string relativePath) => relativePath
        .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
        .Any(ExcludedDirectories.Contains);

    private static bool IsSupportedDiagnosticFile(string relativePath)
    {
        var extension = Path.GetExtension(relativePath);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase);
    }

    private static StaticAnalysisSeverity ParseSeverity(string severity) => severity.ToLowerInvariant() switch
    {
        "error" => StaticAnalysisSeverity.Error,
        "warning" => StaticAnalysisSeverity.Warning,
        _ => StaticAnalysisSeverity.Info,
    };

    private static int GetJsonInteger(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var integer)
                ? integer
                : 0;

    private static (string FileName, IReadOnlyList<string> Arguments, string DisplayName) CreateNpmInvocation() =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/d", "/c", "npm.cmd run --silent lint -- --format json"], "npm run lint (ESLint)")
            : ("npm", ["run", "--silent", "lint", "--", "--format", "json"], "npm run lint (ESLint)");

    private static async Task<ExternalProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string displayName,
        IProgress<string>? progress)
    {
        try
        {
            return await ExternalProcessRunner.RunAsync(fileName, arguments, workingDirectory, displayName, progress);
        }
        catch (Win32Exception exception)
        {
            throw new StaticAnalysisException($"Comando non disponibile: {displayName}. Verifica che gli strumenti richiesti siano installati e accessibili dal PATH.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new StaticAnalysisException($"Impossibile avviare {displayName}.", exception);
        }
    }

    private static string CombineOutput(ExternalProcessResult result) =>
        result.StandardOutput + Environment.NewLine + result.StandardError;

    private static string GetLastOutput(ExternalProcessResult result)
    {
        var lines = CombineOutput(result).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, lines.TakeLast(20));
    }

    private static StringComparer PathComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
