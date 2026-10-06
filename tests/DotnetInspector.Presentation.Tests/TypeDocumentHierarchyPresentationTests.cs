using System.Collections.Immutable;
using System.Reflection;

using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Presentation.Tests;

public class TypeDocumentHierarchyPresentationTests
{
    [Fact]
    public void CompactPlans_SeparateFormatFromSemanticRequest()
    {
        TypeDocumentHierarchyPresentationPlan tree =
            TypeDocumentHierarchyPresentation.CreateCompactPlan(
                TypeDocumentHierarchyPresentationFormat.Tree,
                includeNonPublic: false);
        TypeDocumentHierarchyPresentationPlan mermaid =
            TypeDocumentHierarchyPresentation.CreateCompactPlan(
                TypeDocumentHierarchyPresentationFormat.Mermaid,
                includeNonPublic: false);

        Assert.Equal(tree.Declarations, mermaid.Declarations);
        Assert.Equal(tree.Hierarchy, mermaid.Hierarchy);
        Assert.NotEqual(tree.Format, mermaid.Format);
        Assert.IsType<InspectionHierarchyPopulationRequest.Rows>(
            tree.Hierarchy.Children);
    }

    [Fact]
    public void Tree_StreamsCompactHierarchy()
    {
        TypeDocumentInspectionContent document =
            Document("Widget`1", genericParameter: "T");
        TypeDocumentHierarchyPresentationPlan plan =
            TypeDocumentHierarchyPresentation.CreateCompactPlan(
                TypeDocumentHierarchyPresentationFormat.Tree,
                includeNonPublic: false);
        using var output = new StringWriter();

        TypeDocumentHierarchyPresentation.Write(
            document,
            plan,
            output);

        Assert.Equal(
            """
            class Example.Widget<T>
            └─ Methods (2 logical, 3 overloads)
               ├─ Second (2 overloads)
               └─ First

            """.ReplaceLineEndings(),
            output.ToString());
    }

    [Fact]
    public void RootSpelling_IsIndependentFromFormat()
    {
        TypeDocumentInspectionContent document =
            Document("Widget");
        TypeDocumentHierarchyPresentationPlan compact =
            TypeDocumentHierarchyPresentation.CreateCompactPlan(
                TypeDocumentHierarchyPresentationFormat.Tree,
                includeNonPublic: false);
        var nameHierarchy =
            new InspectionHierarchyRequest<
                TypeDocumentHierarchyTopology>(
                compact.Hierarchy.Topology,
                InspectionHierarchyNodeSpelling.Name,
                compact.Hierarchy.Children);
        var plan =
            new TypeDocumentHierarchyPresentationPlan(
                TypeDocumentHierarchyPresentationFormat.Tree,
                compact.Declarations,
                nameHierarchy);
        using var output = new StringWriter();

        TypeDocumentHierarchyPresentation.Write(
            document,
            plan,
            output);

        Assert.StartsWith(
            "class Widget",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Example.Widget",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Mermaid_LowersTheSameCompactHierarchy()
    {
        TypeDocumentInspectionContent document =
            Document("Widget`1", genericParameter: "T");
        TypeDocumentHierarchyPresentationPlan plan =
            TypeDocumentHierarchyPresentation.CreateCompactPlan(
                TypeDocumentHierarchyPresentationFormat.Mermaid,
                includeNonPublic: false);
        using var output = new StringWriter();

        TypeDocumentHierarchyPresentation.Write(
            document,
            plan,
            output);

        string result = output.ToString();
        Assert.StartsWith(
            "graph TD",
            result,
            StringComparison.Ordinal);
        Assert.Contains(
            "class Example.Widget&lt;T&gt;",
            result,
            StringComparison.Ordinal);
        Assert.Contains(
            "Methods (2 logical, 3 overloads)",
            result,
            StringComparison.Ordinal);
        Assert.Contains("First", result, StringComparison.Ordinal);
        Assert.Contains(
            "Second (2 overloads)",
            result,
            StringComparison.Ordinal);
        Assert.Equal(
            3,
            result.Split(
                    " --> ",
                    StringSplitOptions.None)
                .Length
                - 1);
    }

    [Fact]
    public void TypeSpelling_PreservesNoncanonicalBacktick()
    {
        TypeDocumentInspectionContent document =
            Document("Widget`1Extra");
        TypeDocumentHierarchyPresentationPlan plan =
            TypeDocumentHierarchyPresentation.CreateCompactPlan(
                TypeDocumentHierarchyPresentationFormat.Tree,
                includeNonPublic: false);
        using var output = new StringWriter();

        TypeDocumentHierarchyPresentation.Write(
            document,
            plan,
            output);

        Assert.Contains(
            "Widget`1Extra",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "class Example.Widget\n",
            output.ToString(),
            StringComparison.Ordinal);
    }

    private static TypeDocumentInspectionContent Document(
        string typeSegment,
        string? genericParameter = null)
    {
        MetadataTypeDefinitionName type =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "Example",
                        [typeSegment]))
                .Name;
        var assembly =
            new LibraryAssemblyIdentity(
                Text("Example"),
                new Version(1, 0, 0, 0),
                null,
                null);
        Guid moduleVersionId =
            Guid.Parse(
                "11111111-2222-3333-4444-555555555555");
        const int typeDefinitionToken = 0x02000001;
        ImmutableArray<TypeDocumentGenericParameter> parameters =
            genericParameter is null
                ? []
                :
                [
                    new(
                        DefinitionSegmentIndex: 0,
                        MetadataIndex: 0,
                        Text(genericParameter),
                        GenericParameterAttributes.None),
                ];
        var subject =
            new TypeSubject(
                assembly,
                moduleVersionId,
                type,
                typeDefinitionToken,
                new TypeDocumentDeclarationSignature(parameters),
                MetadataTypeDeclarationCategory.Class,
                TypeAttributes.Public,
                isByRefLike: false,
                isReadOnly: false,
                definesCoreLibraryRoot: false,
                declaringTypeDefinitionToken: null);
        var binding =
            new TypeMemberGroupPopulationBinding(
                assembly,
                moduleVersionId,
                type,
                typeDefinitionToken,
                TypeMemberGroupSpelling.CSharp,
                includeHidden: false,
                TypeMemberGroupAccessibilityFilter.Public,
                TypeMemberGroupReceiverFilter.All,
                TypeMemberGroupOrdering.Metadata);
        ImmutableArray<TypeMemberGroupShape> rows =
        [
            new(
                new(
                    binding,
                    Text("Second"),
                    MemberGroupCategory.Method,
                    MemberGroupRole.Declared),
                BaselineOrdinal: 1,
                MemberGroupReceiverForms.This,
                ExactMemberCount: 2),
            new(
                new(
                    binding,
                    Text("First"),
                    MemberGroupCategory.Method,
                    MemberGroupRole.Declared),
                BaselineOrdinal: 0,
                MemberGroupReceiverForms.This,
                ExactMemberCount: 1),
        ];
        var population =
            new TypeMemberGroupPopulationResult(
                binding,
                Count: null,
                new TypeMemberGroupRowsOutcome.Read(
                    TypeMemberGroupOrdering.Metadata,
                    rows,
                    Continuation: null),
                Composition: null,
                SelectorCounts: null);
        return new(
            subject,
            new TypeDocumentDeclarations.Available(population),
            assemblyBytes: 1);
    }

    private static InertString Text(string value) =>
        new(TextPolicy.Field, value);
}
