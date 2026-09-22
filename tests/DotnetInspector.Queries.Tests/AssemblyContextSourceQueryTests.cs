using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;

using DotnetInspector.Packages;
using DotnetInspector.Fixtures;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Decompiler;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using Pipeline = ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    static readonly Guid SourceLinkKind =
        new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    [Fact]
    public void RequestFromLegacyApiType_RequiresUnambiguousMetadataName()
    {
        var simple = new ApiType
        {
            Namespace = "Sample",
            Name = "Widget",
            MetadataName = "Widget",
        };
        var ambiguous = new ApiType
        {
            Namespace = "Sample",
            Name = "Inner",
            MetadataName = "Outer+Inner",
        };

        AssemblyTypeSourceRequest request =
            AssemblyTypeSourceRequest.From(simple);

        Assert.Equal(
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Widget"]))
                .Name,
            request.Type);
        Assert.Throws<ArgumentException>(
            () => AssemblyTypeSourceRequest.From(ambiguous));
    }

    [Fact]
    public async Task PathlessMember_AcquiresVerifiedPdbSource()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes());
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                result);
        var pdbSource =
            Assert.IsType<AssemblyMemberSource.Pdb>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            pdbSource.Text,
            StringComparison.Ordinal);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            pdbSource.Inspection.ChecksumVerification);
        Assert.NotNull(pdbSource.Inspection.Mapping);
        Assert.NotNull(pdbSource.Inspection.Document);
        Assert.Null(assembly.Assembly.Path);
        Assert.NotEmpty(host.SymbolRequests);
        Assert.NotEmpty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
        Assert.IsType<
            AssemblyImageAccessResult<int>.Available>(
                group.UseAssemblySession(
                    assembly.Assembly,
                    static session =>
                        session.ApiSurface().Types.Count));
    }

    [Fact]
    public async Task LocalPdbSource_DoesNotRequireSourceLinkMap()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] pdbBytes =
            RemoveSourceLinkCustomDebugInformation(
                assembly.PdbPath);
        using var host =
            QueryHost.WithPdb(
                Path.GetFileName(assembly.PdbPath),
                pdbBytes,
                sourceBytes: [],
                allowLocalSourceReads: true);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry memberResult =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken);
        AssemblyTypeSourceEntry typeResult =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);

        var member =
            Assert.IsType<AssemblyMemberSource.Pdb>(
                Assert.IsType<
                    AssemblyMemberSourceEntry.Available>(
                        memberResult)
                    .Source);
        var type =
            Assert.IsType<AssemblyTypeSource.Pdb>(
                Assert.IsType<
                    AssemblyTypeSourceEntry.Available>(
                        typeResult)
                    .Source);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            member.Inspection.ChecksumVerification);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            type.Inspection.ChecksumVerification);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public async Task BodylessType_AcquiresInferredChecksumVerifiedPdbSource()
    {
        byte[] image = File.ReadAllBytes(
            typeof(BodylessSourceFixture).Assembly.Location);
        TestAssembly assembly = TestAssembly.Create(image);
        using var host = QueryHost.WithSource(
            File.ReadAllBytes(
                Path.Combine(
                    FindRepositoryRoot(),
                    "fixtures",
                    "queries",
                    "DotnetInspector.Queries.EmbeddedFixtures",
                    nameof(BodylessSourceFixture) + ".cs")));
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    nameof(BodylessSourceFixture)),
                host.Context,
                TestContext.Current.CancellationToken);

        var source =
            Assert.IsType<AssemblyTypeSource.Pdb>(
                Assert.IsType<AssemblyTypeSourceEntry.Available>(
                        result)
                    .Source);
        Assert.Contains(
            "public interface BodylessSourceFixture",
            source.Text,
            StringComparison.Ordinal);
        Assert.Equal(
            SourceLinkResolver.SourceResolutionMethod.Inferred,
            Assert.Single(source.Inspection.Mapping!.Documents).ResolutionMethod);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            source.Inspection.ChecksumVerification);
        Assert.Empty(host.SymbolRequests);
        Assert.Single(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task BodylessType_UnsuccessfulSourceResponseFallsBackToDecompiler(
        HttpStatusCode statusCode)
    {
        byte[] image = File.ReadAllBytes(
            typeof(BodylessSourceFixture).Assembly.Location);
        TestAssembly assembly = TestAssembly.Create(image);
        using var host = QueryHost.WithUnavailableSource(statusCode);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    nameof(BodylessSourceFixture)),
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                result);
        var source =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                available.Source);
        var house =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                available.DecompilationHouseOutcome);
        Assert.Contains(
            "interface BodylessSourceFixture",
            source.Text,
            StringComparison.Ordinal);
        Assert.False(source.PdbAttempt.IsComplete);
        Assert.Same(source.Decompilation, house.Attempt);
        Assert.IsType<SourceHouseTarget.TypeTarget>(
            house.Request.Target);
        Assert.NotEmpty(host.SourceRequests);
    }

    [Theory]
    [InlineData("MemorySafetyExtensionEnum")]
    [InlineData("MemorySafetyExtensionDelegate")]
    [InlineData("IMemorySafetyExtensionInterface")]
    [InlineData("MemorySafetyAbstractFixture")]
    public async Task BodylessType_UnsupportedMemorySafetyModeRemainsUnavailable(
        string typeName)
    {
        TestAssembly assembly =
            TestAssembly.Create(UnsupportedMemorySafetyImage());
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(typeName),
                host.Context,
                TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(result);
        Assert.NotNull(unavailable.DecompiledAttempt);
        Assert.Contains(
            unavailable.DecompiledAttempt.Projection.Diagnostics,
            diagnostic => diagnostic.Id
                == DiagnosticIds.MemorySafetyModeUnavailable);
    }

    [Fact]
    public async Task AbstractMember_UnsupportedMemorySafetyModeRemainsUnavailable()
    {
        const string TypeName = "MemorySafetyAbstractFixture";
        TestAssembly assembly =
            TestAssembly.Create(UnsupportedMemorySafetyImage());
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest("Read", TypeName),
                host.Context,
                TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(result);
        Assert.Equal(
            CSharpDecompilationStatus.Failed,
            unavailable.DecompiledAttempt?.Status);
        Assert.Null(unavailable.DecompiledAttempt?.Text);
        Assert.Contains(
            unavailable.DecompiledAttempt!.Projection.Diagnostics,
            diagnostic => diagnostic.Id
                == DiagnosticIds.MemorySafetyModeUnavailable);
    }

    [Fact]
    public async Task AmbiguousBodylessTypeSourceInferenceFallsBackToDecompiler()
    {
        Type selectedType =
            typeof(
                global::DotnetInspector.Queries.EmbeddedFixtures
                    .BodylessSourceCollision.Right
                    .AmbiguousBodylessFixture);
        byte[] image = File.ReadAllBytes(selectedType.Assembly.Location);
        TestAssembly assembly = TestAssembly.Create(image);
        using var host =
            QueryHost.WithUnavailableSource(HttpStatusCode.NotFound);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    selectedType.Namespace!,
                    [selectedType.Name]))
            .Name;

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                new AssemblyTypeSourceRequest(typeName),
                host.Context,
                TestContext.Current.CancellationToken);

        var source =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                Assert.IsType<AssemblyTypeSourceEntry.Available>(
                        result)
                    .Source);
        Assert.Contains(
            "interface AmbiguousBodylessFixture",
            source.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "no portable-PDB source mapping",
            Assert.IsType<FindingInspection<string>.Absent>(
                    source.PdbAttempt.Lines.Value)
                .Detail,
            StringComparison.Ordinal);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public async Task UnresolvedPdbSource_FallsBackToDecompiler()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                result);
        var decompiled =
            Assert.IsType<AssemblyMemberSource.Decompiled>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            decompiled.Text,
            StringComparison.Ordinal);
        var failed = Assert.IsType<FindingInspection<string>.Failed>(
            decompiled.PdbAttempt.Lines.Value);
        Assert.Contains("remains unresolved", failed.Error.Reason);
        Assert.Empty(host.SourceRequests);
        Assert.True(assembly.Policy.SelectionCount > 0);
    }

    [Fact]
    public async Task PdbSourceIntegrityFailure_IsPreservedBesideDecompiler()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            "not the compiled source"u8.ToArray());
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                result);
        var decompiled =
            Assert.IsType<AssemblyMemberSource.Decompiled>(
                available.Source);
        Assert.IsType<FindingInspection<string>.Failed>(
            decompiled.PdbAttempt.Lines.Value);
        Assert.Equal(
            SourceChecksumVerification.Mismatch,
            decompiled.PdbAttempt.ChecksumVerification);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            decompiled.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MemberComparison_DeclaresModeratedCost()
    {
        Assert.Equal(
            InspectionCost.Moderated,
            AssemblyContextSourceComparisonQuery.Definition.Cost);
    }

    [Fact]
    public async Task MemberComparison_ReturnsBothCompleteEndpoints()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes());
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Available>(
                    result);
        var pdb =
            Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(
                available.Pdb);
        var decompiled =
            Assert.IsType<
                AssemblyMemberDecompiledSourceAttempt.Available>(
                    available.Decompiled);
        Assert.Same(request, available.Request);
        Assert.Equal(
            assembly.Participant.Assembly.Registration,
            available.Subject.Registration);
        Assert.Equal(
            request.MetadataToken,
            pdb.Inspection.Mapping!.MetadataToken);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            pdb.Inspection.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            decompiled.Result.Text,
            StringComparison.Ordinal);
        Assert.True(decompiled.Result.PdbSupplied);
        Assert.NotEmpty(decompiled.Result.BodyProjections);
        Assert.True(decompiled.Result.BodyProjectionsAttempted > 0);
        Assert.True(assembly.Policy.SelectionCount > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberComparison_AdjacentPdbRequiresExplicitCapability(
        bool allowAdjacentPdbReads)
    {
        TestAssembly assembly =
            TestAssembly.Create(retainPath: true);
        using var host = QueryHost.WithoutPdb(
            allowLocalSourceReads: true,
            allowAdjacentPdbReads: allowAdjacentPdbReads);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Available>(
                    result);
        if (allowAdjacentPdbReads)
        {
            Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(
                available.Pdb);
            Assert.Empty(host.SymbolRequests);
        }
        else
        {
            Assert.IsType<AssemblyMemberPdbSourceAttempt.Unavailable>(
                available.Pdb);
            Assert.NotEmpty(host.SymbolRequests);
        }
        var decompiled = Assert.IsType<
            AssemblyMemberDecompiledSourceAttempt.Available>(
                available.Decompiled);
        Assert.Equal(allowAdjacentPdbReads, decompiled.Result.PdbSupplied);
        if (!allowAdjacentPdbReads)
            Assert.Equal(DecompilerSymbolSource.None, decompiled.Result.Symbols);
    }

    [Theory]
    [InlineData(nameof(SourceFixture.Count), "set_Count")]
    [InlineData(nameof(SourceFixture.Changed), "add_Changed")]
    public async Task MemberComparison_ResolvesPhysicalAccessor(
        string ownerName,
        string accessorName)
    {
        TestAssembly assembly = TestAssembly.Create();
        (ApiType type, ApiMember owner) =
            assembly.MemberTarget(ownerName);
        ApiMember accessor = Assert.Single(
            ApiMemberAccessors.Create(owner, type),
            candidate => candidate.Name == accessorName);
        AssemblyMemberSourceRequest request =
            AssemblyMemberSourceRequest.From(type, accessor);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes());
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Available>(
                    result);
        var pdb =
            Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(
                available.Pdb);
        var decompiled =
            Assert.IsType<
                AssemblyMemberDecompiledSourceAttempt.Available>(
                    available.Decompiled);
        Assert.Equal(
            request.MetadataToken,
            pdb.Inspection.Mapping!.MetadataToken);
        Assert.Contains(
            ownerName,
            pdb.Inspection.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            accessorName,
            decompiled.Result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberComparison_ResolvesProjectedExtensionMethod()
    {
        TestAssembly assembly = TestAssembly.Create();
        (ApiType type, ApiMember projected) =
            assembly.MemberTarget(
                nameof(SourceProjectionExtensions.ProjectedIncrement),
                nameof(SourceProjectionTarget));
        Assert.Equal("extension-method", projected.Kind);
        AssemblyMemberSourceRequest request =
            AssemblyMemberSourceRequest.From(type, projected);
        Assert.Equal(
            projected.DeclaringTypeDefinitionName,
            request.Type);
        Assert.Equal(
            projected.DeclaringTypeCanonicalName,
            request.Member.TypeFullName);
        Assert.StartsWith(
            projected.Name + "~",
            request.Member.StableSelector,
            StringComparison.Ordinal);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes());
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Available>(
                    result);
        Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(
            available.Pdb);
        var decompiled =
            Assert.IsType<
                AssemblyMemberDecompiledSourceAttempt.Available>(
                    available.Decompiled);
        Assert.Contains(
            nameof(SourceProjectionExtensions.ProjectedIncrement),
            decompiled.Result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberComparison_ResolvesProjectedExtensionOperator()
    {
        TestAssembly assembly = TestAssembly.Create();
        ApiType type =
            assembly.TypeTarget(nameof(InventoryFixture));
        ApiMember projected = Assert.Single(
            type.Members,
            candidate =>
                candidate.Name == "op_Addition"
                && candidate.Kind == "extension-method");
        Assert.Equal("extension-method", projected.Kind);
        AssemblyMemberSourceRequest request =
            AssemblyMemberSourceRequest.From(type, projected);
        Assert.StartsWith(
            "operator:op_Addition~",
            request.Member.StableSelector,
            StringComparison.Ordinal);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            File.ReadAllBytes(
                Path.Combine(
                    Path.GetDirectoryName(SourceFileBytesPath())!,
                    "ApiInventoryQueryTests.cs")));
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Available>(
                    result);
        Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(
            available.Pdb);
        var decompiled =
            Assert.IsType<
                AssemblyMemberDecompiledSourceAttempt.Available>(
                    available.Decompiled);
        Assert.Contains(
            "operator +",
            decompiled.Result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberComparison_PdbFailureDoesNotSuppressDecompilation()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            "not the compiled source"u8.ToArray());
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Available>(
                    result);
        var pdb =
            Assert.IsType<AssemblyMemberPdbSourceAttempt.Unavailable>(
                available.Pdb);
        var decompiled =
            Assert.IsType<
                AssemblyMemberDecompiledSourceAttempt.Available>(
                    available.Decompiled);
        Assert.Equal(
            PdbMemberSourceOutcome.ChecksumMismatch,
            pdb.Inspection.Outcome);
        Assert.Equal(
            SourceChecksumVerification.Mismatch,
            pdb.Inspection.ChecksumVerification);
        Assert.True(decompiled.Result.PdbSupplied);
        Assert.NotEmpty(decompiled.Result.BodyProjections);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            decompiled.Result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberComparison_DecompilationFailurePreservesPdb()
    {
        TestAssembly original = TestAssembly.Create();
        int metadataToken =
            typeof(SourceFixture)
                .GetMethod(nameof(SourceFixture.Describe))!
                .MetadataToken;
        byte[] bytes =
            CorruptMethodBody(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location),
                metadataToken);
        TestAssembly assembly =
            TestAssembly.CreatePackage(
                bytes,
                original.PdbPath);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes());
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Available>(
                    result);
        Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(
            available.Pdb);
        var unavailable =
            Assert.IsType<
                AssemblyMemberDecompiledSourceAttempt.Unavailable>(
                    available.Decompiled);
        Assert.Equal(
            CSharpDecompilationStatus.Failed,
            unavailable.Status);
        CSharpBodyProjection body = Assert.Single(
            unavailable.Result.BodyProjections,
            projection => projection.Address.Token == metadataToken);
        Assert.NotEqual(DecompilationFidelity.Full, body.Projection.Fidelity);
        Assert.NotEmpty(body.Projection.Diagnostics);
        Assert.All(body.Projection.Diagnostics, diagnostic =>
            Assert.Contains(diagnostic.ToString(), unavailable.FailureDetail, StringComparison.Ordinal));
        Assert.Null(
            typeof(
                AssemblyMemberDecompiledSourceAttempt.Unavailable)
                .GetProperty("Text"));
    }

    [Fact]
    public async Task MemberComparison_NeitherEndpointAvailableIsExplicit()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceDelegate.Invoke),
                    nameof(SourceDelegate)),
                host.Context,
                TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Unavailable>(
                    result);
        Assert.Equal(
            PdbMemberSourceOutcome.PortablePdbUnavailable,
            unavailable.Pdb.Inspection.Outcome);
        Assert.Equal(
            CSharpDecompilationStatus.Absent,
            unavailable.Decompiled.Status);
        Assert.NotEmpty(unavailable.Decompiled.FailureDetail);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MemberComparison_MismatchedExactTargetIsNotFound(
        int mismatch)
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest existing =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        AssemblyMemberSourceRequest other =
            assembly.MemberRequest(nameof(SourceFixture.Increment));
        MetadataTypeDefinitionName missingType =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Definitely",
                    ["Missing"]))
                .Name;
        AssemblyMemberSourceRequest request =
            mismatch switch
            {
                0 => new AssemblyMemberSourceRequest(
                    missingType,
                    existing.Member,
                    existing.MetadataToken),
                1 => new AssemblyMemberSourceRequest(
                    existing.Type,
                    other.Member,
                    existing.MetadataToken),
                2 => new AssemblyMemberSourceRequest(
                    existing.Type,
                    existing.Member,
                    other.MetadataToken),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(mismatch)),
            };
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var notFound =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.NotFound>(
                    result);
        Assert.Equal(
            AssemblySourceFailureKind.TargetNotFound,
            notFound.Failure.Kind);
        Assert.Empty(host.SymbolRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Fact]
    public async Task MemberComparison_ResolutionStateFailureIsFailed()
    {
        byte[] bytes =
            File.ReadAllBytes(
                typeof(AssemblyContextSourceQueryTests)
                    .Assembly.Location);
        TestAssembly requestSource =
            TestAssembly.Create(bytes);
        var policy = new FrameworkBindingPolicy();
        var assembly =
            ResolvedAssemblyReference.Create(
                ReadIdentity(bytes),
                path: null,
                () =>
                {
                    policy.ChangeVersion();
                    return new MemoryStream(
                        bytes,
                        writable: false);
                },
                AssemblyResolutionProvenance.Local(
                    "source comparison resolution state failure"));
        var participant =
            new AssemblyContextParticipant(
                assembly,
                policy);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                participant,
                requestSource.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Failed>(
                    result);
        Assert.Equal(
            AssemblySourceFailureKind.InspectionFailed,
            failed.Failure.Kind);
        Assert.IsType<InvalidOperationException>(
            failed.Failure.Error);
        Assert.Empty(host.SymbolRequests);
        Assert.Equal(0, policy.SelectionCount);
    }

    [Fact]
    public async Task MemberComparison_RetainedImageRejectionIsRejected()
    {
        TestAssembly assembly =
            TestAssembly.Create(
                selectedName: "Different.Identity");
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken);

        var rejected =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Rejected>(
                    result);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberComparison_CancellationDuringEitherAttemptAborts(
        bool duringDecompilation)
    {
        TestAssembly assembly = TestAssembly.Create();
        if (duringDecompilation)
            assembly.Policy.CancelSelection = true;
        using QueryHost host =
            duringDecompilation
                ? QueryHost.WithPdb(
                    assembly.PdbPath,
                    SourceFileBytes())
                : QueryHost.WithPdb(
                    assembly.PdbPath,
                    SourceFileBytes(),
                    pdbStore: new CancelingPdbStore());
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberComparison_BindingInvalidationDuringEitherAttemptFails(
        bool duringDecompilation)
    {
        TestAssembly assembly = TestAssembly.Create();
        if (duringDecompilation)
        {
            assembly.Policy.BeforeSelection =
                assembly.Policy.ChangeVersion;
        }
        using QueryHost host =
            duringDecompilation
                ? QueryHost.WithPdb(
                    assembly.PdbPath,
                    SourceFileBytes())
                : QueryHost.WithPdb(
                    assembly.PdbPath,
                    SourceFileBytes(),
                    pdbStore: new ThrowingPdbStore(
                        assembly.Policy.ChangeVersion));
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Failed>(
                    result);
        Assert.Equal(
            AssemblySourceFailureKind.InspectionFailed,
            failed.Failure.Kind);
        Assert.IsType<InvalidOperationException>(
            failed.Failure.Error);
    }

    [Fact]
    public async Task MemberComparison_ForeignBindingSnapshotFails()
    {
        TestAssembly assembly = TestAssembly.Create();
        assembly.Policy.SnapshotVersion =
            new AssemblyBindingPolicyVersion();
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes());
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceComparisonEntry result =
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<AssemblyMemberSourceComparisonEntry.Failed>(
                result);
        Assert.IsType<InvalidOperationException>(
            failed.Failure.Error);
    }

    [Fact]
    public async Task PathlessType_AcquiresVerifiedPdbDocument()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(typeof(SourceFixture).Name);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes());
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                result);
        var pdbSource =
            Assert.IsType<AssemblyTypeSource.Pdb>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture),
            pdbSource.Text,
            StringComparison.Ordinal);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            pdbSource.Inspection.ChecksumVerification);
    }

    [Fact]
    public async Task UnresolvedPdbSourceForType_FallsBackToDecompiler()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(typeof(SourceFixture).Name);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                result);
        var decompiled =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture),
            decompiled.Text,
            StringComparison.Ordinal);
        Assert.True(decompiled.Decompilation.IsAvailable);
        Assert.False(decompiled.Decompilation.PdbSupplied);
        Assert.NotEmpty(decompiled.Decompilation.BodyProjections);
        var house =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                available.DecompilationHouseOutcome);
        Assert.Same(decompiled.Decompilation, house.Attempt);
        Assert.Equal(
            SourceHousePdbContributionKind.Unavailable,
            house.PdbContribution.Kind);
        Assert.Equal(
            SourceHouseLibraryLeaseConsumer.SourceHouse,
            house.LeaseSettlement.Consumer);
        var failed = Assert.IsType<FindingInspection<string>.Failed>(
            decompiled.PdbAttempt.Lines.Value);
        Assert.Contains("remains unresolved", failed.Error.Reason);
    }

    [Fact]
    public async Task DecompilerBudgetRemainsTypedForMemberAndTypeFallback()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithoutPdb(maxDecompilerBodyProjections: 0);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([assembly.Participant]);

        var member = Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group, assembly.Participant,
                assembly.MemberRequest(nameof(SourceFixture.Describe)), host.Context,
                TestContext.Current.CancellationToken));
        var type = Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group, assembly.Participant,
                assembly.TypeRequest(typeof(SourceFixture).Name), host.Context,
                TestContext.Current.CancellationToken));

        foreach (CSharpDecompilationAttempt attempt in
            new[] { member.DecompiledAttempt!, type.DecompiledAttempt! })
        {
            Assert.Equal(CSharpDecompilationStatus.Incomplete, attempt.Status);
            Assert.Equal(0, attempt.BodyProjectionsAttempted);
            Assert.Null(attempt.Text);
            Assert.NotEmpty(attempt.Projection.Diagnostics);
        }
        Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
            member.DecompilationHouseOutcome);
        Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
            type.DecompilationHouseOutcome);
    }

    [Fact]
    public async Task DecompilerBudgetDoesNotSuppressAvailableAuthoredSource()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithPdb(
            assembly.PdbPath, SourceFileBytes(), maxDecompilerBodyProjections: 0);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([assembly.Participant]);
        AssemblyMemberSourceRequest request = assembly.MemberRequest(nameof(SourceFixture.Describe));

        var source = Assert.IsType<AssemblyMemberSourceEntry.Available>(
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group, assembly.Participant, request, host.Context,
                TestContext.Current.CancellationToken));
        Assert.IsType<AssemblyMemberSource.Pdb>(source.Source);

        var comparison = Assert.IsType<AssemblyMemberSourceComparisonEntry.Available>(
            await AssemblyContextSourceComparisonQuery.ExecuteAsync(
                group, assembly.Participant, request, host.Context,
                TestContext.Current.CancellationToken));
        Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(comparison.Pdb);
        var unavailable = Assert.IsType<AssemblyMemberDecompiledSourceAttempt.Unavailable>(
            comparison.Decompiled);
        Assert.Equal(CSharpDecompilationStatus.Incomplete, unavailable.Result.Status);
        Assert.True(unavailable.Result.PdbSupplied);
        Assert.NotEmpty(unavailable.FailureDetail);
    }

    [Fact]
    public async Task DecompilerFallback_AppliesRequestPrinterOptions()
    {
        TestAssembly assembly = TestAssembly.Create();
        var options = new Pipeline.PrinterOptions
        {
            WrapExpressionBodyArrow = true,
        };
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry memberResult =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe),
                    printerOptions: options),
                host.Context,
                TestContext.Current.CancellationToken);
        AssemblyTypeSourceEntry typeResult =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name,
                    options),
                host.Context,
                TestContext.Current.CancellationToken);

        Assert.Contains(
            "\n        =>",
            Assert.IsType<AssemblyMemberSource.Decompiled>(
                Assert.IsType<AssemblyMemberSourceEntry.Available>(
                    memberResult)
                    .Source)
                .Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "\n        =>",
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                Assert.IsType<AssemblyTypeSourceEntry.Available>(
                    typeResult)
                    .Source)
                .Text,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecompilerFallback_IgnoresAmbientSidecarPath(
        bool typeQuery)
    {
        TestAssembly pathless = TestAssembly.Create();
        TestAssembly pathful =
            TestAssembly.Create(retainPath: true);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();

        Assert.Null(pathless.Assembly.Path);
        Assert.NotNull(pathful.Assembly.Path);
        Assert.Equal(
            await DecompileAsync(pathless),
            await DecompileAsync(pathful));

        async Task<string> DecompileAsync(
            TestAssembly assembly)
        {
            AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    [assembly.Participant]);
            if (typeQuery)
            {
                AssemblyTypeSourceEntry typeResult =
                    await AssemblyContextSourceQuery
                        .ExecuteTypeAsync(
                            group,
                            assembly.Participant,
                            assembly.TypeRequest(
                                nameof(
                                    AssemblyContextSourceQueryTests)),
                            host.Context,
                            TestContext.Current.CancellationToken);
                return Assert.IsType<AssemblyTypeSource.Decompiled>(
                        Assert.IsType<
                            AssemblyTypeSourceEntry.Available>(
                                typeResult)
                            .Source)
                    .Text;
            }

            AssemblyMemberSourceEntry memberResult =
                await AssemblyContextSourceQuery
                    .ExecuteMemberAsync(
                        group,
                        assembly.Participant,
                        assembly.MemberRequest(
                            nameof(FindMetadataStreamOffset),
                            nameof(
                                AssemblyContextSourceQueryTests)),
                        host.Context,
                        TestContext.Current.CancellationToken);
            return Assert.IsType<AssemblyMemberSource.Decompiled>(
                    Assert.IsType<
                        AssemblyMemberSourceEntry.Available>(
                            memberResult)
                        .Source)
                .Text;
        }
    }

    [Fact]
    public async Task PreCanceledQueries_StopBeforeSnapshotAndDecompilerFallback()
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                cancellation.Token));

        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SnapshotPrimaryFailure_IsNotMaskedByCleanupFailure(
        bool memberQuery,
        bool fatalFailure)
    {
        Exception primaryFailure =
            fatalFailure
                ? new OutOfMemoryException(
                    "Synthetic fatal snapshot failure.")
                : new OperationCanceledException(
                    "Synthetic snapshot cancellation.");
        var cleanupFailure =
            new HttpRequestException(
                "Synthetic snapshot cleanup failure.");
        PrimaryAndCleanupFailureStream? opened = null;
        TestAssembly assembly =
            TestAssembly.Create(
                openRead: () =>
                {
                    opened =
                        new PrimaryAndCleanupFailureStream(
                            File.ReadAllBytes(
                                typeof(
                                    AssemblyContextSourceQueryTests)
                                    .Assembly.Location),
                            primaryFailure,
                            cleanupFailure);
                    return opened;
                });
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        Func<Task> operation =
            memberQuery
                ? () => AssemblyContextSourceQuery.ExecuteMemberAsync(
                    group,
                    assembly.Participant,
                    assembly.MemberRequest(
                        nameof(SourceFixture.Describe)),
                    host.Context,
                    TestContext.Current.CancellationToken)
                : () => AssemblyContextSourceQuery.ExecuteTypeAsync(
                    group,
                    assembly.Participant,
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name),
                    host.Context,
                    TestContext.Current.CancellationToken);

        Exception error =
            fatalFailure
                ? await Assert.ThrowsAsync<OutOfMemoryException>(
                    operation)
                : await Assert.ThrowsAsync<OperationCanceledException>(
                    operation);

        Assert.Same(primaryFailure, error);
        Assert.Equal(1, Assert.IsType<
            PrimaryAndCleanupFailureStream>(opened).DisposeCount);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SnapshotAcquisitionStateChange_PrecedesEarlyTargetNotFound(
        bool memberQuery,
        bool rotatePolicy)
    {
        byte[] bytes =
            File.ReadAllBytes(
                typeof(AssemblyContextSourceQueryTests)
                    .Assembly.Location);
        TestAssembly requestSource =
            TestAssembly.Create(bytes);
        var policy = new FrameworkBindingPolicy();
        using var openerEntered =
            new ManualResetEventSlim();
        using var openerRelease =
            new ManualResetEventSlim();
        var assembly =
            ResolvedAssemblyReference.Create(
                ReadIdentity(bytes),
                path: null,
                () =>
                {
                    openerEntered.Set();
                    Assert.True(
                        openerRelease.Wait(
                            TimeSpan.FromSeconds(10)),
                        "Timed out waiting for the snapshot state change.");
                    return new MemoryStream(
                        bytes,
                        writable: false);
                },
                AssemblyResolutionProvenance.Local(
                    "source query snapshot race"));
        var participant =
            new AssemblyContextParticipant(
                assembly,
                policy);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [participant]);
        using var cancellation =
            new CancellationTokenSource();
        Task actor = Task.Run(
            () =>
            {
                Assert.True(
                    openerEntered.Wait(
                        TimeSpan.FromSeconds(10)),
                    "Timed out waiting for snapshot acquisition.");
                if (rotatePolicy)
                    policy.ChangeVersion();
                else
                    cancellation.Cancel();
                openerRelease.Set();
            },
            TestContext.Current.CancellationToken);
        MetadataTypeDefinitionName missingType =
            Assert.IsType<
                MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "Definitely",
                        ["Missing"]))
                .Name;

        if (rotatePolicy)
        {
            Exception error;
            if (memberQuery)
            {
                AssemblyMemberSourceRequest sourceRequest =
                    requestSource.MemberRequest(
                        nameof(SourceFixture.Describe));
                var request =
                    new AssemblyMemberSourceRequest(
                        missingType,
                        sourceRequest.Member,
                        sourceRequest.MetadataToken);
                var unavailable =
                    Assert.IsType<
                        AssemblyMemberSourceEntry.Unavailable>(
                            await AssemblyContextSourceQuery
                                .ExecuteMemberAsync(
                                    group,
                                    participant,
                                    request,
                                    host.Context,
                                    cancellation.Token));
                error = unavailable.Failure.Error!;
            }
            else
            {
                var unavailable =
                    Assert.IsType<
                        AssemblyTypeSourceEntry.Unavailable>(
                            await AssemblyContextSourceQuery
                                .ExecuteTypeAsync(
                                    group,
                                    participant,
                                    new AssemblyTypeSourceRequest(
                                        missingType),
                                    host.Context,
                                    cancellation.Token));
                error = unavailable.Failure.Error!;
            }

            Assert.IsType<InvalidOperationException>(error);
        }
        else if (memberQuery)
        {
            AssemblyMemberSourceRequest sourceRequest =
                requestSource.MemberRequest(
                    nameof(SourceFixture.Describe));
            var request =
                new AssemblyMemberSourceRequest(
                    missingType,
                    sourceRequest.Member,
                    sourceRequest.MetadataToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => AssemblyContextSourceQuery
                    .ExecuteMemberAsync(
                        group,
                        participant,
                        request,
                        host.Context,
                        cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => AssemblyContextSourceQuery
                    .ExecuteTypeAsync(
                        group,
                        participant,
                        new AssemblyTypeSourceRequest(
                            missingType),
                        host.Context,
                        cancellation.Token));
        }

        await actor;
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, policy.SelectionCount);
    }

    [Fact]
    public async Task SameDescriptorForeignParticipant_IsRejectedBeforeCancellation()
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        var foreignPolicy =
            new FrameworkBindingPolicy();
        var foreign =
            new AssemblyContextParticipant(
                assembly.Assembly,
                foreignPolicy);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);
        using var cancellation = new CancellationTokenSource();

        foreach (bool canceled in new[] { false, true })
        {
            if (canceled)
                cancellation.Cancel();

            AssemblyMemberSourceEntry member =
                await AssemblyContextSourceQuery
                    .ExecuteMemberAsync(
                        group,
                        foreign,
                        assembly.MemberRequest(
                            nameof(SourceFixture.Describe)),
                        host.Context,
                        cancellation.Token);
            AssemblyTypeSourceEntry type =
                await AssemblyContextSourceQuery
                    .ExecuteTypeAsync(
                        group,
                        foreign,
                        assembly.TypeRequest(
                            typeof(SourceFixture).Name),
                        host.Context,
                        cancellation.Token);

            Assert.IsType<ArgumentException>(
                Assert.IsType<
                    AssemblyMemberSourceEntry.Unavailable>(
                        member)
                    .Failure.Error);
            Assert.IsType<ArgumentException>(
                Assert.IsType<
                    AssemblyTypeSourceEntry.Unavailable>(
                        type)
                    .Failure.Error);
        }

        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
        Assert.Equal(0, foreignPolicy.SelectionCount);
    }

    [Fact]
    public async Task ChangedBindingPolicySnapshot_IsRejectedBeforeCancellation()
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);
        assembly.Policy.ChangeVersion();
        using var cancellation = new CancellationTokenSource();

        foreach (bool canceled in new[] { false, true })
        {
            if (canceled)
                cancellation.Cancel();

            AssemblyMemberSourceEntry member =
                await AssemblyContextSourceQuery
                    .ExecuteMemberAsync(
                        group,
                        assembly.Participant,
                        assembly.MemberRequest(
                            nameof(SourceFixture.Describe)),
                        host.Context,
                        cancellation.Token);
            AssemblyTypeSourceEntry type =
                await AssemblyContextSourceQuery
                    .ExecuteTypeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest(
                            typeof(SourceFixture).Name),
                        host.Context,
                        cancellation.Token);

            Assert.IsType<InvalidOperationException>(
                Assert.IsType<
                    AssemblyMemberSourceEntry.Unavailable>(
                        member)
                    .Failure.Error);
            Assert.IsType<InvalidOperationException>(
                Assert.IsType<
                    AssemblyTypeSourceEntry.Unavailable>(
                        type)
                    .Failure.Error);
        }

        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Fact]
    public async Task BindingPolicyCancellation_PropagatesFromDecompilerFallback()
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);
        assembly.Policy.CancelSelection = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken));

        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.True(assembly.Policy.SelectionCount > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindingPolicyVersionChangeDuringPdbAcquisition_IsRejected(
        bool typeQuery)
    {
        TestAssembly assembly = TestAssembly.Create();
        var pdbStore =
            new ThrowingPdbStore(
                assembly.Policy.ChangeVersion);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            pdbStore: pdbStore);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        Exception error;
        if (typeQuery)
        {
            var result =
                Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(
                    await AssemblyContextSourceQuery.ExecuteTypeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest(
                            typeof(SourceFixture).Name),
                        host.Context,
                        TestContext.Current.CancellationToken));
            error = result.Failure.Error!;
        }
        else
        {
            var result =
                Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(
                    await AssemblyContextSourceQuery.ExecuteMemberAsync(
                        group,
                        assembly.Participant,
                        assembly.MemberRequest(
                            nameof(SourceFixture.Describe)),
                        host.Context,
                        TestContext.Current.CancellationToken));
            error = result.Failure.Error!;
        }

        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(1, pdbStore.ReadAttempts);
        Assert.Equal(0, assembly.Policy.SelectionCount);
        Assert.Empty(host.SourceRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PdbAcquisitionCancellation_PrecedesConcurrentBindingPolicyChange(
        bool typeQuery)
    {
        TestAssembly assembly = TestAssembly.Create();
        using var cancellation = new CancellationTokenSource();
        var pdbStore =
            new ThrowingPdbStore(
                () =>
                {
                    assembly.Policy.ChangeVersion();
                    cancellation.Cancel();
                });
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            pdbStore: pdbStore);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        if (typeQuery)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => AssemblyContextSourceQuery.ExecuteTypeAsync(
                    group,
                    assembly.Participant,
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name),
                    host.Context,
                    cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => AssemblyContextSourceQuery.ExecuteMemberAsync(
                    group,
                    assembly.Participant,
                    assembly.MemberRequest(
                        nameof(SourceFixture.Describe)),
                    host.Context,
                    cancellation.Token));
        }

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(pdbStore.ReadAttempts > 0);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindingPolicyVersionChangeDuringFallback_IsRejected(
        bool typeQuery)
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        assembly.Policy.BeforeSelection =
            assembly.Policy.ChangeVersion;
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        Exception error;
        if (typeQuery)
        {
            var result =
                Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(
                    await AssemblyContextSourceQuery.ExecuteTypeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest(
                            typeof(SourceFixture).Name),
                        host.Context,
                        TestContext.Current.CancellationToken));
            error = result.Failure.Error!;
        }
        else
        {
            var result =
                Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(
                    await AssemblyContextSourceQuery.ExecuteMemberAsync(
                        group,
                        assembly.Participant,
                        assembly.MemberRequest(
                            nameof(SourceFixture.Describe)),
                        host.Context,
                        TestContext.Current.CancellationToken));
            error = result.Failure.Error!;
        }

        Assert.IsType<InvalidOperationException>(error);
        Assert.True(assembly.Policy.SelectionCount > 0);
    }

    [Fact]
    public void CancellationObservingBindingPolicy_ForwardsAndObservesForeignSnapshot()
    {
        var inner = new FrameworkBindingPolicy();
        inner.SnapshotVersion = new AssemblyBindingPolicyVersion();
        var policy =
            new AssemblyContextSourceQuery.CancellationObservingBindingPolicy(
                inner);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.CoreLibrary(),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform);

        AssemblyBindingPolicyVersion version = policy.Version;
        AssemblyBindingSelectionSnapshot snapshot = policy.Select(request);
        Assert.Same(inner.SnapshotVersion, snapshot.Version);
        Assert.NotSame(version, policy.Version);
        Assert.Throws<InvalidOperationException>(policy.ThrowIfObserved);
        Assert.Equal(1, inner.SelectionCount);
    }

    [Fact]
    public void CancellationObservingBindingPolicy_PreservesNullSnapshot()
    {
        var inner = new NullSnapshotPolicy();
        var policy =
            new AssemblyContextSourceQuery.CancellationObservingBindingPolicy(
                inner);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.CoreLibrary(),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform);

        Assert.Null(policy.Select(request));
    }

    [Fact]
    public void CancellationObservingBindingPolicy_ObservesCompositionDomain()
    {
        ResolvedAssemblyReference first =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "First",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(),
                AssemblyResolutionProvenance.Local("first test candidate"));
        ResolvedAssemblyReference second =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "Second",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(),
                AssemblyResolutionProvenance.Local("second test candidate"));
        var inner = new FrameworkBindingPolicy
        {
            SelectOverride = _ =>
                AssemblyBindingSelection.RequireComposition(
                    AssemblyBindingCandidateDomain.Create(
                        [first, second])),
        };
        var policy =
            new AssemblyContextSourceQuery.CancellationObservingBindingPolicy(
                inner);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.CoreLibrary(),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform);

        var required = Assert.IsType<
            AssemblyBindingSelection.CompositionRequired>(
                policy.Select(request).Selection);

        Assert.Equal(
            [first.Registration, second.Registration],
            required.Domain.Candidates.Select(
                candidate => candidate.Registration));
        Assert.DoesNotContain(first, required.Domain.Candidates);
        Assert.DoesNotContain(second, required.Domain.Candidates);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task SelectedDescriptorCancellation_PropagatesFromFallback(
        bool typeQuery,
        bool cancelDuringRead,
        bool cancelDuringCapabilityCheck)
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        byte[] coreLibraryBytes =
            File.ReadAllBytes(
                typeof(object).Assembly.Location);
        AssemblyReferenceIdentity coreLibraryIdentity =
            ReadIdentity(coreLibraryBytes);
        int opens = 0;
        assembly.Policy.SelectOverride =
            request =>
                AssemblyBindingSelection.Found(
                    ResolvedAssemblyReference.Create(
                        request.Target
                            is AssemblyBindingTarget.AssemblyReference reference
                            ? reference.Identity
                            : coreLibraryIdentity,
                        path: null,
                        () =>
                        {
                            Interlocked.Increment(ref opens);
                            if (cancelDuringRead)
                            {
                                return new CancellationOnReadStream(
                                    coreLibraryBytes);
                            }
                            if (cancelDuringCapabilityCheck)
                            {
                                return new CancellationOnCanReadStream();
                            }
                            throw new OperationCanceledException(
                                "Synthetic selected-descriptor cancellation.");
                        },
                        AssemblyResolutionProvenance.Local(
                            "source query cancellation test")));
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        try
        {
            if (typeQuery)
            {
                await AssemblyContextSourceQuery.ExecuteTypeAsync(
                    group,
                    assembly.Participant,
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name),
                    host.Context,
                    TestContext.Current.CancellationToken);
            }
            else
            {
                await AssemblyContextSourceQuery.ExecuteMemberAsync(
                    group,
                    assembly.Participant,
                    assembly.MemberRequest(
                        nameof(SourceFixture.Describe)),
                    host.Context,
                    TestContext.Current.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.True(opens > 0);
            Assert.True(assembly.Policy.SelectionCount > 0);
            return;
        }

        Assert.Fail(
            $"Expected selected-descriptor cancellation; opens={opens}, selections={assembly.Policy.SelectionCount}.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindingPolicyRequestedTokenCancellation_StopsFallback(
        bool typeQuery)
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        using var cancellation = new CancellationTokenSource();
        assembly.Policy.BeforeSelection =
            cancellation.Cancel;
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        if (typeQuery)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => AssemblyContextSourceQuery.ExecuteTypeAsync(
                    group,
                    assembly.Participant,
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name),
                    host.Context,
                    cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => AssemblyContextSourceQuery.ExecuteMemberAsync(
                    group,
                    assembly.Participant,
                    assembly.MemberRequest(
                        nameof(SourceFixture.Describe)),
                    host.Context,
                    cancellation.Token));
        }

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(assembly.Policy.SelectionCount > 0);
    }

    [Fact]
    public async Task SourceStoreFailure_FallsBackRepeatablyWithoutPublishingMemoryEntry()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(typeof(SourceFixture).Name);
        var store = new ThrowingSourceContentStore();
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            store);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            AssemblyTypeSourceEntry result =
                await AssemblyContextSourceQuery.ExecuteTypeAsync(
                    group,
                    assembly.Participant,
                    request,
                    host.Context,
                    TestContext.Current.CancellationToken);

            var available =
                Assert.IsType<AssemblyTypeSourceEntry.Available>(
                    result);
            var decompiled =
                Assert.IsType<AssemblyTypeSource.Decompiled>(
                    available.Source);
            var failed =
                Assert.IsType<FindingInspection<string>.Failed>(
                    decompiled.PdbAttempt.Lines.Value);
            Assert.Contains(
                "source-content store failed",
                failed.Error.Reason,
                StringComparison.Ordinal);
            Assert.Contains(
                nameof(SourceFixture),
                decompiled.Text,
                StringComparison.Ordinal);
        }

        Assert.Equal(2, store.StoreAttempts);
        Assert.Equal(2, host.SourceRequests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SourceStoreOperationalFailure_PreservesPdbSourceFailureAndFallback(
        bool failRead)
    {
        TestAssembly assembly = TestAssembly.Create();
        var store =
            new OperationalFailureSourceContentStore(
                failRead);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            store);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);

        var decompiled =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                Assert.IsType<
                    AssemblyTypeSourceEntry.Available>(
                        result)
                    .Source);
        var failed =
            Assert.IsType<FindingInspection<string>.Failed>(
                decompiled.PdbAttempt.Lines.Value);
        Assert.Contains(
            "source-content store failed",
            failed.Error.Reason,
            StringComparison.Ordinal);
        Assert.Equal(1, store.ReadAttempts);
        Assert.Equal(failRead ? 0 : 1, store.StoreAttempts);
        Assert.Equal(failRead ? 0 : 1, host.SourceRequests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SourceStoreCancellation_Propagates(
        bool cancelRead)
    {
        TestAssembly assembly = TestAssembly.Create();
        using var cancellation = new CancellationTokenSource();
        var store =
            new CancelingSourceContentStore(
                cancellation,
                cancelRead);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            store);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, store.ReadAttempts);
        Assert.Equal(cancelRead ? 0 : 1, store.StoreAttempts);
        Assert.Equal(cancelRead ? 0 : 1, host.SourceRequests.Count);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task SourceStoreSuccessfulCancellation_PropagatesBeforePdbSourceSuccess(
        bool member,
        bool cancelRead)
    {
        TestAssembly assembly = TestAssembly.Create();
        var store =
            new SuccessfulCancelingSourceContentStore(
                cancelRead,
                SourceFileBytes());
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            store);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var cancellation =
                new CancellationTokenSource();
            store.Arm(cancellation);
            if (member)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => AssemblyContextSourceQuery
                        .ExecuteMemberAsync(
                            group,
                            assembly.Participant,
                            assembly.MemberRequest(
                                nameof(SourceFixture.Describe)),
                            host.Context,
                            cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => AssemblyContextSourceQuery
                        .ExecuteTypeAsync(
                            group,
                            assembly.Participant,
                            assembly.TypeRequest(
                                typeof(SourceFixture).Name),
                            host.Context,
                            cancellation.Token));
            }

            Assert.True(
                cancellation.IsCancellationRequested);
        }

        Assert.Equal(2, store.ReadAttempts);
        Assert.Equal(cancelRead ? 0 : 2, store.StoreAttempts);
        Assert.Equal(cancelRead ? 0 : 2, host.SourceRequests.Count);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Fact]
    public async Task PdbStoreFailure_PreservesPdbSourceFailureAndFallsBackForMemberAndType()
    {
        TestAssembly assembly = TestAssembly.Create();
        var pdbStore = new ThrowingPdbStore();
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            pdbStore: pdbStore);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry memberResult =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken);
        var memberAvailable =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                memberResult);
        var memberDecompiled =
            Assert.IsType<AssemblyMemberSource.Decompiled>(
                memberAvailable.Source);
        var memberFailure =
            Assert.IsType<FindingInspection<string>.Failed>(
                memberDecompiled.PdbAttempt.Lines.Value);
        Assert.Contains(
            "Portable PDB acquisition failed",
            memberFailure.Error.Reason,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            memberDecompiled.Text,
            StringComparison.Ordinal);

        AssemblyTypeSourceEntry typeResult =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);
        var typeAvailable =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                typeResult);
        var typeDecompiled =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                typeAvailable.Source);
        var typeFailure =
            Assert.IsType<FindingInspection<string>.Failed>(
                typeDecompiled.PdbAttempt.Lines.Value);
        Assert.Contains(
            "Portable PDB acquisition failed",
            typeFailure.Error.Reason,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(SourceFixture),
            typeDecompiled.Text,
            StringComparison.Ordinal);

        Assert.Equal(2, pdbStore.ReadAttempts);
        Assert.True(assembly.Policy.SelectionCount > 0);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public async Task CorruptEmbeddedPdb_PreservesPdbSourceFailureAndFallsBackForMemberAndType()
    {
        byte[] bytes =
            CorruptEmbeddedPdb(
                File.ReadAllBytes(
                    typeof(EmbeddedSourceFixture)
                        .Assembly.Location));

        TestAssembly assembly =
            TestAssembly.Create(bytes);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry memberResult =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(EmbeddedSourceFixture.Echo),
                    typeof(EmbeddedSourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);
        var memberAvailable =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                memberResult);
        var memberDecompiled =
            Assert.IsType<AssemblyMemberSource.Decompiled>(
                memberAvailable.Source);
        Assert.IsType<FindingInspection<string>.Failed>(
            memberDecompiled.PdbAttempt.Lines.Value);
        Assert.Contains(
            nameof(EmbeddedSourceFixture.Echo),
            memberDecompiled.Text,
            StringComparison.Ordinal);

        AssemblyTypeSourceEntry typeResult =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(EmbeddedSourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);
        var typeAvailable =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                typeResult);
        var typeDecompiled =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                typeAvailable.Source);
        Assert.IsType<FindingInspection<string>.Failed>(
            typeDecompiled.PdbAttempt.Lines.Value);
        Assert.Contains(
            nameof(EmbeddedSourceFixture),
            typeDecompiled.Text,
            StringComparison.Ordinal);

        Assert.True(assembly.Policy.SelectionCount > 0);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public void PdbContextOpenFailure_DisposesAuthoritativeStream()
    {
        byte[] bytes =
            CorruptEmbeddedPdb(
                File.ReadAllBytes(
                    typeof(EmbeddedSourceFixture)
                        .Assembly.Location));
        using var stream =
            new DisposeCountingStream(
                new MemoryStream(
                    bytes,
                    writable: false));
        var descriptor =
            ResolvedAssemblyReference.Create(
                ReadIdentity(bytes),
                path: null,
                () => stream,
                AssemblyResolutionProvenance.Local(
                    "corrupt embedded PDB fixture"));

        Assert.Throws<BadImageFormatException>(
            () => PdbContext.OpenEmbeddedPdbOnly(
                descriptor));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Theory]
    [InlineData(1024, 1, false)]
    [InlineData(1024, 1, true)]
    [InlineData(1, 1024, false)]
    [InlineData(1, 1024, true)]
    public async Task EmbeddedPdbHostLimits_ApplyBeforeQueryOwnedOpen(
        long maxPortablePdbBytes,
        long maxExpandedPdbBytes,
        bool allowAdjacentPdbReads)
    {
        byte[] bytes = File.ReadAllBytes(
            typeof(EmbeddedSourceFixture).Assembly.Location);
        using var stream =
            new DisposeCountingStream(
                new MemoryStream(
                    bytes,
                    writable: false));
        var descriptor =
            ResolvedAssemblyReference.Create(
                ReadIdentity(bytes),
                path: null,
                () => stream,
                AssemblyResolutionProvenance.Local(
                    "embedded PDB limit fixture"));
        using var host = QueryHost.WithoutPdb(
            new SymbolAcquisitionLimits(
                maxSymbolPackageBytes: 1024,
                maxPortablePdbBytes,
                maxSymbolPackageEntries: 1,
                maxExpandedPdbBytes),
            allowAdjacentPdbReads:
                allowAdjacentPdbReads);

        var result =
            await AssemblyContextSourceQuery.OpenSourceLinkAsync(
                descriptor,
                host.Context,
                TestContext.Current.CancellationToken);

        Assert.Null(result.Source);
        Assert.IsType<PdbResourceLimitException>(result.Failure);
        Assert.Equal(1, stream.DisposeCount);
        Assert.Empty(host.SymbolRequests);
    }

    [Fact]
    public async Task PreOpenCancellation_DoesNotOpenAssemblyStream()
    {
        byte[] bytes = File.ReadAllBytes(
            typeof(EmbeddedSourceFixture).Assembly.Location);
        int openCount = 0;
        var descriptor =
            ResolvedAssemblyReference.Create(
                ReadIdentity(bytes),
                path: null,
                () =>
                {
                    openCount++;
                    return new MemoryStream(bytes, writable: false);
                },
                AssemblyResolutionProvenance.Local(
                    "pre-open cancellation fixture"));
        using var host = QueryHost.WithoutPdb();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery.OpenSourceLinkAsync(
                descriptor,
                host.Context,
                cancellation.Token));

        Assert.Equal(0, openCount);
    }

    [Fact]
    public async Task PdbAcquisitionCancellation_DisposesOpenedSourceLinkService()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] bytes =
            File.ReadAllBytes(
                typeof(AssemblyContextSourceQueryTests)
                    .Assembly.Location);
        using var stream =
            new DisposeCountingStream(
                new MemoryStream(
                    bytes,
                    writable: false));
        var descriptor =
            ResolvedAssemblyReference.Create(
                assembly.Assembly.Identity,
                path: null,
                () => stream,
                AssemblyResolutionProvenance.Package(
                    "Example.Source",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var host =
            QueryHost.WithPdb(
                assembly.PdbPath,
                SourceFileBytes(),
                pdbStore: new CancelingPdbStore());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery
                .OpenSourceLinkAsync(
                    descriptor,
                    host.Context,
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostPdbCancellation_DisposesOpenedSourceLinkService(
        bool typeQuery)
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] bytes =
            File.ReadAllBytes(
                typeof(AssemblyContextSourceQueryTests)
                    .Assembly.Location);
        using var stream =
            new DisposeCountingStream(
                new MemoryStream(
                    bytes,
                    writable: false));
        var retained =
            ResolvedAssemblyReference.Create(
                assembly.Assembly.Identity,
                path: null,
                () => stream,
                AssemblyResolutionProvenance.Package(
                    "Example.Source",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        var subject = new AssemblyContextSubject(retained);
        using var cancellation = new CancellationTokenSource();
        var pdbStore =
            new StateChangingPdbStore(
                cancellation.Cancel);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            pdbStore: pdbStore);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        if (typeQuery)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => AssemblyContextSourceQuery.InspectTypeAsync(
                    group,
                    subject,
                    assembly.Participant,
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name),
                    host.Context,
                    retained,
                    assembly.Policy.Version,
                    pdbEvidence: null,
                    cancellationToken: cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => AssemblyContextSourceQuery.InspectMemberAsync(
                    group,
                    subject,
                    assembly.Participant,
                    assembly.MemberRequest(
                        nameof(SourceFixture.Describe)),
                    host.Context,
                    assembly.MemberTarget(
                        nameof(SourceFixture.Describe)),
                    retained,
                    assembly.Policy.Version,
                    cancellation.Token));
        }

        Assert.Equal(1, stream.DisposeCount);
        Assert.Equal(
            1,
            Assert.IsType<BlockingDisposeStream>(
                    pdbStore.AuthoritativeStream)
                .DisposeCount);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StateChangeDuringPdbStreamRelease_IsObserved(
        bool memberQuery,
        bool rotatePolicy)
    {
        TestAssembly assembly = TestAssembly.Create();
        using var cancellation = new CancellationTokenSource();
        using var disposeEntered = new ManualResetEventSlim();
        using var disposeRelease = new ManualResetEventSlim();
        var pdbStore =
            new StateChangingPdbStore(
                afterLocalPath: null,
                disposeEntered,
                disposeRelease);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            pdbStore: pdbStore);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);
        Task actor = Task.Run(
            () =>
            {
                Assert.True(
                    disposeEntered.Wait(
                        TimeSpan.FromSeconds(10)),
                    "Timed out waiting for PDB disposal.");
                if (rotatePolicy)
                    assembly.Policy.ChangeVersion();
                else
                    cancellation.Cancel();
                disposeRelease.Set();
            },
            TestContext.Current.CancellationToken);

        try
        {
            if (rotatePolicy)
            {
                Exception error;
                if (memberQuery)
                {
                    var unavailable =
                        Assert.IsType<
                            AssemblyMemberSourceEntry.Unavailable>(
                                await AssemblyContextSourceQuery
                                    .ExecuteMemberAsync(
                                        group,
                                        assembly.Participant,
                                        assembly.MemberRequest(
                                            nameof(SourceFixture.Describe)),
                                        host.Context,
                                        cancellation.Token));
                    error = unavailable.Failure.Error!;
                }
                else
                {
                    var unavailable =
                        Assert.IsType<
                            AssemblyTypeSourceEntry.Unavailable>(
                                await AssemblyContextSourceQuery
                                    .ExecuteTypeAsync(
                                        group,
                                        assembly.Participant,
                                        assembly.TypeRequest(
                                            typeof(SourceFixture).Name),
                                        host.Context,
                                        cancellation.Token));
                    error = unavailable.Failure.Error!;
                }
                Assert.IsType<InvalidOperationException>(error);
            }
            else if (memberQuery)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => AssemblyContextSourceQuery.ExecuteMemberAsync(
                        group,
                        assembly.Participant,
                        assembly.MemberRequest(
                            nameof(SourceFixture.Describe)),
                        host.Context,
                        cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => AssemblyContextSourceQuery.ExecuteTypeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest(
                            typeof(SourceFixture).Name),
                        host.Context,
                        cancellation.Token));
            }
        }
        finally
        {
            disposeRelease.Set();
            await actor;
        }

        Assert.Equal(
            1,
            Assert.IsType<BlockingDisposeStream>(
                    pdbStore.AuthoritativeStream)
                .DisposeCount);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PdbDisposalFailure_PreventsPdbSourceSuccess(
        bool memberQuery,
        bool cancellationFailure)
    {
        TestAssembly assembly = TestAssembly.Create();
        Exception disposalFailure =
            cancellationFailure
                ? new OperationCanceledException(
                    "Synthetic PDB disposal cancellation.")
                : new IOException(
                    "Synthetic PDB disposal failure.");
        var pdbStore =
            new StateChangingPdbStore(
                afterLocalPath: null,
                disposeFailure: disposalFailure);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            pdbStore: pdbStore);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        if (cancellationFailure)
        {
            Exception error =
                memberQuery
                    ? await Assert.ThrowsAsync<
                        OperationCanceledException>(
                            () => AssemblyContextSourceQuery
                                .ExecuteMemberAsync(
                                    group,
                                    assembly.Participant,
                                    assembly.MemberRequest(
                                        nameof(SourceFixture.Describe)),
                                    host.Context,
                                    TestContext.Current.CancellationToken))
                    : await Assert.ThrowsAsync<
                        OperationCanceledException>(
                            () => AssemblyContextSourceQuery
                                .ExecuteTypeAsync(
                                    group,
                                    assembly.Participant,
                                    assembly.TypeRequest(
                                        typeof(SourceFixture).Name),
                                    host.Context,
                                    TestContext.Current.CancellationToken));
            Assert.Same(disposalFailure, error);
        }
        else
        {
            Exception error;
            if (memberQuery)
            {
                var unavailable =
                    Assert.IsType<
                        AssemblyMemberSourceEntry.Unavailable>(
                            await AssemblyContextSourceQuery
                                .ExecuteMemberAsync(
                                    group,
                                    assembly.Participant,
                                    assembly.MemberRequest(
                                        nameof(SourceFixture.Describe)),
                                    host.Context,
                                    TestContext.Current.CancellationToken));
                error = unavailable.Failure.Error!;
            }
            else
            {
                var unavailable =
                    Assert.IsType<
                        AssemblyTypeSourceEntry.Unavailable>(
                            await AssemblyContextSourceQuery
                                .ExecuteTypeAsync(
                                    group,
                                    assembly.Participant,
                                    assembly.TypeRequest(
                                        typeof(SourceFixture).Name),
                                    host.Context,
                                    TestContext.Current.CancellationToken));
                error = unavailable.Failure.Error!;
            }
            Assert.Same(disposalFailure, error);
        }

        Assert.Equal(
            1,
            Assert.IsType<BlockingDisposeStream>(
                    pdbStore.AuthoritativeStream)
                .DisposeCount);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonStandardPdbDisposalFailure_IsTyped(
        bool memberQuery)
    {
        TestAssembly assembly = TestAssembly.Create();
        var disposalFailure =
            new HttpRequestException(
                "Synthetic host-specific PDB disposal failure.");
        var pdbStore =
            new StateChangingPdbStore(
                afterLocalPath: null,
                disposeFailure: disposalFailure);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            pdbStore: pdbStore);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        Exception error;
        if (memberQuery)
        {
            var unavailable =
                Assert.IsType<
                    AssemblyMemberSourceEntry.Unavailable>(
                        await AssemblyContextSourceQuery
                            .ExecuteMemberAsync(
                                group,
                                assembly.Participant,
                                assembly.MemberRequest(
                                    nameof(SourceFixture.Describe)),
                                host.Context,
                                TestContext.Current.CancellationToken));
            error = unavailable.Failure.Error!;
        }
        else
        {
            var unavailable =
                Assert.IsType<
                    AssemblyTypeSourceEntry.Unavailable>(
                        await AssemblyContextSourceQuery
                            .ExecuteTypeAsync(
                                group,
                                assembly.Participant,
                                assembly.TypeRequest(
                                    typeof(SourceFixture).Name),
                                host.Context,
                                TestContext.Current.CancellationToken));
            error = unavailable.Failure.Error!;
        }

        var typed = Assert.IsType<InvalidOperationException>(error);
        Assert.Same(disposalFailure, typed.InnerException);
        Assert.Equal(
            1,
            Assert.IsType<BlockingDisposeStream>(
                    pdbStore.AuthoritativeStream)
                .DisposeCount);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task PdbLoadPrimaryFailure_IsNotMaskedByCleanupFailure(
        bool memberQuery,
        bool fatalFailure,
        bool providerFailure)
    {
        TestAssembly assembly = TestAssembly.Create();
        Exception primaryFailure =
            fatalFailure
                ? new OutOfMemoryException(
                    "Synthetic fatal PDB-load failure.")
                : new OperationCanceledException(
                    "Synthetic PDB-load cancellation.");
        var pdbStore =
            new StateChangingPdbStore(
                afterLocalPath: null,
                disposeFailure:
                    new HttpRequestException(
                        "Synthetic PDB cleanup failure."),
                positionResetFailure:
                    providerFailure
                        ? null
                        : primaryFailure,
                disposeFailureAt: 1,
                prefetchReadFailure:
                    providerFailure
                        ? primaryFailure
                        : null);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            pdbStore: pdbStore);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        Func<Task> operation =
            memberQuery
                ? () => AssemblyContextSourceQuery
                        .ExecuteMemberAsync(
                            group,
                            assembly.Participant,
                            assembly.MemberRequest(
                                nameof(SourceFixture.Describe)),
                            host.Context,
                            TestContext.Current.CancellationToken)
                : () => AssemblyContextSourceQuery
                        .ExecuteTypeAsync(
                            group,
                            assembly.Participant,
                            assembly.TypeRequest(
                                typeof(SourceFixture).Name),
                            host.Context,
                            TestContext.Current.CancellationToken);

        Exception error =
            fatalFailure
                ? await Assert.ThrowsAsync<OutOfMemoryException>(
                    operation)
                : await Assert.ThrowsAsync<OperationCanceledException>(
                    operation);

        Assert.Same(primaryFailure, error);
        Assert.Equal(
            1,
            Assert.IsType<BlockingDisposeStream>(
                    pdbStore.AuthoritativeStream)
                .DisposeCount);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Fact]
    public async Task MalformedPdbDocument_PreservesPdbSourceFailureAndFallsBackForType()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] pdbBytes =
            CorruptDocumentName(
                assembly.PdbPath,
                Path.GetFileName(
                    SourceFileBytesPath()),
                corruptTarget: false);

        await AssertMalformedPdbTypeFallsBackAsync(
            assembly,
            pdbBytes);
    }

    [Fact]
    public async Task MalformedTargetPdbDocument_ProducesFailedPdbSourceEvidenceBeforeTypeFallback()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] pdbBytes =
            CorruptDocumentName(
                assembly.PdbPath,
                Path.GetFileName(
                    SourceFileBytesPath()),
                corruptTarget: true);

        await AssertMalformedPdbTypeFallsBackAsync(
            assembly,
            pdbBytes);
    }

    [Fact]
    public async Task EmptyTargetPdbDocument_ProducesFailedPdbSourceEvidenceBeforeTypeFallback()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] pdbBytes =
            EmptyDocumentName(
                assembly.PdbPath,
                Path.GetFileName(
                    SourceFileBytesPath()));

        await AssertMalformedPdbTypeFallsBackAsync(
            assembly,
            pdbBytes);
    }

    [Fact]
    public async Task MalformedTargetSequencePoints_ProduceFailedPdbSourceEvidenceBeforeTypeFallback()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] pdbBytes =
            CorruptMethodSequencePoints(
                assembly.PdbPath,
                typeof(SourceFixture)
                    .GetMethod(
                        nameof(SourceFixture.Describe))!
                    .MetadataToken);

        await AssertMalformedPdbTypeFallsBackAsync(
            assembly,
            pdbBytes);
    }

    [Fact]
    public async Task RejectedUnrelatedTypeName_ProducesFailedPdbSourceEvidenceBeforeTypeFallback()
    {
        TestAssembly original = TestAssembly.Create();
        byte[] bytes =
            RejectUnrelatedTypeName(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.CreatePackage(
                bytes,
                original.PdbPath);

        await AssertMalformedPdbTypeFallsBackAsync(
            assembly,
            File.ReadAllBytes(original.PdbPath));
    }

    [Fact]
    public async Task NeitherSourceAvailable_ReturnsTypedFailure()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(typeof(SourceDelegate).Name);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(
                result);
        Assert.Equal(
            AssemblySourceFailureKind
                .PdbAndDecompiledUnavailable,
            unavailable.Failure.Kind);
        Assert.NotNull(unavailable.PdbAttempt);
        Assert.NotNull(unavailable.DecompiledAttempt);
        Assert.False(unavailable.DecompiledAttempt!.IsAvailable);
        Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
            unavailable.DecompilationHouseOutcome);
    }

    [Fact]
    public async Task RejectedParticipant_ReturnsAcquisitionFailure()
    {
        TestAssembly assembly =
            TestAssembly.Create(selectedName: "Different.Identity");
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var rejected =
            Assert.IsType<AssemblyMemberSourceEntry.Rejected>(
                result);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
    }

    [Fact]
    public async Task SourcePair_OwnedPdbCleanupFailurePreventsComparison()
    {
        TestAssembly before = TestAssembly.Create();
        TestAssembly after = TestAssembly.Create();
        var error = new IOException("Synthetic paired-source PDB disposal failure.");
        var pdbStore = new StateChangingPdbStore(
            afterLocalPath: null,
            disposeFailure: error);
        using var host = QueryHost.WithPdb(
            before.PdbPath,
            SourceFileBytes(),
            pdbStore: pdbStore);

        AssemblyMemberSourcePairResult result = await ExecuteSourcePairAsync(
            before, after, nameof(SourceFixture.Describe), host,
            typeName: nameof(SourceFixture));

        Assert.Equal(AssemblyMemberSourcePairStatus.Failed, result.Status);
        Assert.Same(error, result.Failure?.Error);
        Assert.Null(result.Comparison);
        Assert.False(result.IsExact);
        Assert.Equal(0, before.Policy.SelectionCount);
        Assert.Equal(0, after.Policy.SelectionCount);
    }

    static async Task AssertMalformedPdbTypeFallsBackAsync(
        TestAssembly assembly,
        byte[] pdbBytes)
    {
        using var host =
            QueryHost.WithPdb(
                Path.GetFileName(assembly.PdbPath),
                pdbBytes,
                SourceFileBytes());
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                result);
        var decompiled =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                available.Source);
        var failed =
            Assert.IsType<FindingInspection<string>.Failed>(
                decompiled.PdbAttempt.Lines.Value);
        Assert.Contains(
            "Portable PDB type source mapping failed",
            failed.Error.Reason,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(SourceFixture),
            decompiled.Text,
            StringComparison.Ordinal);
        Assert.True(assembly.Policy.SelectionCount > 0);
        Assert.NotEmpty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
    }

    public static class SourceFixture
    {
        public static int Count { get; set; }

        public static event Action? Changed
        {
            add { }
            remove { }
        }

        public static string Describe(int value)
            => $"value={value}";

        public static int Increment(int value)
            => value + 1;
    }

    public delegate int SourceDelegate(int value);
}

public sealed class SourceProjectionTarget;

public static class SourceProjectionExtensions
{
    public static int ProjectedIncrement(
        this SourceProjectionTarget target,
        int value) =>
        value + 1;
}
