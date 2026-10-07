using System.Collections.Immutable;

using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Presentation.Tests;

public class LibraryTypeHierarchyPresentationTests
{
    private static readonly Guid s_mvid =
        new("0b8e8f6e-6a0f-4f55-9a3e-0f5f7c2f8e11");

    [Fact]
    public void DefaultPlans_SeparateFormatFromSemanticRequest()
    {
        LibraryTypeHierarchyPresentationPlan tree =
            LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                LibraryTypeHierarchyPresentationFormat.Tree);
        LibraryTypeHierarchyPresentationPlan mermaid =
            LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                LibraryTypeHierarchyPresentationFormat.Mermaid);

        Assert.Equal(tree.Types, mermaid.Types);
        Assert.Equal(tree.Hierarchy, mermaid.Hierarchy);
        Assert.NotEqual(tree.Format, mermaid.Format);
        Assert.Null(tree.Types.Rows!.MemberCount);
        Assert.Null(tree.Types.Rows.Continuation);
    }

    [Fact]
    public void Tree_StreamsNamespacesAndLeafTypesByDefault()
    {
        LibraryTypeHierarchyPresentationPlan plan =
            LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                LibraryTypeHierarchyPresentationFormat.Tree);
        using var output = new StringWriter();

        LibraryTypeHierarchyPresentation.Write(
            Document(),
            plan,
            output);

        Assert.Equal(
            """
            Example 1.2.3.4
            ├─ (global namespace) (1 type)
            │  └─ struct Loose
            ├─ Example (2 types)
            │  ├─ class Widget<T>
            │  └─ interface Zeta
            └─ Example.Moved (1 type)
               └─ Gadget (forwarded)

            """.ReplaceLineEndings(),
            output.ToString());
    }

    [Fact]
    public void RootSpelling_IsIndependentFromFormat()
    {
        LibraryTypeHierarchyPresentationPlan defaults =
            LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                LibraryTypeHierarchyPresentationFormat.Tree);
        var plan =
            new LibraryTypeHierarchyPresentationPlan(
                LibraryTypeHierarchyPresentationFormat.Tree,
                defaults.Types,
                new(
                    defaults.Hierarchy.Topology,
                    InspectionHierarchyNodeSpelling.Name,
                    defaults.Hierarchy.Children));
        using var output = new StringWriter();

        LibraryTypeHierarchyPresentation.Write(Document(), plan, output);

        Assert.StartsWith(
            "Example" + Environment.NewLine,
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Mermaid_LowersTheSameHierarchy()
    {
        LibraryTypeHierarchyPresentationPlan plan =
            LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                LibraryTypeHierarchyPresentationFormat.Mermaid);
        using var output = new StringWriter();

        LibraryTypeHierarchyPresentation.Write(
            Document(),
            plan,
            output);

        string result = output.ToString();
        Assert.StartsWith("graph TD", result, StringComparison.Ordinal);
        Assert.Contains("Example 1.2.3.4", result, StringComparison.Ordinal);
        Assert.Contains(
            "class Widget&lt;T&gt;",
            result,
            StringComparison.Ordinal);
        Assert.Contains(
            "Gadget (forwarded)",
            result,
            StringComparison.Ordinal);
        Assert.Equal(
            7,
            result.Split(" --> ", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ExplicitMemberCountProfile_AddsCountsToTypeLines()
    {
        LibraryTypeHierarchyPresentationPlan defaults =
            LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                LibraryTypeHierarchyPresentationFormat.Tree);
        var plan =
            new LibraryTypeHierarchyPresentationPlan(
                LibraryTypeHierarchyPresentationFormat.Tree,
                new LibraryTypePopulationRequest(
                    LibraryTypeAccessibility.Public,
                    count: null,
                    rows: new LibraryTypePopulationRowsRequest(
                        defaults.Types.Rows!.MaximumRows,
                        memberCount: new LibraryTypeMemberCountRequest())),
                new(
                    defaults.Hierarchy.Topology,
                    defaults.Hierarchy.RootSpelling,
                    new InspectionHierarchyPopulationRequest.Rows(
                        InspectionHierarchyNodeSpelling.Name,
                        new InspectionHierarchyPopulationRequest.Rows(
                            InspectionHierarchyNodeSpelling.Name,
                            new InspectionHierarchyPopulationRequest
                                .Count()))));
        using var output = new StringWriter();

        LibraryTypeHierarchyPresentation.Write(
            Document(counted: true),
            plan,
            output);

        Assert.Contains(
            "class Widget<T> (3 members)",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "struct Loose (1 member)",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "Gadget (forwarded)",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultPlan_RejectsCountedRowsBeforeAnyOutput()
    {
        using var output = new StringWriter();

        Assert.Throws<InvalidOperationException>(
            () => LibraryTypeHierarchyPresentation.Write(
                Document(counted: true),
                LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                    LibraryTypeHierarchyPresentationFormat.Tree),
                output));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData(LibraryTypeHierarchyPresentationFormat.Tree)]
    [InlineData(LibraryTypeHierarchyPresentationFormat.Mermaid)]
    public void ContinuedRows_FailBeforeAnyOutput(
        LibraryTypeHierarchyPresentationFormat format)
    {
        LibraryDocument document = Document();
        var rows =
            (LibraryTypePopulationRowsOutcome.Read)document.Types!.Rows!;
        LibraryDocument continued =
            document with
            {
                Types = document.Types with
                {
                    Rows = rows with
                    {
                        Continuation = new(Text("receipt")),
                    },
                },
            };
        using var output = new StringWriter();

        Assert.Throws<InvalidOperationException>(
            () => LibraryTypeHierarchyPresentation.Write(
                continued,
                LibraryTypeHierarchyPresentation.CreateDefaultPlan(format),
                output));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData(LibraryTypeHierarchyPresentationFormat.Tree)]
    [InlineData(LibraryTypeHierarchyPresentationFormat.Mermaid)]
    public void EmptyPopulation_RendersAsEmpty(
        LibraryTypeHierarchyPresentationFormat format)
    {
        LibraryDocument document = Document();
        var rows =
            (LibraryTypePopulationRowsOutcome.Read)document.Types!.Rows!;
        LibraryDocument empty =
            document with
            {
                Types = document.Types with
                {
                    Rows = rows with { Items = [] },
                },
            };
        using var output = new StringWriter();

        LibraryTypeHierarchyPresentation.Write(
            empty,
            LibraryTypeHierarchyPresentation.CreateDefaultPlan(format),
            output);

        string result = output.ToString();
        Assert.Contains(
            "Example 1.2.3.4 (no public types)",
            result,
            StringComparison.Ordinal);
        if (format == LibraryTypeHierarchyPresentationFormat.Tree)
        {
            Assert.Equal(
                "Example 1.2.3.4 (no public types)" + Environment.NewLine,
                result);
        }
        else
        {
            Assert.DoesNotContain(" --> ", result, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FacetedBinding_FailsBeforeAnyOutput()
    {
        LibraryDocument document = Document();
        LibraryDocument faceted =
            document with
            {
                Types = document.Types! with
                {
                    Binding = document.Types.Binding with
                    {
                        Namespace = "Example",
                    },
                },
            };
        using var output = new StringWriter();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => LibraryTypeHierarchyPresentation.Write(
                    faceted,
                    LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                        LibraryTypeHierarchyPresentationFormat.Tree),
                    output));

        Assert.Contains(
            "unfaceted",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    private static LibraryDocument Document(bool counted = false) =>
        new(
            new(Text("Example"), new Version(1, 2, 3, 4), null, null),
            s_mvid,
            new(
                new(
                    s_mvid,
                    LibraryTypeAccessibility.Public,
                    LibraryTypeDeclarationSelection.DefinitionsAndForwarders,
                    ApiTypeInventoryKinds.All,
                    Namespace: null),
                Count: null,
                new LibraryTypePopulationRowsOutcome.Read(
                    LibraryTypePopulationOrdering.Metadata,
                    [
                        Definition(
                            "Example",
                            "Zeta",
                            "Example.Zeta",
                            ApiTypeInventoryKind.Interface,
                            counted ? 0 : null),
                        Forwarder("Example.Moved", "Gadget", counted),
                        Definition(
                            "Example",
                            "Widget`1",
                            "Example.Widget<T>",
                            ApiTypeInventoryKind.Class,
                            counted ? 3 : null),
                        Definition(
                            "",
                            "Loose",
                            "Loose",
                            ApiTypeInventoryKind.Struct,
                            counted ? 1 : null),
                    ],
                    Continuation: null)),
            new(
                AssemblyBytes: 1,
                MetadataRows: 1,
                RetainedDeclarations: 4,
                RetainedTextCharacters: 1),
            new(
                maxTypes: 10,
                maxMembers: 10,
                maxInspectionFailures: 1,
                maxTypeForwarders: 10,
                maxMetadataRows: 10,
                maxRetainedTextCharacters: 1_000));

    private static LibraryTypeShape Definition(
        string @namespace,
        string name,
        string displayName,
        ApiTypeInventoryKind kind,
        int? memberCount) =>
        new(
            Name(@namespace, name),
            Text(displayName),
            Text(@namespace),
            LibraryTypeDeclarationKind.Definition,
            kind,
            LibraryTypeDefinitionAccessibility.Public,
            isPublicSurface: true,
            forwarding: null,
            memberCount: memberCount is { } value
                ? new LibraryTypeMemberCountOutcome.Counted(value)
                : null);

    private static LibraryTypeShape Forwarder(
        string @namespace,
        string name,
        bool counted) =>
        new(
            Name(@namespace, name),
            Text($"{@namespace}.{name}"),
            Text(@namespace),
            LibraryTypeDeclarationKind.Forwarder,
            definitionKind: null,
            definitionAccessibility: null,
            isPublicSurface: true,
            forwarding: new(
                s_mvid,
                ImmutableArray.Create(default(ExportedTypeToken)),
                new(Text("Example.Target"), new Version(1, 0), null, null)),
            memberCount: counted
                ? new LibraryTypeMemberCountOutcome.NotApplicable(
                    LibraryTypeMemberCountNotApplicableReason.Forwarder)
                : null);

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(@namespace, [name]))
            .Name;

    private static InertString Text(string value) =>
        new(TextPolicy.Field, value);
}
