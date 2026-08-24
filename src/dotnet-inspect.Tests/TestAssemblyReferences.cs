using ILInspector.Metadata;

namespace DotnetInspector.Tests;

internal static class TestAssemblyReferences
{
    public static ResolvedAssemblyReference Designated(string path) =>
        ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Designated("test fixture"));
}
