using NuGetFetch.Plugins;

namespace NuGetFetch.Tests;

/// <summary>
/// Pins how credential plugins are found.
/// </summary>
/// <remarks>
/// <para>
/// Credential providers are never registered in nuget.config — the Azure Artifacts provider has
/// no install or register verb, and writes nothing to any config file. Discovery is entirely by
/// convention, which makes it easy to implement one route and believe the job is done. These
/// tests pin all three, and the precedence between them.
/// </para>
/// <para>
/// Route order and semantics follow NuGet/NuGet.Client,
/// src/NuGet.Core/NuGet.Protocol/Plugins/PluginDiscoverer.cs and PluginDiscoveryUtility.cs.
/// </para>
/// </remarks>
public sealed class PluginDiscoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("plugin-discovery").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public void NetCoreVariable_NamesEntryPointsDirectly()
    {
        string plugin = CreateFile("explicit/CredentialProvider.Microsoft.dll");

        var found = PluginDiscovery.Discover(Env(netCore: plugin), nuGetHome: _root);

        var one = Assert.Single(found);
        Assert.Equal(plugin, one.Path);

        // A .dll cannot be executed directly; it runs as "dotnet <path> -Plugin".
        Assert.True(one.RequiresDotnetHost);
    }

    [Fact]
    public void NetCoreVariable_ReplacesConventionDirectoryRatherThanAddingToIt()
    {
        string explicitPlugin = CreateFile("explicit/Explicit.dll");
        CreateFile("plugins/netcore/Convention/Convention.dll");

        var found = PluginDiscovery.Discover(Env(netCore: explicitPlugin), nuGetHome: _root);

        // NuGet treats the variable as an override, not a supplement. A caller who sets it is
        // stating exactly which plugins may run, and silently adding others would defeat that.
        Assert.Equal([explicitPlugin], found.Select(p => p.Path));
    }

    [Fact]
    public void PluginPathsVariable_IsConsultedOnlyWhenNetCoreVariableIsUnset()
    {
        string netCorePlugin = CreateFile("a/NetCore.dll");
        string genericPlugin = CreateFile("b/Generic.dll");

        var bothSet = PluginDiscovery.Discover(Env(netCore: netCorePlugin, generic: genericPlugin), nuGetHome: _root);
        Assert.Equal([netCorePlugin], bothSet.Select(p => p.Path));

        var genericOnly = PluginDiscovery.Discover(Env(generic: genericPlugin), nuGetHome: _root);
        Assert.Equal([genericPlugin], genericOnly.Select(p => p.Path));
    }

    [Fact]
    public void PluginPathsVariable_AcceptsSeveralEntriesAndDirectories()
    {
        string first = CreateFile("first/First.dll");
        string directory = Path.Combine(_root, "scanned");
        string inDirectory = CreateExecutable("scanned/nuget-plugin-scanned");

        var found = PluginDiscovery.Discover(
            Env(generic: string.Join(Path.PathSeparator, first, directory)),
            nuGetHome: _root);

        Assert.Equal([first, inDirectory], found.Select(p => p.Path));
    }

    [Fact]
    public void ConventionDirectory_RequiresEntryPointNamedAfterItsFolder()
    {
        string matching = CreateFile("plugins/netcore/CredentialProvider.Microsoft/CredentialProvider.Microsoft.dll");
        CreateFile("plugins/netcore/Mismatched/SomethingElse.dll");

        var found = PluginDiscovery.Discover(Env(), nuGetHome: _root);

        // The folder name is the contract. A directory full of dependencies must not produce
        // one "plugin" per assembly.
        Assert.Equal([matching], found.Select(p => p.Path));
    }

    [Fact]
    public void ConventionDirectory_IsAbsentOnMostMachinesAndThatIsNotAnError()
    {
        var found = PluginDiscovery.Discover(Env(), nuGetHome: Path.Combine(_root, "does-not-exist"));

        Assert.Empty(found);
    }

    [Fact]
    public void Path_FindsNuGetPluginPrefixedExecutables()
    {
        string plugin = CreateExecutable("bin/nuget-plugin-microsoft-artifacts-credential-provider");

        var found = PluginDiscovery.Discover(Env(path: Path.Combine(_root, "bin")), nuGetHome: _root);

        var one = Assert.Single(found);
        Assert.Equal(plugin, one.Path);

        // PATH-discovered plugins are apphosts and are launched directly, never via "dotnet".
        Assert.False(one.RequiresDotnetHost);
    }

    [Fact]
    public void Path_IgnoresFilesWithoutThePrefix()
    {
        CreateExecutable("bin/dotnet-inspect");
        CreateExecutable("bin/plugin-nuget-backwards");
        string plugin = CreateExecutable("bin/nuget-plugin-real");

        var found = PluginDiscovery.Discover(Env(path: Path.Combine(_root, "bin")), nuGetHome: _root);

        Assert.Equal([plugin], found.Select(p => p.Path));
    }

    [Fact]
    public void Path_MatchesThePrefixCaseSensitively()
    {
        CreateExecutable("bin/NuGet-Plugin-Uppercase");

        var found = PluginDiscovery.Discover(Env(path: Path.Combine(_root, "bin")), nuGetHome: _root);

        // NuGet compares with StringComparison.Ordinal. Accepting other casings here would find
        // plugins that the real client would not, and the two must agree.
        Assert.Empty(found);
    }

    [Fact]
    public void Path_SkipsNonExecutableFiles()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The execute bit is a Unix concept; Windows filters by extension.");

        CreateFile("bin/nuget-plugin-not-executable");

        var found = PluginDiscovery.Discover(Env(path: Path.Combine(_root, "bin")), nuGetHome: _root);

        Assert.Empty(found);
    }

    [Fact]
    public void Path_ToleratesEntriesThatDoNotExist()
    {
        string plugin = CreateExecutable("bin/nuget-plugin-real");
        string path = string.Join(Path.PathSeparator, "/no/such/directory", Path.Combine(_root, "bin"), string.Empty);

        var found = PluginDiscovery.Discover(Env(path: path), nuGetHome: _root);

        // A real PATH is full of stale and empty entries; none of them is a reason to fail.
        Assert.Equal([plugin], found.Select(p => p.Path));
    }

    [Fact]
    public void ConventionDirectoryAndPath_AreBothSearched()
    {
        string convention = CreateFile("plugins/netcore/Convention/Convention.dll");
        string onPath = CreateExecutable("bin/nuget-plugin-tool");

        var found = PluginDiscovery.Discover(Env(path: Path.Combine(_root, "bin")), nuGetHome: _root);

        // Installing via the classic script and via "dotnet tool install" produce different
        // routes, and a machine may legitimately have both.
        Assert.Equal([convention, onPath], found.Select(p => p.Path));
    }

    private static Func<string, string?> Env(string? netCore = null, string? generic = null, string? path = null) =>
        name => name switch
        {
            "NUGET_NETCORE_PLUGIN_PATHS" => netCore,
            "NUGET_PLUGIN_PATHS" => generic,
            "PATH" => path,
            _ => null,
        };

    /// <remarks>
    /// The relative path is written with '/' for readability, but the product builds its paths
    /// from <see cref="Directory.EnumerateFiles(string)"/> and <see cref="Path.Combine(string, string)"/>,
    /// which yield the platform separator throughout. <see cref="Path.Combine(string, string)"/> does not
    /// rewrite separators inside a segment, so without this normalization the expected and actual
    /// paths differ by separator alone on Windows.
    /// </remarks>
    private string CreateFile(string relativePath)
    {
        string full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, string.Empty);
        return full;
    }

    /// <remarks>
    /// Executability is expressed differently per platform, and the fixture has to match the
    /// product: <c>PluginDiscovery</c> requires a <c>.exe</c> or <c>.bat</c> extension on Windows
    /// and the owner execute bit elsewhere. An extensionless file with the execute bit set is not
    /// a Windows executable, so the extension is part of creating one.
    /// </remarks>
    private string CreateExecutable(string relativePath)
    {
        string full = CreateFile(OperatingSystem.IsWindows() ? relativePath + ".exe" : relativePath);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(full, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return full;
    }
}
