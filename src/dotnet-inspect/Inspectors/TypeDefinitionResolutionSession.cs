using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal sealed record TypeDefinitionApiSurfaceFailure(
    string Kind,
    string Detail);

internal sealed record SelectedTypeBindingContext(
    AssemblyBindingOccurrence Occurrence,
    IAssemblyBindingPolicy Policy)
{
    internal sealed record SelectedBindingParticipant(
        ResolvedAssemblyReference Assembly,
        AssemblyBindingOccurrence? Occurrence);

    internal IReadOnlyList<SelectedBindingParticipant>
        DiscoverParticipants(
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AssemblyBindingPolicyVersion version = Policy.Version;
        int maxCandidates =
            new TypeResolutionContextOptions().MaxCandidates;
        var participants = new Dictionary<
            AssemblyAcquisitionRegistration,
            SelectedBindingParticipant>(
                ReferenceEqualityComparer.Instance);
        var expanded = new HashSet<AssemblyBindingOccurrence>();
        var pending = new Queue<AssemblyBindingOccurrence>();
        pending.Enqueue(Occurrence);

        while (pending.TryDequeue(out AssemblyBindingOccurrence? current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!expanded.Add(current))
                continue;
            Retain(current.Assembly, current);

            using Stream stream = current.Assembly.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                throw new BadImageFormatException(
                    $"The selected assembly "
                    + $"'{current.Assembly.Identity.Name}' has no metadata.");
            }

            MetadataReader reader = peReader.GetMetadataReader();
            foreach (AssemblyReferenceHandle handle
                in reader.AssemblyReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssemblyReferenceIdentity identity =
                    AssemblyReferenceIdentity.From(reader, handle);
                var request = new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(identity),
                    AssemblyBindingOrigin.FromOccurrence(current),
                    PlatformKeys.IsPlatform(identity.PublicKeyToken)
                        ? AssemblyResolutionScope.Platform
                        : AssemblyResolutionScope.Any);
                AssemblyBindingSelectionSnapshot? snapshot =
                    Policy.Select(request);
                if (snapshot is null
                    || !ReferenceEquals(snapshot.Version, version)
                    || !ReferenceEquals(Policy.Version, version))
                {
                    throw new InvalidOperationException(
                        "The binding-policy snapshot changed while "
                        + "forming the match body context.");
                }

                AssemblyBindingSelection selection =
                    AssemblyBindingSelection.ValidateForRequest(
                        request,
                        snapshot.Selection);
                if (selection
                    is not AssemblyBindingSelection.Selected selected)
                {
                    continue;
                }

                Retain(selected.Assembly, selected.Occurrence);
                foreach (ResolvedAssemblyReference shadow
                    in selected.ShadowedAssemblies)
                {
                    Retain(shadow, occurrence: null);
                }

                if (selected.Assembly.Provenance
                        is not AssemblyResolutionProvenance.PlatformAsset)
                {
                    pending.Enqueue(selected.Occurrence);
                }
            }
        }

        if (!ReferenceEquals(Policy.Version, version))
        {
            throw new InvalidOperationException(
                "The binding-policy snapshot changed while "
                + "forming the match body context.");
        }

        return [.. participants.Values];

        void Retain(
            ResolvedAssemblyReference assembly,
            AssemblyBindingOccurrence? occurrence)
        {
            if (participants.TryGetValue(
                    assembly.Registration,
                    out SelectedBindingParticipant? retained))
            {
                if (retained.Occurrence is null
                    && occurrence is not null)
                {
                    participants[assembly.Registration] =
                        new SelectedBindingParticipant(
                            assembly,
                            occurrence);
                }
                return;
            }

            participants.Add(
                assembly.Registration,
                new SelectedBindingParticipant(assembly, occurrence));
            if (participants.Count > maxCandidates)
            {
                throw new InvalidOperationException(
                    $"The match binding context exceeded its "
                    + $"{maxCandidates}-assembly candidate bound.");
            }
        }
    }
}

/// <summary>
/// CLI inspection-lifetime owner for structured type resolution from one acquired
/// assembly. Consumers receive Metadata outcomes and acquisition descriptors rather
/// than reconstructing assembly paths from metadata names.
/// </summary>
internal sealed class TypeDefinitionResolutionSession : IDisposable
{
    readonly ResolvedAssemblyReference _root;
    readonly IAssemblyBindingPolicy _policy;
    readonly TypeResolutionCatalog _catalog = new();

