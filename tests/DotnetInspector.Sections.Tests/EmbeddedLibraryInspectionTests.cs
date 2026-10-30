using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class EmbeddedLibraryInspectionTests
{
    static readonly ApiSurfaceProjectionLimits GenerousLimits = new(
        maxParticipants: 1,
        maxTypes: 10_000,
        maxMembers: 100_000,
        maxInspectionFailures: 100,
        maxTypeForwarders: 10_000,
        maxMetadataRows: 1_000_000);

    [Fact]
    public void ResultContract_IsDetachedFromMetadata()
    {
        Assert.DoesNotContain(
            typeof(EmbeddedLibraryInspectionResult).GetProperties(),
            property => ReferencesMetadata(property.PropertyType));
        Assert.True(typeof(EmbeddedLibraryInspectionExecution).IsValueType);
        Assert.True(typeof(EmbeddedLibraryProvenance).IsValueType);
        Assert.True(typeof(EmbeddedLibraryAssemblyIdentity).IsValueType);
        Assert.True(
            typeof(EmbeddedLibraryApiSurfaceInspectionFailure).IsValueType);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsDetachedEmbeddedLibrary()
    {
        byte[] image = File.ReadAllBytes(
            typeof(EmbeddedLibraryInspectionTests).Assembly.Location);

        EmbeddedLibraryInspectionExecution execution =
            await EmbeddedLibraryInspection.ExecuteAsync(
                "Sections.Tests.dll",
                ImmutableArray.Create(image),
                GenerousLimits,
                cancellationToken: TestContext.Current.CancellationToken);

        InspectionEnvelope<EmbeddedLibraryInspectionResult> envelope =
            execution.Inspection;
        EmbeddedLibraryInspectionResult result = envelope.Content;
        Assert.Equal(
            EmbeddedLibraryInspectionOutcome.Available,
            result.Outcome);
        Assert.Equal(
            "DotnetInspector.Sections.Tests",
            result.Assembly?.Name);
        Assert.Equal(image.LongLength, result.ByteLength);
        Assert.Equal(64, result.Digest.Length);
        ApiSurface surface = Assert.IsType<ApiSurface>(execution.Surface);
        Assert.Contains(
            surface.Types,
            type => type.Name == nameof(EmbeddedLibraryInspectionTests));
        EmbeddedLibraryProvenance provenance =
            Assert.IsType<EmbeddedLibraryProvenance>(result.Provenance);
        Assert.Equal("browser-upload", provenance.ContentRef);
        Assert.Equal($"sha256:{result.Digest}", provenance.Digest);
        Assert.Equal("Sections.Tests.dll", provenance.DeclaredName.ToString());
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsEmptyAndOversizedInputs()
    {
        EmbeddedLibraryInspectionExecution unnamed =
            await EmbeddedLibraryInspection.ExecuteAsync(
                "",
                [1],
                GenerousLimits,
                cancellationToken: TestContext.Current.CancellationToken);
        EmbeddedLibraryInspectionExecution empty =
            await EmbeddedLibraryInspection.ExecuteAsync(
                "empty.dll",
                [],
                GenerousLimits,
                cancellationToken: TestContext.Current.CancellationToken);
        EmbeddedLibraryInspectionExecution oversized =
            await EmbeddedLibraryInspection.ExecuteAsync(
                "large.dll",
                new byte[9].ToImmutableArray(),
                GenerousLimits,
                maximumImageBytes: 8,
                cancellationToken: TestContext.Current.CancellationToken);
        EmbeddedLibraryInspectionExecution truncatedName =
            await EmbeddedLibraryInspection.ExecuteAsync(
                new string('\u202e', 50) + ".dll",
                [1],
                GenerousLimits,
                cancellationToken: TestContext.Current.CancellationToken);

        AssertFailure(
            unnamed,
            EmbeddedLibraryInspectionFailureKind.InvalidDeclaredName);
        AssertFailure(
            empty,
            EmbeddedLibraryInspectionFailureKind.EmptyImage);
        AssertFailure(
            oversized,
            EmbeddedLibraryInspectionFailureKind.ResourceBudget);
        AssertFailure(
            truncatedName,
            EmbeddedLibraryInspectionFailureKind.InvalidDeclaredName);
        Assert.Null(empty.Inspection.Content.Provenance);
        Assert.Null(oversized.Inspection.Content.Provenance);
        Assert.Null(truncatedName.Inspection.Content.Provenance);
    }

    [Fact]
    public async Task ExecuteAsync_DistinguishesNativeAndMalformedImages()
    {
        EmbeddedLibraryInspectionExecution native =
            await EmbeddedLibraryInspection.ExecuteAsync(
                "native.dll",
                "not a PE image"u8.ToArray().ToImmutableArray(),
                GenerousLimits,
                cancellationToken: TestContext.Current.CancellationToken);
        EmbeddedLibraryInspectionExecution malformed =
            await EmbeddedLibraryInspection.ExecuteAsync(
                "malformed.dll",
                BuildMalformedPe().ToImmutableArray(),
                GenerousLimits,
                cancellationToken: TestContext.Current.CancellationToken);

        AssertFailure(
            native,
            EmbeddedLibraryInspectionFailureKind.DescriptorUnavailable);
        AssertFailure(
            malformed,
            EmbeddedLibraryInspectionFailureKind.InvalidImage);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsModuleAndWindowsMetadata()
    {
        EmbeddedLibraryInspectionExecution module =
            await EmbeddedLibraryInspection.ExecuteAsync(
                "module.netmodule",
                BuildManagedImage(isAssembly: false).ToImmutableArray(),
                GenerousLimits,
                cancellationToken: TestContext.Current.CancellationToken);
        EmbeddedLibraryInspectionExecution windowsMetadata =
            await EmbeddedLibraryInspection.ExecuteAsync(
                "unsupported.winmd",
                BuildManagedImage(isAssembly: true, windowsMetadata: true)
                    .ToImmutableArray(),
                GenerousLimits,
                cancellationToken: TestContext.Current.CancellationToken);

        AssertFailure(
            module,
            EmbeddedLibraryInspectionFailureKind.NotAssembly);
        AssertFailure(
            windowsMetadata,
            EmbeddedLibraryInspectionFailureKind.UnsupportedMetadataFormat);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsTruncatedProjection()
    {
        byte[] image = File.ReadAllBytes(
            typeof(EmbeddedLibraryInspectionTests).Assembly.Location);
        var limits = new ApiSurfaceProjectionLimits(
            maxParticipants: 1,
            maxTypes: 1,
            maxMembers: 100_000,
            maxInspectionFailures: 100,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000);

        EmbeddedLibraryInspectionExecution execution =
            await EmbeddedLibraryInspection.ExecuteAsync(
                "Sections.Tests.dll",
                ImmutableArray.Create(image),
                limits,
                cancellationToken: TestContext.Current.CancellationToken);

        AssertFailure(
            execution,
            EmbeddedLibraryInspectionFailureKind.ProjectionTruncated);
    }

    static void AssertFailure(
        EmbeddedLibraryInspectionExecution execution,
        EmbeddedLibraryInspectionFailureKind kind)
    {
        InspectionEnvelope<EmbeddedLibraryInspectionResult> envelope =
            execution.Inspection;
        EmbeddedLibraryInspectionResult result = envelope.Content;
        Assert.Equal(
            EmbeddedLibraryInspectionOutcome.Rejected,
            result.Outcome);
        Assert.Equal(kind, result.Failure?.Kind);
        Assert.Null(execution.Surface);
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
    }

    static bool ReferencesMetadata(Type type) =>
        type.Assembly.GetName().Name
            is "ILInspector.Metadata" or "ILInspector.MetadataPrimitives"
        || (type.IsGenericType
            && type.GetGenericArguments().Any(ReferencesMetadata))
        || (type.HasElementType
            && type.GetElementType() is { } element
            && ReferencesMetadata(element));

    static byte[] BuildMalformedPe()
    {
        byte[] image = new byte[128];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        image[0x3c] = 0x40;
        image[0x40] = (byte)'P';
        image[0x41] = (byte)'E';
        return image;
    }

    static byte[] BuildManagedImage(
        bool isAssembly,
        bool windowsMetadata = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                isAssembly ? "Embedded.dll" : "Embedded.netmodule"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        if (isAssembly)
        {
            metadata.AddAssembly(
                metadata.GetOrAddString("Embedded"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        }

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                windowsMetadata
                    ? "WindowsRuntime 1.4"
                    : "v4.0.30319",
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
