using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.LibraryMetadata.Tests;

public sealed class LibraryTypeDeclarationInventoryInspectionTests
{
    private const int MetadataRootFixedPrefixLength = 16;

    private static readonly LibraryTypeDeclarationInventoryInspectionBounds
        s_bounds = new(
            maximumAssemblyBytes: 16 * 1024 * 1024,
            maximumRetainedDeclarations: 100_000,
            maximumMetadataRows: 1_000_000,
            maximumRetainedTextCharacters: 20_000_000);

    [Fact]
    public async Task
        RealSystemTextJson_IssuesExactResourceFreeCorrespondence()
    {
        byte[] bytes = await RealAssetAsync("System.Text.Json.dll");
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(bytes));
        using LibraryOperationLease operation = library.IssueOperation();

        LibraryTypeDeclarationInventoryInspectionOutcome.Completed completed =
            Assert.IsType<
                LibraryTypeDeclarationInventoryInspectionOutcome.Completed>(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    Request(library.Reference),
                    operation,
                    TestContext.Current.CancellationToken));
        LibraryTypeDeclarationInventoryCorrespondence correspondence =
            completed.Correspondence;

        Assert.Same(library.Reference, correspondence.Library);
        Assert.Same(
            library.Reference.ApiAssembly,
            correspondence.ApiContent);
        Assert.NotEqual(Guid.Empty, correspondence.ModuleVersionId);
        Assert.Equal(bytes.Length, correspondence.AssemblyBytes);
        Assert.True(correspondence.MetadataRows > 0);
        Assert.True(correspondence.RetainedTextCharacters > 0);
        Assert.Equal(
            correspondence.Inventory.Declarations.Length,
            correspondence.DeclarationCount);
        Assert.True(
            correspondence.Inventory.Identity.IsEquivalentTo(
                correspondence.ApiContent.AssemblyIdentity!.Identity));
        Assert.Contains(
            correspondence.Inventory.Declarations,
            declaration =>
                declaration.Kind
                    == AssemblyTypeDeclarationKind.Definition
                && declaration.Name
                    == Name("System.Text.Json", "JsonSerializer"));
        AssertResourceFree(
            typeof(LibraryTypeDeclarationInventoryInspectionOutcome.Completed));
        AssertResourceFree(
            typeof(LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete));
        AssertResourceFree(
            typeof(LibraryTypeDeclarationInventoryCorrespondence));
        AssertResourceFree(
            typeof(LibraryTypeDeclarationInventorySubject));
        AssertResourceFree(typeof(AssemblyTypeDeclarationInventory));
        Assert.Empty(
            typeof(LibraryTypeDeclarationInventoryCorrespondence)
                .GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public async Task RealFacade_PreservesForwardersAsForwarders()
    {
        byte[] bytes = await RealAssetAsync("netstandard.dll");
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(bytes));
        using LibraryOperationLease operation = library.IssueOperation();

        LibraryTypeDeclarationInventoryCorrespondence correspondence =
            Completed(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    Request(library.Reference),
                    operation,
                    TestContext.Current.CancellationToken));

        AssemblyTypeDeclaration declaration = Assert.Single(
            correspondence.Inventory.Declarations,
            candidate =>
                candidate.Name == Name("System", "Object"));
        Assert.Equal(
            AssemblyTypeDeclarationKind.Forwarder,
            declaration.Kind);
        Assert.DoesNotContain(
            correspondence.Inventory.Declarations,
            candidate =>
                candidate.Name == declaration.Name
                && candidate.Kind
                    == AssemblyTypeDeclarationKind.Definition);
    }

    [Fact]
    public async Task
        EquivalentIdentityLibraries_RetainDistinctExactCorrespondence()
    {
        byte[] bytes = await RealAssetAsync("System.Text.Json.dll");
        ManagedMetadataIdentity.Assembly identity = Identity(bytes);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes, bytes]);
        await using OwnedLibrary first =
            OwnedLibrary.Create(artifacts, artifactIndex: 0, identity);
        await using OwnedLibrary second =
            OwnedLibrary.Create(artifacts, artifactIndex: 1, identity);
        using LibraryOperationLease firstOperation =
            first.IssueOperation();
        using LibraryOperationLease secondOperation =
            second.IssueOperation();

        LibraryTypeDeclarationInventoryCorrespondence firstCorrespondence =
            Completed(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    Request(first.Reference),
                    firstOperation,
                    TestContext.Current.CancellationToken));
        LibraryTypeDeclarationInventoryCorrespondence secondCorrespondence =
            Completed(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    Request(second.Reference),
                    secondOperation,
                    TestContext.Current.CancellationToken));

        Assert.True(
            firstCorrespondence.Inventory.Identity.IsEquivalentTo(
                secondCorrespondence.Inventory.Identity));
        Assert.NotSame(first.Reference, second.Reference);
        Assert.NotSame(
            firstCorrespondence.ApiContent,
            secondCorrespondence.ApiContent);
        Assert.NotSame(
            firstCorrespondence.ApiContent.Artifact,
            secondCorrespondence.ApiContent.Artifact);
        Assert.NotSame(
            firstCorrespondence.Inventory,
            secondCorrespondence.Inventory);
    }

    [Fact]
    public async Task AssemblyByteBound_IsCheckedBeforeMetadataInspection()
    {
        byte[] bytes = await RealAssetAsync("System.Text.Json.dll");
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(bytes));
        using LibraryOperationLease operation = library.IssueOperation();
        var bounds = new LibraryTypeDeclarationInventoryInspectionBounds(
            bytes.Length - 1,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue);

        LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete incomplete =
            Assert.IsType<
                LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete>(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    new(
                        library.Reference,
                        bounds),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            LibraryTypeDeclarationInventoryInspectionBound.AssemblyBytes,
            incomplete.Bound);
        Assert.Null(incomplete.Subject);
        Assert.Equal(bytes.Length, incomplete.MeasuredAssemblyBytes);
        Assert.Null(incomplete.MeasuredMetadataRows);
        Assert.Null(incomplete.MeasuredDeclarations);
        Assert.Null(incomplete.MeasuredRetainedTextCharacters);
    }

    [Fact]
    public async Task
        MetadataRowBound_IsCheckedBeforeDeclarationInventory()
    {
        byte[] bytes = await RealAssetAsync("System.Text.Json.dll");
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(bytes));
        using LibraryOperationLease operation = library.IssueOperation();
        var bounds = new LibraryTypeDeclarationInventoryInspectionBounds(
            bytes.Length,
            int.MaxValue,
            maximumMetadataRows: 1,
            maximumRetainedTextCharacters: int.MaxValue);

        LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete incomplete =
            Assert.IsType<
                LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete>(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    new(
                        library.Reference,
                        bounds),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            LibraryTypeDeclarationInventoryInspectionBound.MetadataRows,
            incomplete.Bound);
        Assert.NotNull(incomplete.Subject);
        Assert.True(incomplete.MeasuredMetadataRows > 1);
        Assert.Null(incomplete.MeasuredDeclarations);
        Assert.Null(incomplete.MeasuredRetainedTextCharacters);
    }

    [Fact]
    public async Task
        OneDeclarationBeyondBound_IsIncompleteWithoutShortenedInventory()
    {
        byte[] bytes = await RealAssetAsync("System.Text.Json.dll");
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(bytes));
        using LibraryOperationLease operation = library.IssueOperation();
        LibraryTypeDeclarationInventoryCorrespondence complete =
            Completed(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    Request(library.Reference),
                    operation,
                    TestContext.Current.CancellationToken));
        var bounds = new LibraryTypeDeclarationInventoryInspectionBounds(
            bytes.Length,
            complete.DeclarationCount - 1,
            int.MaxValue,
            int.MaxValue);

        LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete incomplete =
            Assert.IsType<
                LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete>(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    new(
                        library.Reference,
                        bounds),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            LibraryTypeDeclarationInventoryInspectionBound
                .RetainedDeclarations,
            incomplete.Bound);
        Assert.NotNull(incomplete.Subject);
        Assert.Equal(bytes.Length, incomplete.MeasuredAssemblyBytes);
        Assert.True(incomplete.MeasuredMetadataRows > 0);
        Assert.Equal(
            complete.DeclarationCount,
            incomplete.MeasuredDeclarations);
        Assert.True(incomplete.MeasuredRetainedTextCharacters > 0);
    }

    [Fact]
    public async Task
        RetainedTextBound_IsCheckedDuringDeclarationConstruction()
    {
        byte[] bytes = await RealAssetAsync("System.Text.Json.dll");
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(bytes));
        using LibraryOperationLease operation = library.IssueOperation();
        var bounds = new LibraryTypeDeclarationInventoryInspectionBounds(
            bytes.Length,
            int.MaxValue,
            int.MaxValue,
            maximumRetainedTextCharacters: 0);

        LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete incomplete =
            Assert.IsType<
                LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete>(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    new(
                        library.Reference,
                        bounds),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            LibraryTypeDeclarationInventoryInspectionBound
                .RetainedTextCharacters,
            incomplete.Bound);
        Assert.NotNull(incomplete.Subject);
        Assert.True(incomplete.MeasuredMetadataRows > 0);
        Assert.NotNull(incomplete.MeasuredDeclarations);
        Assert.True(incomplete.MeasuredRetainedTextCharacters > 0);
    }

    [Fact]
    public async Task ForeignLibraryLease_IsRejectedBeforeContentAccess()
    {
        byte[] bytes = await RealAssetAsync("System.Text.Json.dll");
        ManagedMetadataIdentity.Assembly identity = Identity(bytes);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes, bytes]);
        await using OwnedLibrary requested =
            OwnedLibrary.Create(artifacts, artifactIndex: 0, identity);
        await using OwnedLibrary foreign =
            OwnedLibrary.Create(artifacts, artifactIndex: 1, identity);
        using LibraryOperationLease foreignOperation =
            foreign.IssueOperation();

        LibraryTypeDeclarationInventoryInspectionOutcome.Rejected rejected =
            Assert.IsType<
                LibraryTypeDeclarationInventoryInspectionOutcome.Rejected>(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    Request(requested.Reference),
                    foreignOperation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            LibraryTypeDeclarationInventoryInspectionRejectionKind
                .LeaseReferenceMismatch,
            rejected.Kind);
    }

    [Fact]
    public async Task AssemblyIdentityMismatch_IsVisibleRejection()
    {
        byte[] bytes = await RealAssetAsync("System.Text.Json.dll");
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        var wrongIdentity = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "System.Text.Json",
                new Version(99, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                wrongIdentity);
        using LibraryOperationLease operation = library.IssueOperation();

        LibraryTypeDeclarationInventoryInspectionOutcome.Rejected rejected =
            Assert.IsType<
                LibraryTypeDeclarationInventoryInspectionOutcome.Rejected>(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    Request(library.Reference),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            LibraryTypeDeclarationInventoryInspectionRejectionKind
                .AssemblyIdentityMismatch,
            rejected.Kind);
    }

    [Theory]
    [InlineData(
        FailureImageKind.NotManagedAssembly,
        LibraryTypeDeclarationInventoryInspectionFailureKind
            .NotManagedAssembly)]
    [InlineData(
        FailureImageKind.ManagedModule,
        LibraryTypeDeclarationInventoryInspectionFailureKind.ManagedModule)]
    [InlineData(
        FailureImageKind.UnsupportedWindowsMetadata,
        LibraryTypeDeclarationInventoryInspectionFailureKind
            .UnsupportedWindowsMetadata)]
    [InlineData(
        FailureImageKind.MalformedMetadata,
        LibraryTypeDeclarationInventoryInspectionFailureKind
            .MalformedMetadata)]
    [InlineData(
        FailureImageKind.EmptyModuleVersionId,
        LibraryTypeDeclarationInventoryInspectionFailureKind
            .EmptyModuleVersionId)]
    public async Task KnownMetadataFailures_AreTyped(
        FailureImageKind imageKind,
        LibraryTypeDeclarationInventoryInspectionFailureKind expected)
    {
        (byte[] bytes, ManagedMetadataIdentity.Assembly identity) =
            FailureImage(imageKind);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                identity);
        using LibraryOperationLease operation = library.IssueOperation();

        LibraryTypeDeclarationInventoryInspectionOutcome.Failed failed =
            Assert.IsType<
                LibraryTypeDeclarationInventoryInspectionOutcome.Failed>(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    Request(library.Reference),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(expected, failed.Kind);
    }

    [Fact]
    public async Task CancellationBeforeBorrow_StopsInspection()
    {
        byte[] bytes = await RealAssetAsync("System.Text.Json.dll");
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(bytes));
        using LibraryOperationLease operation = library.IssueOperation();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => LibraryTypeDeclarationInventoryInspection.Execute(
                Request(library.Reference),
                operation,
                cancellation.Token));

        Assert.NotNull(
            Completed(
                LibraryTypeDeclarationInventoryInspection.Execute(
                    Request(library.Reference),
                    operation,
                    TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task CompletedCorrespondence_SurvivesLibraryRetirement()
    {
        byte[] bytes = await RealAssetAsync("System.Text.Json.dll");
        LibraryTypeDeclarationInventoryCorrespondence correspondence =
            await InspectAndRetireAsync(bytes);

        Assert.Equal(bytes.Length, correspondence.AssemblyBytes);
        Assert.Contains(
            correspondence.Inventory.Declarations,
            declaration =>
                declaration.Name
                    == Name("System.Text.Json", "JsonSerializer"));
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(-1, 1, 1, 1)]
    [InlineData(1, -1, 1, 1)]
    [InlineData(1, 1, -1, 1)]
    [InlineData(1, 1, 1, -1)]
    public void Bounds_RequirePositiveFiniteValues(
        int maximumAssemblyBytes,
        int maximumRetainedDeclarations,
        int maximumMetadataRows,
        int maximumRetainedTextCharacters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LibraryTypeDeclarationInventoryInspectionBounds(
                maximumAssemblyBytes,
                maximumRetainedDeclarations,
                maximumMetadataRows,
                maximumRetainedTextCharacters));
    }

    [Fact]
    public void Bounds_AllowZeroWorkBudgets()
    {
        var bounds = new LibraryTypeDeclarationInventoryInspectionBounds(
            maximumAssemblyBytes: 1,
            maximumRetainedDeclarations: 0,
            maximumMetadataRows: 0,
            maximumRetainedTextCharacters: 0);

        Assert.Equal(0, bounds.MaximumRetainedDeclarations);
        Assert.Equal(0, bounds.MaximumMetadataRows);
        Assert.Equal(0, bounds.MaximumRetainedTextCharacters);
    }

    private static LibraryTypeDeclarationInventoryInspectionRequest Request(
        LibraryReference library) =>
        new(library, s_bounds);

    private static LibraryTypeDeclarationInventoryCorrespondence Completed(
        LibraryTypeDeclarationInventoryInspectionOutcome outcome) =>
        Assert.IsType<
                LibraryTypeDeclarationInventoryInspectionOutcome.Completed>(
                outcome)
            .Correspondence;

    private static async Task<
        LibraryTypeDeclarationInventoryCorrespondence>
        InspectAndRetireAsync(byte[] bytes)
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([bytes]);
        await using OwnedLibrary library =
            OwnedLibrary.Create(
                artifacts,
                artifactIndex: 0,
                Identity(bytes));
        using LibraryOperationLease operation = library.IssueOperation();
        return Completed(
            LibraryTypeDeclarationInventoryInspection.Execute(
                Request(library.Reference),
                operation,
                TestContext.Current.CancellationToken));
    }

    private static async Task<byte[]> RealAssetAsync(string name) =>
        await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "LibraryMetadata",
                name),
            TestContext.Current.CancellationToken);

    private static ManagedMetadataIdentity.Assembly Identity(
        byte[] content)
    {
        using var peReader = new PEReader(
            new MemoryStream(content, writable: false));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        return new ManagedMetadataIdentity.Assembly(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
    }

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

    private static (
        byte[] Bytes,
        ManagedMetadataIdentity.Assembly Identity)
        FailureImage(FailureImageKind kind)
    {
        var arbitraryIdentity = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "Failure",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));
        switch (kind)
        {
            case FailureImageKind.NotManagedAssembly:
                {
                    byte[] original = BuildImage();
                    ManagedMetadataIdentity.Assembly identity =
                        Identity(original);
                    byte[] image = (byte[])original.Clone();
                    RemoveMetadataDirectory(image);
                    return (image, identity);
                }
            case FailureImageKind.ManagedModule:
                return (
                    BuildImage(includeAssembly: false),
                    arbitraryIdentity);
            case FailureImageKind.UnsupportedWindowsMetadata:
                {
                    byte[] image = BuildImage(
                        metadataVersion:
                            "WindowsRuntime 1.4;CLR v4.0.30319");
                    TruncateMetadataAfterVersionField(image);
                    return (image, arbitraryIdentity);
                }
            case FailureImageKind.MalformedMetadata:
                {
                    byte[] original = BuildImage();
                    ManagedMetadataIdentity.Assembly identity =
                        Identity(original);
                    byte[] image = (byte[])original.Clone();
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        image.AsSpan(MetadataStart(image), sizeof(uint)),
                        0xDEADBEEF);
                    return (image, identity);
                }
            case FailureImageKind.EmptyModuleVersionId:
                {
                    byte[] image = BuildImage(emptyModuleVersionId: true);
                    return (image, Identity(image));
                }
            default:
                throw new InvalidOperationException(
                    "Unknown failure image kind.");
        }
    }

    private static byte[] BuildImage(
        bool includeAssembly = true,
        bool emptyModuleVersionId = false,
        string metadataVersion = "v4.0.30319")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Failure.dll"),
            emptyModuleVersionId
                ? default
                : metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        if (includeAssembly)
        {
            metadata.AddAssembly(
                metadata.GetOrAddString("Failure"),
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
        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                metadataVersion,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    private static int MetadataStart(byte[] image)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        return peReader.PEHeaders.MetadataStartOffset;
    }

    private static int CorHeaderStart(byte[] image)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        return peReader.PEHeaders.CorHeaderStartOffset;
    }

    private static void RemoveMetadataDirectory(byte[] image)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        PEHeader peHeader = peReader.PEHeaders.PEHeader!;
        int directoryBase =
            peReader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        image.AsSpan(directoryBase + (14 * 8), 8).Clear();
    }

    private static void TruncateMetadataAfterVersionField(byte[] image)
    {
        int metadataStart = MetadataStart(image);
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(CorHeaderStart(image) + 12, sizeof(int)),
            MetadataRootFixedPrefixLength
                + versionLength);
    }

    private static void AssertResourceFree(Type type)
    {
        Assert.False(typeof(IDisposable).IsAssignableFrom(type));
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(type));
        Assert.DoesNotContain(
            type.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic),
            field =>
                typeof(IDisposable).IsAssignableFrom(field.FieldType)
                || typeof(IAsyncDisposable).IsAssignableFrom(
                    field.FieldType)
                || typeof(Delegate).IsAssignableFrom(field.FieldType)
                || typeof(Stream).IsAssignableFrom(field.FieldType));
    }

    public enum FailureImageKind
    {
        NotManagedAssembly,
        ManagedModule,
        UnsupportedWindowsMetadata,
        MalformedMetadata,
        EmptyModuleVersionId,
    }

    private sealed class OwnedLibrary : IAsyncDisposable
    {
        private readonly LibraryContentOwner _owner;

        private OwnedLibrary(
            LibraryReference reference,
            LibraryContentOwner owner)
        {
            Reference = reference;
            _owner = owner;
        }

        public LibraryReference Reference { get; }

        public static OwnedLibrary Create(
            ArtifactFixture artifacts,
            int artifactIndex,
            ManagedMetadataIdentity.Assembly identity)
        {
            LibraryReference reference =
                LibraryReference.CreateDirect(
                    new LibraryAssemblyCorrespondence(
                        artifacts[artifactIndex],
                        identity,
                        artifacts[artifactIndex],
                        identity));
            return new OwnedLibrary(
                reference,
                new LibraryContentOwner(
                    reference,
                    [artifacts.IssueContentLease(artifactIndex)]));
        }

        public LibraryOperationLease IssueOperation() =>
            Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                    _owner.IssueOperationLease(Reference))
                .Lease;

        public ValueTask DisposeAsync() => _owner.DisposeAsync();
    }

    private sealed class ArtifactFixture : IAsyncDisposable
    {
        private readonly ArtifactSetSession _session;
        private readonly ArtifactQueryLease _queryLease;
        private readonly IReadOnlyList<ArtifactContentReference> _references;

        private ArtifactFixture(
            ArtifactSetSession session,
            ArtifactQueryLease queryLease,
            IReadOnlyList<ArtifactContentReference> references)
        {
            _session = session;
            _queryLease = queryLease;
            _references = references;
        }

        public ArtifactContentReference this[int index] =>
            _references[index];

        public ArtifactContentLease IssueContentLease(int index) =>
            _session.IssueContentLease(
                _references[index],
                _queryLease);

        public static async Task<ArtifactFixture> CreateAsync(
            IReadOnlyList<byte[]> contents)
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            var session = new ArtifactSetSession();
            try
            {
                for (int index = 0; index < contents.Count; index++)
                {
                    int retainedIndex = index;
                    byte[] retainedContent = contents[index];
                    await session.AddRequiredAcquisitionAsync(
                        (scope, _) =>
                        {
                            ArtifactContribution contribution =
                                scope.Register(
                                    new Provenance(
                                        $"library-metadata-declarations-{retainedIndex}"),
                                    _ => new MemoryStream(
                                        retainedContent,
                                        writable: false));
                            return ValueTask.FromResult<
                                ArtifactAcquisitionOutcome>(
                                    new ArtifactAcquisitionOutcome.Acquired(
                                        [contribution],
                                        ArtifactAcquisitionLeases.None));
                        },
                        cancellationToken: cancellationToken);
                }

                Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                    await session.SealAsync(cancellationToken));
                ArtifactQueryLease queryLease =
                    session.IssueLease(
                        session.CreateQueryAuthorization());
                IReadOnlyList<ArtifactContentReference> references =
                    session.GetCatalog(queryLease)
                        .Select(
                            descriptor =>
                                session.GetContentReference(
                                    descriptor.Identity,
                                    queryLease))
                        .ToArray();
                return new ArtifactFixture(
                    session,
                    queryLease,
                    references);
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _queryLease.Dispose();
            await _session.DisposeAsync();
        }
    }

    private sealed record Provenance(string Name) : IArtifactProvenance;
}
