namespace DotnetInspector.Artifacts.Workspaces;

/// <summary>
/// A source-neutral policy role assigned by workspace admission.
/// Possessing a role value does not grant the role.
/// </summary>
/// <remarks>
/// The admission-owned binding is gated by
/// <c>CallerDesignation_IsAssignedByAdmissionRatherThanProvenance</c>.
/// Metadata trust does not consume this role yet.
/// </remarks>
public sealed class ArtifactWorkspaceRole
{
    private ArtifactWorkspaceRole(string name)
    {
        Name = name;
    }

    public static ArtifactWorkspaceRole CallerDesignated { get; } =
        new("caller-designated");

    public string Name { get; }

    public override string ToString() => Name;
}
