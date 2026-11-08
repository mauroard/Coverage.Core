using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Coverage.Core.Models;

namespace Coverage.Core.Services;

public sealed class TestProjectDiscoveryService
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", "dist", "build", "coverage", "TestResults", ".next", ".angular"
    };

    private static readonly HashSet<string> TestPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.NET.Test.Sdk",
        "Microsoft.VisualStudio.TestPlatform.TestFramework",
        "Microsoft.VisualStudio.TestPlatform.TestFramework.Extensions",
        "xunit",
        "xunit.core",
        "NUnit",
        "NUnit3TestAdapter",
        "MSTest.TestFramework",
        "MSTest.TestAdapter",
    };

    public async Task<TestProjectDiscoveryResult> FindTestProjectAsync(string applicationPath)
    {
        try
        {
            return await Task.Run(() => FindTestProject(applicationPath));
        }
        catch (CoverageAnalysisException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new CoverageAnalysisException("Impossibile leggere la soluzione o individuare i progetti unit test.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new CoverageAnalysisException("Accesso negato durante la ricerca del progetto unit test.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new CoverageAnalysisException("Il percorso del progetto applicativo non è valido.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new CoverageAnalysisException("Il sistema non supporta il percorso del progetto applicativo selezionato.", exception);
        }
    }

    private static TestProjectDiscoveryResult FindTestProject(string applicationPath)
    {
        var applicationDirectory = ResolveApplicationDirectory(applicationPath);
        var searchRoot = FindSearchRoot(applicationDirectory);
        var projects = EnumerateProjectFiles(searchRoot).ToArray();
        var applicationProject = FindApplicationProject(applicationDirectory, projects);
        var applicationName = applicationProject is null
            ? Path.GetFileName(applicationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileNameWithoutExtension(applicationProject);
        var testProjects = projects.Where(IsTestProject).ToArray();

        if (testProjects.Length == 0)
        {
            return new TestProjectDiscoveryResult(
                null,
                "Nessun progetto unit test trovato nella cartella selezionata o nella soluzione/repository più vicino.",
                applicationProject);
        }

        if (testProjects.Length == 1)
        {
            return Found(testProjects[0], applicationProject);
        }

        var scoredProjects = testProjects
            .Select(project => new
            {
                Path = project,
                Score = GetAssociationScore(project, applicationProject, applicationName),
            })
            .ToArray();
        var highestScore = scoredProjects.Max(project => project.Score);
        var bestMatches = scoredProjects.Where(project => project.Score == highestScore).ToArray();

        if (highestScore > 0 && bestMatches.Length == 1)
        {
            return Found(bestMatches[0].Path, applicationProject);
        }

        return new TestProjectDiscoveryResult(
            null,
            "Sono stati trovati più progetti unit test e non è possibile associare quello corretto. Seleziona una cartella applicativa più specifica o riorganizza i progetti nella soluzione.",
            applicationProject);
    }

    private static TestProjectDiscoveryResult Found(string projectPath, string? applicationProjectPath) => new(
        Path.GetFullPath(projectPath),
        "Progetto unit test rilevato automaticamente.",
        applicationProjectPath is null ? null : Path.GetFullPath(applicationProjectPath));

    private static string ResolveApplicationDirectory(string applicationPath)
    {
        if (string.IsNullOrWhiteSpace(applicationPath))
        {
            throw new CoverageAnalysisException("Seleziona prima la cartella del progetto applicativo.");
        }

        var path = applicationPath.Trim().Trim('"');
        if (Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        if (File.Exists(path) && Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(Path.GetFullPath(path))!;
        }

        throw new CoverageAnalysisException("La cartella del progetto applicativo non esiste più. Selezionala nuovamente.");
    }

    private static string FindSearchRoot(string applicationDirectory)
    {
        var currentDirectory = new DirectoryInfo(applicationDirectory);
        while (currentDirectory is not null)
        {
            if (ContainsSolution(currentDirectory.FullName))
            {
                return currentDirectory.FullName;
            }

            var gitPath = Path.Combine(currentDirectory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return applicationDirectory;
    }

    private static bool ContainsSolution(string directory)
    {
        return Directory.EnumerateFiles(directory)
            .Any(path => Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateProjectFiles(string root)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            foreach (var file in Directory.EnumerateFiles(currentDirectory)
                         .Where(path => Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase)))
            {
                yield return Path.GetFullPath(file);
            }

            foreach (var directory in Directory.EnumerateDirectories(currentDirectory))
            {
                var directoryInfo = new DirectoryInfo(directory);
                if (ExcludedDirectories.Contains(directoryInfo.Name)
                    || (directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                pendingDirectories.Push(directory);
            }
        }
    }

    private static bool IsTestProject(string projectPath)
    {
        var project = LoadProject(projectPath);
        if (project.Descendants().Any(element => element.Name.LocalName == "IsTestProject"
            && element.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var packageNames = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(GetPackageName)
            .Where(name => name is not null)
            .ToArray();

        return packageNames.Any(name => TestPackages.Contains(name!))
            || HasTestProjectName(Path.GetFileNameWithoutExtension(projectPath));
    }

    private static string? GetPackageName(XElement packageReference)
    {
        var include = packageReference.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase)
                || attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase));
        if (include is not null)
        {
            return include.Value;
        }

        return packageReference.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static bool HasTestProjectName(string projectName)
    {
        return new[]
        {
            ".Tests", ".Test", ".UnitTests", ".UnitTest", ".IntegrationTests", ".IntegrationTest",
            "_Tests", "_Test", "-Tests", "-Test",
        }.Any(suffix => projectName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            || projectName.Equals("Tests", StringComparison.OrdinalIgnoreCase)
            || projectName.Equals("Test", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindApplicationProject(string applicationDirectory, IReadOnlyList<string> projects)
    {
        var applicationProjects = projects
            .Where(path => IsWithinDirectory(applicationDirectory, path) && !IsTestProject(path))
            .ToArray();
        var directoryName = Path.GetFileName(applicationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var nameMatches = applicationProjects
            .Where(path => Path.GetFileNameWithoutExtension(path).Equals(directoryName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (nameMatches.Length == 1)
        {
            return nameMatches[0];
        }

        var directProjects = applicationProjects
            .Where(path => Path.GetDirectoryName(path)?.Equals(applicationDirectory, PathComparison()) == true)
            .ToArray();
        if (directProjects.Length == 1)
        {
            return directProjects[0];
        }

        return applicationProjects.Length == 1 ? applicationProjects[0] : null;
    }

    private static bool IsWithinDirectory(string directory, string filePath)
    {
        var relativePath = Path.GetRelativePath(directory, filePath);
        return !Path.IsPathRooted(relativePath)
            && relativePath != ".."
            && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison())
            && !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, PathComparison());
    }

    private static int GetAssociationScore(string testProjectPath, string? applicationProjectPath, string applicationName)
    {
        var testName = Path.GetFileNameWithoutExtension(testProjectPath);
        var score = ReferencesProject(testProjectPath, applicationProjectPath) ? 1000 : 0;

        if (testName.Equals($"{applicationName}.Tests", StringComparison.OrdinalIgnoreCase)
            || testName.Equals($"{applicationName}.UnitTests", StringComparison.OrdinalIgnoreCase)
            || testName.Equals($"{applicationName}.Test", StringComparison.OrdinalIgnoreCase)
            || testName.Equals($"{applicationName}.UnitTest", StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }
        else if (testName.StartsWith(applicationName + ".", StringComparison.OrdinalIgnoreCase)
            && HasTestProjectName(testName))
        {
            score += 300;
        }

        return score;
    }

    private static bool ReferencesProject(string testProjectPath, string? applicationProjectPath)
    {
        if (applicationProjectPath is null)
        {
            return false;
        }

        var testProject = LoadProject(testProjectPath);
        var projectDirectory = Path.GetDirectoryName(testProjectPath)!;
        foreach (var projectReference in testProject.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
        {
            var include = projectReference.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (string.IsNullOrWhiteSpace(include) || include.Contains("$(", StringComparison.Ordinal))
            {
                continue;
            }

            var referencedPath = Path.GetFullPath(Path.Combine(projectDirectory, include));
            if (PathComparer().Equals(referencedPath, applicationProjectPath))
            {
                return true;
            }
        }

        return false;
    }

    private static XDocument LoadProject(string projectPath)
    {
        try
        {
            return XDocument.Load(projectPath);
        }
        catch (XmlException exception)
        {
            throw new CoverageAnalysisException($"Il file progetto '{projectPath}' non contiene XML valido.", exception);
        }
    }

    private static StringComparer PathComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
