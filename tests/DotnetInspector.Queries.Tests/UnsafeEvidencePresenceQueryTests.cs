using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using QuerySpace.Composition;

namespace DotnetInspector.Queries.Tests;

public sealed class UnsafeEvidencePresenceQueryTests
{
    [Fact]
    public void OwnerRequestClosesTheProducerWithExists()
    {
        QuerySpaceRequest request =
            UnsafeEvidencePresenceQuery.CreateRequest();

        Assert.Equal(
            UnsafeEvidencePresenceQuery.QuerySpaceIdentity,
            request.QuerySpace);
        Assert.Equal(
            QuerySpaceTerminalRequirement.Exists,
            request.Terminal);
        Assert.Equal(
            UnsafeEvidencePresenceQuery.ResultContract,
            request.ResultContract);
        Assert.Equal(
            [UnsafeEvidencePresenceQuery.MethodDefinitionsRowSet],
            request.ParticipatingRowSets);
        Assert.Empty(request.RowIntents);

        var accepted =
            Assert.IsType<UnsafeEvidencePresenceRequestResolution.Accepted>(
                UnsafeEvidencePresenceQuery.ResolveRequest(request));
        Assert.Equal(request.Operation, accepted.Plan.Intent);
        Assert.Equal(
            ProducerTerminal.Exists,
            accepted.Work.TerminalOf(
                UnsafeEvidencePresenceProducer.Instance));
    }

    [Fact]
    public void QuerySpaceAdvertisesOnlyTheSupportedClosing()
    {
        QuerySpaceDescriptor descriptor =
            UnsafeEvidencePresenceQuery.QuerySpace.Descriptor;

        Assert.Equal(
            [QuerySpaceTerminalRequirement.Exists],
            descriptor.Terminals);
        Assert.Equal(
            UnsafeEvidencePresenceQuery
                .MethodDefinitionsRowScopeIdentity,
            Assert.Single(descriptor.RowScopes).Identity);
        Assert.True(
            descriptor.TryGetResultContract(
                QuerySpaceTerminalRequirement.Exists,
                out QuerySpaceResultContractDescriptor? contract));
        Assert.Equal(
            UnsafeEvidencePresenceQuery.ResultContract,
            contract.Identity);
        Assert.DoesNotContain(
            QuerySpaceTerminalRequirement.Rows,
            descriptor.Terminals);
        Assert.DoesNotContain(
            QuerySpaceTerminalRequirement.Count,
            descriptor.Terminals);
    }

    [Fact]
    public void ExactTypeBreadthReceiptsOnlyTheSelectedType()
    {
        string path =
            FixtureCatalog.AnalysisStringLiterals.AssemblyPath();
        TypeDefinitionHandle unsafeType = FindType(
            path,
            "ILInspector.Analysis.ImplementationProfileFixtures",
            "ImplementationProfileSample");
        TypeDefinitionHandle safeType = FindType(
            path,
            "ILInspector.Analysis.ImplementationProfileFixtures",
            "ImplementationProfileHiddenImplementationSample");
        ImmutableArray<MethodDefinitionHandle> unsafeMethods =
            MethodsOf(path, unsafeType);
        ImmutableArray<MethodDefinitionHandle> safeMethods =
            MethodsOf(path, safeType);
        using PdbContext context = PdbContext.OpenMetadataOnly(path);

        var available =
            Assert.IsType<UnsafeEvidencePresenceResult.Available>(
                UnsafeEvidencePresenceQuery.ExecuteExactTypes(
                    path,
                    context,
                    unsafeType));

        Assert.True(available.HasEvidence);
        Assert.Equal(
            MethodDefinitionSourceBreadthKind.ExactTypes,
            available.SourceReceipt.Breadth.Kind);
        Assert.Equal(
            [unsafeType],
            available.SourceReceipt.Breadth.Types);
        Assert.True(
            available.SourceReceipt.Coverage.DefinitionsExamined.Count > 0);
        Assert.All(
            safeMethods,
            method => Assert.False(
                available.SourceReceipt.Coverage
                    .DefinitionsExamined.Contains(method)));
        Assert.All(
            available.SourceReceipt.Coverage
                .DefinitionsExamined.Ranges,
            range =>
            {
                Assert.Contains(range.First, unsafeMethods);
                Assert.Contains(range.Last, unsafeMethods);
            });
    }

