namespace DotnetInspector.Options;

/// <summary>
/// Output verbosity levels following the Height × Width model.
/// </summary>
public enum Verbosity
{
    /// <summary>
    /// Summary line only.
    /// </summary>
    Quiet,

    /// <summary>
    /// The command's primary information section.
    /// </summary>
    Minimal,

    /// <summary>
    /// Full sections with full columns (default).
    /// </summary>
    Normal,

    /// <summary>
    /// All sections including detailed tables.
    /// </summary>
    Detailed
}
