namespace ILInspector.Metadata;

/// <summary>
/// Persistent-cache seam for the SourceLink type→file index, keyed by an assembly's
/// commit hash. This is the engine side of a dependency-inversion boundary:
/// <see cref="SourceLinkService"/> builds the index from PDB data (engine work) but
/// must not depend on the tool tier's concrete disk cache. The engine declares the
/// shape it needs here; the tool tier supplies the implementation and wires it into
/// <see cref="SourceLinkService.DefaultCache"/> at startup.
/// </summary>
public interface ISourceLinkIndexCache
{
    /// <summary>Returns the cached index content for <paramref name="key"/>, or null on miss.</summary>
    string? TryGet(string key);

    /// <summary>Stores index <paramref name="content"/> under <paramref name="key"/> (best-effort).</summary>
    void Set(string key, string content);
}
