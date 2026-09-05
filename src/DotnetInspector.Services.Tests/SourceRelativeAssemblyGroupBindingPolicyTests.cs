using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public class SourceRelativeAssemblyGroupBindingPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractApiSurface_SharedForwarderRetainsBothResolverContexts(
        bool interfaceFirst)
    {
        string consumerPath = FixtureCatalog.ServicesRouteLearningConsumer.AssemblyPath();
        ResolvedAssemblyReference classConsumer = Descriptor(consumerPath);
        ResolvedAssemblyReference interfaceConsumer = Descriptor(consumerPath);
        ResolvedAssemblyReference middle = Descriptor(
            FixtureCatalog.ServicesRouteLearningConsumer.AssetPath("middle"));
        ResolvedAssemblyReference classBase = Descriptor(
            FixtureCatalog.ServicesRouteLearningConsumer.AssetPath("base"));
        ResolvedAssemblyReference interfaceBase = Descriptor(
            FixtureCatalog.ServicesRouteLearningInterfaceBase.AssemblyPath());
        var classPolicy = new AssemblyReferenceBindingPolicy(
            new FixtureResolver(consumerPath, middle, classBase));
        var interfacePolicy = new AssemblyReferenceBindingPolicy(
            new FixtureResolver(consumerPath, middle, interfaceBase));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (classConsumer, (IAssemblyBindingPolicy)classPolicy),
                (interfaceConsumer, (IAssemblyBindingPolicy)interfacePolicy),
            ]);
        AssemblyBindingPolicyVersion version = group.Version;
        using var catalog = new TypeResolutionCatalog();
        (ResolvedAssemblyReference Assembly, TypeParameterTypeKind Kind)[] requests =
            [
                (classConsumer, TypeParameterTypeKind.ReferenceType),
                (interfaceConsumer, TypeParameterTypeKind.NeitherReferenceNorValue),
            ];
        if (interfaceFirst)
            Array.Reverse(requests);

        for (int repeat = 0; repeat < 2; repeat++)
        {
            foreach (var request in requests)
            {
                ApiSurface surface = Assert.IsType<
                    ResolutionAwareApiSurfaceOutcome.Read>(
                        catalog.ExtractApiSurface(request.Assembly, group)).Surface;
                ApiType consumer = Assert.Single(
                    surface.Types,
                    static type => type.FullName
                        == "DotnetInspector.Services.RouteLearning.Consumer`1");

                Assert.Equal(
                    request.Kind,
                    Assert.Single(consumer.TypeParameters).TypeKind);
                Assert.Empty(surface.InspectionFailures);
                Assert.Same(version, group.Version);
            }
        }
    }

    static ResolvedAssemblyReference Descriptor(string path) =>
        ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Local("resolver-lineage fixture"));

    sealed class FixtureResolver(
        string consumerPath,
        ResolvedAssemblyReference middle,
        ResolvedAssemblyReference implementation) : IAssemblyReferenceResolver
    {
        readonly AssemblyDependencyResolver _fallback = new(
            new AssemblyDependencyResolutionOptions(consumerPath)
            {
                PreferImplementationAssemblies = true,
                AllowPlatformAssemblyVersionRollForward = true,
            });

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
        {
            if (identity.Name == middle.Identity.Name)
                return middle;
            if (identity.Name == implementation.Identity.Name)
                return implementation;
            return _fallback.Resolve(identity, scope);
        }
    }
}
