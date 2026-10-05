namespace ILInspector.Analysis;

/// <summary>
/// Selects the Analysis producers that participate in one assembly body
/// acquisition.
/// </summary>
[Flags]
public enum LibraryBodyAnalysisFeatures
{
    /// <summary>Acquire no body-analysis evidence.</summary>
    None = 0,
    /// <summary>Produce calls, unsafe evidence, and method/body signals.</summary>
    MethodEvidence = 1 << 0,
    /// <summary>Produce allocation occurrences; implies <see cref="MethodEvidence"/>.</summary>
    Allocations = 1 << 1,
    /// <summary>
    /// Produce optimization opportunities; implies <see cref="Allocations"/>.
    /// </summary>
    OptimizationOpportunities = 1 << 2,
    /// <summary>
    /// Produce sync-call-in-async opportunities; implies
    /// <see cref="MethodEvidence"/>.
    /// </summary>
    AsyncSiblingOpportunities = 1 << 5,
    /// <summary>
    /// Produce call argument provenance and return-sink value flow required to
    /// authenticate source-generated System.Text.Json wire contracts. A scoped
    /// body census withholds async state-machine field provenance whose
    /// validity depends on the absence of writes in other bodies.
    /// </summary>
    JsonWireContractFlow = 1 << 6,
    /// <summary>
    /// Produce typed physical local-throw sites and explicit body coverage;
    /// implies <see cref="MethodEvidence"/>.
    /// </summary>
    LocalThrows = 1 << 7,
    /// <summary>
    /// Produce objective per-physical-body implementation profiles; implies
    /// <see cref="MethodEvidence"/>.
    /// </summary>
    ImplementationProfiles = 1 << 8,
    /// <summary>The body-analysis features used by general execution.</summary>
    Default = MethodEvidence
        | Allocations
        | OptimizationOpportunities
        | AsyncSiblingOpportunities,
    /// <summary>All available body-analysis producers.</summary>
    All = Default
        | JsonWireContractFlow
        | LocalThrows
        | ImplementationProfiles,
}
