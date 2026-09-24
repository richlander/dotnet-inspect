using System.Reflection;
using System.Text;
using System.Text.Json;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections.Tests;

public sealed class LibraryInspectionOperationTests
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
    public async Task
        RealSystemTextJson_ReturnsDetachedPublicTypeCountAndSettlesLease()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library);

        LibraryDocument document = Document(envelope);
        Assert.Equal("System.Text.Json", document.Assembly.Name.ToString());
        Assert.Equal(new Version(10, 0, 0, 0), document.Assembly.Version);
        Assert.NotEqual(Guid.Empty, document.ModuleVersionId);
        Assert.Equal(content.Length, document.Work.AssemblyBytes);
        Assert.True(document.Work.MetadataRows > 0);
        Assert.True(document.Work.RetainedDeclarations > 0);
        Assert.True(document.Work.RetainedTextCharacters > 0);
        Assert.Equal(
            document.ModuleVersionId,
            document.Types.Binding.ModuleVersionId);
        Assert.Equal(
            LibraryTypeAccessibility.Public,
            document.Types.Binding.Accessibility);
        Assert.Equal(
            LibraryTypeDeclarationSelection.DefinitionsAndForwarders,
            document.Types.Binding.DeclarationSelection);
        Assert.Equal(
            ApiTypeInventoryKinds.All,
            document.Types.Binding.DefinitionKinds);
        LibraryTypePopulationCountOutcome.Counted count =
            Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                    document.Types.Count);
        Assert.True(count.Total > 0);
        Assert.True(count.Forwarders > 0);
        Assert.Equal(
            count.Total,
            count.Definitions + count.Forwarders);
        Assert.Equal(
            count.Definitions,
            count.Classes
                + count.Structs
                + count.Interfaces
                + count.Enums
                + count.Delegates);
        Assert.Empty(envelope.Diagnostics);
        var share = Assert.IsType<InspectionShare.NonProjectable>(
            envelope.Share);
        Assert.Equal("library-inspection/share", share.Path);
        Assert.Equal(
            "A complete portable Workspace scenario was not supplied.",
            share.Reason.ToString());
        await library.RetireAsync();
        AssertDetachedContract();
    }

    [Fact]
    public async Task
        GenericDefinitionWithoutArityMarkerRetainsTypeParameterDisplay()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        byte[] original = Encoding.UTF8.GetBytes("JsonConverter`1");
        byte[] replacement = Encoding.UTF8.GetBytes("JsonConverterXX");
        Assert.Equal(original.Length, replacement.Length);
        int offset = content.AsSpan().IndexOf(original);
        Assert.True(offset >= 0);
        Assert.Equal(
            -1,
            content.AsSpan(offset + original.Length).IndexOf(original));
        replacement.CopyTo(content, offset);

        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        LibraryDocument document = Document(
            Execute(
                library,
                count: false,
                new(
                    maximumRows: 5_000,
                    memberCount: new())));
        LibraryTypePopulationRowsOutcome.Read rows =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                document.Types.Rows);

        Assert.Contains(
            rows.Items,
            row =>
                row.DisplayName.ToString()
                    == "System.Text.Json.Serialization.JsonConverterXX<T>");

        await library.RetireAsync();
        AssertDetachedContract();
    }

    [Fact]
    public async Task
        RealSystemTextJson_BoundedRowsDrainToCountWithRequestedMemberCounts()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var rowsRequest = new LibraryTypePopulationRowsRequest(
            maximumRows: 10,
            memberCount: new());

        LibraryDocument first = Document(
            Execute(
                library,
                count: true,
                rowsRequest));
        LibraryTypePopulationCountOutcome.Counted count =
            Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                first.Types.Count);
        LibraryTypePopulationRowsOutcome.Read segment =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                first.Types.Rows);
        var rows = segment.Items.ToList();
        LibraryTypePopulationContinuation? continuation =
            segment.Continuation;
        while (continuation is not null)
        {
            LibraryDocument next = Document(
                Execute(
                    library,
                    count: false,
                    new(
                        maximumRows: 7,
                        memberCount: new(),
                        continuation: continuation)));
            Assert.Null(next.Types.Count);
            Assert.Equal(first.Types.Binding, next.Types.Binding);
            segment =
                Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                    next.Types.Rows);
            rows.AddRange(segment.Items);
            continuation = segment.Continuation;
        }

        Assert.Equal(count.Total, rows.Count);
        Assert.Equal(
            rows.Count,
            rows.Select(static row => row.Identity).Distinct().Count());
        Assert.All(rows, static row => Assert.True(row.IsPublicSurface));
        Assert.Equal(
            "System.Text.Json.Serialization.JsonConverter<T>",
            rows.Single(
                    row =>
                        row.Identity
                            == Name(
                                "System.Text.Json.Serialization",
                                "JsonConverter`1"))
                .DisplayName
                .ToString());
        Assert.Equal(
            16,
            Assert.IsType<LibraryTypeMemberCountOutcome.Counted>(
                    rows.Single(
                        row =>
                            row.Identity
                                == Name(
                                    "System.Text.Json",
                                    "JsonDocument"))
                        .MemberCount)
                .Value);
        Assert.Equal(
            9,
            Assert.IsType<LibraryTypeMemberCountOutcome.Counted>(
                    rows.Single(
                        row =>
                            row.Identity
                                == Name(
                                    "System.Text.Json",
                                    "JsonException"))
                        .MemberCount)
                .Value);
        Assert.All(
            rows.Where(
                static row =>
                    row.DeclarationKind
                        == LibraryTypeDeclarationKind.Definition),
            static row =>
                Assert.IsType<
                    LibraryTypeMemberCountOutcome.Counted>(
                    row.MemberCount));
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealSystemTextJson_DefinitionKindFacetBindsCountAndRows()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        const ApiTypeInventoryKinds selection =
            ApiTypeInventoryKinds.Classes
            | ApiTypeInventoryKinds.Structs;

        LibraryDocument document = Document(
            Execute(
                library,
                count: true,
                new(maximumRows: 5_000),
                declarationSelection:
                    LibraryTypeDeclarationSelection.Definitions,
                definitionKinds: selection));

        Assert.Equal(
            selection,
            document.Types.Binding.DefinitionKinds);
        LibraryTypePopulationCountOutcome.Counted count =
            Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                document.Types.Count);
        LibraryTypePopulationRowsOutcome.Read rows =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                document.Types.Rows);
        Assert.True(rows.IsComplete);
        Assert.True(count.Classes > 0);
        Assert.True(count.Structs > 0);
        Assert.Equal(0, count.Forwarders);
        Assert.Equal(0, count.Interfaces);
        Assert.Equal(0, count.Enums);
        Assert.Equal(0, count.Delegates);
        Assert.Equal(count.Total, rows.Items.Length);
        Assert.All(
            rows.Items,
            static row =>
            {
                Assert.Equal(
                    LibraryTypeDeclarationKind.Definition,
                    row.DeclarationKind);
                Assert.True(
                    row.DefinitionKind
                        is ApiTypeInventoryKind.Class
                            or ApiTypeInventoryKind.Struct);
            });
        LibraryDocument allDefinitions = Document(
            Execute(
                library,
                count: false,
                new(maximumRows: 5_000),
                declarationSelection:
                    LibraryTypeDeclarationSelection.Definitions));
        Assert.True(
            document.Work.RetainedTextCharacters
                < allDefinitions.Work.RetainedTextCharacters);
        Assert.True(
            rows.Items.Length
                < Assert.IsType<
                        LibraryTypePopulationRowsOutcome.Read>(
                        allDefinitions.Types.Rows)
                    .Items
                    .Length);
        LibraryDocument classesAndForwarders = Document(
            Execute(
                library,
                count: true,
                new(maximumRows: 5_000),
                definitionKinds:
                    ApiTypeInventoryKinds.Classes));
        LibraryTypePopulationCountOutcome.Counted mixedCount =
            Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                classesAndForwarders.Types.Count);
        LibraryTypePopulationRowsOutcome.Read mixedRows =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                classesAndForwarders.Types.Rows);
        Assert.True(mixedCount.Classes > 0);
        Assert.True(mixedCount.Forwarders > 0);
        Assert.Equal(0, mixedCount.Structs);
        Assert.Equal(0, mixedCount.Interfaces);
        Assert.Equal(0, mixedCount.Enums);
        Assert.Equal(0, mixedCount.Delegates);
        Assert.Equal(mixedCount.Total, mixedRows.Items.Length);
        Assert.All(
            mixedRows.Items,
            static row =>
                Assert.True(
                    row.DeclarationKind
                        == LibraryTypeDeclarationKind.Forwarder
                    || row.DefinitionKind
                        == ApiTypeInventoryKind.Class));

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealFacade_RowsRetainForwarderEvidenceWithoutTargetResolution()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealNetstandardAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(
                library,
                count: false,
                new(
                    maximumRows: 5_000,
                    memberCount: new()));
        LibraryDocument document = Document(envelope);

        Assert.Null(document.Types.Count);
        LibraryTypePopulationRowsOutcome.Read rows =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                document.Types.Rows);
        Assert.True(rows.IsComplete);
        LibraryTypeShape systemObject =
            rows.Items.Single(
                row => row.Identity == Name("System", "Object"));
        Assert.Equal(
            LibraryTypeDeclarationKind.Forwarder,
            systemObject.DeclarationKind);
        Assert.Null(systemObject.DefinitionKind);
        Assert.Null(systemObject.DefinitionAccessibility);
        LibraryTypeForwardingEvidence forwarding =
            Assert.IsType<LibraryTypeForwardingEvidence>(
                systemObject.Forwarding);
        Assert.Equal(
            document.ModuleVersionId,
            forwarding.SourceModuleVersionId);
        Assert.NotEmpty(forwarding.Declarations);
        Assert.False(forwarding.TargetAssembly.Name.IsEmpty);
        Assert.Equal(
            LibraryTypeMemberCountNotApplicableReason.Forwarder,
            Assert.IsType<
                    LibraryTypeMemberCountOutcome.NotApplicable>(
                    systemObject.MemberCount)
                .Reason);

        string json = JsonSerializer.Serialize(
            envelope,
            LibraryInspectionJsonContext.Default
                .LibraryInspectionEnvelope);
        InspectionEnvelope<LibraryInspectionOutcome> roundTrippedEnvelope =
            JsonSerializer.Deserialize(
                json,
                LibraryInspectionJsonContext.Default
                    .LibraryInspectionEnvelope)
            ?? throw new InvalidOperationException(
                "The Library inspection envelope did not deserialize.");
        LibraryTypeForwardingEvidence roundTrippedForwarding =
            Assert.IsType<LibraryTypeForwardingEvidence>(
                Assert.Single(
                        Assert.IsType<
                                LibraryTypePopulationRowsOutcome.Read>(
                                Document(roundTrippedEnvelope).Types.Rows)
                            .Items,
                        row =>
                            row.Identity
                                == Name("System", "Object"))
                    .Forwarding);
        Assert.Equal(
            forwarding.Declarations.Select(static token => token.Value),
            roundTrippedForwarding.Declarations.Select(
                static token => token.Value));
        Assert.Equal(
            forwarding.SourceModuleVersionId,
            roundTrippedForwarding.SourceModuleVersionId);
        Assert.Equal(
            forwarding.TargetAssembly,
            roundTrippedForwarding.TargetAssembly);
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        DeclarationSelectionFiltersCountAndRowsWithoutLosingFirstClassKinds()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        foreach (
            LibraryTypeDeclarationSelection selection
            in new[]
            {
                LibraryTypeDeclarationSelection.Definitions,
                LibraryTypeDeclarationSelection.Forwarders,
            })
        {
            LibraryDocument document = Document(
                Execute(
                    library,
                    count: true,
                    new(
                        maximumRows: 5_000,
                        memberCount: new()),
                    declarationSelection: selection));
            LibraryTypePopulationCountOutcome.Counted count =
                Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                    document.Types.Count);
            LibraryTypePopulationRowsOutcome.Read rows =
                Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                    document.Types.Rows);

            Assert.True(rows.IsComplete);
            Assert.Equal(selection, document.Types.Binding.DeclarationSelection);
            Assert.Equal(
                selection == LibraryTypeDeclarationSelection.Definitions
                    ? ApiTypeInventoryKinds.All
                    : ApiTypeInventoryKinds.None,
                document.Types.Binding.DefinitionKinds);
            Assert.Equal(count.Total, rows.Items.Length);
            Assert.NotEmpty(rows.Items);
            if (selection == LibraryTypeDeclarationSelection.Definitions)
            {
                Assert.Equal(0, count.Forwarders);
                Assert.All(
                    rows.Items,
                    static row =>
                        Assert.Equal(
                            LibraryTypeDeclarationKind.Definition,
                            row.DeclarationKind));
            }
            else
            {
                Assert.Equal(0, count.Definitions);
                Assert.Equal(count.Total, count.Forwarders);
                Assert.All(
                    rows.Items,
                    static row =>
                        Assert.Equal(
                            LibraryTypeDeclarationKind.Forwarder,
                            row.DeclarationKind));
            }
        }

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealSystemXml_DeclarationSelectionPreservesFacadeForwarders()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemXmlAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        LibraryDocument combined = Document(
            Execute(
                library,
                count: true,
                new(maximumRows: 5_000)));
        LibraryTypePopulationCountOutcome.Counted combinedCount =
            Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                combined.Types.Count);
        LibraryTypePopulationRowsOutcome.Read combinedRows =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                combined.Types.Rows);

        Assert.True(combinedRows.IsComplete);
        Assert.Equal(combinedCount.Total, combinedRows.Items.Length);
        Assert.Equal(
            combinedCount.Total,
            combinedCount.Definitions + combinedCount.Forwarders);
        LibraryTypeShape xmlReader =
            Assert.Single(
                combinedRows.Items,
                row =>
                    row.Identity
                        == Name("System.Xml", "XmlReader"));
        Assert.Equal(
            LibraryTypeDeclarationKind.Forwarder,
            xmlReader.DeclarationKind);
        Assert.Equal(
            "System.Xml.ReaderWriter",
            Assert.IsType<LibraryTypeForwardingEvidence>(
                    xmlReader.Forwarding)
                .TargetAssembly.Name.ToString());

        LibraryDocument forwarders = Document(
            Execute(
                library,
                count: true,
                new(maximumRows: 5_000),
                declarationSelection:
                    LibraryTypeDeclarationSelection.Forwarders));
        LibraryTypePopulationCountOutcome.Counted forwarderCount =
            Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                forwarders.Types.Count);
        LibraryTypePopulationRowsOutcome.Read forwarderRows =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                forwarders.Types.Rows);

        Assert.True(forwarderRows.IsComplete);
        Assert.Equal(0, forwarderCount.Definitions);
        Assert.Equal(combinedCount.Forwarders, forwarderCount.Total);
        Assert.Equal(forwarderCount.Total, forwarderRows.Items.Length);
        Assert.Contains(
            forwarderRows.Items,
            row => row.Identity == Name("System.Xml", "XmlReader"));
        Assert.All(
            forwarderRows.Items,
            static row =>
                Assert.Equal(
                    LibraryTypeDeclarationKind.Forwarder,
                    row.DeclarationKind));

        LibraryDocument definitions = Document(
            Execute(
                library,
                count: true,
                new(maximumRows: 5_000),
                declarationSelection:
                    LibraryTypeDeclarationSelection.Definitions));
        LibraryTypePopulationCountOutcome.Counted definitionCount =
            Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                definitions.Types.Count);
        LibraryTypePopulationRowsOutcome.Read definitionRows =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                definitions.Types.Rows);

        Assert.True(definitionRows.IsComplete);
        Assert.Equal(0, definitionCount.Forwarders);
        Assert.Equal(combinedCount.Definitions, definitionCount.Total);
        Assert.Equal(definitionCount.Total, definitionRows.Items.Length);
        Assert.DoesNotContain(
            definitionRows.Items,
            row => row.Identity == Name("System.Xml", "XmlReader"));
        Assert.All(
            definitionRows.Items,
            static row =>
                Assert.Equal(
                    LibraryTypeDeclarationKind.Definition,
                    row.DeclarationKind));

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        ContinuationsRejectMalformedIncompatibleStaleAndOutOfRangeUse()
    {
        byte[] jsonContent =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        byte[] facadeContent =
            await LibraryInspectionTestLibrary.RealNetstandardAsync();
        await using LibraryInspectionTestLibrary json =
            await LibraryInspectionTestLibrary.CreateAsync(
                jsonContent,
                LibraryInspectionTestLibrary.Identity(jsonContent));
        await using LibraryInspectionTestLibrary facade =
            await LibraryInspectionTestLibrary.CreateAsync(
                facadeContent,
                LibraryInspectionTestLibrary.Identity(facadeContent));

        LibraryTypePopulationContinuation continuation =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                    Document(
                        Execute(
                            json,
                            count: false,
                            new(maximumRows: 1)))
                        .Types.Rows)
                .Continuation!;
        AssertRowsRejection(
            Execute(
                json,
                count: false,
                new(
                    maximumRows: 1,
                    continuation:
                        new(
                            new InertString(
                                TextPolicy.Field,
                                "not-base64")))),
            LibraryTypePopulationRowsRejection.InvalidContinuation);
        AssertRowsRejection(
            Execute(
                json,
                count: false,
                new(
                    maximumRows: 1,
                    memberCount: new(),
                    continuation: continuation)),
            LibraryTypePopulationRowsRejection
                .IncompatibleContinuation);
        AssertRowsRejection(
            Execute(
                json,
                count: false,
                new(
                    maximumRows: 1,
                    continuation: continuation),
                declarationSelection:
                    LibraryTypeDeclarationSelection.Definitions),
            LibraryTypePopulationRowsRejection
                .IncompatibleContinuation);
        AssertRowsRejection(
            Execute(
                json,
                count: false,
                new(
                    maximumRows: 1,
                    continuation: continuation),
                definitionKinds:
                    ApiTypeInventoryKinds.Classes),
            LibraryTypePopulationRowsRejection
                .IncompatibleContinuation);
        AssertRowsRejection(
            Execute(
                facade,
                count: false,
                new(
                    maximumRows: 1,
                    continuation: continuation)),
            LibraryTypePopulationRowsRejection.StaleContinuation);

        byte[] payload =
            Convert.FromBase64String(
                continuation.Value.ToString());
        payload[^4] = 0xFF;
        payload[^3] = 0xFF;
        payload[^2] = 0xFF;
        payload[^1] = 0x7F;
        AssertRowsRejection(
            Execute(
                json,
                count: false,
                new(
                    maximumRows: 1,
                    continuation:
                        new(
                            new InertString(
                                TextPolicy.Field,
                                Convert.ToBase64String(payload))))),
            LibraryTypePopulationRowsRejection
                .ContinuationOutOfRange);

        await json.RetireAsync();
        await facade.RetireAsync();
    }

    [Fact]
    public async Task
        RowTextBoundLeavesCountAvailableAndRowsIncomplete()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        LibraryDocument countOnly = Document(Execute(library));
        var bounds = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            s_bounds.MaxMembers,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            s_bounds.MaxMetadataRows,
            checked((int)countOnly.Work.RetainedTextCharacters));

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(
                library,
                count: true,
                new(maximumRows: 1),
                bounds);

        LibraryDocument document = Document(envelope);
        Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
            document.Types.Count);
        LibraryTypePopulationRowsOutcome.Incomplete incomplete =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Incomplete>(
                document.Types.Rows);
        Assert.Equal(
            LibraryTypePopulationRowsBound.RetainedTextCharacters,
            incomplete.Bound);
        Assert.Equal(
            bounds.MaxRetainedTextCharacters,
            incomplete.Limit);
        Assert.True(incomplete.Measured > incomplete.Limit);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "library-inspection.types.rows.incomplete.retained-text-characters");
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        DefinitionBoundRetainsDocumentAndIncompleteCount()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var bounds = new ApiSurfaceExtractionBounds(
            maxTypes: 1,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library, bounds);

        Assert.Equal(
            LibraryTypePopulationCountBound.Definitions,
            Assert.IsType<
                    LibraryTypePopulationCountOutcome.Incomplete>(
                    Document(envelope).Types.Count)
                .Bound);
        Assert.Equal(
            1,
            Assert.IsType<
                    LibraryTypePopulationCountOutcome.Incomplete>(
                    Document(envelope).Types.Count)
                .Limit);
        Assert.True(
            Assert.IsType<
                    LibraryTypePopulationCountOutcome.Incomplete>(
                    Document(envelope).Types.Count)
                .Measured > 1);
        Assert.Equal(
            "library-inspection.types.count.incomplete.definitions",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealFacade_CountsDefinitionsAndForwardersWithoutTargetResolution()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealNetstandardAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library);

        LibraryTypePopulationCountOutcome.Counted count =
            Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                Document(envelope).Types.Count);
        Assert.True(count.Forwarders > 0);
        Assert.Equal(
            count.Total,
            count.Definitions + count.Forwarders);
        Assert.Equal(
            count.Definitions,
            count.Classes
                + count.Structs
                + count.Interfaces
                + count.Enums
                + count.Delegates);
        Assert.Empty(envelope.Diagnostics);
        await library.RetireAsync();
    }

    [Fact]
    public async Task ForwarderBoundRetainsDocumentAndIncompleteCount()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealNetstandardAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var bounds = new ApiSurfaceExtractionBounds(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 1,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library, bounds);

        LibraryTypePopulationCountOutcome.Incomplete incomplete =
            Assert.IsType<LibraryTypePopulationCountOutcome.Incomplete>(
                Document(envelope).Types.Count);
        Assert.Equal(
            LibraryTypePopulationCountBound.Forwarders,
            incomplete.Bound);
        Assert.Equal(1, incomplete.Limit);
        Assert.True(incomplete.Measured > incomplete.Limit);
        Assert.Equal(
            "library-inspection.types.count.incomplete.forwarders",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task MetadataRowBoundRetainsDocumentAndIncompleteCount()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var bounds = new ApiSurfaceExtractionBounds(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1,
            maxRetainedTextCharacters: 20_000_000);

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library, bounds);

        LibraryDocument document = Document(envelope);
        LibraryTypePopulationCountOutcome.Incomplete incomplete =
            Assert.IsType<LibraryTypePopulationCountOutcome.Incomplete>(
                document.Types.Count);
        Assert.Equal(
            LibraryTypePopulationCountBound.MetadataRows,
            incomplete.Bound);
        Assert.Equal(1, incomplete.Limit);
        Assert.True(incomplete.Measured > incomplete.Limit);
        Assert.Equal(incomplete.Measured, document.Work.MetadataRows);
        Assert.Equal(0, document.Work.RetainedDeclarations);
        Assert.Equal(
            "library-inspection.types.count.incomplete.metadata-rows",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RetainedDeclarationBoundRetainsDocumentAndIncompleteCount()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var bounds = new ApiSurfaceExtractionBounds(
            maxTypes: 0,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 0,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library, bounds);

        LibraryDocument document = Document(envelope);
        LibraryTypePopulationCountOutcome.Incomplete incomplete =
            Assert.IsType<LibraryTypePopulationCountOutcome.Incomplete>(
                document.Types.Count);
        Assert.Equal(
            LibraryTypePopulationCountBound.RetainedDeclarations,
            incomplete.Bound);
        Assert.Equal(0, incomplete.Limit);
        Assert.True(incomplete.Measured > incomplete.Limit);
        Assert.Equal(
            incomplete.Measured,
            document.Work.RetainedDeclarations);
        Assert.Equal(
            "library-inspection.types.count.incomplete.retained-declarations",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RetainedTextBoundRetainsDocumentAndIncompleteCount()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var bounds = new ApiSurfaceExtractionBounds(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 0);

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library, bounds);

        LibraryDocument document = Document(envelope);
        LibraryTypePopulationCountOutcome.Incomplete incomplete =
            Assert.IsType<LibraryTypePopulationCountOutcome.Incomplete>(
                document.Types.Count);
        Assert.Equal(
            LibraryTypePopulationCountBound.RetainedTextCharacters,
            incomplete.Bound);
        Assert.Equal(0, incomplete.Limit);
        Assert.True(incomplete.Measured > incomplete.Limit);
        Assert.Equal(
            incomplete.Measured,
            document.Work.RetainedTextCharacters);
        Assert.Equal(
            "library-inspection.types.count.incomplete.retained-text-characters",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        EquivalentLibrariesProduceEqualDocumentsAndRejectForeignLease()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        ManagedMetadataIdentity.Assembly identity =
            LibraryInspectionTestLibrary.Identity(content);
        var equivalentIdentity = new ManagedMetadataIdentity.Assembly(
            identity.Identity with
            {
                Name = identity.Identity.Name.ToUpperInvariant(),
                Culture = "neutral",
            });
        await using LibraryInspectionTestLibrary first =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                identity);
        await using LibraryInspectionTestLibrary second =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                equivalentIdentity);

        Assert.Equal(
            Document(Execute(first)),
            Document(Execute(second)));

        InspectionEnvelope<LibraryInspectionOutcome> rejected =
            LibraryInspectionOperation.Execute(
                Request(first.Reference),
                second.IssueOperation(),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            LibraryInspectionRejection.LeaseReferenceMismatch,
            Assert.IsType<LibraryInspectionOutcome.Rejected>(
                    rejected.Content)
                .Reason);
        Assert.Equal(
            "library-inspection.rejected.lease-reference-mismatch",
            Assert.Single(rejected.Diagnostics).Code);

        await first.RetireAsync();
        await second.RetireAsync();
    }

    [Fact]
    public async Task
        MalformedTypePopulationReturnsTypedTopLevelFailure()
    {
        byte[] content =
            LibraryInspectionTestLibrary.BuildMetadataImage(
                malformedPublicType: true);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.ProbeIdentity());

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library);

        Assert.Equal(
            LibraryInspectionFailure.MalformedMetadata,
            Assert.IsType<LibraryInspectionOutcome.Failed>(
                    envelope.Content)
                .Reason);
        Assert.Equal(
            "library-inspection.failed.malformed-metadata",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        PublicModuleExportRetainsDocumentAndTypedCountUnavailability()
    {
        byte[] content =
            LibraryInspectionTestLibrary.BuildMetadataImage(
                includeModuleExport: true);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.ProbeIdentity());

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library);

        Assert.Equal(
            LibraryTypePopulationCountUnavailableReason
                .UnsupportedModuleExport,
            Assert.IsType<
                    LibraryTypePopulationCountOutcome.Unavailable>(
                    Document(envelope).Types.Count)
                .Reason);
        Assert.Equal(
            "library-inspection.types.count.unavailable.unsupported-module-export",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        PublicModuleExportMakesRowsUnavailableWithoutReturningAPrefix()
    {
        byte[] content =
            LibraryInspectionTestLibrary.BuildMetadataImage(
                includeModuleExport: true);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.ProbeIdentity());

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(
                library,
                count: false,
                new(maximumRows: 1));

        Assert.Equal(
            LibraryTypePopulationRowsUnavailableReason
                .UnsupportedModuleExport,
            Assert.IsType<
                    LibraryTypePopulationRowsOutcome.Unavailable>(
                    Document(envelope).Types.Rows)
                .Reason);
        Assert.Equal(
            "library-inspection.types.rows.unavailable.unsupported-module-export",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        FacetSelectionExcludesUnsupportedModuleExports()
    {
        byte[] content =
            LibraryInspectionTestLibrary.BuildMetadataImage(
                includeModuleExport: true);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.ProbeIdentity());

        foreach (
            (
                LibraryTypeDeclarationSelection Declarations,
                ApiTypeInventoryKinds DefinitionKinds) selection
            in new[]
            {
                (
                    LibraryTypeDeclarationSelection.Definitions,
                    ApiTypeInventoryKinds.All),
                (
                    LibraryTypeDeclarationSelection.Forwarders,
                    ApiTypeInventoryKinds.None),
                (
                    LibraryTypeDeclarationSelection
                        .DefinitionsAndForwarders,
                    ApiTypeInventoryKinds.Classes),
            })
        {
            InspectionEnvelope<LibraryInspectionOutcome> envelope =
                Execute(
                    library,
                    count: true,
                    new(maximumRows: 1),
                    declarationSelection:
                        selection.Declarations,
                    definitionKinds:
                        selection.DefinitionKinds);
            LibraryDocument document = Document(envelope);
            LibraryTypePopulationCountOutcome.Counted count =
                Assert.IsType<
                    LibraryTypePopulationCountOutcome.Counted>(
                    document.Types.Count);
            LibraryTypePopulationRowsOutcome.Read rows =
                Assert.IsType<
                    LibraryTypePopulationRowsOutcome.Read>(
                    document.Types.Rows);

            Assert.Equal(0, count.Total);
            Assert.Empty(rows.Items);
            Assert.True(rows.IsComplete);
            Assert.Empty(envelope.Diagnostics);
        }

        await library.RetireAsync();
    }

    [Fact]
    public async Task AssemblyIdentityMismatchReturnsTypedRejection()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        var wrongIdentity = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "System.Text.Json",
                new Version(99, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                wrongIdentity);

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library);

        Assert.Equal(
            LibraryInspectionRejection.AssemblyIdentityMismatch,
            Assert.IsType<LibraryInspectionOutcome.Rejected>(
                    envelope.Content)
                .Reason);
        Assert.Equal(
            "library-inspection.rejected.assembly-identity-mismatch",
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    [Theory]
    [MemberData(nameof(FailedImages))]
    public async Task UnsupportedContentReturnsTypedFailure(
        byte[] content,
        LibraryInspectionFailure expected,
        string diagnosticCode)
    {
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.ProbeIdentity());

        InspectionEnvelope<LibraryInspectionOutcome> envelope =
            Execute(library);

        Assert.Equal(
            expected,
            Assert.IsType<LibraryInspectionOutcome.Failed>(
                    envelope.Content)
                .Reason);
        Assert.Equal(
            diagnosticCode,
            Assert.Single(envelope.Diagnostics).Code);
        await library.RetireAsync();
    }

    public static TheoryData<
        byte[],
        LibraryInspectionFailure,
        string> FailedImages()
    {
        byte[] windowsMetadata =
            LibraryInspectionTestLibrary.BuildMetadataImage(
                metadataVersion:
                    "WindowsRuntime 1.4;CLR v4.0.30319");
        return new TheoryData<
            byte[],
            LibraryInspectionFailure,
            string>
        {
            {
                [1, 2, 3],
                LibraryInspectionFailure.MalformedMetadata,
                "library-inspection.failed.malformed-metadata"
            },
            {
                LibraryInspectionTestLibrary.BuildMetadataImage(
                    includeAssembly: false),
                LibraryInspectionFailure.ManagedModule,
                "library-inspection.failed.managed-module"
            },
            {
                windowsMetadata,
                LibraryInspectionFailure.UnsupportedWindowsMetadata,
                "library-inspection.failed.unsupported-windows-metadata"
            },
            {
                LibraryInspectionTestLibrary.BuildMetadataImage(
                    emptyModuleVersionId: true),
                LibraryInspectionFailure.EmptyModuleVersionId,
                "library-inspection.failed.empty-module-version-id"
            },
        };
    }

    [Fact]
    public async Task CancellationAndRequestFailureSettleTransferredLease()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary cancelled =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => LibraryInspectionOperation.Execute(
                Request(cancelled.Reference),
                cancelled.IssueOperation(),
                cancellation.Token));
        await cancelled.RetireAsync();

        await using LibraryInspectionTestLibrary invalid =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        Assert.Throws<ArgumentNullException>(
            () => LibraryInspectionOperation.Execute(
                null!,
                invalid.IssueOperation(),
                TestContext.Current.CancellationToken));
        await invalid.RetireAsync();
    }

    [Fact]
    public void PopulationRequestRejectsUnknownDeclarationSelection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LibraryTypePopulationRequest(
                LibraryTypeAccessibility.Public,
                new(),
                declarationSelection:
                    (LibraryTypeDeclarationSelection)int.MaxValue));
    }

    [Fact]
    public void PopulationRequestRejectsInvalidDefinitionKindSelections()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LibraryTypePopulationRequest(
                LibraryTypeAccessibility.Public,
                new(),
                definitionKinds:
                    (ApiTypeInventoryKinds)int.MaxValue));
        Assert.Throws<ArgumentException>(
            () => new LibraryTypePopulationRequest(
                LibraryTypeAccessibility.Public,
                new(),
                declarationSelection:
                    LibraryTypeDeclarationSelection.Definitions,
                definitionKinds:
                    ApiTypeInventoryKinds.None));
        Assert.Throws<ArgumentException>(
            () => new LibraryTypePopulationRequest(
                LibraryTypeAccessibility.Public,
                new(),
                declarationSelection:
                    LibraryTypeDeclarationSelection.Forwarders,
                definitionKinds:
                    ApiTypeInventoryKinds.Classes));
    }

    [Fact]
    public void PlanAndClosedOutcomeSerializeWithSourceGeneration()
    {
        Guid moduleVersionId = Guid.NewGuid();
        var plan = new LibraryInspectionPlan(
            new(
                LibraryTypeAccessibility.Public,
                new(),
                new(
                    maximumRows: 10,
                    memberCount: new(),
                    continuation:
                        new(
                            new InertString(
                                TextPolicy.Field,
                                "opaque-receipt"))),
                LibraryTypeDeclarationSelection
                    .DefinitionsAndForwarders,
                ApiTypeInventoryKinds.Classes
                    | ApiTypeInventoryKinds.Structs),
            s_bounds);
        var document = new LibraryDocument(
            new(
                new InertString(TextPolicy.Field, "Example"),
                new Version(1, 2, 3, 4),
                null,
                null),
            moduleVersionId,
            new(
                new(
                    moduleVersionId,
                    LibraryTypeAccessibility.Public,
                    LibraryTypeDeclarationSelection
                        .DefinitionsAndForwarders,
                    ApiTypeInventoryKinds.All),
                new LibraryTypePopulationCountOutcome.Counted(
                    forwarders: 6,
                    classes: 1,
                    structs: 2,
                    interfaces: 3,
                    enums: 4,
                    delegates: 5),
                new LibraryTypePopulationRowsOutcome.Read(
                    LibraryTypePopulationOrdering.Metadata,
                    [
                        new(
                            Name("Example", "Widget"),
                            new InertString(
                                TextPolicy.Field,
                                "Example.Widget"),
                            new InertString(
                                TextPolicy.Field,
                                "Example"),
                            LibraryTypeDeclarationKind.Definition,
                            ApiTypeInventoryKind.Class,
                            LibraryTypeDefinitionAccessibility.Public,
                            isPublicSurface: true,
                            forwarding: null,
                            memberCount:
                                new LibraryTypeMemberCountOutcome.Counted(
                                    3)),
                    ],
                    Continuation: null)),
            new(
                AssemblyBytes: 1234,
                MetadataRows: 567,
                RetainedDeclarations: 21,
                RetainedTextCharacters: 890),
            s_bounds);
        InspectionEnvelope<LibraryInspectionOutcome>[] envelopes =
        [
            Envelope(new LibraryInspectionOutcome.Available(document)),
            Envelope(
                new LibraryInspectionOutcome.Available(
                    document with
                    {
                        Types = document.Types with
                        {
                            Count =
                                new LibraryTypePopulationCountOutcome
                                    .Unavailable(
                                        LibraryTypePopulationCountUnavailableReason
                                            .UnsupportedModuleExport),
                        },
                    })),
            Envelope(
                new LibraryInspectionOutcome.Available(
                    document with
                    {
                        Types = document.Types with
                        {
                            Count =
                                new LibraryTypePopulationCountOutcome
                                    .Incomplete(
                                        LibraryTypePopulationCountBound
                                            .MetadataRows,
                                        Limit: 1,
                                        Measured: 2),
                        },
                    })),
            Envelope(
                new LibraryInspectionOutcome.Rejected(
                    LibraryInspectionRejection.AssemblyIdentityMismatch)),
            Envelope(
                new LibraryInspectionOutcome.Failed(
                    LibraryInspectionFailure.MalformedMetadata)),
        ];

        string planJson = JsonSerializer.Serialize(
            plan,
            LibraryInspectionJsonContext.Default.LibraryInspectionPlan);
        Assert.Contains("\"types\"", planJson, StringComparison.Ordinal);
        Assert.Contains("\"count\"", planJson, StringComparison.Ordinal);
        Assert.Contains("\"rows\"", planJson, StringComparison.Ordinal);
        Assert.Contains(
            "\"declarationSelection\": \"DefinitionsAndForwarders\"",
            planJson,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"definitionKinds\": \"Classes, Structs\"",
            planJson,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"memberCount\"",
            planJson,
            StringComparison.Ordinal);
        LibraryInspectionPlan roundTrippedPlan =
            JsonSerializer.Deserialize(
                planJson,
                LibraryInspectionJsonContext.Default
                    .LibraryInspectionPlan)
            ?? throw new InvalidOperationException(
                "The Library inspection plan did not deserialize.");
        Assert.NotNull(roundTrippedPlan.Types.Count);
        Assert.NotNull(roundTrippedPlan.Types.Rows);
        Assert.Equal(
            LibraryTypeDeclarationSelection
                .DefinitionsAndForwarders,
            roundTrippedPlan.Types.DeclarationSelection);
        Assert.Equal(
            ApiTypeInventoryKinds.Classes
                | ApiTypeInventoryKinds.Structs,
            roundTrippedPlan.Types.DefinitionKinds);
        Assert.NotNull(
            roundTrippedPlan.Types.Rows.MemberCount);
        Assert.Equal(
            "opaque-receipt",
            roundTrippedPlan.Types.Rows.Continuation!
                .Value.ToString());
        Assert.Equal(plan.Bounds, roundTrippedPlan.Bounds);
        foreach (
            InspectionEnvelope<LibraryInspectionOutcome> envelope
            in envelopes)
        {
            string json = JsonSerializer.Serialize(
                envelope,
                LibraryInspectionJsonContext.Default
                    .LibraryInspectionEnvelope);
            Assert.Contains("\"content\"", json, StringComparison.Ordinal);
            Assert.Contains("\"share\"", json, StringComparison.Ordinal);
            Assert.NotNull(
                JsonSerializer.Deserialize(
                    json,
                    LibraryInspectionJsonContext.Default
                        .LibraryInspectionEnvelope));
        }
    }

    private static LibraryInspectionRequest Request(
        LibraryReference library) =>
        new(
            library,
            new(
                new(
                    LibraryTypeAccessibility.Public,
                    new()),
                s_bounds));

    private static InspectionEnvelope<LibraryInspectionOutcome> Execute(
        LibraryInspectionTestLibrary library) =>
        Execute(library, s_bounds);

    private static InspectionEnvelope<LibraryInspectionOutcome> Execute(
        LibraryInspectionTestLibrary library,
        ApiSurfaceExtractionBounds bounds) =>
        LibraryInspectionOperation.Execute(
            new(
                library.Reference,
                new(
                    new(
                        LibraryTypeAccessibility.Public,
                        new()),
                    bounds)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static InspectionEnvelope<LibraryInspectionOutcome> Execute(
        LibraryInspectionTestLibrary library,
        bool count,
        LibraryTypePopulationRowsRequest rows,
        ApiSurfaceExtractionBounds? bounds = null,
        LibraryTypeDeclarationSelection declarationSelection =
            LibraryTypeDeclarationSelection.DefinitionsAndForwarders,
        ApiTypeInventoryKinds definitionKinds =
            ApiTypeInventoryKinds.All) =>
        LibraryInspectionOperation.Execute(
            new(
                library.Reference,
                new(
                    new(
                        LibraryTypeAccessibility.Public,
                        count
                            ? new LibraryTypePopulationCountRequest()
                            : null,
                        rows,
                        declarationSelection,
                        definitionKinds),
                    bounds ?? s_bounds)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static void AssertRowsRejection(
        InspectionEnvelope<LibraryInspectionOutcome> envelope,
        LibraryTypePopulationRowsRejection expected) =>
        Assert.Equal(
            expected,
            Assert.IsType<
                    LibraryTypePopulationRowsOutcome.Rejected>(
                    Document(envelope).Types.Rows)
                .Reason);

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

    private static InspectionEnvelope<LibraryInspectionOutcome> Envelope(
        LibraryInspectionOutcome outcome) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                "library-inspection/share",
                "A complete portable Workspace scenario was not supplied."));

    private static LibraryDocument Document(
        InspectionEnvelope<LibraryInspectionOutcome> envelope) =>
        Assert.IsType<LibraryInspectionOutcome.Available>(
                envelope.Content)
            .Document;

    private static void AssertDetachedContract()
    {
        Type[] forbidden =
        [
            typeof(IDisposable),
            typeof(IAsyncDisposable),
            typeof(Stream),
            typeof(Delegate),
            typeof(LibraryReference),
            typeof(LibraryOperationLease),
        ];
        Type[] contract =
        [
            typeof(InspectionEnvelope<LibraryInspectionOutcome>),
            typeof(LibraryInspectionOutcome),
            typeof(LibraryInspectionOutcome.Available),
            typeof(LibraryInspectionOutcome.Rejected),
            typeof(LibraryInspectionOutcome.Failed),
            typeof(LibraryAssemblyIdentity),
            typeof(LibraryDocument),
            typeof(LibraryTypePopulationResult),
            typeof(LibraryTypePopulationBinding),
            typeof(LibraryTypePopulationCountOutcome),
            typeof(LibraryTypePopulationCountOutcome.Counted),
            typeof(LibraryTypePopulationCountOutcome.Unavailable),
            typeof(LibraryTypePopulationCountOutcome.Incomplete),
            typeof(LibraryTypePopulationRowsOutcome),
            typeof(LibraryTypePopulationRowsOutcome.Read),
            typeof(LibraryTypePopulationRowsOutcome.Unavailable),
            typeof(LibraryTypePopulationRowsOutcome.Rejected),
            typeof(LibraryTypePopulationRowsOutcome.Incomplete),
            typeof(LibraryTypePopulationRowsOutcome.Failed),
            typeof(LibraryTypeShape),
            typeof(LibraryTypeForwardingEvidence),
            typeof(LibraryTypeMemberCountOutcome),
            typeof(LibraryTypeMemberCountOutcome.Counted),
            typeof(LibraryTypeMemberCountOutcome.NotApplicable),
            typeof(LibraryTypePopulationContinuation),
            typeof(LibraryInspectionWork),
        ];
        foreach (Type type in contract)
        {
            foreach (
                PropertyInfo property
                in type.GetProperties(
                    BindingFlags.Instance | BindingFlags.Public))
            {
                Assert.DoesNotContain(
                    forbidden,
                    candidate =>
                        candidate.IsAssignableFrom(
                            property.PropertyType));
            }
        }
    }
}
