using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace ILInspector.Research;

/// <summary>
/// Why one Research comparison admission was rejected.
/// </summary>
/// <remarks>
/// <c>ResearchAdmission_RejectsEveryDeclaredInvalidShape</c> derives its
/// expected set from this declaration, so a missing or stale member fails that
/// gate.
/// </remarks>
public enum ResearchAdmissionRejectionKind
{
    /// <summary>The request contains no question.</summary>
    MissingQuestions,

    /// <summary>The request contains a null question.</summary>
    MissingQuestion,

    /// <summary>A side collection contains a null occurrence.</summary>
    MissingInput,

    /// <summary>An occurrence does not carry its required borrowed value.</summary>
    MissingInputEvidence,

    /// <summary>An occurrence belongs to another comparison profile.</summary>
    ProfileMismatch,

    /// <summary>
    /// The same occurrence instance appears more than once, so no exact
    /// association between occurrence and Research identity exists.
    /// </summary>
    DuplicateOccurrence,
}

/// <summary>
/// The typed location of one invalid caller shape inside an admission request.
/// </summary>
/// <remarks>
/// Positions locate the caller's invalid shape. They are request coordinates,
/// never identity or occurrence association.
/// </remarks>
public abstract class ResearchAdmissionLocation
{
    private protected ResearchAdmissionLocation()
    {
    }

    /// <summary>The request as a whole.</summary>
    public sealed class Operation : ResearchAdmissionLocation
    {
        internal Operation()
        {
        }
    }

    /// <summary>One requested question position.</summary>
    public sealed class Question : ResearchAdmissionLocation
    {
        internal Question(int index) => Index = index;

        /// <summary>The question's position in the request.</summary>
        public int Index { get; }
    }

    /// <summary>One requested side-local input position.</summary>
    public sealed class Input : ResearchAdmissionLocation
    {
        internal Input(int questionIndex, ResearchComparisonSide side, int index)
        {
            QuestionIndex = questionIndex;
            Side = side;
            Index = index;
        }

        /// <summary>The owning question's position in the request.</summary>
        public int QuestionIndex { get; }

        /// <summary>The side the invalid occurrence occupies.</summary>
        public ResearchComparisonSide Side { get; }

        /// <summary>The occurrence position within its side collection.</summary>
        public int Index { get; }
    }
}

/// <summary>
/// One typed Research admission rejection. Expected invalid shapes are
/// rejections, not exceptions.
/// </summary>
public sealed class ResearchAdmissionRejection
{
    internal ResearchAdmissionRejection(
        ResearchAdmissionRejectionKind kind,
        ResearchComparisonProfile profile,
        ResearchAdmissionLocation location,
        string summary)
    {
        Kind = kind;
        Profile = profile;
        Location = location;
        Summary = summary;
    }

    /// <summary>Why the admission was rejected.</summary>
    public ResearchAdmissionRejectionKind Kind { get; }

    /// <summary>The profile the rejected request described.</summary>
    public ResearchComparisonProfile Profile { get; }

    /// <summary>Where the invalid shape was found.</summary>
    public ResearchAdmissionLocation Location { get; }

    /// <summary>A bounded Research-owned summary of the rejection.</summary>
    public string Summary { get; }
}

/// <summary>
/// One admitted side-local input: its owner-issued Research identity and the
/// exact occurrence for which that identity was issued.
/// </summary>
public sealed class ResearchAdmittedInput
{
    internal ResearchAdmittedInput(
        ResearchComparisonInputId id,
        ResearchComparisonInputOccurrence occurrence)
    {
        Id = id;
        Occurrence = occurrence;
    }

    /// <summary>The owner-issued side-local input identity.</summary>
    public ResearchComparisonInputId Id { get; }

    /// <summary>The exact occurrence this identity was issued for.</summary>
    public ResearchComparisonInputOccurrence Occurrence { get; }

    /// <summary>The operation that parents this input.</summary>
    public ResearchComparisonOperationId Operation => Id.Operation;

    /// <summary>The question that parents this input.</summary>
    public ResearchComparisonQuestionId Question => Id.Question;

    /// <summary>The side this input occupies.</summary>
    public ResearchComparisonSide Side => Id.Side;
}

