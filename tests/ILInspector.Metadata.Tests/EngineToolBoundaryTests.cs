using System.Xml.Linq;

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
    public void MetadataHasNoSourceLinkDependencyOrProductSymbols()
    {
        string metadataDir = Path.Combine(FindRepoRoot(), "src", "ILInspector.Metadata");
        string project = Path.Combine(metadataDir, "ILInspector.Metadata.csproj");

        Assert.DoesNotContain(
            ReadProjectReferences(project),
            reference => Path.GetFileNameWithoutExtension(
                    reference.Replace('\\', '/'))
                .Equals("SourceLinkFetch", StringComparison.Ordinal));

        var violations = Directory
            .EnumerateFiles(metadataDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
                && !file.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file)
                .Contains("SourceLink", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "ILInspector.Metadata must contain only PE/PDB extraction and raw correlations; "
            + "SourceLink product symbols belong to ILInspector.SourceLink. Violations: "
            + string.Join(", ", violations));
    }

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
