using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Coverage.Core.Services;

internal sealed record ExternalProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ExternalProcessRunner
{
    public static async Task<ExternalProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string displayName,
        IProgress<string>? progress)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Non è stato possibile avviare {displayName}.");
        }

        var standardOutputTask = ReadOutputAsync(process.StandardOutput, displayName, progress);
        var standardErrorTask = ReadOutputAsync(process.StandardError, displayName, progress);
        await process.WaitForExitAsync();
        var output = await Task.WhenAll(standardOutputTask, standardErrorTask);

        return new ExternalProcessResult(process.ExitCode, output[0], output[1]);
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, string displayName, IProgress<string>? progress)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            output.AppendLine(line);
            progress?.Report($"{displayName}: {line}");
        }

        return output.ToString();
    }
}
