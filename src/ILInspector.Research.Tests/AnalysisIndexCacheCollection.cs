namespace ILInspector.Research.Tests;

/// <summary>
/// Serializes tests that read or configure the process-lifetime, shared
/// static <see cref="ILInspector.Research.AnalysisIndexCache"/> (bounded
/// reuse caches and path-identity history). The collection's
/// <see cref="CollectionDefinitionAttribute.DisableParallelization"/>
/// setting is the runtime gate: without it, xUnit's default cross-class
/// parallelism could let one test's cache activity evict or overwrite
/// entries another test is asserting against mid-run.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AnalysisIndexCacheCollection
{
    public const string Name = "AnalysisIndexCache";
}
