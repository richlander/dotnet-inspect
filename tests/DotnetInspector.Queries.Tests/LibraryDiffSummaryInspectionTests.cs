using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using ILInspector.ILDiff;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class LibraryDiffSummaryInspectionTests
{
    static ApiSurfaceProjectionLimits GenerousLimits { get; } =
        new(
            64,
            1_000_000,
            1_000_000,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue);

    [Fact]
    public async Task Execute_ProjectsOnlyHighLevelApiAndBodyCategories()
    {
        LibraryDiffSummary summary = await Execute();

        LibraryDiffSummaryType bodyOnly = Assert.Single(
            summary.Types,
            type => type.Display
                == "LibraryApiDiffFixture.BodyOnlyChange");
        Assert.Equal(
            LibraryDiffCategory.MethodBodyChange,
            bodyOnly.Categories);
        LibraryDiffSummaryMember value =
            Assert.Single(bodyOnly.Members);
        Assert.Equal(
            LibraryDiffCategory.MethodBodyChange,
            value.Categories);
        Assert.Equal("Value", value.After!.Display);

        LibraryDiffSummaryType added = Assert.Single(
            summary.Types,
            type => type.Display
                == "LibraryApiDiffFixture.AddedType");
        Assert.True(
            added.Categories.HasFlag(
                LibraryDiffCategory.ApiAddition));
        Assert.DoesNotContain(
            added.Members,
            member => member.Categories.HasFlag(
                LibraryDiffCategory.MethodBodyChange));

        LibraryDiffSummaryType definitionChange =
            Assert.Single(
                summary.Types,
                type => type.Display
                    == "LibraryApiDiffFixture.TypeDefinitionOnly");
        Assert.True(
            definitionChange.Categories.HasFlag(
                LibraryDiffCategory.ApiChange));
        Assert.Empty(definitionChange.Members);
        Assert.True(summary.Counts.ApiChangeCount > 0);
        Assert.True(summary.Counts.MethodBodyChangeCount > 0);
        Assert.Equal(0, summary.Counts.UnavailableMethodBodyCount);
        Assert.DoesNotContain(
            summary.Types,
            type => type.Display
                == "LibraryApiDiffFixture.AttributeOnlyChange");
    }

    [Fact]
    public async Task Execute_TypeFilterAvoidsUnrelatedSummaryRows()
    {
        const string typeName =
            "LibraryApiDiffFixture.BodyOnlyChange";

        LibraryDiffSummary summary =
            await Execute(new HashSet<string>(
                [typeName],
                StringComparer.Ordinal));

        LibraryDiffSummaryType type =
            Assert.Single(summary.Types);
        Assert.Equal(typeName, type.Identifier);
        Assert.Equal(1, summary.Counts.ChangedTypeCount);
        Assert.Equal(1, summary.Counts.ChangedMemberCount);
        Assert.Equal(1, summary.Counts.MethodBodyChangeCount);
    }

    [Fact]
    public async Task Execute_AttributeScopeIncludesAttributeOnlyChanges()
    {
        const string typeName =
            "LibraryApiDiffFixture.AttributeOnlyChange";

        LibraryDiffSummary summary =
            await Execute(
                new HashSet<string>(
                    [typeName],
                    StringComparer.Ordinal),
                ApiDiffScope.Signature
                    | ApiDiffScope.Attributes);

        LibraryDiffSummaryType type =
            Assert.Single(summary.Types);
        Assert.Equal(typeName, type.Identifier);
        Assert.Equal(
            LibraryDiffCategory.ApiChange,
            type.Categories);
        Assert.Empty(type.Members);
    }

    [Fact]
    public async Task Execute_TypeOnlyChangeContributesToAggregateCount()
    {
        const string typeName =
            "LibraryApiDiffFixture.TypeDefinitionOnly";

        LibraryDiffSummary summary =
            await Execute(new HashSet<string>(
                [typeName],
                StringComparer.Ordinal));

        LibraryDiffSummaryType type = Assert.Single(summary.Types);
        Assert.Equal(LibraryDiffCategory.ApiChange, type.Categories);
        Assert.Empty(type.Members);
        Assert.Equal(1, summary.Counts.ApiChangeCount);
    }

    [Fact]
    public async Task Execute_AddedAccessorIsNotAlsoABodyChange()
    {
        const string typeName =
            "LibraryApiDiffFixture.AccessorAddition";

        LibraryDiffSummary summary =
            await Execute(new HashSet<string>(
                [typeName],
                StringComparer.Ordinal));

        Assert.DoesNotContain(
            summary.Types,
            type => type.Categories.HasFlag(
                LibraryDiffCategory.MethodBodyChange)
                || type.Members.Any(member =>
                    member.Categories.HasFlag(
                        LibraryDiffCategory.MethodBodyChange)));
        Assert.Equal(0, summary.Counts.MethodBodyChangeCount);
    }

    [Fact]
    public async Task Execute_ExceptionRegionChangeIsABodyChange()
    {
        const string typeName =
            "LibraryApiDiffFixture.ExceptionRegionChange";

        LibraryDiffSummary summary =
            await Execute(new HashSet<string>(
                [typeName],
                StringComparer.Ordinal));

        Assert.Equal(0, summary.Counts.UnavailableMethodBodyCount);
        LibraryDiffSummaryType type = Assert.Single(summary.Types);
        Assert.Equal(
            LibraryDiffCategory.MethodBodyChange,
            type.Categories);
        Assert.Equal(
            LibraryDiffCategory.MethodBodyChange,
            Assert.Single(type.Members).Categories);
    }

    [Fact]
    public void IlSummary_SeesExceptionCatchTypeChange()
    {
        using var beforeStream = File.OpenRead(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath());
        using var afterStream = File.OpenRead(
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath());
        using var beforePe = new PEReader(beforeStream);
        using var afterPe = new PEReader(afterStream);
        MetadataReader beforeReader = beforePe.GetMetadataReader();
        MetadataReader afterReader = afterPe.GetMetadataReader();
        MethodDefinition beforeMethod =
            FindMethod(
                beforeReader,
                "ExceptionRegionChange",
                "Value");
        MethodDefinition afterMethod =
            FindMethod(
                afterReader,
                "ExceptionRegionChange",
                "Value");
        MethodBodyBlock beforeBody =
            beforePe.GetMethodBody(beforeMethod.RelativeVirtualAddress);
        MethodBodyBlock afterBody =
            afterPe.GetMethodBody(afterMethod.RelativeVirtualAddress);

        Assert.Single(beforeBody.ExceptionRegions);
        Assert.Single(afterBody.ExceptionRegions);
        IlBodyChangeSummary summary = IlBodyDiff.CompareSummary(
            beforeReader,
            beforeBody,
            afterReader,
            afterBody);

        Assert.Equal(IlBodyChangeSummaryOutcome.Changed, summary.Outcome);
    }

    static async Task<LibraryDiffSummary> Execute(
        IReadOnlySet<string>? typeFilters = null,
        ApiDiffScope diffScope = ApiDiffScope.Signature)
    {
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            Group(
                workspace,
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
                "Before",
                policy);
        using AssemblyContextGroup afterGroup =
            Group(
                workspace,
                FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
                "After",
                policy);
        InspectionEnvelope<LibraryDiffSummaryOutcome> envelope =
            LibraryDiffSummaryInspection.Execute(
                beforeGroup,
                Assert.Single(beforeGroup.Participants),
                afterGroup,
                Assert.Single(afterGroup.Participants),
                GenerousLimits,
                typeFilters,
                diffScope);
        return Assert.IsType<
            LibraryDiffSummaryOutcome.Available>(
                envelope.Content).Summary;
    }

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        string path,
        string provenanceLabel,
        IAssemblyBindingPolicy policy)
        => workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            provenanceLabel)),
                    policy),
            ]);

    static MethodDefinition FindMethod(
        MetadataReader reader,
        string typeName,
        string methodName)
    {
        foreach (TypeDefinitionHandle typeHandle
            in reader.TypeDefinitions)
        {
            TypeDefinition type =
                reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;
            foreach (MethodDefinitionHandle methodHandle
                in type.GetMethods())
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) == methodName)
                    return method;
            }
        }
        throw new InvalidOperationException(
            $"Method {typeName}.{methodName} was not found.");
    }

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
            => new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind
                            .CandidateUnavailable)));
    }
}
