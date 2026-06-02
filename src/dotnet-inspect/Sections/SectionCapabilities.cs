namespace DotnetInspector.Sections;

/// <summary>
/// Optional work a section may perform to enrich itself. These are independent of a section's
/// render behavior (<see cref="ISectionDescriptor{TModel}.IsExpensive"/> /
/// <see cref="ISectionDescriptor{TModel}.ExplicitOnly"/>): a section can render cheaply yet still
/// declare a capability used to fill in extra rows when authorized.
/// </summary>
[Flags]
public enum SectionCapabilities
{
    None = 0,

    /// <summary>
    /// May acquire a missing PDB from a symbol server (msdl / .snupkg). No-op when the assembly
    /// already has an embedded or adjacent PDB. Governs remote acquisition only.
    /// </summary>
    MayDownloadPdb = 1 << 0,

    /// <summary>
    /// May fetch source file bodies over the network and hash them (the slow integrity work).
    /// </summary>
    MayFetchSources = 1 << 1,
}
