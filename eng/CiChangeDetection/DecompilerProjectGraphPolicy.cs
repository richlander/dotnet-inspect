using System.Diagnostics;
using System.Text.Json;

namespace CiChangeDetection;

internal static class DecompilerProjectGraphPolicy
{
    internal static void Validate(string repository)
    {
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
                || !Directory.Exists(Path.Combine(repository, line))))
        {
            throw new InvalidOperationException(
                "eng/decompiler-gate-skip-projects.txt must contain unique, " +
                "existing, canonical repository-relative project directories.");
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
                "src/ILInspector.Decompiler.Tests/ILInspector.Decompiler.Tests.csproj");
            startInfo.ArgumentList.Add("-t:GenerateRestoreGraphFile");
            startInfo.ArgumentList.Add(
                $"-p:RestoreGraphOutputPath={graphPath}");
            startInfo.ArgumentList.Add("-nologo");
            startInfo.ArgumentList.Add("-v:q");

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Could not start dotnet msbuild for the decompiler project graph.");
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            bool timedOut = !process.WaitForExit(milliseconds: 30_000);
            if (timedOut)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
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

            string[] unsafeExemptions = actual
                .Intersect(projectClosure)
                .Order()
                .ToArray();
            if (unsafeExemptions.Length != 0)
            {
                throw new InvalidOperationException(
                    "eng/decompiler-gate-skip-projects.txt exempts projects " +
                    "in the evaluated ILInspector.Decompiler.Tests graph: [" +
                    string.Join(", ", unsafeExemptions) + "].");
            }
        }
        finally
        {
            File.Delete(graphPath);
        }
    }
}
