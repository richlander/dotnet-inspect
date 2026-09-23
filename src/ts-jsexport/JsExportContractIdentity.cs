using System.Reflection;
using ILInspector.Metadata;

namespace TsJsExport;

internal static class JsExportContractIdentity
{
    static readonly AssemblyName s_assemblyName =
        typeof(JsExportRootAttribute).Assembly.GetName();

    public static ApiAssemblyIdentity Api { get; } =
        new(
            s_assemblyName.Name
                ?? throw new InvalidOperationException(
                    "TsJsExport.Contracts has no assembly name."),
            s_assemblyName.Version,
            s_assemblyName.CultureName,
            PublicKeyToken());

    public static AssemblyReferenceIdentity Reference { get; } =
        new(
            s_assemblyName.Name
                ?? throw new InvalidOperationException(
                    "TsJsExport.Contracts has no assembly name."),
            s_assemblyName.Version,
            s_assemblyName.CultureName,
            PublicKeyToken());

    static string? PublicKeyToken()
    {
        byte[]? token = s_assemblyName.GetPublicKeyToken();
        return token is { Length: > 0 }
            ? Convert.ToHexString(token).ToLowerInvariant()
            : null;
    }
}
