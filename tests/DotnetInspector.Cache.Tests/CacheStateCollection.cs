namespace DotnetInspector.Cache.Tests;

/// <summary>
/// Serializes tests that replace the process-global cache root or cache
/// telemetry subscriptions.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CacheStateCollection
{
    public const string Name = "PersistentCache and telemetry";
}
