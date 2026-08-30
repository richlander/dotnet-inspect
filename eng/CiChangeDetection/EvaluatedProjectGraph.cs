using System.Diagnostics;
using System.Text.Json;

namespace CiChangeDetection;

internal static class EvaluatedProjectGraph
{
    internal static bool IsProjectDirectory(
        string repository,
        string relativePath)
    {
        string directory = Path.Combine(repository, relativePath);
        return Directory.Exists(directory)
            && Directory.EnumerateFiles(
                    directory,
                    "*.*proj",
                    SearchOption.TopDirectoryOnly)
                .Any(path =>
                    Path.GetExtension(path) is ".csproj" or ".vbproj" or ".fsproj");
    }

    internal static IReadOnlySet<string> ProjectDirectories(
        string repository,
        string rootProject)
    {
        string graphPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-project-graph-{Guid.NewGuid():N}.json");
        try
        {
            ProcessStartInfo startInfo = new("dotnet")
            {
                UseShellExecute = false,
                WorkingDirectory = repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("msbuild");
            startInfo.ArgumentList.Add(rootProject);
            startInfo.ArgumentList.Add("-t:GenerateRestoreGraphFile");
            startInfo.ArgumentList.Add(
                $"-p:RestoreGraphOutputPath={graphPath}");
            startInfo.ArgumentList.Add("-p:Configuration=Release");
            // Change detection runs before workload installation and only needs
            // evaluated project references, not workload packs.
            startInfo.ArgumentList.Add("-p:MSBuildEnableWorkloadResolver=false");
            startInfo.ArgumentList.Add("-nologo");
            startInfo.ArgumentList.Add("-v:q");

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    $"Could not start dotnet msbuild for {rootProject}.");
            Task<string> standardOutputTask =
                process.StandardOutput.ReadToEndAsync();
            Task<string> standardErrorTask =
                process.StandardError.ReadToEndAsync();
            bool timedOut = !process.WaitForExit(milliseconds: 30_000);
            if (timedOut)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            Task.WaitAll(standardOutputTask, standardErrorTask);
            string standardOutput = standardOutputTask.Result;
            string standardError = standardErrorTask.Result;
            if (timedOut || process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Could not evaluate the project graph for {rootProject}."
                    + $"{Environment.NewLine}stdout:{Environment.NewLine}"
                    + standardOutput
                    + $"{Environment.NewLine}stderr:{Environment.NewLine}"
                    + standardError);
            }

            using JsonDocument graph = JsonDocument.Parse(
                File.ReadAllText(graphPath));
            return graph.RootElement
                .GetProperty("projects")
                .EnumerateObject()
                .Select(project =>
                {
                    string relative = Path.GetRelativePath(
                        repository,
                        project.Name);
                    if (Path.IsPathRooted(relative)
                        || relative == ".."
                        || relative.StartsWith(
                            $"..{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Project graph entry is outside the repository: "
                            + project.Name);
                    }

                    return Path.GetDirectoryName(relative)!
                        .Replace(Path.DirectorySeparatorChar, '/');
                })
                .ToHashSet(StringComparer.Ordinal);
        }
        finally
        {
            File.Delete(graphPath);
        }
    }
}
