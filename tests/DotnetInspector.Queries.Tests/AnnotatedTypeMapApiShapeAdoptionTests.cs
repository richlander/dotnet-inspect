using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Sections;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AnnotatedTypeMapApiShapeAdoptionTests
{
    const string FixtureNamespace = "FindingDiffAnyFixture";

    [Fact]
    public void ProductionCatalogAndContributionTrailArePinned()
    {
        Assert.Equal(
            [
                "shape.api\tApiShape\t-\tType, Members\tContribution=shape.api.contribution",
            ],
            AnnotatedTypeMapApiShapeAdoption.Catalog.Describe());

        AnnotatedTypeMapPlan plan = Plan(
            [AnnotatedTypeMapTestData.TypeFocus(), AnnotatedTypeMapTestData.TypeFocus()]);
        Assert.Equal(
            [
                "result-kind=Contribution",
                "shape.api\tSelected\tshape.api.contribution",
            ],
            plan.Explain());
    }

    [Fact]
    public void AdapterEncodesANonAdmittedMemberAsNoApplicableInput()
    {
        using PEReader before = Open(after: false);
        using PEReader after = Open(after: true);
        TypeDefinitionHandle type = Type(after, "PrivateMemberChanged");
        FindingDiffAnyMemberState hidden = Assert.Single(
            new FindingDiffAnyTypePairing(before, after)
                .Members($"{FixtureNamespace}.PrivateMemberChanged").Members,
            member => member.After.Kind == HandleKind.MethodDefinition
                && after.GetMetadataReader().GetString(
                    after.GetMetadataReader()
                        .GetMethodDefinition((MethodDefinitionHandle)member.After)
                        .Name) == "Helper");
        AnnotatedTypeMapApiShapeAuthority authority = Authority();
        SubjectRelationFocusAuthority focus = Focus(authority, after, type);
        AnnotatedTypeMapPlan plan = Plan([focus], [new(hidden.Key)]);

        InspectionQueryResults results =
            AnnotatedTypeMapApiShapeAdoption.QueryCatalog.Run(
                plan.Queries,
                new(plan, focus, after, type));
        AnnotatedTypeMapContributionResult settled =
            AnnotatedTypeMapGeneration.SettleContributions(plan, results);
        var content = Assert.IsType<AnnotatedTypeMapApiShapeContent>(
            settled.Content(
                AnnotatedTypeMapFamily.ApiShape,
                AnnotatedTypeMapSide.Single));

        Assert.Equal(
            AnnotatedTypeMapOutcomeKind.NoApplicableInput,
            Assert.Single(content.Members).Value.Outcome.Kind);
        Assert.Equal(
            AnnotatedTypeMapOutcomeKind.Complete,
            content.TypeEntry!.Outcome.Kind);
        Assert.Throws<ArgumentNullException>(() =>
            new AnnotatedTypeMapApiMemberFacts(null!));
    }

    [Fact]
    public void FamilySettlerAgreesWithInlineSettlementForEveryFixtureType()
    {
        using PEReader before = Open(after: false);
        using PEReader after = Open(after: true);
        FindingDiffAnyResult inline = FindingDiffAny.Compare(
            before,
            after,
            bodyReader: null,
            apiSettler: null,
            FindingDiffAnyAxes.Api,
            TestContext.Current.CancellationToken);
        FindingDiffAnyResult settled = FindingDiffAny.Compare(
            before,
            after,
            bodyReader: null,
            AnnotatedTypeMapApiShapeAdoption.CreateSettler(Authority(), Authority()),
            FindingDiffAnyAxes.Api,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            inline.Types.Select(type => (type.FullName, type.Api)),
            settled.Types.Select(type => (type.FullName, type.Api)));
    }

    private static AnnotatedTypeMapPlan Plan(
        SubjectRelationFocusAuthority[] focuses,
        AnnotatedTypeMapMemberKey[]? keys = null) =>
        Assert.IsType<AnnotatedTypeMapPlanningResult.Accepted>(
            AnnotatedTypeMapApiShapeAdoption.Catalog.Resolve(
                new(
                    AnnotatedTypeMapRequestForm.Contribution,
                    focuses,
                    [AnnotatedTypeMapFamily.ApiShape],
                    memberKeys: keys),
                AnnotatedTypeMapApiShapeAdoption.QueryCatalog))
            .Plan;

    private static AnnotatedTypeMapApiShapeAuthority Authority()
    {
        SubjectRelationFocusAuthority focus = AnnotatedTypeMapTestData.TypeFocus();
        ResolvedAssemblyReference assembly =
            AnnotatedTypeMapTestData.Library(AnnotatedTypeMapTestData.Coordinate())
                .Participant.Assembly;
        return new(
            focus.Workspace,
            assembly.Registration,
            assembly.Identity,
            assembly.Provenance);
    }

    private static SubjectRelationFocusAuthority Focus(
        AnnotatedTypeMapApiShapeAuthority authority,
        PEReader image,
        TypeDefinitionHandle type)
    {
        MetadataTypeDefinitionName name = Assert.IsType<MetadataTypeDefinitionNameReadResult.Read>(
            MetadataTypeDefinitionName.Read(image.GetMetadataReader(), type)).Name;
        return SubjectRelationFocusAuthority.ForAcquiredType(
            authority.Workspace,
            authority.Registration,
            authority.Assembly,
            authority.Provenance,
            name);
    }

    private static PEReader Open(bool after)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        return new PEReader(File.OpenRead(after ? pair.NewAssemblyPath() : pair.OldAssemblyPath()));
    }

    private static TypeDefinitionHandle Type(PEReader image, string name)
    {
        MetadataReader reader = image.GetMetadataReader();
        return reader.TypeDefinitions.Single(handle =>
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            return type.GetDeclaringType().IsNil
                && reader.GetString(type.Namespace) == FixtureNamespace
                && reader.GetString(type.Name) == name;
        });
    }
}
