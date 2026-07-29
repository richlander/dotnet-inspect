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

    static (string[] Packable, int Scanned) ScanProjects()
    {
        string root = FindRepositoryRoot();
        List<string> packable = [];
        int scanned = 0;

        foreach (string path in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsExcluded(root, path))
                continue;

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

    static bool IsExcluded(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar)
            .Any(static segment => segment is "bin" or "obj" or ".git" or "artifacts" or "node_modules");

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
