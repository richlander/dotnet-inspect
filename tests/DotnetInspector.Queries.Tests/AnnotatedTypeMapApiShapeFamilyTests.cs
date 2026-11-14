using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// The API shape family, its adapter, the production registry, and the
/// settler (docs/design/annotated-type-map-api-family.md#gates): Agreement,
/// Key selection, Trail, and Encoding.
/// </summary>
public sealed class AnnotatedTypeMapApiShapeFamilyTests
{
    const string Fixture = "FindingDiffAnyFixture.";
    const string ContributionQuery = "annotated-type-map.shape.api.contribution";

    [Fact]
    public void ProductionRegistrySnapshotIsPinned()
    {
        Assert.Equal(
            [$"shape.api\tApiShape\t-\tType, Members\tContribution={ContributionQuery}"],
            AnnotatedTypeMapProduction.Catalog.Describe());
        Assert.True(AnnotatedTypeMapProduction.Queries.Contains(AnnotatedTypeMapApiShapeFamily.ContributionQuery));
    }

    [Fact]
    public void Settler_AgreesWithTheInlineSettlement_ForEveryFixtureType()
    {
        FindingDiffAnyResult inline = Compare(settler: null);
        var settled = new Recorder();
        FindingDiffAnyResult viaFamily = Compare(settled.Settler.Settle);

        Assert.True(settled.Plans.Count > 3);
        Assert.Equal(
            inline.Types.Select(type => (type.FullName, type.Api)),
            viaFamily.Types.Select(type => (type.FullName, type.Api)));
    }

    [Fact]
    public void SettlerPlan_ExplainsTheApiShapeFamilyBoundToItsAdapter_WithOneFocusPerSide()
    {
        var recorder = new Recorder();
        Compare(recorder.Settler.Settle);

        AnnotatedTypeMapPlan plan = recorder.Plans[Fixture + "OverloadDefaultChanged"];
        Assert.Equal(
            ["result-kind=Contribution", $"shape.api\tSelected\t{ContributionQuery}"],
            plan.Explain());
        Assert.True(plan.Request.VersionPair);
        Assert.Equal(2, plan.Request.Focus.Length);
        Assert.NotSame(plan.Request.Focus[0], plan.Request.Focus[1]);
        Assert.Same(plan.Request.Focus[0], plan.Request.FocusOf(AnnotatedTypeMapSide.Before));
        Assert.Same(plan.Request.Focus[1], plan.Request.FocusOf(AnnotatedTypeMapSide.After));
    }

    [Fact]
    public void SettlerRequest_CarriesTheOwningMembersKeys_OrNoKeySet()
    {
        var recorder = new Recorder();
        Compare(recorder.Settler.Settle);

        FindingDiffAnyMemberState changed = Assert.Single(
            Members("OverloadDefaultChanged").Members,
            member => member.Api == FindingDiffAnyState.Changed);
        Assert.Equal(
            [new AnnotatedTypeMapMemberKey(changed.Key)],
            recorder.Plans[Fixture + "OverloadDefaultChanged"].Request.MemberKeys!.Value);

        Assert.Equal(
            Members("TypeAttributeChanged").Members.Select(member => new AnnotatedTypeMapMemberKey(member.Key)).ToHashSet(),
            recorder.Plans[Fixture + "TypeAttributeChanged"].Request.MemberKeys!.Value.ToHashSet());

        Assert.Empty(Members("ITypeAttributeOnly").Members);
        Assert.Null(recorder.Plans[Fixture + "ITypeAttributeOnly"].Request.MemberKeys);
    }

