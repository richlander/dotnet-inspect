using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class ImplementationProfilesQueryTests
{
    [Fact]
    public void Execute_RetainsProfilesAndExactOverloadRelationships()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
            LibraryBodyAnalysisFeatures.ImplementationProfiles);

        ImplementationProfilesResult result =
            ImplementationProfilesQuery.Execute(index);

        var available =
            Assert.IsType<ImplementationProfilesResult.Available>(
                result);
        MethodImplementationProfile[] profiles =
        [
            .. available.Profiles.Where(
                profile =>
                    profile.Method.DeclaringType.Name
                        == "ImplementationProfileSample"
                    && profile.Method.Name == "Analyze"),
        ];

        Assert.Equal(4, profiles.Length);
        var implementation = Assert.Single(
            profiles,
            profile => profile.Method.ParameterTypes.Length == 2);
        var wrapper = Assert.Single(
            profiles,
            profile =>
                profile.Method.ParameterTypes.Length == 1
                && profile.Method.ParameterTypes[0].Name == "Int32");
        Assert.Single(
            profiles,
            profile =>
                profile.Method.ParameterTypes.Length == 1
                && profile.Method.ParameterTypes[0].Name == "String");
        MethodImplementationProfile[] asyncProfiles =
        [
            .. available.Profiles.Where(
                profile =>
                    profile.Method.DeclaringType.Name
                        == "ImplementationProfileSample"
                    && profile.Method.Name == "AnalyzeAsync"
                    && profile.Method.ParameterTypes.Length == 1
                    && profile.Method.ParameterTypes[0].Name
                        == "Int32"),
        ];
        Assert.Equal(2, asyncProfiles.Length);
        Assert.Single(
            asyncProfiles,
            profile => profile.Async);

        OverloadCallRelationship relationship = Assert.Single(
            available.OverloadRelationships,
            relationship =>
                relationship.Caller.DeclaringType.Name
                    == "ImplementationProfileSample"
                && relationship.Caller.Name == "Analyze");
        Assert.Equal(wrapper.Method, relationship.Caller);
        Assert.Equal(implementation.Method, relationship.Callee);
        Assert.True(relationship.ILOffset >= 0);
        Assert.Empty(available.Diagnostics);
        Assert.Equal(
            InspectionCost.Unbounded,
            ImplementationProfilesQuery.Definition.Cost);
    }

    [Fact]
    public void Execute_MissingProfileAcquisitionRemainsTypedFailure()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
            LibraryBodyAnalysisFeatures.MethodEvidence);

        ImplementationProfilesResult result =
            ImplementationProfilesQuery.Execute(index);

        var failed =
            Assert.IsType<ImplementationProfilesResult.Failed>(
                result);
        Assert.Contains(
            "were not requested",
            failed.Error.Message,
            StringComparison.Ordinal);
    }
}
