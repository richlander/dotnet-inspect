using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    /// <summary>
    /// <see cref="LibraryBodyAnalysisFeatures.JsonWireContractFlow"/> is the
    /// non-vacuity gate for TypeScript facade generation's value-flow opt-in: plain method
    /// evidence retains direct calls without materializing their argument,
    /// receiver, or result flow, while the named feature supplies them.
    /// </summary>
    [Fact]
    public void MethodEvidence_OmitsCallValueFlowUntilJsonWireContractFlowIsRequested()
    {
        string path = typeof(CallSiteFixtures).Assembly.Location;
        LibraryBodyIndex plain = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        LibraryBodyIndex jsonWire = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);

        Assert.NotEmpty(plain.DirectCalls);
        Assert.Empty(plain.ResultSinks);
        Assert.All(
            plain.DirectCalls,
            call =>
            {
                Assert.Empty(call.ArgumentSources);
                Assert.Null(call.ReceiverSource);
                Assert.Equal(DirectCallResultUse.Unknown, call.ResultUse);
            });

        Assert.NotEmpty(jsonWire.ResultSinks);
        Assert.Contains(
            jsonWire.DirectCalls,
            call => call.ArgumentSources.Count > 0);
        Assert.Contains(
            jsonWire.DirectCalls,
            call => call.ReceiverSource is not null);
    }

    [Fact]
    public void FieldIdentity_CanonicalizesLocalMemberRefAliasBySignature()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "FieldAlias.dll",
                ImmutableArray.Create(EmitFieldAliasAssembly()),
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        FieldStoreFact[] stores =
        [
            .. index.FieldStores.OrderBy(store => store.ILOffset),
        ];

        Assert.Equal(9, stores.Length);
        FieldIdentity canonical = Assert.IsType<FieldIdentity>(
            stores[0].Identity);
        Assert.All(
            stores[1..4],
            store => Assert.Equal(canonical, store.Identity));
        Assert.Equal(
            stores[0].FieldToken,
            canonical.LocalDefinitionToken);
        FieldIdentity external = Assert.IsType<FieldIdentity>(
            stores[4].Identity);
        Assert.NotEqual(canonical, external);
        Assert.Equal(0, external.LocalDefinitionToken);

        // A TypeSpec parent naming a type in this module aliases the same runtime field, so it
        // canonicalizes to the same local FieldDef the direct store resolved to. Leaving it as a
        // token-0 identity would make it neither null nor equal, and a consumer counting
        // single-initialization candidates would drop it rather than count it.
        FieldIdentity typeSpec = Assert.IsType<FieldIdentity>(
            stores[5].Identity);
        Assert.Equal(canonical, typeSpec);
        Assert.Equal(
            stores[0].FieldToken,
            typeSpec.LocalDefinitionToken);
        Assert.All(
            stores[6..],
            store => Assert.Null(store.Identity));
    }

    /// <summary>
    /// Canonicalization keeps each alias's own declaring-type spelling while equality for local
    /// identities compares only name and local token. <c>GetHashCode</c> must therefore ignore
    /// the declaring type whenever the token is present, or equal identities land in different
    /// hash buckets.
    /// </summary>
    [Fact]
    public void FieldIdentity_LocalAliases_HashConsistentlyWithEquality()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "FieldAlias.dll",
                ImmutableArray.Create(EmitFieldAliasAssembly()),
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        FieldIdentity[] local =
        [
            .. index.FieldStores
                .OrderBy(store => store.ILOffset)
                .Select(store => store.Identity)
                .OfType<FieldIdentity>()
                .Where(identity => identity.LocalDefinitionToken != 0),
        ];

        // The FieldDef store plus every alias that canonicalized onto it.
        Assert.Equal(5, local.Length);

        // Non-vacuity: these aliases really do carry distinct declaring-type resolutions
        // (CurrentAssembly, AssemblyReference, and ModuleReference origins for the same type),
        // which is precisely the state the old GetHashCode mixed in and Equals ignores.
        Assert.True(
            local
                .Select(identity => identity.DeclaringType.Resolution)
                .Distinct()
                .Count() > 1,
            "expected aliases to retain distinct declaring-type resolutions");

        Assert.All(
            local,
            identity =>
            {
                Assert.Equal(local[0], identity);
                Assert.Equal(local[0].GetHashCode(), identity.GetHashCode());
            });
        Assert.Single(new HashSet<FieldIdentity>(local));
    }

    /// <summary>
    /// The narrowness gate for <see cref="FieldIdentity.MightBeSameFieldAs"/>: counting
    /// unproven writes as candidates must not degrade into counting every same-named field
    /// anywhere, or an ordinary assembly stops authenticating.
    /// </summary>
    [Fact]
    public void FieldIdentity_DistinguishesUnprovenForeignFields()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "FieldAlias.dll",
                ImmutableArray.Create(EmitFieldAliasAssembly()),
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        FieldStoreFact[] stores =
        [
            .. index.FieldStores.OrderBy(store => store.ILOffset),
        ];
        FieldIdentity canonical = Assert.IsType<FieldIdentity>(
            stores[0].Identity);
        FieldIdentity external = Assert.IsType<FieldIdentity>(
            stores[4].Identity);

        // Same field name, a declaring type that resolves to a different assembly, and no local
        // definition behind it: not this field, and not a candidate.
        Assert.Equal(canonical.Name, external.Name);
        Assert.False(canonical.MightBeSameFieldAs(external));
        Assert.False(external.MightBeSameFieldAs(canonical));

        // An access that resolved to nothing at all might be any field.
        Assert.True(canonical.MightBeSameFieldAs(null));

        // Every alias that canonicalized onto this field stays a candidate.
        Assert.All(
            stores[..4],
            store => Assert.True(
                canonical.MightBeSameFieldAs(store.Identity)));
    }

    [Fact]
    public void DirectCalls_ClassifyCallsiteMultiplicity()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var straight = Assert.Single(index.DirectCalls.Where(call =>
            call.Caller.Name == nameof(CallSiteFixtures.CallsExactAllocationLeaf)
            && call.Callee.Name == nameof(CallSiteFixtures.ExactAllocationLeaf)));
        Assert.Equal(AllocationMultiplicity.Once, straight.Multiplicity);

        var conditional = Assert.Single(index.DirectCalls.Where(call =>
            call.Caller.Name == nameof(CallSiteFixtures.ConditionallyCallsExactAllocationLeaf)
            && call.Callee.Name == nameof(CallSiteFixtures.ExactAllocationLeaf)));
        Assert.Equal(AllocationMultiplicity.Conditional, conditional.Multiplicity);
    }

    [Fact]
    public void DirectCalls_MarksCallsInsideLoopRegions()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var call = Assert.Single(index.DirectCalls.Where(c =>
            c.Caller.Name == nameof(CallSiteFixtures.CallsConsoleWriteLineInLoop)
            && c.Callee.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));

        Assert.True(call.InLoop);
    }

    [Fact]
    public void DirectCalls_MarksLoopCall_WhenForwardBranchPrecedesLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var call = Assert.Single(index.DirectCalls.Where(c =>
            c.Caller.Name == nameof(CallSiteFixtures.GuardThenCallsInLoop)
            && c.Callee.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));

        Assert.True(call.InLoop);
    }
}
