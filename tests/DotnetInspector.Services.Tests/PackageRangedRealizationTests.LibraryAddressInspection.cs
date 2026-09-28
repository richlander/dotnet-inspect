using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using ILInspector.Research;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageRangedRealizationTests
{
    private const string AddressPackageId = "System.Text.Json";
    private const string AddressPackageVersion = "10.0.0";
    private const string AddressApiPath =
        "ref/net10.0/System.Text.Json.dll";
    private const string AddressImplementationPath =
        "lib/net10.0/System.Text.Json.dll";
    private const string AddressPortablePdbPath =
        "lib/net10.0/System.Text.Json.pdb";

    [Fact]
    public async Task
        RangedUnmaterializedPortablePdbIsOmitted()
    {
        (RangedEnvironment environment,
            PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff,
            _) = await AcquireAddressInputAsync();
        await using (environment)
        {
            PackageHouseLibraryMaterializationOutcome.Completed completed =
                Assert.IsType<
                    PackageHouseLibraryMaterializationOutcome.Completed>(
                    await PackageHouseLibraryMaterializer.MaterializeAsync(
                        settlement,
                        handoff,
                        PackageHouseLibraryOptionalArtifacts
                            .ImplementationPortablePdb,
                        cancellationToken:
                            TestContext.Current.CancellationToken));

            Assert.Equal(
                PackageHouseLibraryOptionalArtifactOmissionKind.Unreadable,
                completed.Receipt.ImplementationPortablePdbOmission);
            Assert.DoesNotContain(
                completed.Receipt.Library.Contents,
                contentReference =>
                    contentReference.HasRole(
                        LibraryContentRole.PortablePdb));
            await completed.Owner.DisposeAsync();
            await completed.Artifacts.DisposeAsync();
        }
    }

    [Fact]
    public async Task
        SourceIntentContinuesWhenRangedPortablePdbWasNotMaterialized()
    {
        (RangedEnvironment environment,
            PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff,
            byte[] assembly) = await AcquireAddressInputAsync();
        await using (environment)
        {
            (int methodToken, int ilOffset) =
                FirstAddress(assembly);

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
        }
    }

    private static async Task<(
        RangedEnvironment Environment,
        PackageHouseSettlement.Acquired Settlement,
        PackageHouseLibraryHandoff.Compile Handoff,
        byte[] Assembly)> AcquireAddressInputAsync()
    {
        byte[] assembly = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "PackageHouse",
                "System.Text.Json.dll"));
        byte[] archive = CreateAddressArchive(assembly);
        var server = new RangeFeed(
            AddressPackageId,
            AddressPackageVersion,
            archive);
        RangedEnvironment environment =
            RangedEnvironment.Create(server);
        try
        {
            PackageHouseSettlement.Acquired settlement =
                Assert.IsType<PackageHouseSettlement.Acquired>(
                    await environment.RealizeAsync(
                        new InMemoryPackageStore(),
                        PackagePayloadAccess.Ranged,
                        "net10.0",
                        sizeCut: 0,
                        AddressPackageId,
                        AddressPackageVersion,
                        ["System.Text.Json.dll"],
                        libraryHandoff:
                            PackageHouseLibraryHandoffMode
                                .SelectedLibraries));
            var content = Assert.IsType<RangedPackageContent>(
                settlement.Payload.Content);
            Assert.True(content.IsMaterialized(AddressApiPath));
            Assert.True(
                content.IsMaterialized(
                    AddressImplementationPath));
            Assert.False(
                content.IsMaterialized(
                    AddressPortablePdbPath));
            PackageHouseRealizationReceipt.Compile realization =
                Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                    settlement.Result.Evidence.Realization);
            PackageHouseLibraryHandoff.Compile handoff =
                Assert.IsType<PackageHouseLibraryHandoff.Compile>(
                    Assert.Single(realization.LibraryHandoffs));
            return (environment, settlement, handoff, assembly);
        }
        catch
        {
            await environment.DisposeAsync();
            throw;
        }
    }

    private static byte[] CreateAddressArchive(byte[] assembly)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            Add(
                $"{AddressPackageId}.nuspec",
                Encoding.UTF8.GetBytes(
                    "<?xml version=\"1.0\"?><package><metadata>"
                    + $"<id>{AddressPackageId}</id>"
                    + $"<version>{AddressPackageVersion}</version>"
                    + "<authors>test</authors>"
                    + "<description>test</description>"
                    + "</metadata></package>"));
            Add(AddressApiPath, assembly);
            Add(AddressImplementationPath, assembly);
            Add(AddressPortablePdbPath, [0]);

            void Add(string path, byte[] content)
            {
                using Stream entry = zip.CreateEntry(
                    path,
                    CompressionLevel.NoCompression).Open();
                entry.Write(content);
            }
        }

        return output.ToArray();
    }

    private static (int MethodToken, int ILOffset)
        FirstAddress(byte[] assembly)
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
