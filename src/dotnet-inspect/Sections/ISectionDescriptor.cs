using DotnetInspector.Options;

namespace DotnetInspector.Sections;

/// <summary>
/// Metadata descriptor for a named section. Declares the section's name,
/// minimum verbosity tier, scanner dependency, and a static check for
/// whether the model has data that the section can render.
/// </summary>
/// <typeparam name="TModel">The model type this section inspects.</typeparam>
public interface ISectionDescriptor<TModel>
{
    /// <summary>Section display name (must match the MarkoutSection Name).</summary>
    static abstract string Name { get; }

    /// <summary>Minimum verbosity at which this section is shown by default.</summary>
    static abstract Verbosity MinVerbosity { get; }

    /// <summary>
    /// Scanner key identifying the data collection step this section requires.
    /// Null means the section's data is always collected (core metadata).
    /// Multiple sections may share a scanner key (e.g., Unsafe and P/Invoke
    /// both require "ClassifiedMethods").
    /// </summary>
    static abstract string? ScannerKey { get; }

    /// <summary>
    /// Returns <c>true</c> if the model contains data this section can render.
    /// Called without allocating a renderer instance.
    /// </summary>
    static abstract bool CanRender(TModel model);
}
