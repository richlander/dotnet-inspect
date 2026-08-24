using ILInspector.Research;

namespace ILInspector.Research.Tests;

public sealed class AnnotatedSourceFindingProvenanceCatalogTests
{
    [Fact]
    public void DocumentedDescriptorCatalog_EqualsEveryReachableAnnotatedSourceProducer()
    {
        // This audited set is the typed source for
        // docs/design/annotated-source-finding-provenance.md. It must retain every descriptor
        // that can reach an Annotated Source document from either registry profile.
        var documented = new HashSet<string>(StringComparer.Ordinal)
        {
            "alloc.box",
            "alloc.array",
            "alloc.new",
            "alloc.closure",
            "alloc.statemachine",
            "alloc.delegate",
            "alloc.enumerator",
            "unsafe.deref",
            "unsafe.stackalloc",
            "unsafe.calli",
            "lifetime.ref-return",
            "lifetime.stack-bound",
            "lifetime.ref-struct-return",
            "lifetime.pointer-return",
            "lifetime.stack-escape",
            "call.edge",
            "cost.method",
            "cost.callee",
            "semantics.callee",
            "safety.callee",
        };
        var reachable = ResearchFactRegistry.Default.DescriptorIds
            .Concat(ResearchFactRegistry.CallRelationships.DescriptorIds)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            documented.SetEquals(reachable),
            $"Documented-only: {string.Join(", ", documented.Except(reachable).Order())}; "
                + $"producer-only: {string.Join(", ", reachable.Except(documented).Order())}.");
    }
}
