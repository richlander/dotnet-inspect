using System.Reflection;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Seam pin for the type-forwarder resolution sink in <see cref="SourceEnricher"/>.
/// </summary>
/// <remarks>
/// The refusal itself is owned and proved by <c>PdbContextForwarderResolutionTests</c>, which
/// plants a payload where a traversing forwarder name would land. What that canary cannot see is
/// whether a caller reaches the owner at all: <c>TryEnrichFromForwardedAssemblyAsync</c> used to
/// take the forwarding assembly's path and rebuild the resolution itself, which is exactly how it
/// escaped the guard. These tests pin the shape that fix depends on — the method resolves through
/// a <see cref="PdbContext"/> and is handed no filesystem path to re-derive one from — so
/// reintroducing a second resolver fails here rather than silently restoring the sink. They pin
/// the seam, not the guard; a caller that took a <see cref="PdbContext"/> and still built its own
/// path would pass this and be caught only by review.
/// </remarks>
public class SourceEnricherForwarderSeamTests
{
    private static MethodInfo ForwardedAssemblyMethod
        => typeof(SourceEnricher).GetMethod(
               "TryEnrichFromForwardedAssemblyAsync",
               BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new InvalidOperationException(
               "SourceEnricher.TryEnrichFromForwardedAssemblyAsync is missing; the forwarder seam moved.");

    [Fact]
    public void ForwardedEnrichment_ResolvesThroughPdbContext()
    {
        var parameters = ForwardedAssemblyMethod.GetParameters();

        Assert.Contains(parameters, p => p.ParameterType == typeof(PdbContext));
    }

    [Fact]
    public void ForwardedEnrichment_IsNotHandedAPathToRebuildResolutionFrom()
    {
        var pathLike = ForwardedAssemblyMethod.GetParameters()
            .Where(p => p.ParameterType == typeof(string))
            .Where(p => p.Name is not null &&
                        (p.Name.Contains("path", StringComparison.OrdinalIgnoreCase) ||
                         p.Name.Contains("dir", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            pathLike.Count == 0,
            $"Forwarded enrichment must resolve through PdbContext, not from a path of its own: {string.Join(", ", pathLike)}");
    }
}
