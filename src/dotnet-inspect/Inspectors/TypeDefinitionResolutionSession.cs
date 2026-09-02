using DotnetInspector.Options;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal sealed record TypeDefinitionApiSurfaceFailure(
    string Kind,
    string Detail);

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
            options?.PlatformFramework)
    {
    }

    public TypeDefinitionResolutionSession(
        string assemblyPath,
        bool isPlatformAssembly,
        string? projectAssetsPath,
        string? targetFramework,
        string? platformFramework = null)
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
            platformFramework)
    {
    }

    internal TypeDefinitionResolutionSession(
        ResolvedAssemblyReference root,
        bool isPlatformAssembly,
        string? projectAssetsPath,
        string? targetFramework,
        string? platformFramework = null)
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
