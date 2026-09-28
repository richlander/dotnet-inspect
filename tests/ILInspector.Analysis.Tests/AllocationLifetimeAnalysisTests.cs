using System.Security.Cryptography;

using DotnetInspector.Fixtures;

namespace ILInspector.Analysis.Tests;

public sealed class AllocationLifetimeAnalysisTests
{
    const string JurassicSha256 =
        "cfcd1b03b23dc061bd1abd05713903e0382ba47249b16c89b86a025fee119ac4";

    [Fact]
    public void CompiledFixture_UsesOneLifetimeVerdictForFactsAndTriage()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisAllocationLifetime
                .AssemblyPath());

        AllocationOccurrence local = Allocation(
            index,
            "ConstructFromLocalChars");
        AllocationOccurrence returned = Allocation(
            index,
            "ReturnLocalChars");

        Assert.Equal(AllocationEscape.LocalOnly, local.Escape);
        Assert.Equal(AllocationEscape.Escapes, returned.Escape);
        Assert.Equal(
            AllocationEscapeKind.Return,
            returned.EscapeKind);
        Assert.Contains(
            local.LifetimeEvidence.Uses,
            use => use.Kind
                == AllocationLifetimeUseKind
                    .TrustedNonCapturingCall);
        Assert.Equal(
            4,
            local.LifetimeEvidence.Uses.Count(
                use => use.Kind
                    == AllocationLifetimeUseKind.ElementWrite));
        Assert.Empty(local.LifetimeEvidence.Limitations);
        Assert.Contains(
            returned.LifetimeEvidence.Uses,
            use => use.Kind
                == AllocationLifetimeUseKind.Return);
        Assert.Empty(returned.LifetimeEvidence.Limitations);
        Assert.Contains(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.Name
                    == "ConstructFromLocalChars"
                && candidate.Shape
                    == "stackalloc-candidate"
                && candidate.ILOffset == local.ILOffset);
        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.Name == "ReturnLocalChars"
                && candidate.Shape
                    == "stackalloc-candidate");

        OptimizationOpportunity inLoop = Assert.Single(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.Name
                    == "AllocateInsideLoop"
                && candidate.Shape is
                    "small-array"
                    or "stackalloc-candidate");
        Assert.Equal("small-array", inLoop.Shape);
        Assert.Contains(
            "inside a loop",
            inLoop.Caveat,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompiledFixture_RequiresCoreLibraryPrimitiveIdentity()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisAllocationLifetime
                .AssemblyPath());

        AllocationOccurrence primitive = Allocation(
            index,
            "GenuinePrimitiveStaysLocal");
        AllocationOccurrence lookalike = Allocation(
            index,
            "PrimitiveLookalikeStaysLocal");

        Assert.Equal(AllocationEscape.LocalOnly, primitive.Escape);
        Assert.Equal(AllocationEscape.LocalOnly, lookalike.Escape);
        Assert.Contains(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.Name
                    == "GenuinePrimitiveStaysLocal"
                && candidate.Shape
                    == "stackalloc-candidate"
                && candidate.ILOffset
                    == primitive.ILOffset);
        OptimizationOpportunity rejected = Assert.Single(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.Name
                    == "PrimitiveLookalikeStaysLocal"
                && candidate.ILOffset
                    == lookalike.ILOffset);
        Assert.Equal("small-array", rejected.Shape);
        Assert.Contains(
            "element type",
            rejected.Caveat,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompiledFixture_TracksByReferenceConsumer()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisAllocationLifetime
                .AssemblyPath());

        AllocationOccurrence local = Allocation(
            index,
            "ReadThroughRefLocal");
        AllocationOccurrence transferred = Allocation(
            index,
            "PassArrayByReference");

        Assert.Equal(AllocationEscape.LocalOnly, local.Escape);
        Assert.Equal(
            AllocationLifetimeUseKind.LengthRead,
            Assert.Single(local.LifetimeEvidence.Uses).Kind);
        Assert.Empty(local.LifetimeEvidence.Limitations);

        Assert.Equal(
            AllocationEscape.Escapes,
            transferred.Escape);
        Assert.Equal(
            AllocationLifetimeUseKind.ByReferenceTransfer,
            Assert.Single(transferred.LifetimeEvidence.Uses)
                .Kind);
        Assert.Empty(
            transferred.LifetimeEvidence.Limitations);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Jurassic_LocalSurrogateArrayBecomesStackallocCandidate()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "AllocationLifetime",
            "Jurassic.dll");
        using (FileStream stream = File.OpenRead(path))
        {
            Assert.Equal(
                JurassicSha256,
                Convert.ToHexStringLower(SHA256.HashData(stream)));
        }

        var index = LibraryBodyIndex.Open(path);
        MethodIdentity method = Assert.Single(
            index.Methods,
            candidate =>
                candidate.DeclaringType
                    .ToQualifiedDisplayString()
                    == "Jurassic.Compiler.Lexer"
                && candidate.Name
                    == "ReadExtendedUnicodeSequence");
        AllocationOccurrence allocation = Assert.Single(
            index.GetAllocationOccurrences()[
                method.MetadataToken],
            occurrence =>
                occurrence.Kind == AllocationKind.Array
                && occurrence.ILOffset == 0x00cf);

        Assert.Equal(AllocationEscape.LocalOnly, allocation.Escape);
        Assert.Contains(
            new AllocationLifetimeUse(
                0x0102,
                AllocationLifetimeUseKind
                    .TrustedNonCapturingCall),
            allocation.LifetimeEvidence.Uses);
        Assert.Empty(allocation.LifetimeEvidence.Limitations);
        OptimizationOpportunity opportunity = Assert.Single(
            index.OptimizationOpportunities,
            candidate =>
                candidate.Method.MetadataToken
                    == method.MetadataToken
                && candidate.ILOffset
                    == allocation.ILOffset
                && candidate.Shape is
                    "small-array"
                    or "stackalloc-candidate");
        Assert.Equal(
            "stackalloc-candidate",
            opportunity.Shape);
        Assert.False(opportunity.InLoop);
        Assert.Null(opportunity.Caveat);
    }

    static AllocationOccurrence Allocation(
        LibraryBodyIndex index,
        string methodName)
    {
        MethodIdentity method = Assert.Single(
            index.Methods,
            candidate =>
                candidate.DeclaringType.Name
                    == "AllocationLifetimeSamples"
                && candidate.Name == methodName);
        return Assert.Single(
            index.GetAllocationOccurrences()[
                method.MetadataToken],
            occurrence =>
                occurrence.Kind == AllocationKind.Array);
    }
}
