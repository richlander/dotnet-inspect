using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research;

/// <summary>
/// One caller-authored role assignment for exactly one admitted input.
/// </summary>
/// <remarks>
/// Role is Research-owned target-planning evidence, not admission evidence, so
/// it is supplied here rather than added to
/// <see cref="ImplementationAssemblyInput"/>. Planning requires an explicit,
/// complete, and exact assignment for every admitted input before it mints any
/// exposed identity; a missing, duplicated, foreign, or undeclared assignment
/// becomes a typed planning rejection.
/// </remarks>
public sealed class ResearchTargetInputRoleAssignment
{
    public ResearchTargetInputRoleAssignment(
        ResearchAdmittedInput input,
        ResearchTargetInputRole role)
    {
        ArgumentNullException.ThrowIfNull(input);
        Input = input;
        Role = role;
    }

    /// <summary>The admitted input this assignment covers.</summary>
    public ResearchAdmittedInput Input { get; }

    /// <summary>The role Research planning assigns to that input.</summary>
    public ResearchTargetInputRole Role { get; }
}

/// <summary>
/// One immutable caller-authored member-selection occurrence.
/// </summary>
/// <remarks>
/// A selection occurrence is a reference-identity object, exactly like
/// <see cref="ResearchComparisonInputOccurrence"/>: repeating the same
/// declaring-type intent and selector in two occurrences mints two distinct
/// target scopes without ordinal, content, display, or structural equality.
/// Occurrences carry intent, never identity.
/// <c>ResearchTargetScopes_DeriveBijectivelyFromSelectionOccurrences</c> gates
/// that property.
/// </remarks>
public abstract class ResearchMemberSelectionOccurrence
{
    private protected ResearchMemberSelectionOccurrence(
        ResearchComparisonQuestionId question,
        string declaringTypeFullName,
        MemberTargetSelector selector,
        ResearchTargetRequestKind kind)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringTypeFullName);
        ArgumentNullException.ThrowIfNull(selector);
        Question = question;
        DeclaringTypeFullName = declaringTypeFullName;
        Selector = selector;
        Kind = kind;
    }

    /// <summary>The admitted question this selection is asked within.</summary>
    public ResearchComparisonQuestionId Question { get; }

    /// <summary>
    /// The exact declaring-type full name this selection intends. Metadata
    /// selection runs only against a type definition whose metadata full name
    /// equals this value; no prefix, suffix, case-insensitive, or display
    /// spelling recovers it.
    /// </summary>
    public string DeclaringTypeFullName { get; }

    /// <summary>The exact typed Metadata selector.</summary>
    public MemberTargetSelector Selector { get; }

    /// <summary>Whether this selection is carried or exact-address.</summary>
    public ResearchTargetRequestKind Kind { get; }
}

/// <summary>
/// One carried selection: an API-level selector evaluated against every
/// admitted input in its question.
/// </summary>
/// <remarks>
/// A carried selection has no resolved relationship role before Metadata
/// selection and never borrows one from the opposite side.
/// </remarks>
public sealed class ResearchCarriedMemberSelection :
    ResearchMemberSelectionOccurrence
{
    public ResearchCarriedMemberSelection(
        ResearchComparisonQuestionId question,
        string declaringTypeFullName,
        MemberTargetSelector selector)
        : base(
            question,
            declaringTypeFullName,
            selector,
            ResearchTargetRequestKind.Carried)
    {
    }
}

/// <summary>
/// One exact-address selection: a physical method address and relationship
/// role asserted for exactly one designated admitted input.
/// </summary>
/// <remarks>
/// The asserted evidence is evaluated only in its designated side-local input.
/// Resolution still derives address and role from the live image; a derived
/// value that differs from the asserted evidence blocks the attempt before any
/// later census.
/// <c>ResearchTargetAttempt_AddressEvidenceMismatchBlocksBeforeCensus</c> gates
/// that.
/// </remarks>
public sealed class ResearchExactAddressMemberSelection :
    ResearchMemberSelectionOccurrence
{
    public ResearchExactAddressMemberSelection(
        ResearchComparisonQuestionId question,
        ResearchAdmittedInput input,
        string declaringTypeFullName,
        MemberTargetSelector selector,
        MetadataMethodAddress address,
        ResearchTargetRelationshipRole assertedRole)
        : base(
            question,
            declaringTypeFullName,
            selector,
            ResearchTargetRequestKind.ExactAddress)
    {
        ArgumentNullException.ThrowIfNull(input);
        Input = input;
        Address = address;
        AssertedRole = assertedRole;
    }

    /// <summary>The one admitted input this selection designates.</summary>
    public ResearchAdmittedInput Input { get; }

    /// <summary>The exact asserted durable method address.</summary>
    public MetadataMethodAddress Address { get; }

    /// <summary>The exact asserted relationship role.</summary>
    public ResearchTargetRelationshipRole AssertedRole { get; }
}

