using DotnetInspector.Options;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

/// <summary>
/// CLI inspection-lifetime owner for structured type resolution from one acquired
/// assembly. Consumers receive Metadata outcomes and acquisition descriptors rather
/// than reconstructing assembly paths from metadata names.
/// </summary>
internal sealed class TypeDefinitionResolutionSession : IDisposable
{
    readonly ResolvedAssemblyReference _root;
    readonly AssemblyReferenceBindingPolicy _policy;
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
    {
        _root = ResolvedAssemblyReference.CreateFromPath(
            assemblyPath,
            isPlatformAssembly
                ? AssemblyResolutionProvenance.Platform(
                    platformFramework ?? "InstalledPlatform",
                    frameworkVersion: null,
                    "TypeDefinitionResolutionSession")
                : AssemblyResolutionProvenance.Local(
                    "TypeDefinitionResolutionSession"));

        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(assemblyPath)
            {
                ProjectAssetsPath = projectAssetsPath,
                TargetFramework = targetFramework,
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
                AllowPlatformAssemblyVersionRollForward = true,
            });
        _policy = new AssemblyReferenceBindingPolicy(resolver);
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

    public void Dispose() => _catalog.Dispose();
}
