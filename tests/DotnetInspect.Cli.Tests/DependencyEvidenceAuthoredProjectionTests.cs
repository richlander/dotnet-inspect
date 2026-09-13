using System.Text;
using System.Text.Json;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Tests;

public sealed class DependencyEvidenceAuthoredProjectionTests
{
    [Fact]
    public void Create_RetainsAuthoredIdentityProvenanceOccurrencesAndFailures()
    {
        AuthoredProjectDependencyFactsResult.Incomplete provider =
            Assert.IsType<AuthoredProjectDependencyFactsResult.Incomplete>(
                AuthoredProjectDependencyFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        """
                        <Project>
                          <ItemGroup>
                            <PackageReference Include="Example.Package" Version="1.0" />
                            <PackageReference Include="example.package" Version="1.0.0" />
                            <PackageReference Version="2.0.0" />
                          </ItemGroup>
                        </Project>
                        """)));
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery
                            .CreateAuthoredProjectInput(provider),
                    ]));

        DependencyEvidenceProjection projection =
            DependencyEvidenceProjection.Create(outcome);
        DependencyEvidenceRootRow root = Assert.Single(projection.Roots);
        DependencyEvidenceGroupRow group =
            Assert.Single(projection.DependencyGroups);
        DependencyEvidenceFailureRow unresolved = projection.Failures.Single(
            failure => failure.Reason
                == AuthoredProjectUnresolvedDependencySyntaxKind
                    .PackageReferenceWithoutInclude.ToString());
        AuthoredProjectUnresolvedDependencySyntax sourceUnresolved =
            Assert.Single(provider.Value.UnresolvedDependencySyntax);

        Assert.Equal(
            provider.Value.ContentProvenance.Sha256,
            root.ContentDigest);
        Assert.Equal(2, group.SourceOccurrenceCount);
        Assert.Equal(sourceUnresolved.OpaqueIdentity, unresolved.EvidenceIdentity);

        DependencyEvidenceDocument document = DependencyEvidenceDocument.Create(
            projection,
            new HashSet<string>(
                [
                    DependencyEvidenceSections.Roots,
                    DependencyEvidenceSections.DependencyGroups,
                    DependencyEvidenceSections.Failures,
                ],
                StringComparer.Ordinal),
            rows: null);
        DependencyEvidenceRootJson rootJson = Assert.Single(document.Roots!);
        DependencyEvidenceGroupJson groupJson =
            Assert.Single(document.DependencyGroups!);
        DependencyEvidenceFailureJson unresolvedJson =
            document.Failures!.Single(
                failure => failure.Reason
                    == AuthoredProjectUnresolvedDependencySyntaxKind
                        .PackageReferenceWithoutInclude.ToString());

        Assert.Equal(
            DependencyEvidenceIdentityOwner.AuthoredProject,
            rootJson.Identity.Owner);
        Assert.Equal(
            provider.Value.Identity.FactsDigest,
            rootJson.Identity.AuthoredProject?.FactsDigest);
        Assert.Equal(
            DependencyEvidenceIdentityOwner.AuthoredProject,
            groupJson.Identity.Owner);
        Assert.Equal(
            provider.Value.Identity.FactsDigest,
            groupJson.Identity.AuthoredProject?.Root.FactsDigest);
        var declarationOccurrence =
            Assert.Single(groupJson.Occurrences);
        Assert.Equal(
            DependencyEvidenceIdentityOwner.AuthoredProject,
            declarationOccurrence.Owner);
        Assert.Equal(
            2,
            declarationOccurrence.AuthoredDeclarationSourceOccurrenceCount);
        Assert.Equal(sourceUnresolved.OpaqueIdentity, unresolvedJson.EvidenceIdentity);
    }

    [Fact]
    public void Create_OpaqueAuthoredScopesExposeOnlyInertDisplayEvidence()
    {
        AuthoredProjectDependencyFactsResult.Incomplete provider =
            Assert.IsType<AuthoredProjectDependencyFactsResult.Incomplete>(
                AuthoredProjectDependencyFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        """
                        <Project>
                          <PropertyGroup>
                            <TargetFramework>future-tfm</TargetFramework>
                          </PropertyGroup>
                          <PropertyGroup>
                            <TargetFramework>$(FutureTarget)</TargetFramework>
                          </PropertyGroup>
                          <ItemGroup Condition="'$(Configuration)' == 'Release'">
                            <PackageReference Include="Example.Package"
                                              Version="1.0.0" />
                          </ItemGroup>
                        </Project>
                        """)));
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery
                            .CreateAuthoredProjectInput(provider),
                    ]));
        DependencyEvidenceProjection projection =
            DependencyEvidenceProjection.Create(outcome);
        DependencyEvidenceDocument document = DependencyEvidenceDocument.Create(
            projection,
            new HashSet<string>(
                [
                    DependencyEvidenceSections.Dependencies,
                    DependencyEvidenceSections.DependencyGroups,
                ],
                StringComparer.Ordinal),
            rows: null);

        Assert.Equal(3, document.DependencyGroups!.Count);
        Assert.All(
            document.DependencyGroups,
            group => Assert.Null(group.Identity.AuthoredProject?.ScopeIdentity));
        Assert.All(
            projection.DependencyGroups,
            group => Assert.StartsWith("group:", group.OrderKey));
        DependencyEvidenceDependencyJson dependency =
            Assert.Single(document.Dependencies!);
        Assert.Equal(
            document.DependencyGroups.Single(
                group => group.Group == dependency.Group).OrderKey,
            dependency.GroupOrderKey);
        string json = JsonSerializer.Serialize(
            document,
            DependencyEvidenceJsonContext.Default.DependencyEvidenceDocument);
        PackageDependencyEvidenceDeclarationResult.Available declarations =
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                Assert.Single(outcome.Roots).Declaration);
        Assert.All(
            declarations.Groups,
            group => Assert.DoesNotContain(group.OrderKey, json));
        Assert.Contains("\"source_spelling\": \"future-tfm\"", json);
        Assert.Contains("\"source_spelling\": \"$(FutureTarget)\"", json);
        Assert.DoesNotContain("\"scope_identity\"", json);
    }
}
