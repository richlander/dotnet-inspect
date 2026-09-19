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
        Action<string>? log)
    {
        bool hasMemberToken =
            memberMetadataToken != 0
            && memberMetadataAssemblyPath is { Length: > 0 };
        bool tokenAddressesLookupImage =
            hasMemberToken
            && LibraryMetadataService
                .ReferenceTreePathComparer(OperatingSystem.IsWindows())
                .Equals(
                    Path.GetFullPath(dllPath),
                    Path.GetFullPath(memberMetadataAssemblyPath!));

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
}
