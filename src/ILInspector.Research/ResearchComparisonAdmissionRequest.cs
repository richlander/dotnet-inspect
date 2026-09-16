using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research;

/// <summary>
/// One immutable caller-side occurrence of a borrowed comparison input.
/// </summary>
/// <remarks>
/// An occurrence is the exact association handle between a caller-owned input
/// value and the Research identity minted for it. It is deliberately a
/// reference-identity object: repeating the same borrowed value in two
/// occurrences keeps the two admitted inputs distinguishable without ordinal,
/// content, display, or structural equality. Occurrences carry evidence, never
/// identity.
/// <c>ResearchAdmission_RepeatedBorrowedValuesRetainDistinctOccurrences</c>
/// gates that property.
/// </remarks>
public abstract class ResearchComparisonInputOccurrence
{
    private protected ResearchComparisonInputOccurrence(
        ResearchComparisonProfile profile)
        => Profile = profile;

    /// <summary>The profile whose admission may borrow this occurrence.</summary>
    public ResearchComparisonProfile Profile { get; }

    /// <summary>
    /// The borrowed member this occurrence fails to supply, or
    /// <see langword="null"/> when its evidence is complete. Admission turns a
    /// non-null result into a typed rejection that exposes no identity and no
    /// partial population.
    /// </summary>
    internal abstract string? MissingEvidenceMember { get; }
}

/// <summary>
/// One occurrence of a borrowed implementation-comparison input: an acquired
/// assembly descriptor, its reference resolver, and its body index.
/// </summary>
/// <remarks>
/// Admission borrows these values as evidence. It does not open the assembly,
/// inspect its content or path, resolve references, or read the body index.
/// Direct constructor arguments are validated at construction. The overload
/// taking an already-constructed <see cref="ImplementationAssemblyInput"/>
/// deliberately retains incomplete nested evidence, so that shape reaches
/// admission and becomes a typed rejection.
/// </remarks>
public sealed class ImplementationComparisonInputOccurrence :
    ResearchComparisonInputOccurrence
{
    public ImplementationComparisonInputOccurrence(
        ImplementationAssemblyInput input)
        : base(ResearchComparisonProfile.ImplementationComparison)
    {
        ArgumentNullException.ThrowIfNull(input);
        Input = input;
    }

    public ImplementationComparisonInputOccurrence(
        ResolvedAssemblyReference assembly,
        IAssemblyReferenceResolver resolver,
        LibraryBodyIndex bodyIndex)
        : this(Complete(assembly, resolver, bodyIndex))
    {
    }

    /// <summary>The borrowed implementation input.</summary>
    public ImplementationAssemblyInput Input { get; }

    /// <summary>The borrowed acquisition-owned assembly descriptor.</summary>
    public ResolvedAssemblyReference Assembly => Input.Assembly;

    /// <summary>The borrowed assembly-reference resolver.</summary>
    public IAssemblyReferenceResolver Resolver => Input.Resolver;

    /// <summary>The borrowed Analysis body index.</summary>
    public LibraryBodyIndex BodyIndex => Input.BodyIndex;

    internal override string? MissingEvidenceMember
        => Input.Assembly is null
            ? nameof(ImplementationAssemblyInput.Assembly)
            : Input.Resolver is null
                ? nameof(ImplementationAssemblyInput.Resolver)
                : Input.BodyIndex is null
                    ? nameof(ImplementationAssemblyInput.BodyIndex)
                    : null;

    static ImplementationAssemblyInput Complete(
        ResolvedAssemblyReference assembly,
        IAssemblyReferenceResolver resolver,
        LibraryBodyIndex bodyIndex)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(bodyIndex);
        return new ImplementationAssemblyInput(assembly, resolver, bodyIndex);
    }
}

/// <summary>
/// One occurrence of a borrowed body-signal input. The body-signal profile
/// admits only an Analysis body index today.
/// </summary>
/// <remarks>
/// Admission never opens <see cref="LibraryBodyIndex.Path"/> or inspects the
/// index content.
/// </remarks>
public sealed class BodySignalComparisonInputOccurrence :
    ResearchComparisonInputOccurrence
{
    public BodySignalComparisonInputOccurrence(LibraryBodyIndex bodyIndex)
        : base(ResearchComparisonProfile.BodySignal)
    {
        ArgumentNullException.ThrowIfNull(bodyIndex);
        BodyIndex = bodyIndex;
    }

    /// <summary>The borrowed Analysis body index.</summary>
    public LibraryBodyIndex BodyIndex { get; }

    internal override string? MissingEvidenceMember => null;
}

/// <summary>
/// One caller-authored comparison question: the Before and After input
/// occurrences that admission is asked to admit together.
/// </summary>
/// <remarks>
/// The caller-owned collections are copied on construction, so later mutation
/// of the caller's collection cannot alter the request or any admitted
/// population.
/// <c>ResearchAdmission_CopiesCallerOwnedCollections</c> gates that property.
/// Null elements are retained deliberately: an invalid caller shape becomes a
/// typed admission rejection that exposes no identity and no partial
/// population, rather than a construction-time exception.
/// </remarks>
public sealed class ResearchComparisonAdmissionQuestion
{
    public ResearchComparisonAdmissionQuestion(
        IEnumerable<ResearchComparisonInputOccurrence?> before,
        IEnumerable<ResearchComparisonInputOccurrence?> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        Before = [.. before];
        After = [.. after];
    }

    /// <summary>The Before-side occurrences, in caller order.</summary>
    public ImmutableArray<ResearchComparisonInputOccurrence?> Before { get; }

    /// <summary>The After-side occurrences, in caller order.</summary>
    public ImmutableArray<ResearchComparisonInputOccurrence?> After { get; }

    internal ImmutableArray<ResearchComparisonInputOccurrence?> Side(
        ResearchComparisonSide side)
        => side switch
        {
            ResearchComparisonSide.Before => Before,
            ResearchComparisonSide.After => After,
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };
}

/// <summary>
/// One caller-authored admission request for a single rank-1 comparison
/// profile.
/// </summary>
/// <remarks>
/// The caller-owned question collection is copied on construction. Admission
/// validates the profile shape of the whole request before it exposes any
/// identity.
/// </remarks>
public sealed class ResearchComparisonAdmissionRequest
{
    public ResearchComparisonAdmissionRequest(
        ResearchComparisonProfile profile,
        IEnumerable<ResearchComparisonAdmissionQuestion?> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        Profile = profile switch
        {
            ResearchComparisonProfile.ImplementationComparison => profile,
            ResearchComparisonProfile.BodySignal => profile,
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
        Questions = [.. questions];
    }

    /// <summary>The profile this request describes.</summary>
    public ResearchComparisonProfile Profile { get; }

    /// <summary>The requested questions, in caller order.</summary>
    public ImmutableArray<ResearchComparisonAdmissionQuestion?> Questions { get; }
}
