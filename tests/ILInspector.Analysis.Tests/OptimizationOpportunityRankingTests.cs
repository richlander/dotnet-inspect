using System.Collections.Immutable;

namespace ILInspector.Analysis.Tests;

public sealed class OptimizationOpportunityRankingTests
{
    [Fact]
    public void Order_PrioritizesActionabilityBeforeConfidenceAndReach()
    {
        OptimizationOpportunity[] opportunities =
        [
            Opportunity(
                "HighReach",
                "small-array",
                "high",
                rootReach: 100),
            Opportunity(
                "Algorithmic",
                "linq-scan-in-loop",
                "medium",
                rootReach: 1),
        ];

        Assert.Equal(
            ["Algorithmic", "HighReach"],
            OptimizationOpportunityRanking.Order(opportunities)
                .Select(opportunity => opportunity.Method.Name));
    }

    [Fact]
    public void RankMembers_UsesLeadingJudgmentThenMemberEvidenceCounts()
    {
        OptimizationOpportunity[] opportunities =
        [
            Opportunity(
                "ManyLow",
                "small-array",
                "low",
                rootReach: 100),
            Opportunity(
                "ManyLow",
                "small-array",
                "low",
                rootReach: 50,
                metadataToken: 0x06000001),
            Opportunity(
                "OneHigh",
                "allocation-hotspot",
                "high",
                rootReach: 1,
                metadataToken: 0x06000002),
        ];

        ImmutableArray<OptimizationOpportunityMemberRanking> ranked =
            OptimizationOpportunityRanking.RankMembers(
                opportunities);

        Assert.Equal(
            ["OneHigh", "ManyLow"],
            ranked.Select(member => member.Method.Name));
        Assert.Equal(
            OptimizationOpportunityPriority.High,
            ranked[0].Priority);
        Assert.Equal(2, ranked[1].Opportunities.Length);
    }

    [Fact]
    public void IteratesInLoop_UsesSemanticMultiplicityBeforeStructure()
    {
        OptimizationOpportunity conditional = Opportunity(
            "Conditional",
            "capturing-delegate",
            "high",
            rootReach: 1) with
        {
            InLoop = true,
            Multiplicity = "conditional",
        };
        OptimizationOpportunity repeated = conditional with
        {
            Multiplicity = "loop",
        };

        Assert.False(
            OptimizationOpportunityRanking.IteratesInLoop(
                conditional));
        Assert.True(
            OptimizationOpportunityRanking.IteratesInLoop(
                repeated));
    }

    [Fact]
    public void RankMembers_AttributesLiftedBodiesToTheirSourceOwner()
    {
        OptimizationOpportunity lifted = Opportunity(
            "<PublicOwner>g__Local|0_0",
            "box-value-type",
            "high",
            rootReach: 1) with
        {
            SourceOwner = Method(
                "PublicOwner",
                metadataToken: 0x06000002),
        };

        OptimizationOpportunityMemberRanking ranking =
            Assert.Single(
                OptimizationOpportunityRanking.RankMembers(
                    [lifted]));

        Assert.Equal("PublicOwner", ranking.Method.Name);
        Assert.Same(lifted, Assert.Single(ranking.Opportunities));
    }

    [Fact]
    public void IncludePerformanceOpportunity_SuppressesGeneratedFrameworkMember()
    {
        TypeRef generatedType =
            TypeRef.Definition("Ranking", "Example", "Generated");
        OptimizationOpportunity opportunity = Opportunity(
            "WriteTo",
            "box-value-type",
            "high",
            rootReach: 1) with
        {
            Method = Method(
                "WriteTo",
                metadataToken: 0x06000002) with
            {
                DeclaringType = generatedType,
            },
        };

        Assert.False(
            OptimizationOpportunityRanking
                .IncludePerformanceOpportunity(
                    opportunity,
                    new HashSet<TypeRef> { generatedType }));
    }

    static OptimizationOpportunity Opportunity(
        string name,
        string shape,
        string confidence,
        int rootReach,
        int metadataToken = 0x06000001)
    {
        MethodIdentity method = Method(name, metadataToken);
        return new OptimizationOpportunity(
            method,
            shape,
            "evidence",
            "fix",
            confidence,
            InLoop: false,
            ILOffset: null,
            Caveat: null,
            rootReach);
    }

    static MethodIdentity Method(
        string name,
        int metadataToken) =>
        new(
            "Ranking",
            Guid.Empty,
            TypeRef.Definition("Ranking", "Example", "Probe"),
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            metadataToken,
            IsStatic: true);
}
