namespace DotnetInspector.Services.Tests;

/// <summary>
/// Serializes services tests that configure the process-global
/// <see cref="DotnetInspector.Core.CoreCache"/> root directly or through
/// <see cref="DotnetInspector.Packages.NuGetCache"/>. The collection's
/// <see cref="CollectionDefinitionAttribute.DisableParallelization"/> setting is the runtime gate.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CoreCacheCollection
{
    public const string Name = "CoreCache";
}