/// <summary>
/// One admitted comparison question and its complete side-local input
/// population.
/// </summary>
public sealed class ResearchAdmittedQuestion
{
    internal ResearchAdmittedQuestion(
        ResearchComparisonQuestionId id,
        ImmutableArray<ResearchAdmittedInput> before,
        ImmutableArray<ResearchAdmittedInput> after)
    {
        Id = id;
        Before = before;
        After = after;
        Inputs = [.. before, .. after];
    }

    /// <summary>The owner-issued question identity.</summary>
    public ResearchComparisonQuestionId Id { get; }

    /// <summary>The operation that parents this question.</summary>
    public ResearchComparisonOperationId Operation => Id.Operation;

    /// <summary>The admitted Before-side inputs, in admitted order.</summary>
    public ImmutableArray<ResearchAdmittedInput> Before { get; }

    /// <summary>The admitted After-side inputs, in admitted order.</summary>
    public ImmutableArray<ResearchAdmittedInput> After { get; }

    /// <summary>Every admitted input in this question, Before side first.</summary>
    public ImmutableArray<ResearchAdmittedInput> Inputs { get; }

    /// <summary>The admitted inputs on one side.</summary>
    public ImmutableArray<ResearchAdmittedInput> Side(ResearchComparisonSide side)
        => side switch
        {
            ResearchComparisonSide.Before => Before,
            ResearchComparisonSide.After => After,
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };
}

/// <summary>
/// The complete immutable population admitted by one Research comparison
/// admission: one operation, its questions, its side-local inputs, and the
/// exact occurrence association for every input.
/// </summary>
/// <remarks>
/// Admission is atomic. Either this complete population exists or the
/// admission returns a typed rejection that exposes none of it.
/// <c>ResearchAdmission_ReturnsAtomicExactInputAssociations</c> and
/// <c>ResearchAdmission_InvalidProfileInputExposesNoPartialPopulation</c> gate
/// those properties. The occurrence association is held as a frozen private
/// copy keyed by reference identity; no mutable caller- or minting-reachable
/// dictionary is retained, and
/// <c>ResearchAdmittedPopulation_RetainsOnlyImmutableState</c> gates that.
/// </remarks>
public sealed class ResearchAdmittedPopulation
{
    readonly FrozenDictionary<ResearchComparisonInputOccurrence, ResearchAdmittedInput>
        _byOccurrence;
    readonly FrozenDictionary<ResearchComparisonInputId, ResearchAdmittedInput>
        _byId;

    internal ResearchAdmittedPopulation(
        ResearchComparisonProfile profile,
        ResearchComparisonOperationId operation,
        ImmutableArray<ResearchAdmittedQuestion> questions,
        IEnumerable<KeyValuePair<ResearchComparisonInputOccurrence, ResearchAdmittedInput>> byOccurrence)
    {
        Profile = profile;
        Operation = operation;
        Questions = questions;
        Inputs = [.. questions.SelectMany(static question => question.Inputs)];

        // Freeze a private copy keyed by occurrence reference identity, so no
        // caller-reachable state can alter an admitted association.
        _byOccurrence = byOccurrence.ToFrozenDictionary(
            ReferenceEqualityComparer.Instance);
        _byId = Inputs.ToFrozenDictionary(
            static input => input.Id,
            static input => input,
            (IEqualityComparer<ResearchComparisonInputId>)
                ReferenceEqualityComparer.Instance);
    }

    /// <summary>The profile this population was admitted for.</summary>
    public ResearchComparisonProfile Profile { get; }

    /// <summary>The owner-issued operation identity.</summary>
    public ResearchComparisonOperationId Operation { get; }

    /// <summary>The admitted questions, in admitted order.</summary>
    public ImmutableArray<ResearchAdmittedQuestion> Questions { get; }

    /// <summary>Every admitted input across every question.</summary>
    public ImmutableArray<ResearchAdmittedInput> Inputs { get; }

