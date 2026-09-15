using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>The physical instruction, not an exception propagation edge.</summary>
public enum LocalThrowInstructionKind
{
    Throw,
    Rethrow,
}

public enum LocalThrowUnresolvedReason
{
    Rethrow,
    UnresolvedValue,
    MultipleSources,
    UnsupportedValue,
    UnresolvedType,
    NonExceptionType,
}

/// <summary>
/// A proven construction feeding a throw, or an explicit reason its exception
/// type is unavailable. Type resolution remains relative to the producing image.
/// </summary>
public abstract record LocalThrowTypeEvidence
{
    public sealed record Known(
        TypeRef ExceptionType,
        int ConstructionOffset,
        int ConstructorToken,
        MetadataTypeDefinitionAddress Definition) : LocalThrowTypeEvidence;

    public sealed record Unresolved(
        LocalThrowUnresolvedReason Reason) : LocalThrowTypeEvidence;
}

public sealed record LocalThrowSite(
    int ILOffset,
    LocalThrowInstructionKind Kind,
    LocalThrowTypeEvidence Type);

public enum LocalThrowUnavailableReason
{
    ScopeExcluded,
    NoManagedBody,
    ReferenceAssembly,
    AnalysisFailed,
}

/// <summary>
/// Local-throw coverage for one physical MethodDef in the index's module.
/// Synthesized bodies are not attributed to a kickoff or enclosing method.
/// </summary>
public abstract record MethodLocalThrowEvidence(
    int MethodToken,
    ImmutableArray<LocalThrowSite> Sites)
{
    /// <summary>
    /// Every local throw site was inspected and has a proven exception type.
    /// This is not completeness for escaping or propagated exceptions.
    /// </summary>
    public bool IsComplete => this is Inspected
        && Sites.All(static site => site.Type is LocalThrowTypeEvidence.Known);

    public sealed record Inspected(
        MethodIdentity Method,
        ImmutableArray<LocalThrowSite> Sites)
        : MethodLocalThrowEvidence(Method.MetadataToken, Sites);

    public sealed record Unavailable(
        int MethodToken,
        LocalThrowUnavailableReason Reason,
        ImmutableArray<LocalThrowSite> Sites,
        string? Detail = null)
        : MethodLocalThrowEvidence(MethodToken, Sites);
}
