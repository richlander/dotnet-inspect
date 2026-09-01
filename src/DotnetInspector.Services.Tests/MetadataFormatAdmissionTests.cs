using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public sealed class MetadataFormatAdmissionTests
{
    [Fact]
    public void PlatformHasType_RejectsWindowsMetadata()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-platform-{Guid.NewGuid():N}.winmd");
        File.WriteAllBytes(path, BuildManagedWindowsMetadata());
        try
        {
            Assert.Throws<UnsupportedMetadataFormatException>(
                () => PlatformResolver.HasType(
                    path,
                    "System.Object"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IntrinsicBinding_RejectsWindowsMetadata()
    {
        byte[] image = BuildManagedWindowsMetadata();
        var assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Unsupported",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local(
                "services format admission test"));

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => IntrinsicCoreLibraryBinding.Select(
                assembly,
                static _ => AssemblyBindingSelection.NotFound()));
    }

    [Fact]
    public void IntrinsicBinding_CleanupCannotReplaceFormatRejection()
    {
        ThrowingDisposeMemoryStream? opened = null;
        var assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Unsupported",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => opened = new ThrowingDisposeMemoryStream(
                BuildManagedWindowsMetadata()),
            AssemblyResolutionProvenance.Local(
                "services format admission test"));

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => IntrinsicCoreLibraryBinding.Select(
                assembly,
                static _ => AssemblyBindingSelection.NotFound()));
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void IntrinsicBinding_CleanupCannotReplaceRetainedCandidateFailure()
    {
        ThrowingDisposeMemoryStream? opened = null;
        var assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Consumer",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => opened = new ThrowingDisposeMemoryStream(
                BuildCoreLibraryReferenceAssembly()),
            AssemblyResolutionProvenance.Local(
                "services format admission test"));

        var unavailable =
            Assert.IsType<AssemblyBindingSelection.Unavailable>(
                IntrinsicCoreLibraryBinding.Select(
                    assembly,
                    static _ => CandidateFailure(
                        CandidateOpenFailureKind
                            .UnsupportedMetadataFormat)));

        Assert.Equal(
            CandidateOpenFailureKind.UnsupportedMetadataFormat,
            unavailable.Failure.CandidateFailureKind);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void IntrinsicBinding_PreservesCandidateFailurePrecedence(
        int scenario,
        bool higherPriorityFirst)
    {
        var assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Consumer",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => new MemoryStream(
                BuildCoreLibraryReferenceAssembly(),
                writable: false),
            AssemblyResolutionProvenance.Local(
                "services format admission test"));
        AssemblyBindingSelection higherPriority =
            scenario switch
            {
                0 => CandidateFailure(
                    CandidateOpenFailureKind.InvalidImage,
                    MetadataRootMalformedReason.InvalidSignature),
                1 => CandidateFailure(
                    CandidateOpenFailureKind.ResourceBudget),
                2 => CandidateFailure(
                    CandidateOpenFailureKind
                        .UnsupportedMetadataFormat),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(scenario)),
            };
        AssemblyBindingSelection lowerPriority =
            scenario switch
            {
                0 or 2 => CandidateFailure(
                    CandidateOpenFailureKind.Unreadable),
                1 => CandidateFailure(
                    CandidateOpenFailureKind
                        .UnsupportedMetadataFormat),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(scenario)),
            };
        AssemblyBindingSelection first = higherPriorityFirst
            ? higherPriority
            : lowerPriority;
        AssemblyBindingSelection second = higherPriorityFirst
            ? lowerPriority
            : higherPriority;
        IAssemblyBindingPolicy bindingPolicy =
            new SourceRelativeAssemblyGroupBindingPolicy(
                [(assembly, new CoreLibraryFailurePolicy(first, second))]);

        var unavailable =
            Assert.IsType<AssemblyBindingSelection.Unavailable>(
                bindingPolicy.Select(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.CoreLibrary(),
                        AssemblyBindingOrigin.FromAssembly(assembly),
                        AssemblyResolutionScope.Platform)));

        Assert.Equal(
            scenario switch
            {
                0 => CandidateOpenFailureKind.InvalidImage,
                1 => CandidateOpenFailureKind.ResourceBudget,
                2 => CandidateOpenFailureKind
                    .UnsupportedMetadataFormat,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(scenario)),
            },
            unavailable.Failure.CandidateFailureKind);
        Assert.Equal(
            scenario == 0
                ? MetadataRootMalformedReason.InvalidSignature
                : null,
            unavailable.Failure.MetadataRootReason);
    }

    [Fact]
    public void PackageTypeProbe_RejectedMemberDoesNotHideHealthyMatch()
    {
        string typeName = typeof(MetadataFormatAdmissionTests).FullName!;
        string root = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-type-probe-{Guid.NewGuid():N}");
        string healthy = Path.Combine(root, "lib", "net8.0", "Lib.dll");
        string unsupported = Path.Combine(root, "lib", "net10.0", "Lib.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(healthy)!);
        Directory.CreateDirectory(Path.GetDirectoryName(unsupported)!);
        File.Copy(
            typeof(MetadataFormatAdmissionTests).Assembly.Location,
            healthy);
        File.WriteAllBytes(unsupported, BuildManagedWindowsMetadata());
        try
        {
            TfmSelector.PackageTypeAssemblyResolution resolution =
                TfmSelector.FindAssemblyContainingTypeWithFailures(
                    root,
                    typeName);

            Assert.Equal(healthy, resolution.Path);
            Assert.Equal(
                TfmSelector.PackageTypeProbeFailureKind
                    .UnsupportedMetadataFormat,
                Assert.Single(resolution.Failures).Kind);

            // The rejected higher-TFM member scopes to itself, so it must not
            // replace the healthy match the same scan established.
            (string? path, string? tfm) =
                TfmSelector.FindAssemblyContainingType(root, typeName);

            Assert.Equal(healthy, path);
            Assert.Equal("net8.0", tfm);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageTypeProbe_SoleRejectedMemberSurfacesTypedFailure()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-type-probe-{Guid.NewGuid():N}");
        string unsupported = Path.Combine(root, "lib", "net10.0", "Lib.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(unsupported)!);
        File.WriteAllBytes(unsupported, BuildManagedWindowsMetadata());
        try
        {
            Assert.Throws<UnsupportedMetadataFormatException>(
                () => TfmSelector.FindAssemblyContainingType(
                    root,
                    typeof(MetadataFormatAdmissionTests).FullName!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static byte[] BuildManagedWindowsMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Unsupported.winmd"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Unsupported"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
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
                "WindowsRuntime 1.4;CLR v4.0.30319",
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildCoreLibraryReferenceAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Consumer.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Consumer"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static AssemblyBindingSelection CandidateFailure(
        CandidateOpenFailureKind kind,
        MetadataRootMalformedReason? reason = null) =>
        AssemblyBindingSelection.CannotSelect(
            new AssemblyBindingFailure(
                AssemblyBindingFailureKind.CandidateUnavailable,
                kind)
            {
                MetadataRootReason = reason,
            });

    sealed class CoreLibraryFailurePolicy(
        AssemblyBindingSelection first,
        AssemblyBindingSelection second)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            var reference =
                Assert.IsType<AssemblyBindingTarget.AssemblyReference>(
                    request.Target);
            return reference.Identity.Name.Equals(
                "System.Private.CoreLib",
                StringComparison.Ordinal)
                ? first
                : second;
        }
    }

    sealed class ThrowingDisposeMemoryStream(byte[] image)
        : MemoryStream(image, writable: false)
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
            if (disposing)
            {
                throw new InvalidOperationException(
                    "Synthetic disposal failure.");
            }
        }
    }
}
