using System.Runtime.Versioning;

namespace InspectWeb.Engine.SourceFacade;

/// <summary>
/// Maps <c>InspectWeb.Engine.Core</c>'s DTO-neutral call-graph correspondence onto this facade's
/// own transport record. The call-graph facade publishes whole graphs; annotated source carries
/// individual destinations, and each owns its own wire declaration.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserSourceWireProjection
{
    internal static BrowserCallGraphTarget Project(BrowserCallGraphTargetInfo target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new(
            target.Id,
            target.Assembly,
            target.AssemblyVersion,
            target.AssemblyCulture,
            target.AssemblyPublicKeyToken,
            target.TypeFullName,
            target.TypeMetadataId,
            target.TypeDefinitionId,
            target.MemberName,
            target.ParameterTypes,
            target.ReturnType,
            target.GenericArity,
            target.MetadataToken,
            target.SelectorKey,
            target.Kind,
            target.PlatformPack,
            target.SurfaceAssemblyId);
    }
}