/// <summary>
/// One caller-authored target-planning request over exactly one admitted
/// implementation-comparison population.
/// </summary>
/// <remarks>
/// Caller-owned collections are copied on construction, so later mutation of a
/// caller's collection cannot alter the request or any planned result. Null
/// elements are retained deliberately: an invalid caller shape becomes a typed
/// planning rejection that exposes no identity and no partial plan, rather than
/// a construction-time exception.
/// </remarks>
public sealed class ResearchTargetPlanningRequest
{
    public ResearchTargetPlanningRequest(
        ResearchAdmittedPopulation population,
        IEnumerable<ResearchTargetInputRoleAssignment?> inputRoles,
        IEnumerable<ResearchMemberSelectionOccurrence?> selections)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(inputRoles);
        ArgumentNullException.ThrowIfNull(selections);
        Population = population;
        InputRoles = [.. inputRoles];
        Selections = [.. selections];
    }

    /// <summary>The admitted population this request plans targets for.</summary>
    public ResearchAdmittedPopulation Population { get; }

    /// <summary>
    /// The complete role assignment, in caller order. Planning requires
    /// exactly one assignment per admitted input.
    /// </summary>
    public ImmutableArray<ResearchTargetInputRoleAssignment?> InputRoles { get; }

    /// <summary>The selection occurrences, in caller order.</summary>
    public ImmutableArray<ResearchMemberSelectionOccurrence?> Selections { get; }
}

/// <summary>
/// Why one Research target-planning request was rejected.
/// </summary>
/// <remarks>
/// <c>ResearchTargetPlanning_RejectsEveryDeclaredInvalidShape</c> derives its
/// expected set from this declaration, so a missing or stale member fails that
/// gate.
/// </remarks>
public enum ResearchTargetPlanningRejectionKind
{
    /// <summary>
    /// The admitted population is not an implementation comparison. The
    /// body-signal profile has no typed Metadata target evidence, so it cannot
    /// enter the target path.
    /// </summary>
    UnsupportedProfile,

    /// <summary>The request contains no selection occurrence.</summary>
    MissingSelections,

    /// <summary>The request contains a null selection occurrence.</summary>
    MissingSelection,

    /// <summary>
    /// The same selection-occurrence instance appears more than once, so no
    /// exact association between occurrence and scope exists.
    /// </summary>
    DuplicateSelection,

    /// <summary>
    /// A selection names a question that this population did not admit.
    /// </summary>
    ForeignQuestion,

    /// <summary>
    /// An exact-address selection designates an input that its named question
    /// did not admit.
    /// </summary>
    ForeignInput,

    /// <summary>
    /// A role assignment is null, or an admitted input has no assignment.
    /// </summary>
    MissingInputRole,

    /// <summary>One admitted input carries more than one role assignment.</summary>
    DuplicateInputRole,

    /// <summary>
    /// A role assignment names an input that this population did not admit.
    /// </summary>
    ForeignInputRole,

    /// <summary>A role assignment carries an undeclared role value.</summary>
    UndeclaredInputRole,

    /// <summary>
    /// An exact-address selection asserts an undeclared relationship role.
    /// </summary>
    UndeclaredRelationshipRole,
}

/// <summary>
/// The typed location of one invalid caller shape inside a planning request.
/// </summary>
/// <remarks>
/// Positions locate the caller's invalid shape. They are request coordinates,
/// never identity or occurrence association.
/// </remarks>
public abstract class ResearchTargetPlanningLocation
{
    private protected ResearchTargetPlanningLocation()
    {
    }

    /// <summary>The request as a whole.</summary>
    public sealed class Operation : ResearchTargetPlanningLocation
    {
        internal Operation()
        {
        }
    }

    /// <summary>One requested role-assignment position.</summary>
    public sealed class InputRole : ResearchTargetPlanningLocation
    {
        internal InputRole(int index) => Index = index;

        /// <summary>The assignment's position in the request.</summary>
        public int Index { get; }
    }

    /// <summary>One requested selection-occurrence position.</summary>
    public sealed class Selection : ResearchTargetPlanningLocation
    {
        internal Selection(int index) => Index = index;

        /// <summary>The selection's position in the request.</summary>
        public int Index { get; }
    }
}

/// <summary>
/// One typed Research target-planning rejection. Expected invalid caller
/// shapes are rejections, not exceptions.
/// </summary>
public sealed class ResearchTargetPlanningRejection
{
    internal ResearchTargetPlanningRejection(
        ResearchTargetPlanningRejectionKind kind,
        ResearchTargetPlanningLocation location,
        string summary)
    {
        Kind = kind;
        Location = location;
        Summary = summary;
    }

    /// <summary>Why the request was rejected.</summary>
    public ResearchTargetPlanningRejectionKind Kind { get; }

    /// <summary>Where the invalid shape was found.</summary>
    public ResearchTargetPlanningLocation Location { get; }

    /// <summary>A bounded Research-owned summary of the rejection.</summary>
    public string Summary { get; }
}

/// <summary>
/// The typed outcome of one Research target-planning invocation.
/// </summary>
/// <remarks>
/// Cancellation is deliberately not an arm: an observed cancellation
/// propagates as <see cref="OperationCanceledException"/> and exposes no
/// planned identity. <see cref="Rejected"/> exposes no
/// <see cref="ResearchTargetResolution"/>, so a partial plan is
/// unrepresentable.
/// <c>ResearchTargetCancellation_ExposesNoPartialPopulationOrResult</c> gates
/// both.
/// </remarks>
public abstract class ResearchTargetPlanningOutcome
{
    private protected ResearchTargetPlanningOutcome()
    {
    }

    /// <summary>The complete, terminal target resolution.</summary>
    public sealed class Planned : ResearchTargetPlanningOutcome
    {
        internal Planned(ResearchTargetResolution resolution)
            => Resolution = resolution;

        public ResearchTargetResolution Resolution { get; }
    }

    /// <summary>
    /// The request shape was invalid. This arm exposes no target identity and
    /// no partial plan.
    /// </summary>
    public sealed class Rejected : ResearchTargetPlanningOutcome
    {
        internal Rejected(ResearchTargetPlanningRejection rejection)
            => Rejection = rejection;

        public ResearchTargetPlanningRejection Rejection { get; }
    }
}
