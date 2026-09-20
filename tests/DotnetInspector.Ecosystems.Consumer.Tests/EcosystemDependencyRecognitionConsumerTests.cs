using DotnetInspector.Ecosystems;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems.Consumer.Tests;

public sealed class EcosystemDependencyRecognitionConsumerTests
{
    [Fact]
    public void PublicProfileAndClassifierRequireNoFriendAccess()
    {
        var observation =
            new EcosystemDependencyObservation.AssemblyReference(
                new(1),
                1,
                new AssemblyReferenceIdentity(
                    "Microsoft.Extensions.Options",
                    new Version(10, 0, 0, 0),
                    Culture: null,
                    PublicKeyToken: null),
                new PortableLibraryIdentity(
                    "Consumer.Library",
                    "1.0.0.0",
                    culture: null,
                    publicKeyToken: null));

        EcosystemDependencyClassification classification =
            EcosystemDependencyClassifier.Classify(
                EcosystemPackCatalog.DependencyRecognitionProfile,
                [observation]);

        EcosystemDependencyRecognitionEntry recognition =
            Assert.Single(classification.Recognized);
        Assert.Equal(
            EcosystemPackIds.MicrosoftExtensions,
            recognition.Ecosystem.Id);
        Assert.Same(observation, recognition.Observation);
    }
}