    public TypeDefinitionResolutionSession(
        string assemblyPath,
        bool isPlatformAssembly,
        ApiOptions? options = null)
        : this(
            assemblyPath,
            isPlatformAssembly,
            options?.ProjectAssetsPath,
            options?.Tfm,
            options?.PlatformFramework,
            packageDirectory: null,
            sourceOptions: options?.SourceOptions,
            usePackageSourcePolicy: options?.PackagePath is not null)
    {
    }

    public TypeDefinitionResolutionSession(
        string assemblyPath,
        bool isPlatformAssembly,
        string? projectAssetsPath,
        string? targetFramework,
        string? platformFramework = null,
        string? packageDirectory = null,
        NuGetSourceOptions? sourceOptions = null,
        bool usePackageSourcePolicy = false)
        : this(
            ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                isPlatformAssembly
                    ? AssemblyResolutionProvenance.Platform(
                        platformFramework ?? "InstalledPlatform",
                        frameworkVersion: null,
                        "TypeDefinitionResolutionSession")
                    : AssemblyResolutionProvenance.Local(
                        "TypeDefinitionResolutionSession")),
            isPlatformAssembly,
            projectAssetsPath,
            targetFramework,
            platformFramework,
            packageDirectory,
            sourceOptions,
            usePackageSourcePolicy)
    {
    }

    internal TypeDefinitionResolutionSession(
        ResolvedAssemblyReference root,
        bool isPlatformAssembly,
        string? projectAssetsPath,
        string? targetFramework,
        string? platformFramework = null,
        string? packageDirectory = null,
        NuGetSourceOptions? sourceOptions = null,
        bool usePackageSourcePolicy = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Path is not { } assemblyPath)
            throw new ArgumentException(
                "The CLI resolution root must have a filesystem path.",
                nameof(root));
        _root = root;

        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(assemblyPath)
            {
                ProjectAssetsPath = projectAssetsPath,
                TargetFramework = targetFramework,
                RootPackageDirectory = packageDirectory,
                PackageSourceOptions = sourceOptions,
                UsePackageSourcePolicy = usePackageSourcePolicy,
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework =
                    string.Equals(
                        platformFramework,
                        "aspnetcore",
                        StringComparison.OrdinalIgnoreCase),
                PreferImplementationAssemblies = true,
                AllowPlatformAssemblyVersionRollForward = true,
            });
        _policy = resolver;
    }

    public TypeResolutionOutcome Resolve(MetadataTypeDefinitionName type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var request = TypeResolutionRequest.FromAssembly(
            _root,
            AssemblyResolutionScope.Any,
            type);
        using TypeResolutionContext context = _catalog.CreateContext(
            _policy,
            [_root],
            [request]);
        return context.Resolve(request);
    }

    internal SelectedTypeBindingContext BindingContext(
        AssemblyBindingOccurrence occurrence) =>
        new(occurrence, _policy);

    public ApiSurface? ExtractApiSurface(
        bool includeAll = false,
        bool typesOnly = false) =>
        ExtractApiSurface(_root, includeAll, typesOnly);

    public ApiSurface? ExtractApiSurface(
        ResolvedAssemblyReference source,
        bool includeAll = false,
        bool typesOnly = false) =>
        ExtractApiSurface(
            source,
            includeAll,
            typesOnly,
            out _);

    internal ApiSurface? ExtractApiSurface(
        ResolvedAssemblyReference source,
        bool includeAll,
        bool typesOnly,
        out TypeDefinitionApiSurfaceFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(source);
        failure = null;
        try
        {
            ResolutionAwareApiSurfaceOutcome outcome =
                _catalog.ExtractApiSurface(
                    source,
                    _policy,
                    includeAll,
                    typesOnly);
            if (outcome
                is ResolutionAwareApiSurfaceOutcome.Rejected rejected)
            {
                failure = new TypeDefinitionApiSurfaceFailure(
                    rejected.Failure.Kind.ToString(),
                    rejected.Failure.Detail);
                return null;
            }

            var read =
                (ResolutionAwareApiSurfaceOutcome.Read)outcome;
            ApiSurface surface = read.Surface;
            if (source.Path is { } path)
                surface.SetInspectionSourceAssemblyPath(path);

            return surface;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or ArgumentException)
        {
            failure = new TypeDefinitionApiSurfaceFailure(
                ex.GetType().Name,
                ex.Message);
            return null;
        }
    }

    public void Dispose() => _catalog.Dispose();
}
