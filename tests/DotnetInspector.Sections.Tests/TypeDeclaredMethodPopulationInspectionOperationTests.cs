using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using QuerySpace.Composition;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeDeclaredMethodPopulationInspectionOperationTests
{
    [Fact]
    public void QuerySpace_UsesDistinctCountAndRowsContracts()
    {
        QuerySpaceRequest count =
            TypeDeclaredMethodPopulationQuery.CreateRequest(
                QuerySpaceTerminalRequirement.Count);
        QuerySpaceRequest rows =
            TypeDeclaredMethodPopulationQuery.CreateRequest(
                QuerySpaceTerminalRequirement.Rows);
        TypeDeclaredMethodPopulationQueryResult countPlan =
            TypeDeclaredMethodPopulationQuery.ResolveRequest(
                count,
                TestContext.Current.CancellationToken);
        TypeDeclaredMethodPopulationQueryResult rowsPlan =
            TypeDeclaredMethodPopulationQuery.ResolveRequest(
                rows,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            TypeDeclaredMethodPopulationQuery.CountResultContract,
            count.ResultContract);
        Assert.Equal(
            TypeDeclaredMethodPopulationQuery.RowsResultContract,
            rows.ResultContract);
        Assert.Same(
            count,
            TypeDeclaredMethodPopulationQuery.CreateRequest(
                QuerySpaceTerminalRequirement.Count));
        Assert.Same(
            rows,
            TypeDeclaredMethodPopulationQuery.CreateRequest(
                QuerySpaceTerminalRequirement.Rows));
        Assert.Same(
            countPlan,
            TypeDeclaredMethodPopulationQuery.ResolveRequest(
                count,
                TestContext.Current.CancellationToken));
        Assert.Same(
            rowsPlan,
            TypeDeclaredMethodPopulationQuery.ResolveRequest(
                rows,
                TestContext.Current.CancellationToken));
        Assert.IsType<
            TypeDeclaredMethodPopulationQueryResult.Accepted>(
                countPlan);
        Assert.IsType<
            TypeDeclaredMethodPopulationQueryResult.Accepted>(
                rowsPlan);
    }

    [Fact]
    public async Task PreparedInspectionExecutesCanonicalPlansRepeatedly()
    {
        await using var workspace = new InspectionWorkspace();
        (
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =
                CreateGroup(workspace);
        using (group)
        {
            MetadataTypeDefinitionName type =
                Name("System.Text.Json", "JsonSerializer");
            using TypeDeclaredMethodPopulationPreparedInspection prepared =
                TypeDeclaredMethodPopulationInspectionOperation.Prepare(
                    group,
                    participant,
                    type,
                    Binding(type),
                    TestContext.Current.CancellationToken);

            var count = Assert.IsType<
                TypeDeclaredMethodPopulationOutcome.Counted>(
                    TypeDeclaredMethodPopulationInspectionOperation
                        .Execute(
                            prepared,
                            TypeDeclaredMethodPopulationQuery.CreateRequest(
                                QuerySpaceTerminalRequirement.Count),
                            cancellationToken:
                                TestContext.Current.CancellationToken)
                        .Content);
            var rows = Assert.IsType<
                TypeDeclaredMethodPopulationOutcome.Read>(
                    TypeDeclaredMethodPopulationInspectionOperation
                        .Execute(
                            prepared,
                            TypeDeclaredMethodPopulationQuery.CreateRequest(
                                QuerySpaceTerminalRequirement.Rows),
                            cancellationToken:
                                TestContext.Current.CancellationToken)
                        .Content);

            Assert.Equal(count.Count, rows.Count);
            Assert.Equal(count.Count, rows.Rows.Length);
            Assert.Equal(count.Binding, rows.Binding);
            Assert.Equal(1, count.Receipt.TypeDefinitionRowsRead);
        }
    }

    [Fact]
    public async Task PreparedFailureReusesSettledOutcome()
    {
        await using var workspace = new InspectionWorkspace();
        (
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =
                CreateGroup(workspace);
        using (group)
        {
            MetadataTypeDefinitionName type =
                Name("System.Text.Json", "JsonSerializer");
            MetadataTypeDefinitionBinding valid = Binding(type);
            using TypeDeclaredMethodPopulationPreparedInspection prepared =
                TypeDeclaredMethodPopulationInspectionOperation.Prepare(
                    group,
                    participant,
                    type,
                    new(
                        Guid.NewGuid(),
                        valid.Definition),
                    TestContext.Current.CancellationToken);
            QuerySpaceRequest count =
                TypeDeclaredMethodPopulationQuery.CreateRequest(
                    QuerySpaceTerminalRequirement.Count);

            TypeDeclaredMethodPopulationOutcome first =
                TypeDeclaredMethodPopulationInspectionOperation.Execute(
                    prepared,
                    count,
                    cancellationToken:
                        TestContext.Current.CancellationToken).Content;
            TypeDeclaredMethodPopulationOutcome second =
                TypeDeclaredMethodPopulationInspectionOperation.Execute(
                    prepared,
                    count,
                    cancellationToken:
                        TestContext.Current.CancellationToken).Content;

            Assert.Same(first, second);
            Assert.Equal(
                TypeDeclaredMethodPopulationRejection.BindingRejected,
                Assert.IsType<
                    TypeDeclaredMethodPopulationOutcome.Rejected>(first)
                    .Reason);
        }
    }

    [Fact]
    public async Task
        RealSystemTextJson_CountAndRowsShareCardinalityAndExposeWork()
    {
        await using var workspace = new InspectionWorkspace();
        (
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =
                CreateGroup(workspace);
        using (group)
        {
            MetadataTypeDefinitionName type =
                Name("System.Text.Json", "JsonSerializer");
            TypeDeclaredMethodPopulationOutcome.Counted counted =
                Assert.IsType<
                    TypeDeclaredMethodPopulationOutcome.Counted>(
                        Execute(
                            group,
                            participant,
                            type,
                            QuerySpaceTerminalRequirement.Count).Content);
            TypeDeclaredMethodPopulationOutcome.Read read =
                Assert.IsType<TypeDeclaredMethodPopulationOutcome.Read>(
                    Execute(
                        group,
                        participant,
                        type,
                        QuerySpaceTerminalRequirement.Rows).Content);

            Assert.Equal(counted.Count, read.Count);
            Assert.Equal(counted.Count, read.Rows.Length);
            Assert.True(counted.Count > 0);
            Assert.Equal(0, counted.Receipt.MethodDefinitionHandlesVisited);
            Assert.Equal(0, counted.Receipt.MethodDefinitionRowsRead);
            Assert.Equal(0, counted.Receipt.MethodNamesDecoded);
            Assert.Equal(0, counted.Receipt.MethodSignaturesDecoded);
            Assert.Equal(0, counted.Receipt.MethodAttributesDecoded);
            Assert.Equal(0, counted.Receipt.ProjectedRows);
            Assert.Equal(
                read.Count,
                read.Receipt.MethodDefinitionHandlesVisited);
            Assert.Equal(read.Count, read.Receipt.ProjectedRows);
            Assert.Equal(0, read.Receipt.MethodDefinitionRowsRead);
            Assert.Equal(0, read.Receipt.MethodNamesDecoded);
            Assert.Equal(0, read.Receipt.MethodSignaturesDecoded);
            Assert.Equal(0, read.Receipt.MethodAttributesDecoded);
            Assert.Equal(counted.Binding, read.Binding);

            var envelope =
                new InspectionEnvelope<
                    TypeDeclaredMethodPopulationOutcome>(
                        counted,
                        new InspectionShare.NonProjectable(
                            "test",
                            "test"));
            string json = JsonSerializer.Serialize(
                envelope,
                TypeDeclaredMethodPopulationInspectionJsonContext.Default
                    .InspectionEnvelopeTypeDeclaredMethodPopulationOutcome);
            InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>?
                roundTrip =
                    JsonSerializer.Deserialize(
                        json,
                        TypeDeclaredMethodPopulationInspectionJsonContext
                            .Default
                            .InspectionEnvelopeTypeDeclaredMethodPopulationOutcome);
            TypeDeclaredMethodPopulationOutcome.Counted
                roundTrippedCounted =
                    Assert.IsType<
                        TypeDeclaredMethodPopulationOutcome.Counted>(
                            roundTrip?.Content);
            Assert.Equal(counted.Count, roundTrippedCounted.Count);
            Assert.Equal(counted.Binding, roundTrippedCounted.Binding);
            Assert.Equal(counted.Receipt, roundTrippedCounted.Receipt);
        }
    }

    [Fact]
    public async Task BoundedRows_IsIncompleteBeforeMethodTraversal()
    {
        await using var workspace = new InspectionWorkspace();
        (
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =
                CreateGroup(workspace);
        using (group)
        {
            TypeDeclaredMethodPopulationOutcome.Incomplete incomplete =
                Assert.IsType<
                    TypeDeclaredMethodPopulationOutcome.Incomplete>(
                        Execute(
                            group,
                            participant,
                            Name(
                                "System.Text.Json",
                                "JsonSerializer"),
                            QuerySpaceTerminalRequirement.Rows,
                            maximumRows: 1).Content);

            Assert.True(incomplete.Count > incomplete.Limit);
            Assert.Equal(1, incomplete.Limit);
            Assert.NotNull(incomplete.Binding);
            Assert.NotNull(incomplete.Receipt);
            Assert.Equal(
                0,
                incomplete.Receipt.MethodDefinitionHandlesVisited);
            Assert.Equal(0, incomplete.Receipt.ProjectedRows);
        }
    }

    [Fact]
    public async Task ForeignBinding_RemainsVisible()
    {
        await using var workspace = new InspectionWorkspace();
        (
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =
                CreateGroup(workspace);
        using (group)
        {
            MetadataTypeDefinitionName type =
                Name("System.Text.Json", "JsonSerializer");
            MetadataTypeDefinitionBinding valid = Binding(type);
            TypeDeclaredMethodPopulationOutcome.Rejected rejected =
                Assert.IsType<
                    TypeDeclaredMethodPopulationOutcome.Rejected>(
                        Execute(
                            group,
                            participant,
                            type,
                            QuerySpaceTerminalRequirement.Count,
                            binding: new(
                                Guid.NewGuid(),
                                valid.Definition)).Content);

            Assert.Equal(
                TypeDeclaredMethodPopulationRejection.BindingRejected,
                rejected.Reason);
            Assert.NotNull(rejected.Subject);
        }
    }

    [Fact]
    public async Task ForeignQuerySpace_IsRejectedBeforeInspection()
    {
        await using var workspace = new InspectionWorkspace();
        (
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =
                CreateGroup(workspace);
        using (group)
        {
            MetadataTypeDefinitionName type =
                Name("System.Text.Json", "JsonSerializer");
            QuerySpaceRequest foreign =
                MemberOverloadPopulationQuery.CreateRequest(
                    MemberOverloadAccessibilityFilter.Public,
                    MemberOverloadReceiverFilter.All,
                    includeHidden: false,
                    MemberOverloadOrdering.Metadata,
                    QuerySpaceTerminalRequirement.Count);
            var request =
                new TypeDeclaredMethodPopulationInspectionRequest(
                    group,
                    participant,
                    type,
                    Binding(type),
                    foreign);

            TypeDeclaredMethodPopulationOutcome.Rejected rejected =
                Assert.IsType<
                    TypeDeclaredMethodPopulationOutcome.Rejected>(
                        TypeDeclaredMethodPopulationInspectionOperation
                            .Execute(
                                request,
                                TestContext.Current.CancellationToken)
                            .Content);

            Assert.Equal(
                TypeDeclaredMethodPopulationRejection.QueryRejected,
                rejected.Reason);
            Assert.Null(rejected.Subject);
        }
    }

    [Fact]
    public async Task ParticipantOpenFailure_RemainsVisible()
    {
        await using var workspace = new InspectionWorkspace();
        string path = SystemTextJsonPath();
        AssemblyReferenceIdentity identity;
        using (var stream = File.OpenRead(path))
        using (var reader = new PEReader(stream))
        {
            identity = AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        }
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => throw new IOException("Participant open failed."),
                AssemblyResolutionProvenance.Local(
                    "declared-method rejected participant"));
        var participant = new AssemblyContextParticipant(
            assembly,
            NoResolverAssemblyBindingPolicy.Instance);
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        TypeDeclaredMethodPopulationOutcome.Rejected rejected =
            Assert.IsType<
                TypeDeclaredMethodPopulationOutcome.Rejected>(
                    Execute(
                        group,
                        participant,
                        Name("System.Text.Json", "JsonSerializer"),
                        QuerySpaceTerminalRequirement.Count).Content);

        Assert.Equal(
            TypeDeclaredMethodPopulationRejection.ParticipantRejected,
            rejected.Reason);
        Assert.NotNull(rejected.Subject);
    }

    [Fact]
    public async Task DescendingMethodRange_RemainsVisibleAsSettledFailure()
    {
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");
        MetadataTypeDefinitionBinding binding = Binding(type);
        string path = CreateDescendingMethodRange(binding);
        await AssertSettledFailure(
            type,
            binding,
            path,
            "descending MethodDef range");
    }

    [Fact]
    public async Task OutOfRangeMethodRange_RemainsVisibleAsSettledFailure()
    {
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");
        MetadataTypeDefinitionBinding binding = Binding(type);
        string path = CreateOutOfRangeMethodRange(binding);
        await AssertSettledFailure(
            type,
            binding,
            path,
            "outside the method-list table");
    }

    private static async Task AssertSettledFailure(
        MetadataTypeDefinitionName type,
        MetadataTypeDefinitionBinding binding,
        string path,
        string expectedDetail)
    {
        try
        {
            await using var workspace = new InspectionWorkspace();
            (
                AssemblyContextGroup group,
                AssemblyContextParticipant participant) =
                    CreateGroup(workspace, path);
            using (group)
            using (TypeDeclaredMethodPopulationPreparedInspection prepared =
                TypeDeclaredMethodPopulationInspectionOperation.Prepare(
                    group,
                    participant,
                    type,
                    binding,
                    TestContext.Current.CancellationToken))
            {
                QuerySpaceRequest count =
                    TypeDeclaredMethodPopulationQuery.CreateRequest(
                        QuerySpaceTerminalRequirement.Count);
                QuerySpaceRequest rows =
                    TypeDeclaredMethodPopulationQuery.CreateRequest(
                        QuerySpaceTerminalRequirement.Rows);
                TypeDeclaredMethodPopulationOutcome countOutcome =
                    TypeDeclaredMethodPopulationInspectionOperation.Execute(
                        prepared,
                        count,
                        cancellationToken:
                            TestContext.Current.CancellationToken).Content;
                TypeDeclaredMethodPopulationOutcome rowsOutcome =
                    TypeDeclaredMethodPopulationInspectionOperation.Execute(
                        prepared,
                        rows,
                        cancellationToken:
                            TestContext.Current.CancellationToken).Content;

                Assert.Same(countOutcome, rowsOutcome);
                var failed = Assert.IsType<
                    TypeDeclaredMethodPopulationOutcome.Failed>(countOutcome);
                Assert.Contains(
                    expectedDetail,
                    failed.Detail.ToString(),
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static InspectionEnvelope<
        TypeDeclaredMethodPopulationOutcome> Execute(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataTypeDefinitionName type,
            QuerySpaceTerminalRequirement terminal,
            int maximumRows = int.MaxValue,
            MetadataTypeDefinitionBinding? binding = null) =>
        TypeDeclaredMethodPopulationInspectionOperation.Execute(
            new(
                group,
                participant,
                type,
                binding ?? Binding(type),
                TypeDeclaredMethodPopulationQuery.CreateRequest(terminal),
                maximumRows),
            TestContext.Current.CancellationToken);

    private static (
        AssemblyContextGroup Group,
        AssemblyContextParticipant Participant) CreateGroup(
            InspectionWorkspace workspace,
            string? path = null)
    {
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                path ?? SystemTextJsonPath(),
                AssemblyResolutionProvenance.Local(
                    "declared-method population test"));
        var participant = new AssemblyContextParticipant(
            assembly,
            NoResolverAssemblyBindingPolicy.Instance);
        return (
            workspace.CreateAssemblyContextGroup([participant]),
            participant);
    }

    private static string SystemTextJsonPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "LibraryOverview",
            "System.Text.Json.dll");

    private static MetadataTypeDefinitionBinding Binding(
        MetadataTypeDefinitionName type)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(SystemTextJsonPath());
        TypeDefinitionToken definition =
            session.ProbeDeclaration(type) switch
            {
                TypeDeclarationResult.Defined defined =>
                    defined.Definition,
                TypeDeclarationResult.DefinitionKindUnavailable
                    unavailable =>
                    unavailable.Definition,
                _ => throw new InvalidOperationException(
                    "The test Type must resolve to a local TypeDef."),
            };
        return new(session.ModuleVersionId(), definition);
    }

    private static string CreateDescendingMethodRange(
        MetadataTypeDefinitionBinding binding) =>
        CreateMethodRange(
            binding,
            static (_, firstMethodRow) => firstMethodRow - 1);

    private static string CreateOutOfRangeMethodRange(
        MetadataTypeDefinitionBinding binding) =>
        CreateMethodRange(
            binding,
            static (reader, _) =>
                reader.GetTableRowCount(TableIndex.MethodDef) + 2);

    private static string CreateMethodRange(
        MetadataTypeDefinitionBinding binding,
        Func<MetadataReader, int, int> nextMethodList)
    {
        byte[] image = File.ReadAllBytes(SystemTextJsonPath());
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        var type = (TypeDefinitionHandle)MetadataTokens.EntityHandle(
            binding.Definition.Value);
        MethodDefinitionHandleCollection methods =
            reader.GetTypeDefinition(type).GetMethods();
        var enumerator = methods.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        int firstMethodRow =
            MetadataTokens.GetRowNumber(enumerator.Current);
        int typeRow = MetadataTokens.GetRowNumber(type);
        Assert.True(typeRow < reader.GetTableRowCount(TableIndex.TypeDef));

        int methodIndexSize =
            reader.GetTableRowCount(TableIndex.MethodDef) < (1 << 16)
                ? sizeof(ushort)
                : sizeof(uint);
        int typeRowSize = reader.GetTableRowSize(TableIndex.TypeDef);
        int nextTypeRowOffset =
            pe.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(TableIndex.TypeDef)
            + (typeRow * typeRowSize);
        Span<byte> methodList =
            image.AsSpan(
                nextTypeRowOffset + typeRowSize - methodIndexSize,
                methodIndexSize);
        int nextMethodStart = nextMethodList(
            reader,
            firstMethodRow);
        if (methodIndexSize == sizeof(ushort))
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                methodList,
                checked((ushort)nextMethodStart));
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodList,
                checked((uint)nextMethodStart));
        }

        string path = Path.Combine(
            Path.GetTempPath(),
            $"declared-method-range-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image);
        return path;
    }

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;
}
