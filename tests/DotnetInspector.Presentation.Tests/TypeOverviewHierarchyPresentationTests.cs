using System.Collections.Immutable;
using System.Reflection;

using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Presentation.Tests;

public class TypeOverviewHierarchyPresentationTests
{
    [Fact]
    public async Task CompactInspection_ResolvesAndInspectsExactType()
    {
        AssemblyDescriptorSelectionResult selection =
            ResolvedAssemblyReference.SelectFromPath(
                typeof(TypeOverviewHierarchyPresentationTests)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "Type overview hierarchy presentation test"));
        ResolvedAssemblyReference assembly =
            Assert.IsType<AssemblyDescriptorSelectionResult.Ready>(
                selection).Reference;

        TypeOverviewHierarchyInspectionExecution execution =
            await TypeOverviewHierarchyInspection.ExecuteAsync(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance,
                typeof(TypeOverviewHierarchyPresentationTests).FullName!,
                TypeOverviewHierarchyPresentationFormat.Tree,
                includeNonPublic: false,
                new(
                    maxTypes: 10_000,
                    maxMembers: 100_000,
                    maxInspectionFailures: 1_000,
                    maxTypeForwarders: 10_000,
                    maxMetadataRows: 1_000_000,
                    maxRetainedTextCharacters: 1_000_000),
                new(
                    maxCapturedImageBytes: 64 * 1024 * 1024,
                    maxRetainedArtifactBytes: 64 * 1024 * 1024),
                TestContext.Current.CancellationToken);

        Assert.Null(execution.Failure);
        Assert.Empty(execution.CleanupFailures);
        TypeOverviewDocumentInspectionOutcome.Available available =
            Assert.IsType<
                TypeOverviewDocumentInspectionOutcome.Available>(
                    execution.Inspection!.Content);
        using var output = new StringWriter();

        TypeOverviewHierarchyPresentation.Write(
            available.Document,
            execution.Presentation,
            output);

        Assert.Contains(
            nameof(TypeOverviewHierarchyPresentationTests),
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompactInspection_AllResolvesNonPublicExactType()
    {
        AssemblyDescriptorSelectionResult selection =
            ResolvedAssemblyReference.SelectFromPath(
                typeof(TypeOverviewHierarchyInternalFixture)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "Type overview hierarchy presentation test"));
        ResolvedAssemblyReference assembly =
            Assert.IsType<AssemblyDescriptorSelectionResult.Ready>(
                selection).Reference;

        TypeOverviewHierarchyInspectionExecution execution =
            await TypeOverviewHierarchyInspection.ExecuteAsync(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance,
                typeof(TypeOverviewHierarchyInternalFixture).FullName!,
                TypeOverviewHierarchyPresentationFormat.Tree,
                includeNonPublic: true,
                new(
                    maxTypes: 10_000,
                    maxMembers: 100_000,
                    maxInspectionFailures: 1_000,
                    maxTypeForwarders: 10_000,
                    maxMetadataRows: 1_000_000,
                    maxRetainedTextCharacters: 1_000_000),
                new(
                    maxCapturedImageBytes: 64 * 1024 * 1024,
                    maxRetainedArtifactBytes: 64 * 1024 * 1024),
                TestContext.Current.CancellationToken);

        Assert.Null(execution.Failure);
        Assert.Empty(execution.CleanupFailures);
        Assert.IsType<TypeOverviewDocumentInspectionOutcome.Available>(
            execution.Inspection!.Content);
    }

