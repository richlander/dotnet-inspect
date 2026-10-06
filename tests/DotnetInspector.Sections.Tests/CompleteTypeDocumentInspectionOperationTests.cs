using System.Text.Json;

using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class CompleteTypeDocumentInspectionOperationTests
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    [Fact]
    public async Task CompleteDocumentMatchesDrainedOverview()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        InspectionEnvelope<CompleteTypeDocumentInspectionOutcome>
            inspection = Execute(
                library,
                includeHidden: true);
        TypeDocument document = Available(inspection);
        TypeOverviewDocument overview =
            AvailableOverview(
                TypeOverviewDocumentInspectionOperation.Execute(
                    new(
                        library.Reference,
                        new(
                            Name(
                                "System.Text.Json",
                                "JsonSerializer"),
                            new(maximumRows: int.MaxValue),
                            s_bounds,
                            includeHidden: true)),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        TypeMemberGroupRowsOutcome.Read overviewRows =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                overview.Members.Rows);

        Assert.Equal(104, document.Members.Length);
        Assert.Null(overviewRows.Continuation);
        Assert.Equal(
            overview.Subject.Assembly,
            document.Subject.Assembly);
        Assert.Equal(
            overview.Subject.ModuleVersionId,
            document.Subject.ModuleVersionId);
        Assert.Equal(
            overview.Subject.Type,
            document.Subject.Type);
        Assert.Equal(
            overview.Subject.TypeDefinitionToken,
            document.Subject.TypeDefinitionToken);
        Assert.Equal(
            overview.Subject.Category,
            document.Subject.Category);
        Assert.Equal(
            overview.Members.Binding,
            document.MemberGroups);
        Assert.Equal(
            overviewRows.Items
                .Select(row => (
                    Name: row.Binding.Name.ToString(),
                    row.Binding.Category,
                    Count: Assert.IsType<int>(
                        row.ExactMemberCount)))
                .OrderBy(item => item.Name)
                .ThenBy(item => item.Category),
            document.Members
                .GroupBy(member => (
                    Name: member.Subject.Group.Name.ToString(),
                    member.Subject.Group.Category))
                .Select(group => (
                    group.Key.Name,
                    group.Key.Category,
                    Count: group.Count()))
                .OrderBy(item => item.Name)
                .ThenBy(item => item.Category));
        Assert.All(
            document.Members,
            member =>
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(
                        member.DisplaySignature.ToString()));
                Assert.Equal(
                    member.Subject.Anchor.CanonicalSignature,
                    member.CanonicalSignature.ToString());
                Assert.Equal(
                    member.Subject.Anchor.Fingerprint,
                    member.Subject.Fingerprint.ToString());
                Assert.Equal(
                    document.MemberGroups.Type,
                    member.Subject.Group.DeclaringType);
                Assert.Equal(
                    document.MemberGroups.Spelling,
                    member.Subject.Group.Spelling);
            });
        Assert.Equal(
            "type-member-group-population-inspection/share",
            Assert.IsType<InspectionShare.NonProjectable>(
                    inspection.Share)
                .Path);
        Assert.Empty(inspection.Diagnostics);

        var outcome =
            new CompleteTypeDocumentInspectionOutcome.Available(
                document);
        string json = JsonSerializer.Serialize(
            (CompleteTypeDocumentInspectionOutcome)outcome,
            TypeMemberInspectionDocumentJsonContext.Default
                .CompleteTypeDocumentInspectionOutcome);
        CompleteTypeDocumentInspectionOutcome copy =
            JsonSerializer.Deserialize(
                json,
                TypeMemberInspectionDocumentJsonContext.Default
                    .CompleteTypeDocumentInspectionOutcome)!;
        TypeDocument roundTripped =
            Assert.IsType<
                CompleteTypeDocumentInspectionOutcome.Available>(
                    copy)
                .Document;

        Assert.Equal(104, roundTripped.Members.Length);
        Assert.Contains("\"displaySignature\"", json);
        Assert.Contains("\"canonicalSignature\"", json);
        Assert.DoesNotContain("\"documentation\":", json);
        Assert.DoesNotContain("\"source\":", json);

        await library.RetireAsync();
    }

    [Fact]
    public async Task ProjectsMethodsPropertiesFieldsAndEvents()
    {
        byte[] content = await File.ReadAllBytesAsync(
            typeof(CompleteTypeDocumentFixture).Assembly.Location,
            TestContext.Current.CancellationToken);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeDocument document =
            Available(
                Execute(
                    library,
                    type: Name(
                        "DotnetInspector.Sections.Tests",
                        nameof(CompleteTypeDocumentFixture))));

        Assert.Contains(
            document.Members,
            member =>
                member.Subject.Group.Category
                    == MemberGroupCategory.Method
                && member.Subject.Group.Name.ToString()
                    == nameof(CompleteTypeDocumentFixture.Method)
                && member.Subject.DocumentationId.ToString()
                    .StartsWith(
                        "M:",
                        StringComparison.Ordinal));
        Assert.Contains(
            document.Members,
            member =>
                member.Subject.Group.Category
                    == MemberGroupCategory.Property
                && member.Subject.Group.Name.ToString()
                    == nameof(CompleteTypeDocumentFixture.Property)
                && member.Subject.DocumentationId.ToString()
                    .StartsWith(
                        "P:",
                        StringComparison.Ordinal));
        Assert.Contains(
            document.Members,
            member =>
                member.Subject.Group.Category
                    == MemberGroupCategory.Field
                && member.Subject.Group.Name.ToString()
                    == nameof(CompleteTypeDocumentFixture.Field)
                && member.Subject.DocumentationId.ToString()
                    .StartsWith(
                        "F:",
                        StringComparison.Ordinal));
        Assert.Contains(
            document.Members,
            member =>
                member.Subject.Group.Category
                    == MemberGroupCategory.Event
                && member.Subject.Group.Name.ToString()
                    == nameof(CompleteTypeDocumentFixture.Changed)
                && member.Subject.DocumentationId.ToString()
                    .StartsWith(
                        "E:",
                        StringComparison.Ordinal));
        Assert.All(
            document.Members,
            member =>
                Assert.True(member.Subject.MetadataToken > 0));

        await library.RetireAsync();
    }

    [Fact]
    public async Task ReceiverFilterAppliesBeforeDeclarations()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeDocument declared =
            Available(
                Execute(
                    library,
                    receiver: TypeMemberGroupReceiverFilter.Static));
        TypeDocument extension =
            Available(
                Execute(
                    library,
                    receiver:
                        TypeMemberGroupReceiverFilter.Extension));

        Assert.Equal(
            25,
            declared.Members.Count(member =>
                member.Subject.Group.Name.ToString()
                    == "Deserialize"));
        Assert.All(
            declared.Members,
            member =>
                Assert.Equal(MemberReceiver.Static, member.Receiver));
        Assert.Equal(
            15,
            extension.Members.Count(member =>
                member.Subject.Group.Name.ToString()
                    == "Deserialize"));
        Assert.All(
            extension.Members,
            member =>
                Assert.Equal(MemberReceiver.Extension, member.Receiver));

        await library.RetireAsync();
    }

    [Fact]
    public async Task SpellingAndAccessibilityApplyBeforeProjection()
    {
        byte[] content = await File.ReadAllBytesAsync(
            typeof(CompleteTypeDocumentFixture).Assembly.Location,
            TestContext.Current.CancellationToken);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        MetadataTypeDefinitionName type =
            Name(
                "DotnetInspector.Sections.Tests",
                nameof(CompleteTypeDocumentFixture));

        TypeDocument csharp =
            Available(Execute(library, type));
        TypeDocument metadata =
            Available(
                Execute(
                    library,
                    type,
                    spelling: TypeMemberGroupSpelling.Metadata));
        TypeOverviewDocument metadataOverview =
            AvailableOverview(
                TypeOverviewDocumentInspectionOperation.Execute(
                    new(
                        library.Reference,
                        new(
                            type,
                            new(maximumRows: int.MaxValue),
                            s_bounds,
                            TypeMemberGroupSpelling.Metadata)),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        TypeDocument all =
            Available(
                Execute(
                    library,
                    type,
                    accessibility:
                        TypeMemberGroupAccessibilityFilter.All));

        Assert.DoesNotContain(
            csharp.Members,
            member => member.Subject.Group.Name.ToString()
                is "get_Property" or "add_Changed");
        Assert.Contains(
            metadata.Members,
            member => member.Subject.Group.Name.ToString()
                == "get_Property");
        Assert.Contains(
            metadata.Members,
            member => member.Subject.Group.Name.ToString()
                == "add_Changed");
        Assert.Equal(
            metadata.Members.Length,
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                    metadataOverview.Members.Rows)
                .Items
                .Sum(row =>
                    Assert.IsType<int>(row.ExactMemberCount)));
        Assert.DoesNotContain(
            csharp.Members,
            member => member.Subject.Group.Name.ToString()
                == "PrivateMethod");
        MemberDeclaration privateMethod =
            Assert.Single(
                all.Members,
                member => member.Subject.Group.Name.ToString()
                    == "PrivateMethod");
        Assert.Equal(
            "private",
            privateMethod.Accessibility.ToString());

        await library.RetireAsync();
    }

    [Fact]
    public async Task BoundsAndMissingTypeAreTyped()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var memberBound = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            maxMembers: 1,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            s_bounds.MaxMetadataRows,
            s_bounds.MaxRetainedTextCharacters);
        var textBound = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            s_bounds.MaxMembers,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            s_bounds.MaxMetadataRows,
            maxRetainedTextCharacters: 0);

        CompleteTypeDocumentInspectionOutcome.Incomplete members =
            Assert.IsType<
                CompleteTypeDocumentInspectionOutcome.Incomplete>(
                    Execute(library, bounds: memberBound).Content);
        CompleteTypeDocumentInspectionOutcome.Incomplete text =
            Assert.IsType<
                CompleteTypeDocumentInspectionOutcome.Incomplete>(
                    Execute(library, bounds: textBound).Content);
        CompleteTypeDocumentInspectionOutcome.Rejected missing =
            Assert.IsType<
                CompleteTypeDocumentInspectionOutcome.Rejected>(
                    Execute(
                        library,
                        type: Name(
                            "System.Text.Json",
                            "Missing"))
                    .Content);

        Assert.Equal(
            CompleteTypeDocumentInspectionBound.Members,
            members.Bound);
        Assert.Equal(1, members.Limit);
        Assert.Equal(2, members.Measured);
        Assert.Equal(
            CompleteTypeDocumentInspectionBound
                .RetainedTextCharacters,
            text.Bound);
        Assert.Equal(0, text.Limit);
        Assert.True(text.Measured > 0);
        Assert.Equal(
            CompleteTypeDocumentInspectionRejection.TypeNotFound,
            missing.Reason);

        await library.RetireAsync();
    }

    private static InspectionEnvelope<
            CompleteTypeDocumentInspectionOutcome>
        Execute(
            LibraryInspectionTestLibrary library,
            MetadataTypeDefinitionName? type = null,
            TypeMemberGroupSpelling spelling =
                TypeMemberGroupSpelling.CSharp,
            TypeMemberGroupAccessibilityFilter accessibility =
                TypeMemberGroupAccessibilityFilter.Public,
            TypeMemberGroupReceiverFilter receiver =
                TypeMemberGroupReceiverFilter.All,
            bool includeHidden = false,
            ApiSurfaceExtractionBounds? bounds = null) =>
        CompleteTypeDocumentInspectionOperation.Execute(
            new(
                library.Reference,
                new(
                    type
                        ?? Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                    bounds ?? s_bounds,
                    spelling,
                    accessibility,
                    receiver,
                    includeHidden)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static TypeDocument Available(
        InspectionEnvelope<CompleteTypeDocumentInspectionOutcome>
            envelope)
    {
        if (envelope.Content
            is CompleteTypeDocumentInspectionOutcome.Failed failed)
        {
            Assert.Fail($"Complete Type document failed: {failed.Reason}.");
        }

        return Assert.IsType<
                CompleteTypeDocumentInspectionOutcome.Available>(
                envelope.Content)
            .Document;
    }

    private static TypeOverviewDocument AvailableOverview(
        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
            envelope) =>
        Assert.IsType<
                TypeOverviewDocumentInspectionOutcome.Available>(
                envelope.Content)
            .Document;

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;
}

public sealed class CompleteTypeDocumentFixture
{
    public int Field;

    public int Property { get; set; }

    public event EventHandler? Changed;

    public void Method(int value)
    {
        Field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void PrivateMethod()
    {
    }
}
