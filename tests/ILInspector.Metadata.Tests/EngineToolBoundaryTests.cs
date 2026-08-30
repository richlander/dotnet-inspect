using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Architecture guardrail for the engine/tool boundary (#2568, #2579): a production
/// <c>ILInspector.*</c> (engine) project must never reference a <c>DotnetInspector.*</c>
/// tool project. The source-neutral <c>DotnetInspector.Artifacts</c> contract floor is
/// the sole exception despite its historical project-name prefix. Test projects are
/// intentionally excluded — pulling shared fixtures/services into a test is normal and
/// is a separate policy call from the production boundary.
/// </summary>
public class EngineToolBoundaryTests
{
    [Fact]
    public void MetadataHasNoSourceLinkFetchProjectReference()
    {
        string project = Path.Combine(
            FindRepoRoot(),
            "src",
            "ILInspector.Metadata",
            "ILInspector.Metadata.csproj");

        Assert.DoesNotContain(
            ReadEvaluatedProjectReferences(project),
            reference => Path.GetFileNameWithoutExtension(
                    reference.Replace('\\', '/'))
                .Equals("SourceLinkFetch", StringComparison.Ordinal));
    }

    [Fact]
    public void EngineProjectsReferenceOnlyTheSourceNeutralArtifactFloor()
    {
        var srcDir = Path.Combine(FindRepoRoot(), "src");
        var violations = new List<string>();

        foreach (var csproj in Directory.EnumerateFiles(srcDir, "ILInspector.*.csproj", SearchOption.AllDirectories))
        {
            var projectName = Path.GetFileNameWithoutExtension(csproj);
            if (projectName.EndsWith(".Tests", StringComparison.Ordinal))
                continue;

            foreach (var reference in ReadProjectReferences(csproj))
            {
                // csproj Include paths use either separator (the repo mixes `..\` and
                // `../`); normalize so the leaf name is extracted on any OS.
                var referenceName = Path.GetFileNameWithoutExtension(reference.Replace('\\', '/'));
                bool isArtifactContractFloor =
                    projectName == "ILInspector.Metadata"
                    && referenceName == "DotnetInspector.Artifacts";
                if (referenceName.StartsWith("DotnetInspector.", StringComparison.Ordinal)
                    && !isArtifactContractFloor)
                {
                    violations.Add($"{projectName} -> {referenceName}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Production ILInspector.* (engine) projects may reference only "
            + "DotnetInspector.Artifacts from the DotnetInspector.* project family. "
            + "Violations:\n  "
            + string.Join("\n  ", violations));
    }

    static IReadOnlyList<string> ReadEvaluatedProjectReferences(string project)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add("msbuild");
        process.StartInfo.ArgumentList.Add(project);
        process.StartInfo.ArgumentList.Add("-getItem:ProjectReference");
        process.StartInfo.ArgumentList.Add("-p:Configuration=Release");
        process.StartInfo.ArgumentList.Add("-nologo");
        process.StartInfo.ArgumentList.Add("-v:q");

        process.Start();
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
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
                $"Could not evaluate ProjectReference for {project}."
                + $"{Environment.NewLine}{standardOutput}"
                + $"{Environment.NewLine}{standardError}");
        }

        using JsonDocument document = JsonDocument.Parse(standardOutput);
        return
        [
            .. document.RootElement
                .GetProperty("Items")
                .GetProperty("ProjectReference")
                .EnumerateArray()
                .Select(reference => reference
                    .GetProperty("FullPath")
                    .GetString()
                    ?? throw new InvalidOperationException(
                        "Evaluated ProjectReference did not include FullPath."))
        ];
    }

    static IEnumerable<string> ReadProjectReferences(string csprojPath)
    {
        // Parse the XML rather than pattern-match text, so attribute order/extra
        // attributes (e.g. a Condition before Include) can't slip a reference past the
        // guardrail. Match by local name to tolerate any MSBuild XML namespace.
        var doc = XDocument.Load(csprojPath);
        foreach (var element in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var include = element.Attribute("Include")?.Value;
            if (!string.IsNullOrEmpty(include))
                yield return include;
        }
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "dotnet-inspect.slnx")))
            dir = dir.Parent;

        Assert.True(dir != null, "Could not locate the repository root (dotnet-inspect.slnx) from the test output directory.");
        return dir!.FullName;
    }
}
