using System.Collections.Immutable;
using DotnetInspector.Queries.Consumer;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace DotnetInspector.Queries.Tests;

public sealed class BorrowedDeclarationInventoryConsumerTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public void ReferencePack_PreservesForwarderAndDefinitionAfterAuthorizedBorrowEnds()
    {
        AssemblyTypeDeclarationInventory facade = Read("netstandard.dll");
        AssemblyTypeDeclarationInventory runtime = Read("System.Runtime.dll");
        var objectName = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("System", ["Object"])).Name;

        Assert.Equal("netstandard", facade.Identity.Name);
        Assert.Contains(objectName, facade.Forwarders);
        Assert.DoesNotContain(objectName, facade.Definitions);
        Assert.Equal("System.Runtime", runtime.Identity.Name);
        Assert.Contains(runtime.TypeDefinitions, entry => entry.IsPublic && entry.Name == objectName);
        Assert.DoesNotContain(objectName, runtime.Forwarders);
        Assert.Empty(facade.ModuleExports);
        Assert.Empty(runtime.ModuleExports);
        Assert.Equal(AssemblySurfaceKind.Facade, AssemblySurfaceClassifier.Classify(facade).Classification.Kind);
        Assert.Equal(AssemblySurfaceKind.Implementation, AssemblySurfaceClassifier.Classify(runtime).Classification.Kind);
    }

    static AssemblyTypeDeclarationInventory Read(string fileName)
    {
        var owner = new ArtifactGenerationAuthority();
        CancellationToken token = TestContext.Current.CancellationToken;
        try
        {
            ArtifactAdmissionAuthorization admission = owner.CreateAdmissionAuthorization();
            using ArtifactAdmissionLease admissionLease = owner.IssueLease(admission);
            ArtifactContribution contribution;
            using (ArtifactContributionScope scope = owner.BeginContribution(admission))
            {
                contribution = scope.Register(new Provenance(),
                    _ => throw new InvalidOperationException("Source reopening is not part of this query."));
            }
            byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                "RealAssets", "DeclarationInventory", fileName));
            RetainedArtifactContent content = owner.CreateRetainedContent(
                contribution.Registration, ImmutableArray.Create(bytes));
            ArtifactAssemblyProjection projection =
                Assert.IsType<ArtifactAssemblyProjectionOutcome.Projected>(
                    ArtifactAssemblyProjectionOutcome.FromAccess(
                        content.WithAdmissionContent(admissionLease,
                            ArtifactAssemblyInspection.Project, token))).Value;
            owner.CompleteAdmission(admission);
            using ArtifactQueryLease lease = owner.IssueLease(owner.CreateQueryAuthorization());
            var outcome = ArtifactAssemblyQueryOutcome<AssemblyTypeDeclarationInventoryOutcome>.FromAccess(
                content.WithQueryContent(lease, (view, cancellationToken) =>
                    BorrowedDeclarationInventoryConsumer.Read(view, projection, cancellationToken), token));
            return Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Read>(
                Assert.IsType<ArtifactAssemblyQueryOutcome<AssemblyTypeDeclarationInventoryOutcome>.Validated>(
                    outcome).Value).Inventory;
        }
        finally
        {
            owner.EndGeneration();
        }
    }

    sealed record Provenance : IArtifactProvenance;
}
