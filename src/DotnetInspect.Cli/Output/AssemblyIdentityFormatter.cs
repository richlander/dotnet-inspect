using ILInspector.Metadata;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal static class AssemblyIdentityFormatter
{
    public static string Format(AssemblyReferenceIdentity identity) =>
        $"{identity.Name}, Version={identity.Version}, "
            + $"Culture={identity.Culture ?? "neutral"}, "
            + "PublicKeyToken="
            + $"{identity.PublicKeyToken ?? "null"}";

    public static string Format(LibraryAssemblyIdentity identity) =>
        $"{identity.Name}, Version={identity.Version}, "
            + $"Culture={identity.Culture?.ToString() ?? "neutral"}, "
            + "PublicKeyToken="
            + $"{identity.PublicKeyToken?.ToString() ?? "null"}";
}
