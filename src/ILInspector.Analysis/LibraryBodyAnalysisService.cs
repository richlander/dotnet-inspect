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
        IAssemblyReferenceResolver? resolver = null)
    {
        RequireCompatibilityRequest(request);
        return ExecutePath(path, request, resolver).CompatibilityIndex();
    }

    /// <summary>
    /// Executes Analysis over an exact path and publishes independently typed
    /// focused results from one body walk.
    /// </summary>
    public static LibraryBodyAnalysisExecution ExecutePath(
        string path,
        LibraryBodyAnalysisRequest request,
        IAssemblyReferenceResolver? resolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);
        LibraryBodyAnalysisPlan plan = request.Plan;

        if (resolver is not null
            && UsesReferenceResolution(plan))
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
                    bindingPolicy: null);
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
            bindingPolicy: null);
    }

    /// <summary>
    /// Executes Analysis over caller-provided immutable PE image content
    /// without reopening <paramref name="sourceName"/> as a path.
    /// </summary>
    public static LibraryBodyIndex AnalyzeImage(
        string sourceName,
        ImmutableArray<byte> image,
        LibraryBodyAnalysisRequest request,
        IAssemblyReferenceResolver? resolver = null)
    {
        RequireCompatibilityRequest(request);
        return ExecuteImage(
            sourceName,
            image,
            request,
            resolver).CompatibilityIndex();
    }

    /// <summary>
    /// Executes Analysis over caller-provided immutable PE image content,
    /// publishing independently typed focused results without reopening
    /// <paramref name="sourceName"/> as a path.
    /// </summary>
    public static LibraryBodyAnalysisExecution ExecuteImage(
        string sourceName,
        ImmutableArray<byte> image,
        LibraryBodyAnalysisRequest request,
        IAssemblyReferenceResolver? resolver = null)
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
            resolver is not null
                && reader.IsAssembly
                && UsesReferenceResolution(plan)
                ? CreateRootSnapshot(sourceName, reader, image)
                : null;
        return BuildFromReader(
            sourceName,
            peReader,
            plan,
            resolver,
            rootSnapshot,
            bindingPolicy: null);
    }

    /// <summary>
    /// Executes Analysis over caller-provided immutable PE image content using
    /// an existing binding-policy snapshot and its exact root descriptor.
    /// </summary>
    public static LibraryBodyAnalysisExecution ExecuteImage(
        string sourceName,
        ImmutableArray<byte> image,
        LibraryBodyAnalysisRequest request,
        IAssemblyBindingPolicy bindingPolicy,
        ResolvedAssemblyReference rootAssembly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (image.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A prefetched PE image is required.",
                nameof(image));
        }
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentNullException.ThrowIfNull(rootAssembly);

        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        LibraryBodyRootSnapshot? rootSnapshot =
            reader.IsAssembly && UsesReferenceResolution(request.Plan)
                ? CreateRootSnapshot(
                    sourceName,
                    reader,
                    image,
                    rootAssembly)
                : null;
        return BuildFromReader(
            sourceName,
            peReader,
            request.Plan,
            resolver: null,
            rootSnapshot,
            bindingPolicy);
    }

    private static LibraryBodyAnalysisExecution BuildFromReader(
        string sourceName,
        PEReader peReader,
        LibraryBodyAnalysisPlan plan,
        IAssemblyReferenceResolver? resolver,
        LibraryBodyRootSnapshot? rootSnapshot,
        IAssemblyBindingPolicy? bindingPolicy)
    {
        if (!peReader.HasMetadata)
        {
            throw new BadImageFormatException(
                $"No managed metadata: {sourceName}");
        }

        MetadataReader reader = peReader.GetMetadataReader();
        LibraryBodyModuleIdentity moduleIdentity =
            LibraryBodyModuleIdentity.FromImage(reader);
        bool useReferenceResolution = UsesReferenceResolution(plan);
        IAssemblyReferenceResolver? analysisResolver =
            useReferenceResolution ? resolver : null;
        IAssemblyBindingPolicy? analysisBindingPolicy =
            useReferenceResolution ? bindingPolicy : null;
        ImplementationMetricWorkBudget? implementationMetricWork =
            ImplementationMetricWorkBudget.Create(
                plan.ImplementationMetrics,
                chargeAttribution:
                    plan.RequestedFeatures
                        == LibraryBodyAnalysisFeatures.None);
        ImplementationMetricExecutionRecorder?
            implementationMetricRecorder =
                plan.ImplementationMetrics is not { } metricPlan
                    ? null
                    : new(
                        metricPlan,
                        plan.RequestedFeatures);
        using var builder = new LibraryBodyAnalysisBuilder(
            sourceName,
            reader,
            peReader,
            analysisResolver,
            analysisResolver is null
                && analysisBindingPolicy is null
                    ? null
                    : rootSnapshot,
            analysisBindingPolicy,
            implementationMetricWork:
                implementationMetricWork,
            implementationMetricRecorder:
                implementationMetricRecorder);
        LibraryBodyAnalysisResult analysis =
            builder.Build(plan);
        return new LibraryBodyAnalysisExecution(
            sourceName,
            moduleIdentity,
            reader.GetString(
                reader.GetModuleDefinition().Name),
            analysis,
            plan);
    }

    private static bool UsesReferenceResolution(
        LibraryBodyAnalysisPlan plan) =>
        plan.IncludesResourceOccurrences
        || plan.Includes(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities)
        || plan.Includes(
            LibraryBodyAnalysisFeatures.AsyncSiblingOpportunities)
        || plan.Includes(
            LibraryBodyAnalysisFeatures.LocalThrows);

    private static void RequireCompatibilityRequest(
        LibraryBodyAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ResourceEffects is not null)
        {
            throw new ArgumentException(
                "Resource Occurrence Analysis publishes a focused result. "
                + "Use ExecutePath or ExecuteImage.",
                nameof(request));
        }
    }

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

    private static LibraryBodyRootSnapshot CreateRootSnapshot(
        string sourceName,
        MetadataReader reader,
        ImmutableArray<byte> image,
        ResolvedAssemblyReference rootAssembly)
    {
        AssemblyReferenceIdentity imageIdentity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
        if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                imageIdentity,
                rootAssembly.Identity))
        {
            throw new ArgumentException(
                "The root descriptor does not identify the provided image.",
                nameof(rootAssembly));
        }
        if (image.Length
            > AssemblyImageSnapshot.DefaultMaxRetainedImageBytes)
        {
            throw new InvalidOperationException(
                "The root assembly exceeds the retained-image budget.");
        }

        AssemblyImageSnapshotResult result =
            AssemblyImageSnapshot.FromRetainedContent(
                rootAssembly,
                image);
        return result switch
        {
            AssemblyImageSnapshotResult.Ready ready =>
                new LibraryBodyRootSnapshot(
                    rootAssembly,
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
