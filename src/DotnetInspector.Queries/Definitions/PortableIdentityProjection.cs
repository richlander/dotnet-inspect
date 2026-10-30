using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Definitions;

public static class PortableIdentityProjection
{
    /// <summary>Detaches one exact source coordinate's assembly identity.</summary>
    public static PortableLibraryIdentity FromExactLibrary(
        ExactLibrarySourceCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return FromAssembly(coordinate.LibraryIdentity.Identity);
    }

    /// <summary>Detaches one realized workspace member's assembly identity.</summary>
    public static PortableLibraryIdentity FromWorkspaceContextMember(
        WorkspaceContextMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return FromAssembly(member.Participant.Assembly.Identity);
    }

    internal static PortableLibraryIdentity FromAssembly(
        AssemblyReferenceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Version version = identity.Version
            ?? throw new InvalidOperationException(
                "The selected Library has no exact assembly version.");
        return new PortableLibraryIdentity(
            identity.Name,
            version.ToString(4),
            string.IsNullOrEmpty(identity.Culture)
                || identity.Culture.Equals(
                    "neutral",
                    StringComparison.OrdinalIgnoreCase)
                    ? null
                    : identity.Culture,
            string.IsNullOrEmpty(identity.PublicKeyToken)
                ? null
                : identity.PublicKeyToken);
    }
}
