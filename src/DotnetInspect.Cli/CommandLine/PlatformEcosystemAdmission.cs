using DotnetInspector.Ecosystems;
using DotnetInspector.Services;

namespace DotnetInspect.Cli.CommandLine;

internal static class PlatformEcosystemAdmission
{
    private static readonly EcosystemPackId[] PlatformEcosystems =
    [
        EcosystemPackIds.Runtime,
        EcosystemPackIds.AspNetCore,
    ];

    internal static bool HasAssemblyName(string name) =>
        EcosystemPackCatalog.HasAssemblyNameAssociation(
            name,
            PlatformEcosystems);

    internal static bool TryParseQualifiedTypeName(
        string qualifiedName,
        out string assemblyName,
        out string typeName)
    {
        assemblyName = null!;
        typeName = null!;

        int dotCount = 0;
        for (int index = 0; index < qualifiedName.Length; index++)
        {
            if (qualifiedName[index] == '.')
                dotCount++;
        }
        if (dotCount < 2)
            return false;

        for (int index = qualifiedName.Length - 1; index >= 0; index--)
        {
            if (qualifiedName[index] != '.')
                continue;

            string candidateAssembly = qualifiedName[..index];
            string candidateType = qualifiedName[(index + 1)..];
            if (candidateAssembly.Length == 0
                || candidateType.Length == 0
                || !HasAssemblyName(candidateAssembly))
            {
                continue;
            }

            var result = PlatformResolver.ResolveAssembly(candidateAssembly);
            if (result.AssemblyPath is null)
                continue;

            assemblyName = candidateAssembly;
            typeName = candidateType;
            return true;
        }

        return false;
    }
}
