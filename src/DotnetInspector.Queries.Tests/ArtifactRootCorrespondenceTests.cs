using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;

using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class ArtifactRootCorrespondenceTests
{
    const string Framework = "net11.0";
    const string ExtendedFramework = "net10.0-browser-wasm";

    [Fact]
    public void PackageArtifactRootCorrespondence_IsExactAndResourceFree()
    {
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Resource.Free");
        using var workspace = new InspectionWorkspace();
        PackageArtifactRootCorrespondence correspondence =
            workspace.CreatePackageArtifactRootCorrespondence(binding);

        AssertResourceFree(typeof(ArtifactRootCorrespondence));
        AssertResourceFree(correspondence.GetType());
        Assert.Empty(
            typeof(PackageArtifactRootCorrespondence)
                .GetConstructors());
        Assert.Same(
            workspace.Identity,
            correspondence.WorkspaceIdentity);
    }

    [Fact]
    public void PackageArtifactRootCorrespondence_StableOnlyAcrossCorrespondingReplacement()
    {
        PackageRootBinding first =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Stable.Correspondence");
        PackageRootBinding replacement =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Stable.Correspondence");
        PackageRootBinding changedPackage =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Changed.Correspondence");
        PackageRootBinding changedTarget =
            Binding(
                "Stable.Correspondence",
                producer: "tests",
                targetFramework: "net10.0");
        PackageRootBinding omittedTarget =
            Binding(
                "Stable.Correspondence",
                producer: "tests",
                targetFramework: null);
        PackageRootBinding blankTarget =
            Binding(
                "Stable.Correspondence",
                producer: "tests",
                targetFramework: " ");
        PackageRootBinding equivalentTargetSpelling =
            Binding(
                "Stable.Correspondence",
                producer: "tests",
                targetFramework: "NET11.0");
        PackageRootBinding extendedTarget =
            Binding(
                "Stable.Correspondence",
                producer: "tests",
                targetFramework: ExtendedFramework,
                assetTargetFramework: ExtendedFramework);
        PackageRootBinding equivalentExtendedTargetSpelling =
            Binding(
                "Stable.Correspondence",
                producer: "tests",
                targetFramework: ExtendedFramework.ToUpperInvariant(),
                assetTargetFramework: ExtendedFramework);
        PackageRootBinding changedProducer =
            Binding(
                "Stable.Correspondence",
                producer: "alternate",
                targetFramework: Framework);
        PackageRootBinding changedRuntime =
            Binding(
                "Stable.Correspondence",
                producer: "tests",
                targetFramework: Framework,
                runtimeIdentifier: "linux-x64");
        using var workspace = new InspectionWorkspace();
        using var otherWorkspace = new InspectionWorkspace();

        ArtifactRootCorrespondence firstCorrespondence =
            workspace.CreatePackageArtifactRootCorrespondence(first);
        ArtifactRootCorrespondence replacementCorrespondence =
            workspace.CreatePackageArtifactRootCorrespondence(replacement);

        Assert.Equal(
            firstCorrespondence,
            replacementCorrespondence);
        Assert.Equal(
            firstCorrespondence,
            workspace.CreatePackageArtifactRootCorrespondence(
                equivalentTargetSpelling));
        Assert.Equal(
            workspace.CreatePackageArtifactRootCorrespondence(
                omittedTarget),
            workspace.CreatePackageArtifactRootCorrespondence(
                blankTarget));
        Assert.True(extendedTarget.Root.AssetSelection.IsSelected);
        Assert.True(
            equivalentExtendedTargetSpelling.Root.AssetSelection.IsSelected);
        Assert.Equal(
            workspace.CreatePackageArtifactRootCorrespondence(
                extendedTarget),
            workspace.CreatePackageArtifactRootCorrespondence(
                equivalentExtendedTargetSpelling));
        Assert.NotEqual(
            firstCorrespondence,
            workspace.CreatePackageArtifactRootCorrespondence(
                omittedTarget));
        Assert.NotEqual(
            firstCorrespondence,
            workspace.CreatePackageArtifactRootCorrespondence(
                changedPackage));
        Assert.NotEqual(
            firstCorrespondence,
            workspace.CreatePackageArtifactRootCorrespondence(
                changedTarget));
        Assert.NotEqual(
            firstCorrespondence,
            workspace.CreatePackageArtifactRootCorrespondence(
                changedProducer));
        Assert.NotEqual(
            firstCorrespondence,
            workspace.CreatePackageArtifactRootCorrespondence(
                changedRuntime));
        Assert.NotEqual(
            firstCorrespondence,
            otherWorkspace.CreatePackageArtifactRootCorrespondence(
                first));
    }

    [Fact]
    public void PackageArtifactRootCorrespondence_ExactRequestMatchPerformsNoPhysicalAccess()
    {
        PackageArtifactRootRequest omittedRequest =
            PackageArtifactRootRequest.Create(
                new RealizedMemberCoordinate.Package(
                    "exact.match",
                    "1.0.0",
                    "tests",
                    framework: null,
                    runtimeIdentifier: null),
                selectionTargetFramework: " ",
                selectionRuntimeIdentifier: null);
        PackageArtifactRootRequest extendedRequest =
            PackageArtifactRootRequest.Create(
                new RealizedMemberCoordinate.Package(
                    "exact.match",
                    "1.0.0",
                    "tests",
                    ExtendedFramework,
                    runtimeIdentifier: null),
                ExtendedFramework.ToUpperInvariant(),
                selectionRuntimeIdentifier: null);
        var content = new CountingPackageContent(
            new InMemoryPackageContent(
                Archive(
                    (
                        "lib/net11.0/Exact.Match.dll",
                        File.ReadAllBytes(
                            typeof(AssemblyReferenceIdentity)
                                .Assembly.Location)),
                    (
                        $"lib/{ExtendedFramework}/Exact.Match.dll",
                        File.ReadAllBytes(
                            typeof(AssemblyReferenceIdentity)
                                .Assembly.Location))),
                fromCache: false,
                producerKey: "tests"));
        var payload =
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create(
                    "Exact.Match",
                    "1.0.0"),
                content,
                "tests",
                PackagePayloadOrigin.Download);
        PackageRootBinding omittedBinding =
            PackageRootBinding.CreateFromSource(
                payload,
                selectionTargetFramework: null);
        PackageRootBinding extendedBinding =
            PackageRootBinding.CreateFromSource(
                payload,
                ExtendedFramework);
        content.ResetAccessCount();
        using var workspace = new InspectionWorkspace();

        PackageArtifactRootCorrespondence omitted =
            workspace.CreatePackageArtifactRootCorrespondence(
                omittedBinding);
        PackageArtifactRootCorrespondence extended =
            workspace.CreatePackageArtifactRootCorrespondence(
                extendedBinding);

        Assert.True(omitted.Matches(omittedRequest));
        Assert.True(extended.Matches(extendedRequest));
        AssertResourceFree(typeof(PackageArtifactRootRequest));
        Assert.Equal(0, content.AccessCount);
    }

    [Fact]
    public void PackageArtifactRootCorrespondence_RuntimeCloseStopsIssuance()
    {
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Closed.Correspondence");
        var workspace = new InspectionWorkspace();
        PackageArtifactRootCorrespondence correspondence =
            workspace.CreatePackageArtifactRootCorrespondence(binding);

        workspace.Dispose();

        Assert.Same(
            workspace.Identity,
            correspondence.WorkspaceIdentity);
        Assert.Throws<ObjectDisposedException>(
            () => workspace.CreatePackageArtifactRootCorrespondence(
                binding));
    }

    static PackageRootBinding Binding(
        string packageId,
        string producer,
        string? targetFramework,
        string? runtimeIdentifier = null,
        string assetTargetFramework = Framework)
    {
        var content = new InMemoryPackageContent(
            Archive(
                (
                    $"lib/{assetTargetFramework}/{packageId}.dll",
                    File.ReadAllBytes(
                        typeof(AssemblyReferenceIdentity)
                            .Assembly.Location))),
            fromCache: false,
            producerKey: producer);
        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create(
                    packageId,
                    "1.0.0"),
                content,
                producer,
                PackagePayloadOrigin.Download),
            targetFramework,
            runtimeIdentifier);
    }

    static byte[] Archive(
        params (string Path, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream entry =
                    archive.CreateEntry(path).Open();
                entry.Write(content);
            }
        }

        return stream.ToArray();
    }

    static void AssertResourceFree(Type type)
    {
        Type[] forbidden =
        [
            typeof(InspectionWorkspace),
            typeof(PackageRootBinding),
            typeof(PackageRootRealization),
            typeof(IPackageContent),
            typeof(AssemblyContextGroup),
            typeof(Stream),
            typeof(Delegate),
        ];
        var inspected = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(type);
        while (pending.TryPop(out Type? candidate))
        {
            if (!inspected.Add(candidate)
                || candidate.IsPrimitive
                || candidate.IsEnum
                || candidate == typeof(string)
                || candidate == typeof(Type))
            {
                continue;
            }

            foreach (FieldInfo field in candidate.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(
                    forbidden,
                    forbiddenType =>
                        forbiddenType.IsAssignableFrom(
                            field.FieldType));
                if (field.FieldType.Namespace?.StartsWith(
                        "DotnetInspector",
                        StringComparison.Ordinal)
                    == true)
                {
                    pending.Push(field.FieldType);
                }
            }

            if (candidate.BaseType is { } baseType
                && baseType != typeof(object))
            {
                pending.Push(baseType);
            }
        }
    }

    sealed class CountingPackageContent(
        IPackageContent inner) : IPackageContent
    {
        int _accessCount;

        public int AccessCount => Volatile.Read(ref _accessCount);

        public string? RootPath
        {
            get
            {
                Interlocked.Increment(ref _accessCount);
                return inner.RootPath;
            }
        }

        public string? NupkgPath
        {
            get
            {
                Interlocked.Increment(ref _accessCount);
                return inner.NupkgPath;
            }
        }

        public bool FromCache
        {
            get
            {
                Interlocked.Increment(ref _accessCount);
                return inner.FromCache;
            }
        }

        public string ProducerKey
        {
            get
            {
                Interlocked.Increment(ref _accessCount);
                return inner.ProducerKey;
            }
        }

        public PackageContentGenerationIdentity GenerationIdentity
        {
            get
            {
                Interlocked.Increment(ref _accessCount);
                return inner.GenerationIdentity;
            }
        }

        public bool RequiresArchiveTreeMatch
        {
            get
            {
                Interlocked.Increment(ref _accessCount);
                return inner.RequiresArchiveTreeMatch;
            }
        }

        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Increment(ref _accessCount);
            return inner.TryOpenArchive(out stream);
        }

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Increment(ref _accessCount);
            return inner.TryOpenEntry(relativePath, out stream);
        }

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Increment(ref _accessCount);
            return inner.TryOpenEntry(
                relativePath,
                maxExpandedBytes,
                out stream);
        }

        public IEnumerable<string> EnumerateEntries()
        {
            Interlocked.Increment(ref _accessCount);
            return inner.EnumerateEntries();
        }

        public void ResetAccessCount() =>
            Interlocked.Exchange(ref _accessCount, 0);
    }
}
