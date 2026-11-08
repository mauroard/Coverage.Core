using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using Coverage.Core.Models;

namespace Coverage.Core.Services;

public sealed class CoverageAnalysisService
{
    private const string FrontendCoverageScript = "test:coverage";
    private static readonly HashSet<string> SourceExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", "dist", "build", "coverage", "TestResults", ".next", ".angular"
    };
    private static readonly HashSet<string> ReportExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", "dist", "build", ".next", ".angular"
    };

    public async Task<CoverageAnalysisResult> AnalyzeAsync(
        string applicationPath,
        string testProjectPath,
        CoverageProjectKind projectKind,
        IProgress<string>? progress = null)
    {
        try
        {
            var applicationRoot = ResolveApplicationRoot(applicationPath);
            var testProject = ResolveTestProject(testProjectPath);
            var sourceFiles = EnumerateFiles(applicationRoot, SourceExcludedDirectories)
                .Where(path => IsSupportedSource(path, projectKind))
                .Select(Path.GetFullPath)
                .Distinct(PathComparer())
                .OrderBy(path => path, PathComparer())
                .ToArray();

            if (sourceFiles.Length == 0)
            {
                throw new CoverageAnalysisException(projectKind == CoverageProjectKind.Microservice
                    ? "Non sono stati trovati file C# nel percorso del progetto applicativo."
                    : "Non sono stati trovati file C#, TypeScript o CSHTML nel percorso del progetto frontend.");
            }

            var reportDirectory = Path.Combine(Path.GetTempPath(), "Coverage.Core", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(reportDirectory);

            progress?.Report("Esecuzione dei test .NET e raccolta coverage...");
            var testResult = await RunProcessAsync(
                "dotnet",
                [
                    "test",
                    testProject,
                    "--collect",
                    "XPlat Code Coverage;Format=cobertura",
                    "--results-directory",
                    reportDirectory,
                ],
                Path.GetDirectoryName(testProject)!,
                "dotnet test",
                progress);
            EnsureSuccessful(testResult, "dotnet test", true);

            var coberturaReports = EnumerateFiles(reportDirectory, ReportExcludedDirectories)
                .Where(path => Path.GetFileName(path).Equals("coverage.cobertura.xml", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (coberturaReports.Length == 0)
            {
                var addCoverletCommand = $"dotnet add \"{testProject}\" package coverlet.collector";
                var testOutput = GetLastProcessOutput(testResult, 20);
                var diagnostics = string.IsNullOrWhiteSpace(testOutput)
                    ? string.Empty
                    : $"{Environment.NewLine}Output di dotnet test:{Environment.NewLine}{testOutput}";

                throw new CoverageAnalysisException(
                    $"I test .NET sono terminati, ma non è stato generato un report Cobertura.{Environment.NewLine}"
                    + $"Aggiungi coverlet.collector al progetto unit test selezionato con:{Environment.NewLine}{addCoverletCommand}{Environment.NewLine}"
                    + $"Se il collector è già presente, verifica Microsoft.NET.Test.Sdk e che XPlat Code Coverage sia disponibile.{diagnostics}");
            }

            var reportReader = new CoverageReportReader(applicationRoot, sourceFiles);
            foreach (var report in coberturaReports)
            {
                reportReader.ReadCobertura(report);
            }

            var warnings = new List<string>();
            if (projectKind == CoverageProjectKind.FrontendMvc)
            {
                var packageDirectories = FindFrontendCoveragePackages(applicationRoot);
                if (packageDirectories.Count == 0)
                {
                    warnings.Add($"Non è stato trovato uno script npm '{FrontendCoverageScript}'; verranno letti gli eventuali report LCOV già presenti.");
                }

                foreach (var packageDirectory in packageDirectories)
                {
                    progress?.Report($"Esecuzione npm run {FrontendCoverageScript} in {Path.GetRelativePath(applicationRoot, packageDirectory)}...");
                    var npmInvocation = CreateNpmInvocation();
                    var npmResult = await RunProcessAsync(
                        npmInvocation.FileName,
                        npmInvocation.Arguments,
                        packageDirectory,
                        npmInvocation.DisplayName,
                        progress);
                    EnsureSuccessful(npmResult, npmInvocation.DisplayName, false);
                }

                var lcovReports = EnumerateFiles(applicationRoot, ReportExcludedDirectories)
                    .Where(path => Path.GetFileName(path).Equals("lcov.info", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (lcovReports.Length == 0)
                {
                    warnings.Add("Non è stato trovato alcun report lcov.info: il coverage TypeScript non sarà disponibile finché i test frontend non generano LCOV.");
                }
                else
                {
                    foreach (var report in lcovReports)
                    {
                        reportReader.ReadLcov(report);
                    }
                }
            }

            var files = sourceFiles
                .Select(sourceFile => CreateFileResult(applicationRoot, sourceFile, reportReader))
                .ToArray();

            if (files.All(file => !file.HasCoverageData))
            {
                warnings.Add("I report sono stati generati, ma non contengono linee di coverage riconducibili ai file del progetto selezionato.");
            }

            if (projectKind == CoverageProjectKind.FrontendMvc)
            {
                foreach (var extension in new[] { ".cs", ".ts", ".tsx", ".cshtml" })
                {
                    var matchingFiles = files.Where(file => file.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matchingFiles.Length > 0 && matchingFiles.All(file => !file.HasCoverageData))
                    {
                        warnings.Add($"Nessun dato di coverage disponibile per i file {extension}.");
                    }
                }
            }

            return new CoverageAnalysisResult
            {
                ProjectKind = projectKind,
                Files = files,
                Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
                ReportDirectory = reportDirectory,
            };
        }
        catch (CoverageAnalysisException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new CoverageAnalysisException("Impossibile leggere o scrivere i file del progetto o i report di coverage.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new CoverageAnalysisException("Accesso negato a un file o a una cartella del progetto.", exception);
        }
        catch (JsonException exception)
        {
            throw new CoverageAnalysisException("Uno dei file package.json del frontend non contiene JSON valido.", exception);
        }
        catch (XmlException exception)
        {
            throw new CoverageAnalysisException("Un report Cobertura generato dai test non è valido.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new CoverageAnalysisException("Uno dei percorsi indicati non è valido.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new CoverageAnalysisException("Il sistema non supporta uno dei percorsi o dei comandi richiesti.", exception);
        }
    }

    private static string ResolveApplicationRoot(string input)
    {
        var path = NormalizeInputPath(input);
        if (Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        if (File.Exists(path) && Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(Path.GetFullPath(path))!;
        }

        throw new CoverageAnalysisException("Il percorso dell'applicazione deve indicare una cartella esistente o un file .csproj esistente.");
    }

    private static string ResolveTestProject(string input)
    {
        var path = NormalizeInputPath(input);
        if (File.Exists(path) && Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(path);
        }

        if (!Directory.Exists(path))
        {
            throw new CoverageAnalysisException("Il percorso del progetto di test deve indicare una cartella esistente o un file .csproj esistente.");
        }

        var projects = EnumerateFiles(Path.GetFullPath(path), SourceExcludedDirectories)
            .Where(project => Path.GetExtension(project).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .OrderBy(project => project, PathComparer())
            .ToArray();

        return projects.Length switch
        {
            1 => projects[0],
            0 => throw new CoverageAnalysisException("Nella cartella selezionata non è stato trovato alcun file .csproj."),
            _ => throw new CoverageAnalysisException("Nella cartella selezionata sono presenti più file .csproj. Indica direttamente il progetto unit test da eseguire."),
        };
    }

    private static string NormalizeInputPath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new CoverageAnalysisException("Indica sia il percorso dell'applicazione sia quello del progetto unit test.");
        }

        return input.Trim().Trim('"');
    }

    private static bool IsSupportedSource(string path, CoverageProjectKind projectKind)
    {
        var extension = Path.GetExtension(path);
        if (projectKind == CoverageProjectKind.Microservice)
        {
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase);
        }

        if (Path.GetFileName(path).EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateFiles(string root, IReadOnlySet<string> excludedDirectories) =>
        ProjectFileEnumerator.Enumerate(root, excludedDirectories);

    private static List<string> FindFrontendCoveragePackages(string applicationRoot)
    {
        var packages = new List<string>();
        foreach (var packageJson in EnumerateFiles(applicationRoot, SourceExcludedDirectories)
                     .Where(path => Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase)))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("scripts", out var scripts)
                && scripts.ValueKind == JsonValueKind.Object
                && scripts.TryGetProperty(FrontendCoverageScript, out var script)
                && script.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(script.GetString()))
            {
                packages.Add(Path.GetDirectoryName(packageJson)!);
            }
        }

        return packages.OrderBy(path => path, PathComparer()).ToList();
    }

    private static CoverageFileResult CreateFileResult(string applicationRoot, string sourceFile, CoverageReportReader reportReader)
    {
        var (coveredLines, totalLines) = reportReader.GetCoverage(sourceFile);
        var extension = Path.GetExtension(sourceFile).ToLowerInvariant();
        var language = extension switch
        {
            ".cs" => "C#",
            ".ts" or ".tsx" => "TypeScript",
            ".cshtml" => "Razor / CSHTML",
            _ => extension,
        };

        return new CoverageFileResult
        {
            RelativePath = Path.GetRelativePath(applicationRoot, sourceFile)
                .Replace(Path.DirectorySeparatorChar, '/'),
            Language = language,
            Extension = extension,
            CoveredLines = coveredLines,
            TotalLines = totalLines,
        };
    }

    private static (string FileName, IReadOnlyList<string> Arguments, string DisplayName) CreateNpmInvocation()
    {
        return OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/d", "/c", "npm.cmd run test:coverage"], "npm run test:coverage")
            : ("npm", ["run", "test:coverage"], "npm run test:coverage");
    }

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
            throw new CoverageAnalysisException($"Comando non disponibile: {displayName}. Verifica che gli strumenti richiesti siano installati e accessibili dal PATH.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new CoverageAnalysisException($"Impossibile avviare {displayName}.", exception);
        }
    }

    private static void EnsureSuccessful(ExternalProcessResult result, string displayName, bool includeCoverletHint)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        var output = GetLastProcessOutput(result, 30);
        var hint = includeCoverletHint
            ? " Verifica inoltre che il progetto unit test includa coverlet.collector."
            : string.Empty;

        throw new CoverageAnalysisException(
            $"{displayName} è terminato con codice {result.ExitCode}.{hint}{Environment.NewLine}{output}");
    }

    private static string GetLastProcessOutput(ExternalProcessResult result, int lineCount)
    {
        var combinedOutput = result.StandardOutput + Environment.NewLine + result.StandardError;
        var outputLines = combinedOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, outputLines.TakeLast(lineCount));
    }

    private static StringComparer PathComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
