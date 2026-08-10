using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Typed result of reading an assembly's direct references.</summary>
public abstract record AssemblyReferencesResult
{
    private AssemblyReferencesResult()
    {
    }

    /// <summary>The direct assembly-reference identities, which may be empty.</summary>
    public sealed record Available(
        ImmutableArray<AssemblyReferenceIdentity> Identities) : AssemblyReferencesResult
    {
        public ImmutableArray<AssemblyReference> References
            => Identities.Select(static identity => identity.ToReference()).ToImmutableArray();
    }

    /// <summary>The query failed while reading the assembly-reference table.</summary>
    public sealed record Failed(Exception Error) : AssemblyReferencesResult;
}

/// <summary>Reads direct assembly references from an already-open assembly session.</summary>
public static class AssemblyReferencesQuery
{
    public static InspectionQuery<AssemblyReferencesResult> Definition { get; } =
        new("Assembly references", InspectionCost.NetworkFree);

    public static AssemblyReferencesResult Execute(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            return new AssemblyReferencesResult.Available(
                session.AssemblyReferenceIdentities().ToImmutableArray());
        }
        catch (Exception ex)
        {
            return new AssemblyReferencesResult.Failed(ex);
        }
    }
}
