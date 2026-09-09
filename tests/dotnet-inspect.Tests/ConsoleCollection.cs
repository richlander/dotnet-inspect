namespace DotnetInspector.Tests;

/// <summary>
/// Owns process-global console, cache, network-policy, and command-host state.
/// </summary>
/// <remarks>
/// Tests in this collection may replace or delete the active <c>CoreCache</c>/
/// <c>NuGetCache</c> root. Assembly-exclusive scheduling prevents any external
/// collection from observing that temporary state (#4271).
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    public const string Name = "Console";
}
