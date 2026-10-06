using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Packages;
using DotnetInspector.Sections;
using ILInspector.Research;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    [Fact]
    public async Task
        SelectedPackageLibraryExecutesSharedAddressInspection()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        InMemoryPackageContent content = CreatePackageContent(
            (MaterializedLibraryPath, assembly));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        (int methodToken, int ilOffset) =
            FirstPackageAddress(assembly);

        InspectionEnvelope<LibraryAddressInspectionOutcome> inspection =
            await PackageLibraryAddressInspection.ExecuteAsync(
                settlement,
                handoff,
                new LibraryAddressIntent.IlPoint(
                    methodToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities
                        .InstructionContext),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var completed = Assert.IsType<
            LibraryAddressInspectionOutcome.Completed>(
                inspection.Content);
        var document = Assert.IsType<
            LibraryAddressDocument.IlPoint>(completed.Document);
        var resolved = Assert.IsType<
            LibraryIlAddressOutcome.Resolved>(document.Result);
        Assert.Equal(methodToken, resolved.MethodToken);
        Assert.Equal(ilOffset, resolved.ILOffset);
        Assert.NotNull(resolved.Projection.InstructionContext);
        Assert.Empty(inspection.Diagnostics);
        Assert.IsType<InspectionShare.NonProjectable>(
            inspection.Share);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        SourceFreeIntentDoesNotMaterializeAdjacentPortablePdb()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        InMemoryPackageContent content = CreatePackageContent(
            (MaterializedLibraryPath, assembly),
            (
                "lib/net10.0/System.Text.Json.pdb",
                "not a portable pdb"u8.ToArray()));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        (int methodToken, int ilOffset) =
            FirstPackageAddress(assembly);

        InspectionEnvelope<LibraryAddressInspectionOutcome> inspection =
            await PackageLibraryAddressInspection.ExecuteAsync(
                settlement,
                handoff,
                new LibraryAddressIntent.IlPoint(
                    methodToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities
                        .InstructionContext),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<LibraryAddressInspectionOutcome.Completed>(
            inspection.Content);
        Assert.DoesNotContain(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code.Contains(
                    "pdb",
                    StringComparison.OrdinalIgnoreCase));
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        SourceIntentPreservesMalformedPortablePdbOutcome()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        InMemoryPackageContent content = CreatePackageContent(
            (MaterializedLibraryPath, assembly),
            (
                "lib/net10.0/System.Text.Json.pdb",
                "not a portable pdb"u8.ToArray()));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        (int methodToken, int ilOffset) =
            FirstPackageAddress(assembly);

        InspectionEnvelope<LibraryAddressInspectionOutcome> inspection =
            await PackageLibraryAddressInspection.ExecuteAsync(
                settlement,
                handoff,
                new LibraryAddressIntent.IlPoint(
                    methodToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities.SourceLocation),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            LibraryAddressInspectionFailure.MalformedMetadata,
            Assert.IsType<LibraryAddressInspectionOutcome.Failed>(
                    inspection.Content)
                .Reason);
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "library-address.failed.malformed-metadata");
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        SourceIntentDisclosesPortablePdbMaterializationLimit()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        byte[] portablePdb =
            GC.AllocateUninitializedArray<byte>(
                assembly.Length + 1);
        InMemoryPackageContent content = CreatePackageContent(
            (MaterializedLibraryPath, assembly),
            (
                "lib/net10.0/System.Text.Json.pdb",
                portablePdb));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        (int methodToken, int ilOffset) =
            FirstPackageAddress(assembly);
        var limits = new PackageLibraryAddressInspectionLimits
        {
            Materialization =
                new PackageHouseLibraryMaterializationLimits
                {
                    MaxContentBytes = assembly.LongLength,
                    MaxRetainedBytes = assembly.LongLength,
                },
        };

        InspectionEnvelope<LibraryAddressInspectionOutcome> inspection =
            await PackageLibraryAddressInspection.ExecuteAsync(
                settlement,
                handoff,
                new LibraryAddressIntent.IlPoint(
                    methodToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities.SourceLocation),
                limits,
                TestContext.Current.CancellationToken);

        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "package-library-address.portable-pdb.content-byte-limit");
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        SourceIntentContinuesAfterUnreadablePortablePdbOmission()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        var content = new ManifestListedUnreadablePackageContent(
            CreatePackageContent(
                (MaterializedLibraryPath, assembly),
                (
                    "lib/net10.0/System.Text.Json.pdb",
                    "listed but unavailable"u8.ToArray())),
            "lib/net10.0/System.Text.Json.pdb",
            throwOnOpen: false);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        (int methodToken, int ilOffset) =
            FirstPackageAddress(assembly);

        InspectionEnvelope<LibraryAddressInspectionOutcome> inspection =
            await PackageLibraryAddressInspection.ExecuteAsync(
                settlement,
                handoff,
                new LibraryAddressIntent.IlPoint(
                    methodToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities.SourceLocation),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsNotType<LibraryAddressInspectionOutcome.Failed>(
            inspection.Content);
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "package-library-address.portable-pdb.unreadable");
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        ForeignHandoffProducesVisibleMaterializationFailure()
    {
        byte[] archive = CreatePackageArchive(
            (
                MaterializedLibraryPath,
                ReadRealAsset("System.Text.Json.dll")));
        var firstContent = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        var secondContent = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment firstEnvironment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        await using HouseEnvironment secondEnvironment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired firstSettlement, _) =
            await ExecuteMaterializationInputAsync(
                firstEnvironment,
                firstContent);
        (_, PackageHouseLibraryHandoff.Compile secondHandoff) =
            await ExecuteMaterializationInputAsync(
                secondEnvironment,
                secondContent);

        InspectionEnvelope<LibraryAddressInspectionOutcome> inspection =
            await PackageLibraryAddressInspection.ExecuteAsync(
                firstSettlement,
                secondHandoff,
                new LibraryAddressIntent.HeapPoint(
                    ILInspector.Metadata.MetadataRootKind.Cli,
                    ILInspector.Metadata.HeapKind.String,
                    0),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            LibraryAddressInspectionFailure.Inspection,
            Assert.IsType<LibraryAddressInspectionOutcome.Failed>(
                    inspection.Content)
                .Reason);
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "package-library-address.materialization.invalid-settlement");
        await firstEnvironment.AssertRootSettledAsync();
        await secondEnvironment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        MaterializationByteLimitProducesVisibleAddressFailure()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        InMemoryPackageContent content = CreatePackageContent(
            (MaterializedLibraryPath, assembly));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        var limits = new PackageLibraryAddressInspectionLimits
        {
            Materialization =
                new PackageHouseLibraryMaterializationLimits
                {
                    MaxContentBytes = assembly.LongLength - 1,
                    MaxRetainedBytes = assembly.LongLength,
                },
        };

        InspectionEnvelope<LibraryAddressInspectionOutcome> inspection =
            await PackageLibraryAddressInspection.ExecuteAsync(
                settlement,
                handoff,
                new LibraryAddressIntent.HeapPoint(
                    ILInspector.Metadata.MetadataRootKind.Cli,
                    ILInspector.Metadata.HeapKind.String,
                    0),
                limits,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            LibraryAddressInspectionFailure.Inspection,
            Assert.IsType<LibraryAddressInspectionOutcome.Failed>(
                    inspection.Content)
                .Reason);
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "package-library-address.materialization.content-byte-limit");
        await environment.AssertRootSettledAsync();
    }

    private static (int MethodToken, int ILOffset)
        FirstPackageAddress(byte[] assembly)
    {
        using var reader = new PEReader(
            new MemoryStream(assembly, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        foreach (MethodDefinitionHandle handle
            in metadata.MethodDefinitions)
        {
            MethodDefinition method =
                metadata.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            MethodBodyBlock body =
                reader.GetMethodBody(method.RelativeVirtualAddress);
            if (body.GetILBytes() is not { Length: > 0 })
                continue;
            return (MetadataTokens.GetToken(handle), 0);
        }

        throw new InvalidOperationException(
            "The real package Library contains no method body.");
    }
}
