using ILInspector.Metadata;

namespace DotnetInspect.Cli.Output;

internal static class AssemblyIdentityFormatter
{
    public static string Format(AssemblyReferenceIdentity identity) =>
        $"{identity.Name}, Version={identity.Version}, "
            + $"Culture={identity.Culture ?? "neutral"}, "
            + "PublicKeyToken="
            + $"{identity.PublicKeyToken ?? "null"}";
}
