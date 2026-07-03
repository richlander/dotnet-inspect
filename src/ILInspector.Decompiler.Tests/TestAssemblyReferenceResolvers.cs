using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

static class TestAssemblyReferenceResolvers
{
    static readonly Lazy<IReadOnlyDictionary<string, string>> s_runtimeAssemblies = new(() =>
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key!, group => group.First(), StringComparer.OrdinalIgnoreCase));

    public static IAssemblyReferenceResolver None { get; } = new DelegateResolver((_, _) => null);

    public static IAssemblyReferenceResolver TrustedPlatformAssemblies()
        => new DelegateResolver((identity, scope) =>
            scope == AssemblyResolutionScope.Platform && s_runtimeAssemblies.Value.TryGetValue(identity.Name, out var path)
                ? FromPath(identity, path, "TrustedPlatformAssemblies")
                : null);

    public static IAssemblyReferenceResolver RuntimeAssemblies()
        => new DelegateResolver((identity, _) =>
            s_runtimeAssemblies.Value.TryGetValue(identity.Name, out var path)
                ? FromPath(identity, path, "TrustedPlatformAssemblies")
                : null);

    public static IAssemblyReferenceResolver SingleAssembly(string assemblyPath)
    {
        string assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        return new DelegateResolver((identity, _) =>
            string.Equals(identity.Name, assemblyName, StringComparison.OrdinalIgnoreCase)
                ? FromPath(identity, assemblyPath, "TestAssembly")
                : null);
    }

    static ResolvedAssemblyReference FromPath(AssemblyReferenceIdentity identity, string path, string provenance)
        => new(identity, path, () => File.OpenRead(path), provenance);

    sealed class DelegateResolver(Func<AssemblyReferenceIdentity, AssemblyResolutionScope, ResolvedAssemblyReference?> resolve) : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
            => resolve(identity, scope);
    }
}
