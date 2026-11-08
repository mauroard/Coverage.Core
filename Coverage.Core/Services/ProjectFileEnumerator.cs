using System.Collections.Generic;
using System.IO;

namespace Coverage.Core.Services;

internal static class ProjectFileEnumerator
{
    public static IEnumerable<string> Enumerate(string root, IReadOnlySet<string> excludedDirectories)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            foreach (var file in Directory.EnumerateFiles(currentDirectory))
            {
                yield return file;
            }

            foreach (var directory in Directory.EnumerateDirectories(currentDirectory))
            {
                var directoryInfo = new DirectoryInfo(directory);
                if (excludedDirectories.Contains(directoryInfo.Name)
                    || (directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                pendingDirectories.Push(directory);
            }
        }
    }
}