    [Fact]
    public void FailedExecutionPreservesOutcomeAndReceipt()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"unsafe-evidence-incomplete-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, IncompleteBeforeEvidenceImage());
        try
        {
            using PdbContext context = PdbContext.OpenMetadataOnly(path);

            var incomplete =
                Assert.IsType<UnsafeEvidencePresenceResult.ExecutionIncomplete>(
                    UnsafeEvidencePresenceQuery.Execute(path, context));

            Assert.Equal(ProducerOutcome.Failed, incomplete.Outcome);
            Assert.Equal(
                MethodDefinitionSourceCompletion.ProducerFailed,
                incomplete.SourceReceipt.Completion);
            Assert.Equal(
                ProducerTerminal.Exists,
                incomplete.SourceReceipt.Terminal);
            Assert.True(
                incomplete.SourceReceipt.DefinitionsVisited > 0);
            Assert.Contains(
                "N.Sample::Broken",
                incomplete.Error.Message,
                StringComparison.Ordinal);
            ProducerParticipation participation =
                incomplete.Receipt.For(
                    UnsafeEvidencePresenceProducer.Instance);
            Assert.Equal(ProducerOutcome.Failed, participation.Outcome);
            Assert.Equal(1, participation.UnitsAttempted);
            Assert.Equal(1, participation.UnitsFailed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FailureBeforeExecutionUsesReceiptFreeArm()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"unsafe-evidence-pre-execution-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, IncompleteBeforeEvidenceImage());
        try
        {
            using PdbContext context = PdbContext.OpenMetadataOnly(path);

            var failed = Assert.IsType<UnsafeEvidencePresenceResult.Failed>(
                UnsafeEvidencePresenceQuery.Execute("", context));

            Assert.IsType<ArgumentException>(failed.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static byte[] IncompleteBeforeEvidenceImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("UnsafeEvidenceIncomplete.dll"),
            metadata.GetOrAddGuid(
                new Guid("18167dca-3c97-42ee-ad2b-2a8cb5d6f442")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("UnsafeEvidenceIncomplete"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Sample"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature()
            .Parameters(
                0,
                static returnType => returnType.Void(),
                static _ => { });
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Broken"),
            signatureHandle,
            bodyOffset: 0x00FF_FFF0,
            MetadataTokens.ParameterHandle(1));

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var unsafeCode = new BlobBuilder();
        unsafeCode.WriteByte((byte)ILOpCode.Calli);
        unsafeCode.WriteInt32(0);
        unsafeCode.WriteByte((byte)ILOpCode.Ret);
        int unsafeBodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(unsafeCode),
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("UnsafeLater"),
            signatureHandle,
            unsafeBodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static TypeDefinitionHandle FindType(
        string path,
        string @namespace,
        string name)
    {
        using FileStream stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        MetadataReader reader = image.GetMetadataReader();
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition definition =
                reader.GetTypeDefinition(handle);
            if (reader.GetString(definition.Namespace) == @namespace
                && reader.GetString(definition.Name) == name)
            {
                return handle;
            }
        }

        throw new InvalidOperationException(
            $"Type '{@namespace}.{name}' was not found.");
    }

    static ImmutableArray<MethodDefinitionHandle> MethodsOf(
        string path,
        TypeDefinitionHandle type)
    {
        using FileStream stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        return
        [
            .. image.GetMetadataReader()
                .GetTypeDefinition(type)
                .GetMethods(),
        ];
    }
}
