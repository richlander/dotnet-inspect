using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Stateless execution of library-body Analysis over exact path or immutable
/// image inputs.
/// </summary>
public static class LibraryBodyAnalysisService
{
    public static LibraryBodyIndex AnalyzePath(
        string path,
        LibraryBodyAnalysisRequest request,
        IAssemblyReferenceResolver? resolver = null) =>
        AnalyzePathCore(
            path,
            request,
            resolver,
            resourceEffects: null);

    internal static LibraryBodyIndex AnalyzePathWithResourceEffects(
        string path,
        LibraryBodyAnalysisRequest request,
        IAssemblyReferenceResolver? resolver,
        ResourceEffectAdmission resourceEffects)
    {
        ArgumentNullException.ThrowIfNull(resourceEffects);
        return AnalyzePathCore(
            path,
            request,
            resolver,
            resourceEffects);
    }

    private static LibraryBodyIndex AnalyzePathCore(
        string path,
        LibraryBodyAnalysisRequest request,
        IAssemblyReferenceResolver? resolver,
        ResourceEffectAdmission? resourceEffects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);
        LibraryBodyAnalysisPlan plan = request.Plan;

        if ((resolver is not null
                && UsesReferenceResolution(plan))
            || plan.Includes(
                LibraryBodyAnalysisFeatures.OwnershipFlow))
        {
            LibraryBodyRootSnapshot? rootSnapshot =
                AcquireRootSnapshot(path);
            if (rootSnapshot is not null)
            {
                using var imageReader =
                    new PEReader(rootSnapshot.Snapshot.Content);
                return BuildFromReader(
                    path,
                    imageReader,
                    plan,
                    resolver,
                    rootSnapshot,
                    rootSnapshot.Assembly,
                    resolver is null
                        ? null
                        : new AssemblyReferenceBindingPolicy(resolver),
                    resourceEffects);
            }
        }

