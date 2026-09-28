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
