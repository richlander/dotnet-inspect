namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Serializes tests that restore and build the package-sweep file app through the SDK's
/// shared runfile output. <see cref="EvilPoolSweepProcess"/> extends the same ownership
/// across independent test hosts.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SweepFileAppCollection
{
    public const string Name = "Package sweep file app";
}
