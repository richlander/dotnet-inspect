using System.Reflection;

using ILInspector.Analysis;
using ILInspector.Analysis.ImplementationProfileFixtures;

namespace DotnetInspector.Queries.Tests;

public sealed class MemberDirectCallCountQueryTests
{
    [Fact]
    public void Execute_CountsExactLogicalMemberWithoutRows()
    {
        MethodInfo method = typeof(ImplementationProfileSample)
            .GetMethod(
                nameof(ImplementationProfileSample.CallHiddenTwice),
                BindingFlags.Public | BindingFlags.Static)!;

        MemberDirectCallCountResult result =
            MemberDirectCallCountQuery.Execute(
                method.DeclaringType!.Assembly.Location,
                method.MetadataToken,
                new(
                    maximumPhysicalBodies: 10,
                    maximumEncodedIlBytes: 10_000,
                    maximumAttributionProbeBodies: 20,
                    maximumAttributionProbeIlBytes: 20_000));

        var available =
            Assert.IsType<MemberDirectCallCountResult.Available>(
                result);
        Assert.Equal(2, available.Count);
        Assert.True(available.Analysis.IsComplete);
        Assert.Empty(available.Analysis.UnavailableBodies);
    }

    [Fact]
    public void Execute_SumsGeneratedPhysicalBodiesForLogicalMember()
    {
        MethodInfo method = typeof(MemberDirectCallCountQueryFixture)
            .GetMethod(
                nameof(MemberDirectCallCountQueryFixture
                    .CallsAfterYield),
                BindingFlags.Public | BindingFlags.Static)!;

        MemberDirectCallCountResult result =
            MemberDirectCallCountQuery.Execute(
                method.DeclaringType!.Assembly.Location,
                method.MetadataToken);

        var available =
            Assert.IsType<MemberDirectCallCountResult.Available>(
                result);
        Assert.True(available.Count > 0);
        Assert.All(
            available.Analysis.Counts,
            count => Assert.Equal(
                method.MetadataToken,
                count.Method.MetadataToken));
        Assert.Contains(
            available.Analysis.Counts,
            count => count.EvidenceMethod.MetadataToken
                != method.MetadataToken);
        Assert.Equal(
            available.Count,
            available.Analysis.Counts.Sum(
                static count => count.Count));
    }

    [Fact]
    public void Execute_BodilessMethodCompletesWithZero()
    {
        MethodInfo method = typeof(IMemberDirectCallCountQueryFixture)
            .GetMethod(
                nameof(IMemberDirectCallCountQueryFixture.Route))!;

        MemberDirectCallCountResult result =
            MemberDirectCallCountQuery.Execute(
                method.DeclaringType!.Assembly.Location,
                method.MetadataToken);

        var available =
            Assert.IsType<MemberDirectCallCountResult.Available>(
                result);
        Assert.Equal(0, available.Count);
        Assert.True(available.Analysis.IsComplete);
        Assert.Empty(available.Analysis.Counts);
        Assert.Empty(available.Analysis.UnavailableBodies);
        Assert.Equal(
            method.MetadataToken,
            Assert.Single(
                available.Analysis.DeclaredMethods)
                .MetadataToken);
    }
}

public static class MemberDirectCallCountQueryFixture
{
    public static async Task CallsAfterYield()
    {
        await Task.Yield();
        Console.WriteLine("done");
    }
}

public interface IMemberDirectCallCountQueryFixture
{
    void Route();
}
