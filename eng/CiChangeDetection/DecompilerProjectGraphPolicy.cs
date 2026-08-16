using System.Diagnostics;
using System.Text.Json;

namespace CiChangeDetection;

internal static class DecompilerProjectGraphPolicy
{
    private const string RootProjectDirectory =
        "src/ILInspector.Decompiler.Tests";

    internal static void Validate(string repository)
    {
        static bool IsProjectDirectory(string repository, string relativePath)
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

        static bool IsAtOrBelowProject(string project, string ancestor) =>
            project == ancestor
            || project.StartsWith($"{ancestor}/", StringComparison.Ordinal);

        static bool ProjectTreesOverlap(string left, string right) =>
            IsAtOrBelowProject(left, right)
            || IsAtOrBelowProject(right, left);

        if (!ProjectTreesOverlap(
                "src/dotnet-inspect/Nested",
                "src/dotnet-inspect")
            || ProjectTreesOverlap(
                "src/dotnet-inspect.TestsExtra",
                "src/dotnet-inspect.Tests"))
        {
            throw new InvalidOperationException(
                "Decompiler skip-list project boundary check is not non-vacuous.");
        }

        string manifestPath = Path.Combine(
            repository,
            "eng",
            "decompiler-gate-skip-projects.txt");
        string[] manifestLines = File.ReadAllLines(manifestPath);
        var actual = manifestLines.ToHashSet(StringComparer.Ordinal);
        if (actual.Count != manifestLines.Length
            || manifestLines.Any(line =>
                string.IsNullOrWhiteSpace(line)
                || line != line.Trim()
                || Path.IsPathRooted(line)
                || line.EndsWith('/')
                || line.Split('/').Any(part => part is "" or "." or "..")
                || !IsProjectDirectory(repository, line)))
        {
            throw new InvalidOperationException(
                "eng/decompiler-gate-skip-projects.txt must contain unique, " +
                "existing, canonical repository-relative project roots.");
        }

        string graphPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-decompiler-graph-{Guid.NewGuid():N}.json");
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
            startInfo.ArgumentList.Add(
                $"{RootProjectDirectory}/ILInspector.Decompiler.Tests.csproj");
            startInfo.ArgumentList.Add("-t:GenerateRestoreGraphFile");
            startInfo.ArgumentList.Add(
                $"-p:RestoreGraphOutputPath={graphPath}");
            startInfo.ArgumentList.Add("-p:Configuration=Release");
            startInfo.ArgumentList.Add("-nologo");
            startInfo.ArgumentList.Add("-v:q");

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Could not start dotnet msbuild for the decompiler project graph.");
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
                    "Could not evaluate the decompiler project graph.\n" +
                    $"stdout:\n{standardOutput}\nstderr:\n{standardError}");
            }

            using JsonDocument graph = JsonDocument.Parse(
                File.ReadAllText(graphPath));
            var projectClosure = graph.RootElement
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
                            "Decompiler graph project is outside the " +
                            $"repository: {project.Name}");
                    }

                    return Path.GetDirectoryName(relative)!
                        .Replace(Path.DirectorySeparatorChar, '/');
                })
                .ToHashSet(StringComparer.Ordinal);

            if (!projectClosure.Contains(RootProjectDirectory))
            {
                throw new InvalidOperationException(
                    "Decompiler project graph does not contain its root: " +
                    RootProjectDirectory);
            }

            string[] unsafeExemptions = actual
                .Where(exemption =>
                    projectClosure.Any(project =>
                        ProjectTreesOverlap(project, exemption)))
                .Order()
                .ToArray();
            if (unsafeExemptions.Length != 0)
            {
                throw new InvalidOperationException(
                    "eng/decompiler-gate-skip-projects.txt exempts project " +
                    "trees overlapping projects in the evaluated Release " +
                    "ILInspector.Decompiler.Tests graph: [" +
                    string.Join(", ", unsafeExemptions) + "].");
            }
        }
        finally
        {
            File.Delete(graphPath);
        }
    }
}