    /// <summary>
    /// The admitted input issued for one exact occurrence. Association is by
    /// occurrence reference identity; ordinal, content, display, and
    /// structural equality never recover it.
    /// </summary>
    public bool TryGetInput(
        ResearchComparisonInputOccurrence occurrence,
        [MaybeNullWhen(false)] out ResearchAdmittedInput input)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return _byOccurrence.TryGetValue(occurrence, out input);
    }

    /// <summary>
    /// The admitted input issued for one exact occurrence.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The occurrence was not admitted by this population.
    /// </exception>
    public ResearchAdmittedInput GetInput(
        ResearchComparisonInputOccurrence occurrence)
        => TryGetInput(occurrence, out ResearchAdmittedInput? input)
            ? input
            : throw new ArgumentException(
                "The occurrence was not admitted by this population.",
                nameof(occurrence));

    /// <summary>
    /// The admitted input carrying one exact owner-issued input identity.
    /// Association is by reference identity.
    /// </summary>
    public bool TryGetInput(
        ResearchComparisonInputId id,
        [MaybeNullWhen(false)] out ResearchAdmittedInput input)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.TryGetValue(id, out input);
    }

    /// <summary>
    /// The admitted input carrying one exact owner-issued input identity.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The identity does not belong to this population.
    /// </exception>
    public ResearchAdmittedInput GetInput(ResearchComparisonInputId id)
        => TryGetInput(id, out ResearchAdmittedInput? input)
            ? input
            : throw new ArgumentException(
                "The input identity does not belong to this population.",
                nameof(id));
}

/// <summary>
/// The typed outcome of one Research comparison admission.
/// </summary>
/// <remarks>
/// Cancellation is deliberately not an arm; admission performs no cancellable
/// work. <see cref="Rejected"/> exposes no admitted population, so a partial
/// identity population is unrepresentable.
/// </remarks>
public abstract class ResearchAdmissionOutcome
{
    private protected ResearchAdmissionOutcome()
    {
    }

    /// <summary>The complete admitted population.</summary>
    public sealed class Admitted : ResearchAdmissionOutcome
    {
        internal Admitted(ResearchAdmittedPopulation population)
            => Population = population;

        public ResearchAdmittedPopulation Population { get; }
    }

    /// <summary>
    /// The request shape was invalid. This arm exposes no admitted identity
    /// and no partial population.
    /// </summary>
    public sealed class Rejected : ResearchAdmissionOutcome
    {
        internal Rejected(ResearchAdmissionRejection rejection)
            => Rejection = rejection;

        public ResearchAdmissionRejection Rejection { get; }
    }
}

/// <summary>
/// Research-owned admission for the rank-1 comparison profiles.
/// </summary>
/// <remarks>
/// Admission validates the whole request shape, then mints one operation
/// identity, one question identity per requested question, and one side-local
/// input identity per requested occurrence. An invalid request exposes no
/// identity and no partial population. It borrows the profile-specific input
/// values as evidence: it does not open assemblies, inspect content or paths,
/// resolve selectors, or expose target scope, domain, request, or attempt
/// identities. No target path exists in this slice, so no target identity can
/// be minted at all.
/// <c>ResearchAdmission_DoesNotOpenOrInspectBorrowedInputs</c> gates the
/// borrowed-evidence half both behaviorally and by an IL call-reference walk
/// over the admission-reachable methods.
/// </remarks>
public static class ResearchComparisonAdmission
{
    /// <summary>
    /// Admits one comparison request, returning either the complete population
    /// or one typed rejection.
    /// </summary>
    public static ResearchAdmissionOutcome Admit(
        ResearchComparisonAdmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ResearchAdmissionRejection? rejection = Validate(request);
        if (rejection is not null)
            return new ResearchAdmissionOutcome.Rejected(rejection);

        return new ResearchAdmissionOutcome.Admitted(Mint(request));
    }

