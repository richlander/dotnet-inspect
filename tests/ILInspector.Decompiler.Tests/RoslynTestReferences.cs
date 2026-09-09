using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
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
            if (!IsManagedAssembly(path))
                continue;
            try { builder.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }
        return builder.ToImmutable();
    });

    // True only for a managed PE: an SDK layout can enumerate native / unmanaged
    // DLLs (coreclr, clrjit, *.Native.dll, ...) in TRUSTED_PLATFORM_ASSEMBLIES, and
    // handing those to Roslyn as metadata references fails every compile with CS0009
    // ("PE image doesn't contain managed metadata"). MetadataReference.CreateFromFile
    // is lazy and does not reject them, so filter by the presence of the CLR header
    // here, before the reference is built (issue #2942).
    internal static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            return pe.PEHeaders.CorHeader is not null;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