        PEStreamOptions streamOptions = !plan.IsScoped
            ? PEStreamOptions.PrefetchEntireImage
            : PEStreamOptions.Default;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream, streamOptions);
        return BuildFromReader(
            path,
            peReader,
            plan,
            resolver,
            rootSnapshot: null,
            ownershipAssembly: null,
            ownershipPolicy: null,
            resourceEffects);
    }

    /// <summary>
    /// Executes Analysis over caller-provided immutable PE image content
    /// without reopening <paramref name="sourceName"/> as a path.
    /// </summary>
    public static LibraryBodyIndex AnalyzeImage(
        string sourceName,
        ImmutableArray<byte> image,
        LibraryBodyAnalysisRequest request,
        IAssemblyReferenceResolver? resolver = null) =>
        AnalyzeImageCore(
            sourceName,
            image,
            request,
            resolver,
            ownershipAssembly: null,
            ownershipPolicy: null,
            resourceEffects: null);

    internal static LibraryBodyIndex AnalyzeImageWithOwnershipBinding(
        string sourceName,
        ImmutableArray<byte> image,
        LibraryBodyAnalysisRequest request,
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        return AnalyzeImageCore(
            sourceName,
            image,
            request,
            resolver: null,
            assembly,
            bindingPolicy,
            resourceEffects: null);
    }

    private static LibraryBodyIndex AnalyzeImageCore(
        string sourceName,
        ImmutableArray<byte> image,
        LibraryBodyAnalysisRequest request,
        IAssemblyReferenceResolver? resolver,
        ResolvedAssemblyReference? ownershipAssembly,
        IAssemblyBindingPolicy? ownershipPolicy,
        ResourceEffectAdmission? resourceEffects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (image.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A prefetched PE image is required.",
                nameof(image));
        }
        ArgumentNullException.ThrowIfNull(request);
        LibraryBodyAnalysisPlan plan = request.Plan;

        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        LibraryBodyRootSnapshot? rootSnapshot =
            reader.IsAssembly
                && ((resolver is not null
                        && UsesReferenceResolution(plan))
                    || plan.Includes(
                        LibraryBodyAnalysisFeatures.OwnershipFlow))
                && ownershipAssembly is null
                ? CreateRootSnapshot(sourceName, reader, image)
                : null;
        return BuildFromReader(
            sourceName,
            peReader,
            plan,
            resolver,
            rootSnapshot,
            ownershipAssembly ?? rootSnapshot?.Assembly,
            ownershipPolicy
                ?? (resolver is null
                    ? null
                    : new AssemblyReferenceBindingPolicy(resolver)),
            resourceEffects);
    }

    private static LibraryBodyIndex BuildFromReader(
        string sourceName,
        PEReader peReader,
        LibraryBodyAnalysisPlan plan,
        IAssemblyReferenceResolver? resolver,
        LibraryBodyRootSnapshot? rootSnapshot,
        ResolvedAssemblyReference? ownershipAssembly,
        IAssemblyBindingPolicy? ownershipPolicy,
        ResourceEffectAdmission? resourceEffects)
    {
        if (!peReader.HasMetadata)
        {
            throw new BadImageFormatException(
                $"No managed metadata: {sourceName}");
        }

        MetadataReader reader = peReader.GetMetadataReader();
        LibraryBodyModuleIdentity moduleIdentity =
            LibraryBodyModuleIdentity.FromImage(reader);
        IAssemblyReferenceResolver? analysisResolver =
            UsesReferenceResolution(plan) ? resolver : null;
        using var builder = new LibraryBodyAnalysisBuilder(
            sourceName,
            reader,
            peReader,
            analysisResolver,
            analysisResolver is null
                ? null
                : rootSnapshot);
        LibraryBodyAnalysisResult analysis =
            builder.Build(plan);
        if (plan.Includes(
                LibraryBodyAnalysisFeatures.OwnershipFlow))
        {
            ResourceEffectAdmission admission =
                resourceEffects
                    ?? ArrayPoolResourceEffectModel.Create();
            ResourceEffectResolutionOutcome resolved;
            if (!reader.IsAssembly)
            {
                resolved = new ResourceEffectResolutionOutcome.Rejected(
                    ResourceEffectResolutionRejectionKind
                        .OccurrencePopulationRejected);
            }
            else
            {
                if (ownershipAssembly is null)
                {
                    throw new InvalidOperationException(
                        "Ownership flow requires an exact root assembly.");
                }
                ownershipPolicy ??= resolver is null
                    ? NoResolverAssemblyBindingPolicy.Instance
                    : new AssemblyReferenceBindingPolicy(resolver);
                var provisional = new LibraryBodyIndex(
                    sourceName,
                    moduleIdentity,
                    reader.GetString(
                        reader.GetModuleDefinition().Name),
                    analysis,
                    plan.Features,
                    hasFullMethodEvidenceScope: !plan.IsScoped);
                resolved = ResourceEffectResolver.Resolve(
                    ownershipPolicy,
                    admission,
                    [
                        new CatalogCallGraphParticipant(
                            provisional,
                            ownershipAssembly),
                    ]);
            }
            ImmutableArray<ResourceOwnershipMethodEvidence> methods =
                ResourceOwnershipFlow.Analyze(
                    analysis.OwnershipFlowInputs,
                    resolved,
                    admission);
            analysis = analysis with
            {
                OwnershipFlow = new(
                    methods,
                    ArrayPoolOwnershipProjection.Project(methods)),
                OwnershipFlowInputs = [],
            };
        }
        return new LibraryBodyIndex(
            sourceName,
            moduleIdentity,
            reader.GetString(
                reader.GetModuleDefinition().Name),
            analysis,
            plan.Features,
            hasFullMethodEvidenceScope: !plan.IsScoped);
    }

    private static bool UsesReferenceResolution(
        LibraryBodyAnalysisPlan plan) =>
        plan.Includes(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities)
        || plan.Includes(
            LibraryBodyAnalysisFeatures.AsyncSiblingOpportunities)
        || plan.Includes(
            LibraryBodyAnalysisFeatures.OwnershipFlow)
        || plan.Includes(
            LibraryBodyAnalysisFeatures.LocalThrows);

    private static LibraryBodyRootSnapshot? AcquireRootSnapshot(
        string path)
    {
        string fullPath = Path.GetFullPath(path);
        AssemblyReferenceIdentity identity;
        DateTime lastWriteTimeUtc;
        using (FileStream stream = File.OpenRead(fullPath))
        using (var peReader = new PEReader(
            stream,
            PEStreamOptions.LeaveOpen
                | PEStreamOptions.PrefetchMetadata))
        {
            if (!peReader.HasMetadata)
            {
                throw new BadImageFormatException(
                    $"No managed metadata: {path}");
            }

            MetadataReader reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
                return null;
            identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader);
            lastWriteTimeUtc =
                File.GetLastWriteTimeUtc(stream.SafeFileHandle);
        }

        var assembly = ResolvedAssemblyReference.Create(
            identity,
            fullPath,
            () => File.OpenRead(fullPath),
            AssemblyResolutionProvenance.Local(
                "LibraryBodyIndex"),
            lastWriteTimeUtc);
        AssemblyImageSnapshotResult result =
            AssemblyImageSnapshot.Open(
                assembly,
                length => length
                    <= AssemblyImageSnapshot
                        .DefaultMaxRetainedImageBytes,
                _ => { });
        return result switch
        {
            AssemblyImageSnapshotResult.Ready ready =>
                new LibraryBodyRootSnapshot(
                    assembly,
                    ready.Snapshot),
            AssemblyImageSnapshotResult.Rejected rejected =>
                throw RootSnapshotFailure(path, rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown root-image acquisition result."),
        };
    }

    private static LibraryBodyRootSnapshot CreateRootSnapshot(
        string sourceName,
        MetadataReader reader,
        ImmutableArray<byte> image)
    {
        if (image.Length
            > AssemblyImageSnapshot.DefaultMaxRetainedImageBytes)
        {
            throw new InvalidOperationException(
                "The root assembly exceeds the retained-image budget.");
        }

        byte[] bytes = ImmutableCollectionsMarshal.AsArray(image)!;
        var assembly = ResolvedAssemblyReference.Create(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader),
            path: null,
            openRead: () => new MemoryStream(bytes, writable: false),
            provenance: AssemblyResolutionProvenance.Local(
                "LibraryBodyIndex"));
        AssemblyImageSnapshotResult result =
            AssemblyImageSnapshot.FromRetainedContent(
                assembly,
                image);
        return result switch
        {
            AssemblyImageSnapshotResult.Ready ready =>
                new LibraryBodyRootSnapshot(
                    assembly,
                    ready.Snapshot),
            AssemblyImageSnapshotResult.Rejected rejected =>
                throw RootSnapshotFailure(sourceName, rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown root-image acquisition result."),
        };
    }

    private static Exception RootSnapshotFailure(
        string sourceName,
        CandidateOpenFailure failure) =>
        failure.Kind switch
        {
            CandidateOpenFailureKind.InvalidImage =>
                new BadImageFormatException(
                    $"{failure.Detail} Path: {sourceName}"),
            CandidateOpenFailureKind.Unreadable =>
                new IOException(
                    $"{failure.Detail} Path: {sourceName}"),
            CandidateOpenFailureKind.ResourceBudget =>
                new InvalidOperationException(
                    $"{failure.Detail} Path: {sourceName}"),
            _ => new InvalidOperationException(
                $"Unknown root-image failure for {sourceName}."),
        };
}
