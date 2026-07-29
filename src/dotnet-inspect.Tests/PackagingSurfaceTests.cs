using System.Xml.Linq;

namespace DotnetInspector.Tests;

/// <summary>
/// What this repository ships (#3330). Only the two tools produce packages, and
/// that must be a property of the projects rather than of the argument each
/// <c>dotnet pack</c> invocation happens to pass. Before the default was
/// inverted, <c>dotnet pack dotnet-inspect.slnx</c> emitted 17 packages: the two
/// tools plus 15 internal libraries and a benchmark fixture, all at
/// <c>1.0.0</c>, for code with no versioning story and no intent to be consumed
/// externally. A library that reaches a feed turns the layering rules in
/// <c>AGENTS.md</c> into an external compatibility surface instead of an
/// internal design constraint.
/// <para>
/// The enforcement is the root <c>Directory.Build.props</c>: with
/// <c>IsPackable=false</c> inherited, a new project ships nothing unless someone
/// writes the opt-in, so shipping is the explicit act. These tests hold the two
/// halves that makes true — that the default is actually declared, and that the
/// set of projects opting back in is exactly the shipping tools. They also give
/// review a local answer to "can this library's public API break anyone?",
/// which on #3306 had to be settled by reading two workflow files.
/// </para>
/// </summary>
public sealed class PackagingSurfaceTests
{
    static readonly string[] ShippingProjects =
    [
        "src/dotnet-inspect/dotnet-inspect.csproj",
        "src/runfaster/runfaster.csproj",
    ];

    /// <summary>
    /// The opt-in census. Matching ignores any <c>Condition</c>: a project that
    /// packs under some configuration still ships under that configuration, so
    /// it belongs in this list rather than hiding behind a condition that
    /// happens to be false today.
    /// </summary>
    [Fact]
    public void OnlyTheShippingToolsOptIntoPackaging()
    {
        var (packable, scanned) = ScanProjects();

        Assert.True(scanned > 0, "Scanned no project files; the census would pass vacuously.");
        Assert.Equal(ShippingProjects.OrderBy(static p => p, StringComparer.Ordinal).ToArray(), packable);
    }

    /// <summary>
    /// The half that keeps the census above from passing vacuously in the way
    /// that actually matters. Deleting the default restores the SDK's
    /// <c>IsPackable=true</c> for every project, at which point no project needs
    /// an opt-in and <see cref="OnlyTheShippingToolsOptIntoPackaging"/> still
    /// reports green while 16 unintended packages come back. An unconditional
    /// declaration is required for the same reason: a condition that stops
    /// holding is indistinguishable from the property being gone.
    /// </summary>
    [Fact]
    public void PackagingIsOffByDefaultForEveryProject()
    {
        string root = FindRepositoryRoot();
        var properties = XDocument
            .Load(Path.Combine(root, "Directory.Build.props"))
            .Descendants()
            .Where(static element => element.Name.LocalName == "IsPackable")
            .ToArray();

        var element = Assert.Single(properties);

        Assert.Null(element.Attribute("Condition"));
        Assert.Equal("false", element.Value.Trim(), ignoreCase: true);
    }

    /// <summary>
    /// The gate for the nested-checkout prune in <see cref="EnumerateRepositoryProjects"/>
    /// (#3422). Without it, <see cref="OnlyTheShippingToolsOptIntoPackaging"/> fails for any
    /// contributor whose linked worktree lives under the repository root — a failure that
    /// reads as a regression in whatever change is under test, and that CI never sees because
    /// it clones fresh. Hermetic rather than repository-shaped so it holds whether or not the
    /// machine running it happens to have a worktree present.
    /// </summary>
    [Fact]
    public void ProjectScanSkipsNestedCheckoutsAndBuildOutput()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"packaging-scan-{Guid.NewGuid():N}");
        try
        {
            string Project(params string[] segments)
            {
                string directory = Path.Combine([temp, .. segments]);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "sample.csproj");
                File.WriteAllText(path, "<Project />");
                return path;
            }

            string own = Project("src", "tool");
            string obj = Project("src", "tool", "obj");
            string linkedWorktree = Project(".worktrees", "feature", "src", "tool");
            string nestedClone = Project("vendor", "other", "src", "tool");
            string unmarkedNested = Project("scratch", "copy");

            // A linked worktree marks itself with a .git file; a clone with a .git directory.
            File.WriteAllText(Path.Combine(temp, ".worktrees", "feature", ".git"), "gitdir: ../../.git/worktrees/feature");
            Directory.CreateDirectory(Path.Combine(temp, "vendor", "other", ".git"));

            var found = EnumerateRepositoryProjects(temp).ToArray();

            Assert.Contains(own, found);
            Assert.DoesNotContain(obj, found);
            Assert.DoesNotContain(linkedWorktree, found);
            Assert.DoesNotContain(nestedClone, found);
            // Negative case: an ordinary nested directory is not a checkout and stays censused,
            // so the prune keys on the marker rather than on depth or on a name.
            Assert.Contains(unmarkedNested, found);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    static (string[] Packable, int Scanned) ScanProjects()
    {
        string root = FindRepositoryRoot();
        List<string> packable = [];
        int scanned = 0;

        foreach (string path in EnumerateRepositoryProjects(root))
        {
            scanned++;
            bool optsIn = XDocument.Load(path)
                .Descendants()
                .Any(static element => element.Name.LocalName == "IsPackable"
                    && string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

            if (optsIn)
                packable.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'));
        }

        packable.Sort(StringComparer.Ordinal);
        return (packable.ToArray(), scanned);
    }

    /// <summary>
    /// Every <c>*.csproj</c> belonging to <em>this</em> checkout. Build output and a
    /// nested checkout are both excluded, for the same reason: neither is a project this
    /// repository declares. The nested-checkout prune is what makes the census survive the
    /// worktree workflow <c>AGENTS.md</c> prescribes (#3422) — a linked worktree placed
    /// under the root carries its own copy of <c>src/dotnet-inspect/dotnet-inspect.csproj</c>,
    /// which is legitimately packable and would be censused as a second shipping project.
    /// Pruning on the <c>.git</c> marker rather than on a directory name covers a worktree
    /// or clone under any name, not only the repository's <c>.worktrees/</c> convention.
    /// It removes duplicates of projects the real tree already contributes, so it takes
    /// nothing away from the census.
    /// </summary>
    static IEnumerable<string> EnumerateRepositoryProjects(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();

            foreach (string path in Directory.EnumerateFiles(directory, "*.csproj"))
                yield return path;

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (IsExcludedDirectory(child))
                    continue;
                pending.Push(child);
            }
        }
    }

    static bool IsExcludedDirectory(string directory)
    {
        string name = Path.GetFileName(directory);
        if (name is "bin" or "obj" or ".git" or "artifacts" or "node_modules")
            return true;

        // A linked worktree marks itself with a .git *file*; a nested clone or submodule
        // with a .git directory. Either way the tree below belongs to another checkout.
        string marker = Path.Combine(directory, ".git");
        return File.Exists(marker) || Directory.Exists(marker);
    }

    static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing dotnet-inspect.slnx.");
    }
}
