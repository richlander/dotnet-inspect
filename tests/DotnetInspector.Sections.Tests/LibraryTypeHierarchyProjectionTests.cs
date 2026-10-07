using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class LibraryTypeHierarchyProjectionTests
{
    private static readonly LibraryInspectionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    [Fact]
    public void Request_RejectsUnsupportedTerminalAndSpelling()
    {
        var namespaceCount =
            new InspectionHierarchyRequest<LibraryTypeHierarchyTopology>(
                LibraryTypeHierarchyTopology.LibraryNamespacesAndTypes,
                InspectionHierarchyNodeSpelling.FullSpelling,
                new InspectionHierarchyPopulationRequest.Rows(
                    InspectionHierarchyNodeSpelling.Name,
                    new InspectionHierarchyPopulationRequest.Count()));
        var typeLeaves =
            new InspectionHierarchyRequest<LibraryTypeHierarchyTopology>(
                LibraryTypeHierarchyTopology.LibraryNamespacesAndTypes,
                InspectionHierarchyNodeSpelling.FullSpelling,
                new InspectionHierarchyPopulationRequest.Rows(
                    InspectionHierarchyNodeSpelling.Name,
                    new InspectionHierarchyPopulationRequest.Rows(
                        InspectionHierarchyNodeSpelling.Name)));
        var fullNamespaces =
            new InspectionHierarchyRequest<LibraryTypeHierarchyTopology>(
                LibraryTypeHierarchyTopology.LibraryNamespacesAndTypes,
                InspectionHierarchyNodeSpelling.FullSpelling,
                new InspectionHierarchyPopulationRequest.Rows(
                    InspectionHierarchyNodeSpelling.FullSpelling,
                    new InspectionHierarchyPopulationRequest.Rows(
                        InspectionHierarchyNodeSpelling.Name,
                        new InspectionHierarchyPopulationRequest
                            .Count())));

        foreach (InspectionHierarchyRequest<LibraryTypeHierarchyTopology>
                     request in new[]
                     {
                         namespaceCount,
                         typeLeaves,
                         fullNamespaces,
                     })
        {
            ArgumentException exception =
                Assert.Throws<ArgumentException>(
                    () => LibraryTypeHierarchyProjection
                        .ValidateRequest(request));
            Assert.Contains(
                "namespace Rows by Name",
                exception.Message,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Request_RequiresUnfacetedCountedCompleteRows()
    {
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology> request =
            Hierarchy(InspectionHierarchyNodeSpelling.Name);

        Assert.Contains(
            "Member Count",
            Assert.Throws<ArgumentException>(
                    () => LibraryTypeHierarchyProjection.ValidateRequest(
                        request,
                        Types(memberCount: false),
                        "types"))
                .Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "unfaceted",
            Assert.Throws<ArgumentException>(
                    () => LibraryTypeHierarchyProjection.ValidateRequest(
                        request,
                        Types(@namespace: "System.Text.Json"),
                        "types"))
                .Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Rows request",
            Assert.Throws<ArgumentException>(
                    () => LibraryTypeHierarchyProjection.ValidateRequest(
                        request,
                        new LibraryTypePopulationRequest(
                            LibraryTypeAccessibility.Public,
                            new LibraryTypePopulationCountRequest()),
                        "types"))
                .Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Projection_GroupsRealLibraryByNamespaceWithMemberCounts()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        LibraryTypePopulationRequest types = Types(count: true);
        LibraryDocument document = Execute(library, types);
        InspectionHierarchyRequest<LibraryTypeHierarchyTopology> request =
            Hierarchy(InspectionHierarchyNodeSpelling.Name);
        var sink = new RecordingSink();

        LibraryTypeHierarchyProjection.ValidateDocument(
            document,
            request,
            types);
        LibraryTypeHierarchyProjection.Write(document, request, sink);

        var count =
            Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                document.Types!.Count);
        LibraryTypeHierarchyNode.Namespace[] namespaces =
        [
            .. sink.Events
                .Select(static e => e.Node)
                .OfType<LibraryTypeHierarchyNode.Namespace>(),
        ];
        LibraryTypeHierarchyNode.Type[] declarations =
        [
            .. sink.Events
                .Select(static e => e.Node)
                .OfType<LibraryTypeHierarchyNode.Type>(),
        ];
        Assert.Equal(count.Total, declarations.Length);
        Assert.Equal(
            count.Total,
            namespaces.Sum(static n => n.DeclarationCount));
        Assert.Equal(
            [.. namespaces.Select(static n => n.Name.ToString())
                .Order(StringComparer.Ordinal)],
            namespaces.Select(static n => n.Name.ToString()));
        Assert.Equal(
            namespaces.Length,
            namespaces.Select(static n => n.Name.ToString())
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            count.Forwarders,
            declarations.Count(
                static d => d.Value.MemberCount
                    is LibraryTypeMemberCountOutcome.NotApplicable));
        Assert.All(
            declarations,
            static d => Assert.DoesNotContain(
                d.Value.Namespace.ToString() + ".",
                d.Spelling.ToString(),
                StringComparison.Ordinal));

        int serializer =
            sink.Events.FindIndex(
                static e => e.Node is LibraryTypeHierarchyNode.Type
                {
                    Spelling: var spelling,
                } && spelling.ToString() == "JsonSerializer");
        Assert.True(serializer > 0);
        var serializerNode =
            (LibraryTypeHierarchyNode.Type)sink.Events[serializer].Node;
        Assert.Equal("System.Text.Json", serializerNode.Value.Namespace.ToString());
        Assert.True(
            Assert.IsType<LibraryTypeMemberCountOutcome.Counted>(
                    serializerNode.Value.MemberCount)
                .Value > 0);
        LibraryTypeHierarchyNode.Type enumerator =
            Assert.Single(
                declarations,
                static d => d.Value.DisplayName.ToString()
                    == "System.Text.Json.JsonElement.ArrayEnumerator");
        Assert.Equal(
            "JsonElement.ArrayEnumerator",
            enumerator.Spelling.ToString());
        Assert.Equal(
            "System.Text.Json",
            enumerator.Value.Namespace.ToString());
        Assert.Equal(
            1,
            sink.Events.Count(static e =>
                e.IsLastSibling
                && e.Node is LibraryTypeHierarchyNode.Namespace));
        await library.RetireAsync();
    }

    [Fact]
    public async Task Projection_FullSpellingRetainsQualifiedDisplayName()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        LibraryDocument document = Execute(library, Types());
        var sink = new RecordingSink();

        LibraryTypeHierarchyProjection.Write(
            document,
            Hierarchy(InspectionHierarchyNodeSpelling.FullSpelling),
            sink);

        Assert.Contains(
            sink.Events,
            static e => e.Node is LibraryTypeHierarchyNode.Type
            {
                Spelling: var spelling,
            } && spelling.ToString() == "System.Text.Json.JsonSerializer");
        await library.RetireAsync();
    }

    [Fact]
    public async Task Projection_RejectsContinuedRowsBeforeWriting()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        LibraryDocument document =
            Execute(library, Types(maximumRows: 1));
        var sink = new RecordingSink();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => LibraryTypeHierarchyProjection.Write(
                    document,
                    Hierarchy(InspectionHierarchyNodeSpelling.Name),
                    sink));

        Assert.Contains(
            "complete Type declaration Rows",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(sink.Events);
        await library.RetireAsync();
    }

    [Fact]
    public async Task Projection_RejectsUncountedRowsBeforeWriting()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        LibraryDocument document =
            Execute(library, Types(memberCount: false));
        var sink = new RecordingSink();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => LibraryTypeHierarchyProjection.Write(
                    document,
                    Hierarchy(InspectionHierarchyNodeSpelling.Name),
                    sink));

        Assert.Contains(
            "Member Count outcome",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(sink.Events);
        await library.RetireAsync();
    }

    [Fact]
    public async Task Projection_RejectsFacetedPopulationBeforeWriting()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        LibraryDocument document =
            Execute(library, Types(@namespace: "System.Text.Json"));
        var sink = new RecordingSink();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => LibraryTypeHierarchyProjection.Write(
                    document,
                    Hierarchy(InspectionHierarchyNodeSpelling.Name),
                    sink));

        Assert.Contains(
            "unfaceted",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(sink.Events);
        await library.RetireAsync();
    }

    [Fact]
    public async Task Projection_SpellsNameFromRawTextWhenEncodingModesDiffer()
    {
        // The namespace keeps its literal backslash when encoded alone, while
        // the qualified name contains an escapable "\\" and so doubles every
        // backslash. The qualifier must be removed from raw text.
        byte[] content =
            LibraryInspectionTestLibrary.BuildMetadataImage(
                publicInterface: (@"A\B", @"C\\D"));
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.ProbeIdentity());
        LibraryDocument document = Execute(library, Types());
        LibraryTypeShape row =
            Assert.Single(
                Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                        document.Types!.Rows)
                    .Items);
        Assert.False(
            row.DisplayName.ToString().StartsWith(
                row.Namespace.ToString() + ".",
                StringComparison.Ordinal));
        var sink = new RecordingSink();

        LibraryTypeHierarchyProjection.Write(
            document,
            Hierarchy(InspectionHierarchyNodeSpelling.Name),
            sink);

        Assert.Collection(
            sink.Events,
            static e =>
            {
                var node =
                    Assert.IsType<LibraryTypeHierarchyNode.Namespace>(e.Node);
                Assert.Equal(@"A\B", Decoded(node.Name));
                Assert.Equal(1, node.DeclarationCount);
            },
            static e => Assert.Equal(
                @"C\\D",
                Decoded(
                    Assert.IsType<LibraryTypeHierarchyNode.Type>(e.Node)
                        .Spelling)));
        await library.RetireAsync();
    }

    [Fact]
    public async Task Projection_MarksFacadeForwardersWithoutMemberCounts()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealNetstandardAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        LibraryTypePopulationRequest types = Types(count: true);
        LibraryDocument document = Execute(library, types);
        var sink = new RecordingSink();

        LibraryTypeHierarchyProjection.Write(
            document,
            Hierarchy(InspectionHierarchyNodeSpelling.Name),
            sink);

        var count =
            Assert.IsType<LibraryTypePopulationCountOutcome.Counted>(
                document.Types!.Count);
        LibraryTypeHierarchyNode.Type[] forwarders =
        [
            .. sink.Events
                .Select(static e => e.Node)
                .OfType<LibraryTypeHierarchyNode.Type>()
                .Where(static d => d.Value.DeclarationKind
                    is LibraryTypeDeclarationKind.Forwarder),
        ];
        Assert.True(count.Forwarders > 0);
        Assert.Equal(count.Forwarders, forwarders.Length);
        Assert.All(
            forwarders,
            static d => Assert.IsType<
                LibraryTypeMemberCountOutcome.NotApplicable>(
                d.Value.MemberCount));
        await library.RetireAsync();
    }

    private static InspectionHierarchyRequest<LibraryTypeHierarchyTopology>
        Hierarchy(InspectionHierarchyNodeSpelling typeSpelling) =>
        new(
            LibraryTypeHierarchyTopology.LibraryNamespacesAndTypes,
            InspectionHierarchyNodeSpelling.FullSpelling,
            new InspectionHierarchyPopulationRequest.Rows(
                InspectionHierarchyNodeSpelling.Name,
                new InspectionHierarchyPopulationRequest.Rows(
                    typeSpelling,
                    new InspectionHierarchyPopulationRequest.Count())));

    private static LibraryTypePopulationRequest Types(
        bool count = false,
        bool memberCount = true,
        int maximumRows = 16_384,
        string? @namespace = null) =>
        new(
            LibraryTypeAccessibility.Public,
            count ? new LibraryTypePopulationCountRequest() : null,
            new LibraryTypePopulationRowsRequest(
                maximumRows,
                memberCount: memberCount
                    ? new LibraryTypeMemberCountRequest()
                    : null),
            @namespace: @namespace);

    private static LibraryDocument Execute(
        LibraryInspectionTestLibrary library,
        LibraryTypePopulationRequest types) =>
        Assert.IsType<LibraryInspectionOutcome.Available>(
                LibraryInspectionOperation.Execute(
                        new(library.Reference, new(types, s_bounds)),
                        library.IssueOperation(),
                        TestContext.Current.CancellationToken)
                    .Content)
            .Document;

    private static string Decoded(InertText.InertString value) =>
        Assert.IsType<string>(
            InertText.Encoding.VisualEncoder.TryDecode(value.ToString(), out string? raw)
                ? raw
                : null);

    private sealed class RecordingSink :
        IInspectionHierarchySink<LibraryTypeHierarchyNode>
    {
        internal List<RecordedNode> Events { get; } = [];

        public void WriteNode(
            LibraryTypeHierarchyNode node,
            bool isLastSibling,
            Action<IInspectionHierarchySink<LibraryTypeHierarchyNode>>?
                writeChildren = null)
        {
            Events.Add(new(node, isLastSibling));
            writeChildren?.Invoke(this);
        }
    }

    private sealed record RecordedNode(
        LibraryTypeHierarchyNode Node,
        bool IsLastSibling);
}