    [Fact]
    public void CompactPlans_SeparateFormatFromSemanticRequest()
    {
        TypeOverviewHierarchyPresentationPlan tree =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                TypeOverviewHierarchyPresentationFormat.Tree,
                includeNonPublic: false);
        TypeOverviewHierarchyPresentationPlan mermaid =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                TypeOverviewHierarchyPresentationFormat.Mermaid,
                includeNonPublic: false);

        Assert.Equal(tree.Members, mermaid.Members);
        Assert.Equal(tree.Hierarchy, mermaid.Hierarchy);
        Assert.NotEqual(tree.Format, mermaid.Format);
        var categories =
            Assert.IsType<InspectionHierarchyPopulationRequest.Rows>(
                tree.Hierarchy.Children);
        var memberGroups =
            Assert.IsType<InspectionHierarchyPopulationRequest.Rows>(
                categories.Children);
        Assert.Null(memberGroups.Children);
        Assert.False(tree.Members.Rows!.IncludeExactMemberCount);
    }

    internal sealed class TypeOverviewHierarchyInternalFixture;

    [Fact]
    public void Tree_StreamsCompactHierarchy()
    {
        TypeOverviewDocument document =
            Document("Widget`1", genericParameter: "T");
        TypeOverviewHierarchyPresentationPlan plan =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                TypeOverviewHierarchyPresentationFormat.Tree,
                includeNonPublic: false);
        using var output = new StringWriter();

        TypeOverviewHierarchyPresentation.Write(
            document,
            plan,
            output);

        Assert.Equal(
            """
            class Example.Widget<T>
            └─ Methods (2)
               ├─ Second
               └─ First

            """.ReplaceLineEndings(),
            output.ToString());
    }

    [Theory]
    [InlineData(TypeOverviewHierarchyPresentationFormat.Tree)]
    [InlineData(TypeOverviewHierarchyPresentationFormat.Mermaid)]
    public void ExplicitCountProfile_AddsOverloadCounts(
        TypeOverviewHierarchyPresentationFormat format)
    {
        TypeOverviewDocument document =
            Document("Widget`1", genericParameter: "T", counted: true);
        using var output = new StringWriter();

        TypeOverviewHierarchyPresentation.Write(
            document,
            CountPlan(format),
            output);

        string result = output.ToString();
        Assert.Contains(
            "Methods (2 logical, 3 overloads)",
            result,
            StringComparison.Ordinal);
        Assert.Contains(
            "Second (2 overloads)",
            result,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TypeOverviewHierarchyPresentationFormat.Tree)]
    [InlineData(TypeOverviewHierarchyPresentationFormat.Mermaid)]
    public void DefaultPlan_RejectsCountedRowsBeforeOutput(
        TypeOverviewHierarchyPresentationFormat format)
    {
        TypeOverviewDocument document =
            Document("Widget", counted: true);
        using var output = new StringWriter();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => TypeOverviewHierarchyPresentation.Write(
                    document,
                    TypeOverviewHierarchyPresentation.CreateCompactPlan(
                        format,
                        includeNonPublic: false),
                    output));

        Assert.Contains(
            "without exact-Member Counts",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void CountProfile_RejectsLeafRowsBeforeOutput()
    {
        TypeOverviewDocument document = Document("Widget");
        using var output = new StringWriter();

        Assert.Throws<InvalidOperationException>(
            () => TypeOverviewHierarchyPresentation.Write(
                document,
                CountPlan(TypeOverviewHierarchyPresentationFormat.Tree),
                output));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void RootSpelling_IsIndependentFromFormat()
    {
        TypeOverviewDocument document =
            Document("Widget");
        TypeOverviewHierarchyPresentationPlan compact =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                TypeOverviewHierarchyPresentationFormat.Tree,
                includeNonPublic: false);
        var nameHierarchy =
            new InspectionHierarchyRequest<
                TypeOverviewHierarchyTopology>(
                compact.Hierarchy.Topology,
                InspectionHierarchyNodeSpelling.Name,
                compact.Hierarchy.Children);
        var plan =
            new TypeOverviewHierarchyPresentationPlan(
                TypeOverviewHierarchyPresentationFormat.Tree,
                compact.Members,
                nameHierarchy);
        using var output = new StringWriter();

        TypeOverviewHierarchyPresentation.Write(
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
        TypeOverviewDocument document =
            Document("Widget`1", genericParameter: "T");
        TypeOverviewHierarchyPresentationPlan plan =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                TypeOverviewHierarchyPresentationFormat.Mermaid,
                includeNonPublic: false);
        using var output = new StringWriter();

        TypeOverviewHierarchyPresentation.Write(
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
            "Methods (2)",
            result,
            StringComparison.Ordinal);
        Assert.Contains("First", result, StringComparison.Ordinal);
        Assert.Contains("Second", result, StringComparison.Ordinal);
        Assert.DoesNotContain("overloads", result, StringComparison.Ordinal);
        Assert.Equal(
            3,
            result.Split(
                    " --> ",
                    StringSplitOptions.None)
                .Length
                - 1);
    }

    [Theory]
    [InlineData(TypeOverviewHierarchyPresentationFormat.Tree)]
    [InlineData(TypeOverviewHierarchyPresentationFormat.Mermaid)]
    public void PartialRows_AreRejectedBeforeOutput(
        TypeOverviewHierarchyPresentationFormat format)
    {
        TypeOverviewDocument document =
            Document("Widget", partial: true);
        TypeOverviewHierarchyPresentationPlan plan =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                format,
                includeNonPublic: false);
        using var output = new StringWriter();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => TypeOverviewHierarchyPresentation.Write(
                    document,
                    plan,
                    output));

        Assert.Contains(
            "complete member-group Rows",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData(TypeOverviewHierarchyPresentationFormat.Tree)]
    [InlineData(TypeOverviewHierarchyPresentationFormat.Mermaid)]
    public void PopulationPolicyMismatch_IsRejectedBeforeOutput(
        TypeOverviewHierarchyPresentationFormat format)
    {
        TypeOverviewDocument document =
            Document(
                "Widget",
                accessibility:
                    TypeMemberGroupAccessibilityFilter.All,
                includeHidden: true);
        TypeOverviewHierarchyPresentationPlan plan =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                format,
                includeNonPublic: false);
        using var output = new StringWriter();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => TypeOverviewHierarchyPresentation.Write(
                    document,
                    plan,
                    output));

        Assert.Contains(
            "population binding",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void TypeSpelling_PreservesNoncanonicalBacktick()
    {
        TypeOverviewDocument document =
            Document("Widget`1Extra");
        TypeOverviewHierarchyPresentationPlan plan =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                TypeOverviewHierarchyPresentationFormat.Tree,
                includeNonPublic: false);
        using var output = new StringWriter();

        TypeOverviewHierarchyPresentation.Write(
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

    private static TypeOverviewDocument Document(
        string typeSegment,
        string? genericParameter = null,
        bool partial = false,
        TypeMemberGroupAccessibilityFilter accessibility =
            TypeMemberGroupAccessibilityFilter.Public,
        bool includeHidden = false,
        bool counted = false)
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
                isHidden: false,
                isCompilerGenerated: false,
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
                includeHidden,
                accessibility,
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
                ExactMemberCount: counted ? 2 : null),
            new(
                new(
                    binding,
                    Text("First"),
                    MemberGroupCategory.Method,
                    MemberGroupRole.Declared),
                BaselineOrdinal: 0,
                MemberGroupReceiverForms.This,
                ExactMemberCount: counted ? 1 : null),
        ];
        var population =
            new TypeMemberGroupPopulationResult(
                binding,
                Count: null,
                new TypeMemberGroupRowsOutcome.Read(
                    TypeMemberGroupOrdering.Metadata,
                    rows,
                    partial
                        ? new TypeMemberGroupContinuation(
                            binding,
                            nextOrdinal: 3,
                            includeExactMemberCount: counted)
                        : null),
                Composition: null,
                SelectorCounts: null);
        return new(
            subject,
            population,
            assemblyBytes: 1);
    }

    private static TypeOverviewHierarchyPresentationPlan CountPlan(
        TypeOverviewHierarchyPresentationFormat format)
    {
        TypeOverviewHierarchyPresentationPlan leaf =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                format,
                includeNonPublic: false);
        return new(
            format,
            new TypeMemberGroupPopulationRequest(
                count: null,
                rows: new TypeMemberGroupRowsRequest(
                    leaf.Members.Rows!.MaximumRows,
                    includeExactMemberCount: true),
                spelling: leaf.Members.Spelling,
                accessibility: leaf.Members.Accessibility,
                includeHidden: leaf.Members.IncludeHidden),
            new InspectionHierarchyRequest<TypeOverviewHierarchyTopology>(
                leaf.Hierarchy.Topology,
                leaf.Hierarchy.RootSpelling,
                new InspectionHierarchyPopulationRequest.Rows(
                    InspectionHierarchyNodeSpelling.Name,
                    new InspectionHierarchyPopulationRequest.Rows(
                        InspectionHierarchyNodeSpelling.Name,
                        new InspectionHierarchyPopulationRequest
                            .Count()))));
    }

    private static InertString Text(string value) =>
        new(TextPolicy.Field, value);
}
