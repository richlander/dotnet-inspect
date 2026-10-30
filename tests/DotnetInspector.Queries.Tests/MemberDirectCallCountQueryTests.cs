using System.Reflection;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
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
                    maximumAttributionProbeBodies: 10_000,
                    maximumAttributionProbeIlBytes: 10_000_000));

        var available =
            Assert.IsType<MemberDirectCallCountResult.Available>(
                result);
        Assert.Equal(2, available.Count);
        Assert.True(available.Analysis.IsComplete);
        Assert.Empty(available.Analysis.UnavailableBodies);
        Assert.Equal(
            MethodDefinitionLayers.Declaration
                | MethodDefinitionLayers.Body,
            available.Analysis.SourceReceipt.DeclaredLayers);
        Assert.Equal(
            0,
            available.Analysis.SourceReceipt.ModuleLookups);
        Assert.Equal(
            1,
            available.Analysis.SourceReceipt
                .Coverage.TerminalWork.BodiesAdmitted);
        Assert.Single(available.Analysis.Counts);
    }

    [Fact]
    public void Execute_SumsGeneratedPhysicalBodiesForLogicalMember()
    {
        MethodInfo method = typeof(MemberDirectCallCountQueryFixture)
            .GetMethod(
                nameof(MemberDirectCallCountQueryFixture
                    .CallsFromLambda),
                BindingFlags.Public | BindingFlags.Static)!;

        MemberDirectCallCountResult result =
            MemberDirectCallCountQuery.Execute(
                method.DeclaringType!.Assembly.Location,
                method.MetadataToken);

        var available =
            Assert.IsType<MemberDirectCallCountResult.Available>(
                result);
        Assert.True(available.Count > 0);
        Assert.Contains(
            available.Analysis.Counts,
            count => count.EvidenceMethodToken
                != method.MetadataToken);
        Assert.All(
            available.Analysis.SourceReceipt
                .Coverage.GeneratedExpansion.Origins,
            origin => Assert.Equal(
                method.MetadataToken,
                MetadataTokens.GetToken(origin.DeclaredOwner)));
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
                available.Analysis.BodylessMethodTokens));
        Assert.Equal(
            0,
            available.Analysis.SourceReceipt
                .Coverage.TerminalWork.BodiesAdmitted);
    }

    [Fact]
    public void Execute_CallSiteCountIncludesFunctionLoads()
    {
        MethodInfo method = typeof(MemberDirectCallCountQueryFixture)
            .GetMethod(
                nameof(MemberDirectCallCountQueryFixture
                    .LoadsFunctionPointer),
                BindingFlags.Public | BindingFlags.Static)!;

        var invocations =
            Assert.IsType<MemberDirectCallCountResult.Available>(
                MemberDirectCallCountQuery.Execute(
                    method.DeclaringType!.Assembly.Location,
                    method.MetadataToken));
        var callSites =
            Assert.IsType<MemberCallSiteCountResult.Available>(
                MemberCallSiteCountQuery.Execute(
                    method.DeclaringType.Assembly.Location,
                    method.MetadataToken));

        Assert.Equal(1, invocations.Count);
        Assert.Equal(2, callSites.Count);
    }

    [Fact]
    public void Execute_AttributionExhaustionWithholdsCallSiteCount()
    {
        MethodInfo method = typeof(MemberDirectCallCountQueryFixture)
            .GetMethod(
                nameof(MemberDirectCallCountQueryFixture
                    .CallsFromLambda),
                BindingFlags.Public | BindingFlags.Static)!;

        MemberCallSiteCountResult result =
            MemberCallSiteCountQuery.Execute(
                method.DeclaringType!.Assembly.Location,
                method.MetadataToken,
                new(
                    maximumPhysicalBodies: 100,
                    maximumEncodedIlBytes: long.MaxValue,
                    maximumAttributionProbeBodies: 1,
                    maximumAttributionProbeIlBytes: 1));

        var incomplete =
            Assert.IsType<MemberCallSiteCountResult.Incomplete>(
                result);
        Assert.False(incomplete.Analysis.ScopeComplete);
        Assert.Equal(
            MethodDefinitionSourceCompletion.SourceIncomplete,
            incomplete.Analysis.SourceReceipt.Completion);
        Assert.Contains(
            incomplete.Analysis.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "probe",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_TerminalBodyExhaustionWithholdsCount()
    {
        MethodInfo method = typeof(MemberDirectCallCountQueryFixture)
            .GetMethod(
                nameof(MemberDirectCallCountQueryFixture
                    .CallsAfterYield),
                BindingFlags.Public | BindingFlags.Static)!;

        MemberDirectCallCountResult result =
            MemberDirectCallCountQuery.Execute(
                method.DeclaringType!.Assembly.Location,
                method.MetadataToken,
                new(
                    maximumPhysicalBodies: 1,
                    maximumEncodedIlBytes: long.MaxValue,
                    maximumAttributionProbeBodies: 10_000,
                    maximumAttributionProbeIlBytes: 10_000_000));

        var incomplete =
            Assert.IsType<MemberDirectCallCountResult.Incomplete>(
                result);
        Assert.Equal(
            MethodDefinitionSourceCompletion.SourceIncomplete,
            incomplete.Analysis.SourceReceipt.Completion);
        Assert.Equal(
            MethodDefinitionTerminalWorkLimitKind.Bodies,
            incomplete.Analysis.SourceReceipt
                .Coverage.TerminalWork.ReachedLimit);
        Assert.Equal(
            1,
            incomplete.Analysis.SourceReceipt
                .Coverage.TerminalWork.BodiesAdmitted);
    }
}

public static class MemberDirectCallCountQueryFixture
{
    public static async Task CallsAfterYield()
    {
        await Task.Yield();
        Console.WriteLine("done");
    }

    public static Action LoadsFunctionPointer() => Target;

    public static void CallsFromLambda()
    {
        Action action = static () => Console.WriteLine("lambda");
        action();
    }

    private static void Target()
    {
    }
}

public interface IMemberDirectCallCountQueryFixture
{
    void Route();
}
