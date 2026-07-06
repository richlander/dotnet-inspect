using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ILInspector.Decompiler.Tests;

// Shared, process-lifetime cache of the trusted-platform MetadataReference set used
// by the in-process Roslyn compile-fixture helpers across the suite. Roslyn
// MetadataReference / AssemblyMetadata are immutable and thread-safe and are meant
// to be shared across compilations, so building the ~185-reference set once — rather
// than re-reading every framework assembly on every compile in each of ~20 test
// files — removes a large amount of redundant metadata mapping and allocation churn
// under the parallel test runner.
static class RoslynTestReferences
{
    public static ImmutableArray<MetadataReference> TrustedPlatform => s_trustedPlatform.Value;

    static readonly Lazy<ImmutableArray<MetadataReference>> s_trustedPlatform = new(() =>
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!seen.Add(path))
                continue;
            try { builder.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }
        return builder.ToImmutable();
    });
}
