using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Fact]
    public async Task TypeLocator_DiscoveryAttributesSurviveResidentAppendAndProjection()
    {
        byte[] image = LocatorImage("Discovery", metadata =>
        {
            LocatorDefinition(metadata, "N", "Ordinary");
            LocatorDiscoveryAttribute(metadata,
                LocatorDefinition(metadata, "N", "Hidden"), obsolete: false);
            LocatorDiscoveryAttribute(metadata,
                LocatorDefinition(metadata, "N", "Obsolete"), obsolete: true);
            var target = metadata.AddAssemblyReference(
                metadata.GetOrAddString("NotAcquired"), new Version(1, 0, 0, 0),
                default, default, 0, default);
            metadata.AddExportedType((TypeAttributes)0x00200000,
                metadata.GetOrAddString("N"), metadata.GetOrAddString("Target"), target, 0);
        });
        var workspace = new InspectionWorkspace();
        TypeDeclarationLocatorSectionResult.Evaluated projected;
        await using (workspace)
        {
            WorkspaceDeclarationContext first = await LocatorContext(workspace, image);
            WorkspaceDeclarationLocator locator = workspace.GetDeclarationLocator();
            Assert.Equal(0, locator.InventoryReadCount);
            var initial = Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
                await locator.ExecuteAsync([new TypeDeclarationLocatorRequest.Pattern("*")],
                    cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(4, initial.Answers[0].Candidates.Length);
            WorkspaceDeclarationContext second = await LocatorContext(workspace, image);
            await locator.Maintenance.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, locator.InventoryReadCount);

            var current = Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
                await locator.ExecuteAsync([new TypeDeclarationLocatorRequest.Pattern("*")],
                    cancellationToken: TestContext.Current.CancellationToken));
            AssertEquivalent(Locate(CaptureDeclarations(workspace, first, second),
                new TypeDeclarationLocatorRequest.Pattern("*")), current);
            Assert.True(current.Answers[0].IsComplete);
            Assert.Equal(8, current.Answers[0].Candidates.Length);
            Assert.Equal(2, locator.InventoryReadCount);

            projected = Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                TypeDeclarationLocatorSection.Project(current, TypeDeclarationLocatorSectionPlan.All));
            foreach (var (candidate, row) in current.Answers[0].Candidates.Zip(projected.Answers[0].Candidates))
                Assert.Same(candidate.DiscoveryAttributes, row.DiscoveryAttributes);
        }

        var candidates = projected.Answers[0].Candidates;
        foreach (var group in candidates.GroupBy(candidate => candidate.Name))
        {
            Assert.Equal(2, group.Count());
            Assert.Equal(2, group.Select(candidate => candidate.Observation.ContextOrder).Distinct().Count());
        }
        Assert.All(candidates.Where(candidate => candidate.Name.Segments[0] == "Ordinary"),
            candidate => Assert.Equal(new TypeDeclarationDiscoveryAttributes(false, false), candidate.DiscoveryAttributes));
        Assert.All(candidates.Where(candidate => candidate.Name.Segments[0] == "Hidden"),
            candidate => Assert.Equal(new TypeDeclarationDiscoveryAttributes(true, false), candidate.DiscoveryAttributes));
        Assert.All(candidates.Where(candidate => candidate.Name.Segments[0] == "Obsolete"),
            candidate => Assert.Equal(new TypeDeclarationDiscoveryAttributes(false, true), candidate.DiscoveryAttributes));
        Assert.All(candidates.Where(candidate => candidate.Name.Segments[0] == "Target"),
            candidate => Assert.Null(candidate.DiscoveryAttributes));
        foreach (bool compact in new[] { false, true })
        {
            using JsonDocument document = JsonDocument.Parse(
                TypeDeclarationLocatorSectionJson.Serialize(projected, compact));
            JsonElement rows = document.RootElement.GetProperty("answers")[0].GetProperty("candidates");
            Assert.Equal(8, rows.GetArrayLength());
            foreach (var (row, candidate) in rows.EnumerateArray().Zip(candidates))
            {
                if (candidate.DiscoveryAttributes is { } facts)
                {
                    JsonElement attributes = row.GetProperty("discovery_attributes");
                    Assert.Equal(facts.IsEditorBrowsableNever, attributes.GetProperty("is_editor_browsable_never").GetBoolean());
                    Assert.Equal(facts.IsObsolete, attributes.GetProperty("is_obsolete").GetBoolean());
                }
                else
                {
                    Assert.False(row.TryGetProperty("discovery_attributes", out _));
                }
            }
        }
    }

    [Fact]
    public async Task TypeLocator_MalformedDiscoveryAttributesKeepAttributedIncompleteEvidence()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(workspace,
            LocatorImage("Healthy", metadata => LocatorDefinition(metadata, "N", "Healthy")),
            LocatorImage("Malformed", metadata =>
            {
                LocatorDefinition(metadata, "N", "NotReturned");
                LocatorDiscoveryAttribute(metadata,
                    LocatorDefinition(metadata, "N", "Malformed"),
                    obsolete: false, malformed: true);
            }));
        var resident = Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            await workspace.GetDeclarationLocator().ExecuteAsync(
                [new TypeDeclarationLocatorRequest.Pattern("*")],
                cancellationToken: TestContext.Current.CancellationToken));
        AssertEquivalent(Locate(CaptureDeclarations(workspace, context),
            new TypeDeclarationLocatorRequest.Pattern("*")), resident);
        Assert.Equal("Healthy", Assert.Single(resident.Answers[0].Candidates).Name.Segments[0]);
        Assert.True(resident.Answers[0].IsRealizationComplete);
        Assert.False(resident.Answers[0].IsEvaluationComplete);
        var rejected = Assert.Single(resident.Members.OfType<TypeDeclarationLocatorMemberOutcome.InventoryRejected>());
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
        Assert.Equal("Malformed", rejected.Member.AssemblyIdentity.Name);
    }

    static void LocatorDiscoveryAttribute(
        MetadataBuilder metadata, TypeDefinitionHandle type, bool obsolete, bool malformed = false)
    {
        var scope = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"), new Version(10, 0, 0, 0),
            default, default, 0, default);
        var attribute = metadata.AddTypeReference(scope,
            metadata.GetOrAddString(obsolete ? "System" : "System.ComponentModel"),
            metadata.GetOrAddString(obsolete ? "ObsoleteAttribute" : "EditorBrowsableAttribute"));
        var signature = new BlobBuilder();
        if (obsolete)
        {
            new BlobEncoder(signature).MethodSignature(isInstanceMethod: true)
                .Parameters(0, returnType => returnType.Void(), _ => { });
        }
        else
        {
            var state = metadata.AddTypeReference(scope,
                metadata.GetOrAddString("System.ComponentModel"), metadata.GetOrAddString("EditorBrowsableState"));
            new BlobEncoder(signature).MethodSignature(isInstanceMethod: true)
                .Parameters(1, returnType => returnType.Void(),
                    parameters => parameters.AddParameter().Type().Type(state, isValueType: true));
        }
        var constructor = metadata.AddMemberReference(
            attribute, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(signature));
        byte[] value = obsolete ? [1, 0, 0, 0] : [1, 0, 1, 0, 0, 0, 0, 0];
        if (malformed)
            value[0] = 0;
        metadata.AddCustomAttribute(type, constructor, metadata.GetOrAddBlob(value));
    }
}
