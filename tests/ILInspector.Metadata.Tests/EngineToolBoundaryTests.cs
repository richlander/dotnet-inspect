using System.Text.RegularExpressions;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Architecture guardrail for the engine/tool boundary (#2568, #2579): a production
/// <c>ILInspector.*</c> (engine) project must never reference a <c>DotnetInspector.*</c>
/// (tool) project. The dependency arrow points tool → engine only. Test projects are
/// intentionally excluded — pulling shared fixtures/services into a test is normal and
/// is a separate policy call from the production boundary.
/// </summary>
public class EngineToolBoundaryTests
{
    [Fact]
    public void NoProductionEngineProjectReferencesTheToolTier()
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
                if (referenceName.StartsWith("DotnetInspector.", StringComparison.Ordinal))
                    violations.Add($"{projectName} -> {referenceName}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Production ILInspector.* (engine) projects must not reference DotnetInspector.* (tool); "
            + "the arrow points tool -> engine only. Violations:\n  "
            + string.Join("\n  ", violations));
    }

    static IEnumerable<string> ReadProjectReferences(string csprojPath)
    {
        var text = File.ReadAllText(csprojPath);
        foreach (Match match in Regex.Matches(text, "<ProjectReference\\s+Include=\"([^\"]+)\""))
            yield return match.Groups[1].Value;
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
