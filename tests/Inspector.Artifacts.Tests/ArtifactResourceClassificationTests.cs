using System.Reflection;
using Inspector.Artifacts.Workspaces;
using Inspector.Resources;

namespace Inspector.Artifacts.Tests;

public sealed class ArtifactResourceClassificationTests
{
    [Fact]
    public void CurrentArtifactOwnershipObligations_AreDeclared()
    {
        Type[] expected =
        [
            typeof(ArtifactAccessLease),
            typeof(ArtifactAdmissionLease),
            typeof(IArtifactAccessLease),
            typeof(IArtifactAcquisitionLease),
            typeof(ArtifactContributionScope),
            typeof(ArtifactQueryLease),
            typeof(ArtifactSetSession)
        ];

        Type[] actual = expected
            .Select(type => type.Assembly)
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                type.GetCustomAttribute<ResourceOwnershipAttribute>(
                    inherit: false) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            expected.OrderBy(type => type.FullName, StringComparer.Ordinal),
            actual);
    }

    [Fact]
    public void ArtifactAuthorityAndReferenceEvidence_AreNotOwnershipObligations()
    {
        Type[] resourceFreeTypes =
        [
            typeof(ArtifactAdmissionAuthorization),
            typeof(ArtifactQueryAuthorization),
            typeof(ArtifactContentReference),
            typeof(ArtifactDescriptor)
        ];

        Assert.All(
            resourceFreeTypes,
            type => Assert.Null(
                type.GetCustomAttribute<ResourceOwnershipAttribute>(
                    inherit: false)));
    }
}
