namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Serializes tests that restore and build the package-sweep file app through the SDK's
/// shared runfile output. <see cref="EvilPoolSweepProcess"/> extends the same ownership
/// across independent test hosts. Disabling collection parallelism also keeps other
/// collections from reinitializing the process-global cache while sweep fixtures use it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SweepFileAppCollection
{
    public const string Name = "Package sweep file app";
}