    [Fact]
    public void Adapter_EncodesAMemberTheSurfaceDoesNotAdmitAsNoApplicableInput()
    {
        ImmutableArray<FindingDiffAnyMemberState> members = Members("PrivateMemberChanged").Members;
        Assert.Contains(members, member => member.After.Kind == HandleKind.MethodDefinition);
        using PEReader image = Open(after: true);
        TypeDefinitionHandle type = Type(image, "PrivateMemberChanged");
        SubjectRelationFocusAuthority focus = AnnotatedTypeMapTestData.TypeFocusAt("2.0.0")(Name(image, type));
        ImmutableArray<AnnotatedTypeMapMemberKey> keys =
            [.. members.Select(member => new AnnotatedTypeMapMemberKey(member.Key))];

        using var producer = new FindingDiffAnyApiShapeProducer(image);
        AnnotatedTypeMapContribution contribution = AnnotatedTypeMapApiShapeFamily.Contribute(
            new AnnotatedTypeMapSideInput(focus, producer, type, keys));

        Assert.Same(focus, contribution.Focus);
        AnnotatedTypeMapApiShapeContent content =
            Assert.IsType<AnnotatedTypeMapApiShapeContent>(Assert.Single(contribution.Families));
        Assert.Equal(AnnotatedTypeMapOutcomeKind.Complete, content.TypeEntry!.Outcome.Kind);
        Assert.True(content.TypeEntry.Content!.Admitted);
        // Only the requested keys present on this side: a Member declared on
        // the Before side alone is absent here.
        Assert.Contains(members, member => member.After.IsNil);
        Assert.Equal(
            members.Where(member => !member.After.IsNil).Select(member => new AnnotatedTypeMapMemberKey(member.Key)).ToHashSet(),
            content.Keys.ToHashSet());
        Assert.Contains(content.Members.Values, entry => entry.Outcome.Kind == AnnotatedTypeMapOutcomeKind.NoApplicableInput);
        Assert.Contains(content.Members.Values, entry => entry.Outcome.Kind == AnnotatedTypeMapOutcomeKind.Complete);
        Assert.All(content.Members.Values, entry =>
        {
            Assert.True(entry.Outcome.IsSettled);
            Assert.Equal(entry.Outcome.Kind == AnnotatedTypeMapOutcomeKind.Complete, entry.Content is not null);
        });
        Assert.Throws<ArgumentNullException>(() => new AnnotatedTypeMapApiMemberFacts(null!));
    }

    [Fact]
    public void Adapter_WithoutKeys_ContributesTheTypeEntryAlone_AndATypeNotAdmitted()
    {
        using PEReader image = Open(after: true);
        TypeDefinitionHandle type = Type(image, "InternalBodyOnly");
        SubjectRelationFocusAuthority focus = AnnotatedTypeMapTestData.TypeFocusAt("2.0.0")(Name(image, type));

        using var producer = new FindingDiffAnyApiShapeProducer(image);
        AnnotatedTypeMapApiShapeContent content = Assert.IsType<AnnotatedTypeMapApiShapeContent>(Assert.Single(
            AnnotatedTypeMapApiShapeFamily.Contribute(new AnnotatedTypeMapSideInput(focus, producer, type, memberKeys: null)).Families));

        Assert.Equal(AnnotatedTypeMapOutcomeKind.Complete, content.TypeEntry!.Outcome.Kind);
        Assert.False(content.TypeEntry.Content!.Admitted);
        Assert.Null(content.TypeEntry.Content.Projection);
        Assert.Empty(content.Keys);
    }

    // Records each accepted plan by the Type its Before focus names.
    private sealed class Recorder
    {
        private string? _current;

        public Recorder() =>
            Settler = new AnnotatedTypeMapApiShapeSettler(
                name =>
                {
                    _current = name.ToMetadataFullName();
                    return Before(name);
                },
                After,
                plan => Plans.Add(_current!, plan));

        public AnnotatedTypeMapApiShapeSettler Settler { get; }

        public Dictionary<string, AnnotatedTypeMapPlan> Plans { get; } = new(StringComparer.Ordinal);

        private static readonly Func<MetadataTypeDefinitionName, SubjectRelationFocusAuthority> Before =
            AnnotatedTypeMapTestData.TypeFocusAt("1.0.0");

        private static readonly Func<MetadataTypeDefinitionName, SubjectRelationFocusAuthority> After =
            AnnotatedTypeMapTestData.TypeFocusAt("2.0.0");
    }

    private static FindingDiffAnyResult Compare(FindingDiffAnyApiSettler? settler)
    {
        using PEReader before = Open(after: false);
        using PEReader after = Open(after: true);
        return FindingDiffAny.Compare(before, after, bodyReader: null, settler, FindingDiffAnyAxes.Api);
    }

    private static FindingDiffAnyTypeMembers Members(string typeName)
    {
        using PEReader before = Open(after: false);
        using PEReader after = Open(after: true);
        return new FindingDiffAnyTypePairing(before, after).Members(Fixture + typeName);
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
            return type.GetDeclaringType().IsNil && reader.GetString(type.Name) == name;
        });
    }

    private static MetadataTypeDefinitionName Name(PEReader image, TypeDefinitionHandle type) =>
        Assert.IsType<MetadataTypeDefinitionNameReadResult.Read>(
            MetadataTypeDefinitionName.Read(image.GetMetadataReader(), type)).Name;
}
