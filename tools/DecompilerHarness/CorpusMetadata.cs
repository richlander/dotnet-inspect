using DotnetInspector.Services;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using System.Reflection;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Builds one <see cref="MetadataContext"/> shared across a whole corpus sweep so
/// a dependency such as CoreLib is opened and indexed once for the entire run
/// rather than once per assembly. The locator searches every directory the
/// corpus assemblies live in, plus the running trusted platform assembly set for
/// platform-trusted references. That keeps same-directory framework sweeps
/// version-local while letting product artifact directories resolve framework
/// dependencies that are not copied beside the product DLLs.
/// </summary>
static class CorpusMetadata
{
    public static MetadataContext Create(IEnumerable<string> assemblies)
    {
        var assemblyPaths = assemblies.Select(Path.GetFullPath).ToList();
        var directories = assemblyPaths
            .Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var dependencyResolvers = assemblyPaths
            .Select(path => new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(path)
            {
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = false,
                IncludeDepsJsonAssets = false,
                PreferImplementationAssemblies = true,
            }))
            .ToList();
        var platformAssemblies = TrustedPlatformAssemblies();

        return new MetadataContext(new CorpusAssemblyReferenceResolver(
            directories,
            dependencyResolvers,
            platformAssemblies));
    }

    sealed class CorpusAssemblyReferenceResolver(
        IReadOnlyList<string?> directories,
        IReadOnlyList<AssemblyDependencyResolver> dependencyResolvers,
        IReadOnlyDictionary<string, string> platformAssemblies) : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            if (scope == AssemblyResolutionScope.Platform)
            {
                foreach (var directory in directories)
                {
                    string candidate = Path.Combine(directory!, identity.Name + ".dll");
                    if (File.Exists(candidate) && IsTrustedPlatformAssembly(candidate))
                        return FromPath(identity, candidate, "CorpusPlatformSibling");
                }

                return platformAssemblies.TryGetValue(identity.Name, out var platformPath)
                    ? FromPath(identity, platformPath, "TrustedPlatformAssembly")
                    : null;
            }

            foreach (var directory in directories)
            {
                string candidate = Path.Combine(directory!, identity.Name + ".dll");
                if (File.Exists(candidate))
                    return FromPath(identity, candidate, "CorpusSibling");
            }

            foreach (var resolver in dependencyResolvers)
                if (resolver.Resolve(identity, scope) is { } resolved)
                    return resolved;
            return null;
        }

        static ResolvedAssemblyReference FromPath(AssemblyReferenceIdentity identity, string path, string provenance)
            => ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(provenance));
    }

    static IReadOnlyDictionary<string, string> TrustedPlatformAssemblies()
    {
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var assemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                continue;
            assemblies.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }
        return assemblies;
    }

    static bool IsTrustedPlatformAssembly(string path)
    {
        try
        {
            return PlatformKeys.IsPlatform(ToHex(AssemblyName.GetAssemblyName(path).GetPublicKeyToken()));
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static string? ToHex(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return null;
        var chars = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = "0123456789abcdef"[bytes[i] >> 4];
            chars[i * 2 + 1] = "0123456789abcdef"[bytes[i] & 0xF];
        }
        return new string(chars);
    }
}