    static ResearchAdmissionRejection? Validate(
        ResearchComparisonAdmissionRequest request)
    {
        if (request.Questions.Length == 0)
        {
            return Reject(
                ResearchAdmissionRejectionKind.MissingQuestions,
                request.Profile,
                new ResearchAdmissionLocation.Operation(),
                "An admission request must contain at least one question.");
        }

        var seen = new HashSet<ResearchComparisonInputOccurrence>(
            ReferenceEqualityComparer.Instance);

        for (int questionIndex = 0;
            questionIndex < request.Questions.Length;
            questionIndex++)
        {
            ResearchComparisonAdmissionQuestion? question =
                request.Questions[questionIndex];
            if (question is null)
            {
                return Reject(
                    ResearchAdmissionRejectionKind.MissingQuestion,
                    request.Profile,
                    new ResearchAdmissionLocation.Question(questionIndex),
                    "A requested question must not be null.");
            }

            foreach (ResearchComparisonSide side in Sides)
            {
                ImmutableArray<ResearchComparisonInputOccurrence?> occurrences =
                    question.Side(side);
                for (int index = 0; index < occurrences.Length; index++)
                {
                    ResearchAdmissionLocation location =
                        new ResearchAdmissionLocation.Input(
                            questionIndex,
                            side,
                            index);
                    ResearchComparisonInputOccurrence? occurrence =
                        occurrences[index];
                    if (occurrence is null)
                    {
                        return Reject(
                            ResearchAdmissionRejectionKind.MissingInput,
                            request.Profile,
                            location,
                            "A requested input occurrence must not be null.");
                    }

                    if (occurrence.Profile != request.Profile)
                    {
                        return Reject(
                            ResearchAdmissionRejectionKind.ProfileMismatch,
                            request.Profile,
                            location,
                            $"The occurrence belongs to the {occurrence.Profile} profile.");
                    }

                    if (occurrence.MissingEvidenceMember is string missing)
                    {
                        return Reject(
                            ResearchAdmissionRejectionKind.MissingInputEvidence,
                            request.Profile,
                            location,
                            $"The occurrence does not supply {missing}.");
                    }

                    if (!seen.Add(occurrence))
                    {
                        return Reject(
                            ResearchAdmissionRejectionKind.DuplicateOccurrence,
                            request.Profile,
                            location,
                            "The same occurrence instance was requested more than once.");
                    }
                }
            }
        }

        return null;
    }

    static ResearchAdmittedPopulation Mint(
        ResearchComparisonAdmissionRequest request)
    {
        ResearchComparisonOperationId operation = new();
        var byOccurrence =
            new Dictionary<ResearchComparisonInputOccurrence, ResearchAdmittedInput>(
                ReferenceEqualityComparer.Instance);
        var questions =
            ImmutableArray.CreateBuilder<ResearchAdmittedQuestion>(
                request.Questions.Length);

        foreach (ResearchComparisonAdmissionQuestion? requested in request.Questions)
        {
            ResearchComparisonQuestionId questionId = new(operation);
            ImmutableArray<ResearchAdmittedInput> before = MintSide(
                questionId,
                ResearchComparisonSide.Before,
                requested!.Before,
                byOccurrence);
            ImmutableArray<ResearchAdmittedInput> after = MintSide(
                questionId,
                ResearchComparisonSide.After,
                requested.After,
                byOccurrence);
            questions.Add(new ResearchAdmittedQuestion(questionId, before, after));
        }

        return new ResearchAdmittedPopulation(
            request.Profile,
            operation,
            questions.MoveToImmutable(),
            byOccurrence);
    }

    static ImmutableArray<ResearchAdmittedInput> MintSide(
        ResearchComparisonQuestionId question,
        ResearchComparisonSide side,
        ImmutableArray<ResearchComparisonInputOccurrence?> occurrences,
        Dictionary<ResearchComparisonInputOccurrence, ResearchAdmittedInput> byOccurrence)
    {
        var inputs =
            ImmutableArray.CreateBuilder<ResearchAdmittedInput>(occurrences.Length);
        foreach (ResearchComparisonInputOccurrence? occurrence in occurrences)
        {
            ResearchAdmittedInput input = new(
                new ResearchComparisonInputId(question, side),
                occurrence!);
            byOccurrence.Add(occurrence!, input);
            inputs.Add(input);
        }

        return inputs.MoveToImmutable();
    }

    static ResearchAdmissionRejection Reject(
        ResearchAdmissionRejectionKind kind,
        ResearchComparisonProfile profile,
        ResearchAdmissionLocation location,
        string summary)
        => new(kind, profile, location, summary);

    static ReadOnlySpan<ResearchComparisonSide> Sides =>
        [ResearchComparisonSide.Before, ResearchComparisonSide.After];
}
