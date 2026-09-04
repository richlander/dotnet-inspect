using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;

using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class ArtifactRootScopeProjectionTests
{
    const string Framework = "net11.0";

    [Fact]
    public void PackageArtifactRootCorrespondence_IsExactAndResourceFree()
    {
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Resource.Free");
        using var workspace = new InspectionWorkspace();
        ArtifactRootCorrespondence correspondence =
            workspace.CreatePackageArtifactRootCorrespondence(binding);
        var generation = new ArtifactRootGenerationReference(
            workspace.Identity,
            correspondence);

        AssertResourceFree(typeof(ArtifactRootCorrespondence));
        AssertResourceFree(correspondence.GetType());
        AssertResourceFree(typeof(ArtifactRootGenerationReference));
        Assert.Empty(
            typeof(PackageArtifactRootCorrespondence)
                .GetConstructors());
        Assert.Empty(
            typeof(ArtifactRootGenerationReference)
                .GetConstructors());
        Assert.Same(
            correspondence,
            generation.Correspondence);
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
        var content = new CountingPackageContent(
            new InMemoryPackageContent(
                Archive(
                    (
                        "lib/net11.0/Exact.Match.dll",
                        File.ReadAllBytes(
                            typeof(AssemblyReferenceIdentity)
                                .Assembly.Location))),
                fromCache: false,
                producerKey: "tests"));
        PackageRootBinding binding =
            PackageRootBinding.CreateFromSource(
                new AcquiredPackageSourcePayload(
                    PackageSourceCoordinate.Create(
                        "Exact.Match",
                        "1.0.0"),
                    content,
                    "tests",
                    PackagePayloadOrigin.Download),
                Framework);
        content.ResetAccessCount();
        using var workspace = new InspectionWorkspace();

        PackageArtifactRootCorrespondence first =
            workspace.CreatePackageArtifactRootCorrespondence(binding);
        PackageArtifactRootCorrespondence second =
            workspace.CreatePackageArtifactRootCorrespondence(binding);

        Assert.Equal(first, second);
        Assert.True(first.Matches(binding));
        Assert.Equal(0, content.AccessCount);
    }

    [Fact]
    public async Task PackageArtifactRootGenerationReference_ChangesWithPhysicalGeneration()
    {
        PackageRootBinding firstBinding =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Replacement.Generation");
        PackageRootBinding replacementBinding =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Replacement.Generation");
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageAssemblyContextCompletion first =
            await ExecuteAsync(workspace, firstBinding);
        ArtifactRootScopeProjection firstRoot =
            Assert.Single(first.RootScopeProjections);
        ArtifactRootGenerationReference firstGeneration =
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(
                firstRoot.Status).Generation;

        PackageAssemblyContextCompletion replacement =
            await ExecuteAsync(workspace, replacementBinding);
        ArtifactRootScopeProjection replacementRoot =
            Assert.Single(replacement.RootScopeProjections);
        ArtifactRootGenerationReference replacementGeneration =
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(
                replacementRoot.Status).Generation;
        var current = Assert.IsType<
            ArtifactRootScopeProjectionResult.Current>(
                workspace.GetCurrentRootScopeProjection(
                    firstRoot.Correspondence));

        Assert.Equal(
            firstRoot.Correspondence,
            replacementRoot.Correspondence);
        Assert.NotSame(
            firstGeneration,
            replacementGeneration);
        Assert.Same(replacementRoot, current.Projection);

        await replacement.CloseAsync();
        var absent = Assert.IsType<
            ArtifactRootScopeProjectionResult.Unavailable>(
                workspace.GetCurrentRootScopeProjection(
                    firstRoot.Correspondence));
        Assert.Equal(
            ArtifactRootScopeProjectionUnavailableReason.Absent,
            absent.Reason);
        await first.CloseAsync();
    }

    [Fact]
    public async Task PackageArtifactRootGenerationReference_StaleOrForeignCannotEnterAccess()
    {
        PackageRootBinding firstBinding =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Generation.Access");
        PackageRootBinding replacementBinding =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Generation.Access");
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        await using InspectionWorkspace foreignWorkspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageAssemblyContextCompletion first =
            await ExecuteAsync(workspace, firstBinding);
        PackageAssemblyContextProjection admittedBeforeReplacement =
            first.CreateProjection([firstBinding]);
        ArtifactRootScopeProjection firstRoot =
            Assert.Single(first.RootScopeProjections);
        ArtifactRootGenerationReference firstGeneration =
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(
                firstRoot.Status).Generation;
        PackageAssemblyContextCompletion foreign =
            await ExecuteAsync(foreignWorkspace, firstBinding);
        ArtifactRootGenerationReference foreignGeneration =
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(
                Assert.Single(foreign.RootScopeProjections).Status)
                .Generation;

        PackageAssemblyContextCompletion replacement =
            await ExecuteAsync(workspace, replacementBinding);
        ArtifactRootGenerationReference replacementGeneration =
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(
                Assert.Single(replacement.RootScopeProjections).Status)
                .Generation;
        var stale = Assert.IsType<
            PackageAssemblyContextProjectionAccessResult.Rejected>(
                first.CreateProjection(
                    [firstBinding],
                    [firstGeneration]));
        var foreignResult = Assert.IsType<
            PackageAssemblyContextProjectionAccessResult.Rejected>(
                replacement.CreateProjection(
                    [replacementBinding],
                    [foreignGeneration]));
        var substituted = Assert.IsType<
            PackageAssemblyContextProjectionAccessResult.Rejected>(
                first.CreateProjection(
                    [firstBinding],
                    [replacementGeneration]));
        var unknown = Assert.IsType<
            PackageAssemblyContextProjectionAccessResult.Rejected>(
                replacement.CreateProjection(
                    [replacementBinding],
                    [
                        new ArtifactRootGenerationReference(
                            workspace.Identity,
                            firstRoot.Correspondence),
                    ]));
        var admitted = Assert.IsType<
            PackageAssemblyContextProjectionAccessResult.Admitted>(
                replacement.CreateProjection(
                    [replacementBinding],
                    [replacementGeneration]));

        Assert.Equal(
            PackageAssemblyContextProjectionAccessRejection
                .ArtifactGenerationMismatch,
            stale.Reason);
        Assert.Equal(
            PackageAssemblyContextProjectionAccessRejection
                .ArtifactGenerationMismatch,
            foreignResult.Reason);
        Assert.Equal(
            PackageAssemblyContextProjectionAccessRejection
                .ArtifactGenerationMismatch,
            substituted.Reason);
        Assert.Equal(
            PackageAssemblyContextProjectionAccessRejection
                .ArtifactGenerationMismatch,
            unknown.Reason);
        Assert.Throws<InvalidOperationException>(
            () => first.CreateProjection([firstBinding]));
        Assert.NotEmpty(
            admittedBeforeReplacement.SurfaceRole.Participants);

        await admitted.Projection.ReturnAsync();
        await admittedBeforeReplacement.ReturnAsync();
        await replacement.CloseAsync();
        await first.CloseAsync();
        await foreign.CloseAsync();
    }

    [Fact]
    public async Task PackageArtifactRootCorrespondence_DuplicateLogicalRootsAreRejected()
    {
        PackageRootBinding first =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Duplicate.Root");
        PackageRootBinding replacement =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Duplicate.Root");
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        Assert.Throws<ArgumentException>(
            () => workspace.PreparePackageAssemblyContextCompletion(
                [first, replacement]));
    }

    static async Task<PackageAssemblyContextCompletion> ExecuteAsync(
        InspectionWorkspace workspace,
        PackageRootBinding binding)
    {
        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion(
                [binding]);
        return await operation.ExecuteAsync(operation.Identity);
    }

    static PackageRootBinding Binding(
        string packageId,
        string producer,
        string targetFramework,
        string? runtimeIdentifier = null)
    {
        var content = new InMemoryPackageContent(
            Archive(
                (
                    $"lib/{targetFramework}/{packageId}.dll",
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
            typeof(PackageAssemblyContextCompletion),
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
