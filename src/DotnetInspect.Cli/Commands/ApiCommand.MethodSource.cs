using DotnetInspect.Cli.Inspectors;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Shared helpers for type and member commands.
/// </summary>
public partial class ApiCommand
{
    internal static bool? ResolveMemberBodyState(
        string dllPath,
        string typeName,
        string methodName,
        int overloadIndex,
        bool publicOnly,
        string? memberMetadataAssemblyPath,
        int memberMetadataToken,
        Action<string>? log,
        ResolvedAssemblyReference? sourceAssembly = null)
    {
        bool hasMemberToken =
            memberMetadataToken != 0
            && memberMetadataAssemblyPath is { Length: > 0 };
        if (sourceAssembly is not null)
        {
            bool descriptorTokenAddressesLookupImage =
                hasMemberToken
                && sourceAssembly.Path is { } sourceAssemblyPath
                && AssemblyPathsEqual(
                    sourceAssemblyPath,
                    memberMetadataAssemblyPath!);
            if (hasMemberToken
                && !descriptorTokenAddressesLookupImage)
            {
                return null;
            }
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(sourceAssembly);
            return session.MethodBodies.ResolveMethod(
                typeName,
                methodName,
                overloadIndex,
                publicOnly,
                descriptorTokenAddressesLookupImage
                    ? memberMetadataToken
                    : null)
                ?.HasBody;
        }
        bool tokenAddressesLookupImage =
            hasMemberToken
            && AssemblyPathsEqual(
                dllPath,
                memberMetadataAssemblyPath!);

        if (hasMemberToken)
        {
            using var memberContext = PdbContext.OpenMetadataOnly(
                tokenAddressesLookupImage
                    ? dllPath
                    : memberMetadataAssemblyPath!,
                tokenAddressesLookupImage ? log : null);
            return memberContext.MethodHasBody(memberMetadataToken);
        }

        using var lookupContext = PdbContext.OpenMetadataOnly(dllPath, log);
        return lookupContext.MethodHasBody(
            typeName,
            methodName,
            overloadIndex,
            publicOnly);
    }

    private static bool AssemblyPathsEqual(
        string first,
        string second)
    {
        StringComparer comparer =
            LibraryMetadataService.ReferenceTreePathComparer(
                OperatingSystem.IsWindows());
        if (comparer.Equals(first, second))
            return true;

        return Path.IsPathFullyQualified(first)
            && Path.IsPathFullyQualified(second)
            && comparer.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second));
    }
}
