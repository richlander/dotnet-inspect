using CSharpText;
using ILInspector.MetadataPrimitives;
using InertText;

namespace ILInspector.Metadata;

/// <summary>
/// Classification of an API change's impact on consumers.
/// </summary>
public enum ChangeClassification
{
    /// <summary>New API that doesn't affect existing consumers.</summary>
    Additive,
    /// <summary>Change that will break existing consumers.</summary>
    Breaking,
    /// <summary>Change that may break some consumers depending on usage.</summary>
    PotentiallyBreaking,
    /// <summary>
    /// Change that leaves the public contract (the API axis) as it was: an accessibility
    /// move that keeps external reach, or a change wholly outside the API axis.
    /// </summary>
    NonBreaking,
}

/// <summary>
/// The specific kind of API change detected.
/// </summary>
public enum ChangeKind
{
    // Type-level
    TypeAdded,
    TypeRemoved,
    TypeKindChanged,
    SealedAdded,
    SealedRemoved,
    AbstractAdded,
    AbstractRemoved,
    BaseTypeChanged,
    InterfaceAdded,
    InterfaceRemoved,
    TypeParameterCountChanged,
    TypeParameterVarianceChanged,
    TypeParameterConstraintTightened,
    TypeParameterConstraintLoosened,

    // Member-level
    MemberAdded,
    MemberRemoved,
    MemberSignatureChanged,
    VirtualRemoved,
    AbstractMemberAdded,
    EnumValueChanged,

    // Attribute-level
    TypeAttributeAdded,
    TypeAttributeRemoved,
    MemberAttributeAdded,
    MemberAttributeRemoved,

    // Accessibility: narrowing external reach is breaking, widening is additive,
    // and a move that keeps it is non-breaking.
    TypeAccessibilityChanged,
    MemberAccessibilityChanged,

    // API status (docs/design/api-status-analysis.md): hidden is EditorBrowsable(Never),
    // obsolete is [Obsolete] with its message and error flag.
    TypeHiddenChanged,
    MemberHiddenChanged,
    TypeObsoleteChanged,
    MemberObsoleteChanged,
}

/// <summary>
/// The API dimension that produced a change. Signature is the compatibility
/// surface; attributes are opt-in metadata evidence over otherwise matched APIs;
/// status is a subject's hidden or obsolete API status
/// (docs/design/api-status-analysis.md).
/// </summary>
public enum ApiChangeCategory
{
    Signature,
    Attribute,
    Status,
}

public enum ApiChangeSubjectKind
{
    Type,
    Member,
}

public sealed record ApiTypeHandle(ApiType Type)
{
    public string TypeFullName => Type.FullName;
}

public enum ApiMemberAnchorKind
{
    Method,
    Property,
    Field,
    Event,
}

public sealed record ApiMemberHandle(
    ApiType Type,
    ApiMember Member,
    MemberAnchor? Anchor)
{
    public string TypeFullName => Type.FullName;
    public string MemberName => Member.Name;
    public string? CanonicalSignature => Anchor?.CanonicalSignature;
    public string? Fingerprint => Anchor?.Fingerprint;
    public string? StableSelector => Anchor?.StableSelector;
    public ApiMemberAnchorKind AnchorKind => Member.Kind switch
    {
        "method"
            or "constructor"
            or "operator"
            or "finalizer"
            or "explicit-interface-implementation"
            or "extension-method" => ApiMemberAnchorKind.Method,
        "property" => ApiMemberAnchorKind.Property,
        "field" => ApiMemberAnchorKind.Field,
        "event" => ApiMemberAnchorKind.Event,
        _ => throw new InvalidOperationException(
            $"API member kind '{Member.Kind}' has no anchor kind."),
    };
    public string Identity => StableSelector ?? CanonicalSignature ?? Member.Signature ?? $"{Member.Kind}:{Member.Name}";
}

public sealed record ApiChangeSubject(
    ApiChangeSubjectKind Kind,
    ApiTypeHandle? OldType = null,
    ApiTypeHandle? NewType = null,
    ApiMemberHandle? OldMember = null,
    ApiMemberHandle? NewMember = null)
{
    public string? TypeFullName => NewType?.TypeFullName ?? OldType?.TypeFullName ?? NewMember?.TypeFullName ?? OldMember?.TypeFullName;
    public string? MemberName => NewMember?.MemberName ?? OldMember?.MemberName;
    public string? OldIdentity => OldMember?.Identity;
    public string? NewIdentity => NewMember?.Identity;

    public static ApiChangeSubject Type(ApiType? oldType, ApiType? newType)
        => new(
            ApiChangeSubjectKind.Type,
            oldType is null ? null : new ApiTypeHandle(oldType),
            newType is null ? null : new ApiTypeHandle(newType));

    public static ApiChangeSubject Member(ApiType? oldType, ApiMember? oldMember, ApiType? newType, ApiMember? newMember)
        => new(
            ApiChangeSubjectKind.Member,
            oldType is null ? null : new ApiTypeHandle(oldType),
            newType is null ? null : new ApiTypeHandle(newType),
            oldType is null || oldMember is null ? null : ApiMemberIdentity.CreateHandle(oldType, oldMember),
            newType is null || newMember is null ? null : ApiMemberIdentity.CreateHandle(newType, newMember));
}

[Flags]
public enum ApiDiffScope
{
    Signature = 1,
    Attributes = 2,
    All = Signature | Attributes,
}

/// <summary>
/// A single detected API change.
/// </summary>
public record ApiChange(
    ChangeKind Kind,
    ChangeClassification Classification,
    string Message,
    string? OldValue = null,
    string? NewValue = null,
    ApiChangeCategory Category = ApiChangeCategory.Signature,
    ApiChangeSubject? Subject = null)
{
    /// <remarks>
    /// Every positional property is redeclared, in constructor order, even
    /// though only three need containment. A record that redeclares some of
    /// them emits the compiler-generated ones first, which silently reorders
    /// JSON keys, TSV columns, and the generated ToString. Redeclaring all of
    /// them is what keeps that order the constructor's.
    /// </remarks>
    public ChangeKind Kind { get; init; } = Kind;

    /// <inheritdoc cref="Kind"/>
    public ChangeClassification Classification { get; init; } = Classification;

    /// <summary>
    /// The human-readable description, which embeds untrusted type and member
    /// names. The string facade keeps its legacy spelling while the
    /// <see cref="GetMessageText"/> carrier preserves typed containment for
    /// structural sinks. Identity lives in <see cref="Subject"/> and stays raw
    /// (issue #3319). The
    /// <c>ApiChangeText_WithLiteralEscapeSpellings_RemainsRenderable</c> test
    /// gates literal escape-like text and record-copy updates.
    /// </summary>
    private ApiChangeText _messageText = ApiChangeText.Create(Message);

    public string Message
    {
        get => _messageText.Legacy;
        init => _messageText = ApiChangeText.Create(value);
    }

    /// <inheritdoc cref="Message"/>
    private ApiChangeText? _oldValueText = ApiChangeText.CreateOptional(OldValue);

    public string? OldValue
    {
        get => _oldValueText?.Legacy;
        init => _oldValueText = ApiChangeText.CreateOptional(value);
    }

    /// <inheritdoc cref="Message"/>
    private ApiChangeText? _newValueText = ApiChangeText.CreateOptional(NewValue);

    public string? NewValue
    {
        get => _newValueText?.Legacy;
        init => _newValueText = ApiChangeText.CreateOptional(value);
    }

    /// <summary>Gets the message contained from its untreated input.</summary>
    public InertString GetMessageText() => _messageText.Inert;

    /// <summary>Gets the old value contained from its untreated input.</summary>
    public InertString? GetOldValueText() => _oldValueText?.Inert;

    /// <summary>Gets the new value contained from its untreated input.</summary>
    public InertString? GetNewValueText() => _newValueText?.Inert;

    /// <inheritdoc cref="Kind"/>
    public ApiChangeCategory Category { get; init; } = Category;

    /// <inheritdoc cref="Kind"/>
    public ApiChangeSubject? Subject { get; init; } = Subject;

    private readonly record struct ApiChangeText(
        string Legacy,
        InertString Inert)
    {
        public static ApiChangeText Create(string value) =>
            new(
                CSharpIdentifierCore.ContainComposedName(value),
                new InertString(TextPolicy.Field, value));

        public static ApiChangeText? CreateOptional(string? value) =>
            value is null ? null : Create(value);
    }
}

/// <summary>
/// Named, explicitly-requested classification exceptions -- mirrors
/// <c>IlBodyDiffNormalization</c>'s policy shape. Default (<see cref="None"/>) is strict:
/// a fuzzy/soft member identity match (e.g. an extension method's static/instance
/// projection) is treated as unrelated: a removal plus an addition, each classified on its
/// own (a removal is breaking; an addition is additive, or potentially breaking on an
/// interface).
/// Requesting
/// <see cref="NormalizeMemberProjection"/> instead treats a fuzzy-matched member pair as
/// the same member observed under two projections, classifying only its facet deltas.
/// </summary>
[Flags]
public enum ApiDiffNormalization
{
    None = 0,
    NormalizeMemberProjection = 1,
}

public sealed record ApiDiffOptions(
    ApiDiffScope Scope = ApiDiffScope.Signature,
    ApiDiffNormalization Normalization = ApiDiffNormalization.None)
{
    public static ApiDiffOptions Default { get; } = new();
}

/// <summary>
/// All changes detected for a single type.
/// </summary>
public record TypeDiff(string TypeFullName, IReadOnlyList<ApiChange> Changes)
{
    public int BreakingCount => CountByClassification(ChangeClassification.Breaking);
    public int AdditiveCount => CountByClassification(ChangeClassification.Additive);
    public int PotentiallyBreakingCount => CountByClassification(ChangeClassification.PotentiallyBreaking);
    public int NonBreakingCount => CountByClassification(ChangeClassification.NonBreaking);

    public bool IsAdded => Changes.Count == 1 && Changes[0].Kind == ChangeKind.TypeAdded;
    public bool IsRemoved => Changes.Count == 1 && Changes[0].Kind == ChangeKind.TypeRemoved;

    private int CountByClassification(ChangeClassification classification)
    {
        int count = 0;
        foreach (var change in Changes)
        {
            if (change.Classification == classification)
                count++;
        }
        return count;
    }
}

/// <summary>
/// The complete result of comparing two API surfaces.
/// </summary>
public class ApiDiff
{
    public IReadOnlyList<TypeDiff> TypeDiffs { get; init; } = [];
    public IReadOnlyList<ApiDiffInspectionFailure> InspectionFailures { get; init; } = [];
    public int TotalBreaking { get; init; }
    public int TotalAdditive { get; init; }
    public int TotalPotentiallyBreaking { get; init; }
    public int TotalNonBreaking { get; init; }
    public bool IsEmpty => TypeDiffs.Count == 0 && InspectionFailures.Count == 0;
}

public sealed record ApiDiffInspectionFailure(
    string Side,
    string Operation,
    int SubjectToken,
    MetadataTypeNameFailureMechanism Mechanism,
    string Kind,
    string Detail,
    AssemblyReferenceIdentity? SubjectAssembly = null,
    AssemblyReferenceIdentity? DependencyAssembly = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SourceAssemblyPath { get; init; }
}

/// <summary>
/// The API change facts and classification gates that <see cref="ApiFindingClassifier"/>
/// applies over the Finding lane's type and member correspondence: type facets, member
/// modifiers, constraints, attributes, accessibility moves, API status transitions, and
/// the API-axis reclassification.
/// </summary>
public static class ApiDiffAnalyzer
{
    readonly record struct MemberKey(string Signature, SignatureDecodeStatus? DecodeStatus)
    {
        public override string ToString()
            => DecodeStatus is SignatureDecodeStatus.Degraded
                ? $"{Signature} [decode: degraded]"
                : Signature;
    }

    public static IReadOnlyList<ApiDiffInspectionFailure>
        ProjectInspectionFailures(
            ApiSurface oldSurface,
            ApiSurface newSurface)
    {
        ArgumentNullException.ThrowIfNull(oldSurface);
        ArgumentNullException.ThrowIfNull(newSurface);

        return
        [
            .. oldSurface.InspectionFailures.Select(
                static failure =>
                    ProjectInspectionFailure(
                        "old",
                        failure)),
            .. newSurface.InspectionFailures.Select(
                static failure =>
                    ProjectInspectionFailure(
                        "new",
                        failure)),
        ];
    }

    static ApiDiffInspectionFailure ProjectInspectionFailure(
        string side,
        ApiSurfaceInspectionFailure failure) =>
        new(
            side,
            failure.Operation,
            failure.SubjectToken,
            failure.Mechanism,
            failure.Kind,
            failure.Detail,
            failure.SubjectAssembly,
            failure.DependencyAssembly)
        {
            SourceAssemblyPath =
                failure.SourceAssemblyPath,
        };

    internal static bool HasIncompleteTypeIdentity(
        ApiSurface surface) =>
        surface.InspectionFailures.Any(
            static failure =>
                failure.Operation
                    != ApiSurfaceInspectionFailure
                        .GenericParameterConstraintResolutionOperation);

    /// <summary>
    /// Whether transitions between two admitted states of one subject are compared: its
    /// accessibility moves (docs/design/api-population-scope.md) and its API status
    /// transitions (docs/design/api-status-analysis.md). Only when both surfaces were extracted
    /// with the same scope, and that scope is <see cref="ApiSurfaceExtractionScope.ApiAxis"/>
    /// or <see cref="ApiSurfaceExtractionScope.IncludeAll"/>, the scopes that admit both
    /// states. A surface whose scope is <see langword="null"/> (merged from disagreeing
    /// scopes, or deserialized) compares none.
    /// </summary>
    internal static bool ComparesTransitions(ApiSurface oldSurface, ApiSurface newSurface)
        => oldSurface.ExtractionScope is ApiSurfaceExtractionScope.ApiAxis
                or ApiSurfaceExtractionScope.IncludeAll
            && oldSurface.ExtractionScope == newSurface.ExtractionScope;

    /// <summary>
    /// Whether Breaking and Additive are scoped to the API axis, the public contract
    /// (docs/design/api-population-scope.md): only between two include-all surfaces, the one
    /// scope that admits subjects outside the API axis. There, a change whose subject is
    /// outside the API axis on both sides is <see cref="ChangeClassification.NonBreaking"/>.
    /// </summary>
    internal static bool ClassifiesByApiAxis(ApiSurface oldSurface, ApiSurface newSurface)
        => oldSurface.ExtractionScope is ApiSurfaceExtractionScope.IncludeAll
            && newSurface.ExtractionScope is ApiSurfaceExtractionScope.IncludeAll;

    /// <summary>
    /// Reclassifies each change whose subject no other assembly can reach on either side as
    /// <see cref="ChangeClassification.NonBreaking"/>: Breaking and Additive describe the
    /// public contract. A subject's reach is its effective external reach
    /// (<see cref="EffectiveReach(ApiType)"/>); a side where the subject is absent has none.
    /// </summary>
    internal static void ClassifyOutsideApiAxisAsNonBreaking(List<ApiChange> changes)
    {
        for (int index = 0; index < changes.Count; index++)
        {
            ApiChange change = changes[index];
            if (change.Classification != ChangeClassification.NonBreaking
                && change.Subject is { } subject
                && IsOutsideApiAxis(subject))
            {
                changes[index] = change with { Classification = ChangeClassification.NonBreaking };
            }
        }
    }

    static bool IsOutsideApiAxis(ApiChangeSubject subject)
        => subject.Kind == ApiChangeSubjectKind.Type
            ? EffectiveReach(subject.OldType?.Type) == 0
                && EffectiveReach(subject.NewType?.Type) == 0
            : EffectiveReach(subject.OldMember) == 0
                && EffectiveReach(subject.NewMember) == 0;

    /// <summary>
    /// A Type's external reach: its own accessibility's reach, or none when an enclosing Type
    /// cannot be reached from another assembly. An absent Type has none; a Type whose enclosing
    /// reach was not recorded, or whose accessibility is unrecognized, is treated as reachable.
    /// </summary>
    static int EffectiveReach(ApiType? type)
        => type is null || type.EnclosingTypesExternallyReachable is false
            ? 0
            : ExternalReach(type.Accessibility) ?? int.MaxValue;

    /// <summary>
    /// A Member's external reach: its own accessibility's reach within a reachable declaring
    /// Type, and none within an unreachable one, as for a public method of an internal Type.
    /// </summary>
    static int EffectiveReach(ApiMemberHandle? member)
        => member is null || EffectiveReach(member.Type) == 0
            ? 0
            : ExternalReach(member.Member.Accessibility) ?? int.MaxValue;

    /// <summary>
    /// Type-facet classification only (kind/sealed/abstract/base type/interfaces/type
    /// parameters/type attributes) -- no member-level walk. <see cref="ApiFindingClassifier"/>
    /// sources member-level changes from the Finding lane's own member comparison.
    /// </summary>
    internal static List<ApiChange> CompareTypeFacetsOnly(ApiType oldType, ApiType newType, ApiDiffOptions options, bool compareTransitions)
    {
        List<ApiChange> changes = [];
        if (IncludesSignature(options))
        {
            CompareTypeMetadata(oldType, newType, changes, compareTransitions);
            CompareTypeParameters(oldType, newType, changes);
        }
        if (IncludesAttributes(options))
            CompareAttributes(
                oldType.Attributes,
                newType.Attributes,
                changes,
                ChangeKind.TypeAttributeAdded,
                ChangeKind.TypeAttributeRemoved,
                $"Type '{oldType.FullName}'",
                ApiChangeSubject.Type(oldType, newType));
        return changes;
    }

    private static void CompareTypeMetadata(ApiType oldType, ApiType newType, List<ApiChange> changes, bool compareTransitions)
    {
        if (compareTransitions)
        {
            CompareAccessibility(
                oldType.Accessibility,
                newType.Accessibility,
                ChangeKind.TypeAccessibilityChanged,
                $"Type '{oldType.FullName}'",
                ApiChangeSubject.Type(oldType, newType),
                changes);
            CompareStatus(
                new ApiStatus(
                    oldType.IsHidden,
                    oldType.IsObsolete,
                    oldType.ObsoleteMessage,
                    oldType.ObsoleteIsError),
                new ApiStatus(
                    newType.IsHidden,
                    newType.IsObsolete,
                    newType.ObsoleteMessage,
                    newType.ObsoleteIsError),
                ChangeKind.TypeHiddenChanged,
                ChangeKind.TypeObsoleteChanged,
                $"Type '{oldType.FullName}'",
                ApiChangeSubject.Type(oldType, newType),
                changes);
        }

        // Kind changed
        if (oldType.Kind != newType.Kind)
        {
            changes.Add(new ApiChange(ChangeKind.TypeKindChanged, ChangeClassification.Breaking,
                $"Type kind changed from '{oldType.Kind}' to '{newType.Kind}'",
                oldType.Kind, newType.Kind,
                Subject: ApiChangeSubject.Type(oldType, newType)));
        }

        // Sealed changes — skip for static types (IsSealed && IsAbstract both true)
        bool oldIsStatic = oldType.IsSealed && oldType.IsAbstract;
        bool newIsStatic = newType.IsSealed && newType.IsAbstract;

        if (!oldIsStatic && !newIsStatic)
        {
            if (!oldType.IsSealed && newType.IsSealed)
            {
                changes.Add(new ApiChange(ChangeKind.SealedAdded, ChangeClassification.Breaking,
                    "Type was sealed",
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }
            else if (oldType.IsSealed && !newType.IsSealed)
            {
                changes.Add(new ApiChange(ChangeKind.SealedRemoved, ChangeClassification.Additive,
                    "Type was unsealed",
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }

            if (!oldType.IsAbstract && newType.IsAbstract)
            {
                changes.Add(new ApiChange(ChangeKind.AbstractAdded, ChangeClassification.Breaking,
                    "Type was made abstract",
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }
            else if (oldType.IsAbstract && !newType.IsAbstract)
            {
                changes.Add(new ApiChange(ChangeKind.AbstractRemoved, ChangeClassification.Additive,
                    "Type was made non-abstract",
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }
        }

        // Base type changed
        if (oldType.BaseType != newType.BaseType)
        {
            changes.Add(new ApiChange(ChangeKind.BaseTypeChanged, ChangeClassification.Breaking,
                $"Base type changed from '{oldType.BaseType ?? "none"}' to '{newType.BaseType ?? "none"}'",
                oldType.BaseType, newType.BaseType,
                Subject: ApiChangeSubject.Type(oldType, newType)));
        }

        // Interfaces
        var oldInterfaces = new HashSet<string>(oldType.Interfaces ?? [], StringComparer.Ordinal);
        var newInterfaces = new HashSet<string>(newType.Interfaces ?? [], StringComparer.Ordinal);

        foreach (var removed in oldInterfaces.Except(newInterfaces).Order())
        {
            changes.Add(new ApiChange(ChangeKind.InterfaceRemoved, ChangeClassification.Breaking,
                $"Interface '{removed}' was removed",
                OldValue: removed,
                Subject: ApiChangeSubject.Type(oldType, newType)));
        }

        foreach (var added in newInterfaces.Except(oldInterfaces).Order())
        {
            // Adding interface to an interface is potentially breaking (implementers must add it)
            var classification = newType.Kind == "interface"
                ? ChangeClassification.PotentiallyBreaking
                : ChangeClassification.Additive;

            changes.Add(new ApiChange(ChangeKind.InterfaceAdded, classification,
                $"Interface '{added}' was added",
                NewValue: added,
                Subject: ApiChangeSubject.Type(oldType, newType)));
        }
    }

    private static void CompareTypeParameters(ApiType oldType, ApiType newType, List<ApiChange> changes)
    {
        var oldParams = oldType.TypeParameters ?? [];
        var newParams = newType.TypeParameters ?? [];

        if (oldParams.Count != newParams.Count)
        {
            changes.Add(new ApiChange(ChangeKind.TypeParameterCountChanged, ChangeClassification.Breaking,
                $"Generic parameter count changed from {oldParams.Count} to {newParams.Count}",
                oldParams.Count.ToString(), newParams.Count.ToString(),
                Subject: ApiChangeSubject.Type(oldType, newType)));
            return; // No point comparing individual params when count changed
        }

        for (int i = 0; i < oldParams.Count; i++)
        {
            var oldParam = oldParams[i];
            var newParam = newParams[i];

            // Variance changed
            if (oldParam.Variance != newParam.Variance)
            {
                changes.Add(new ApiChange(ChangeKind.TypeParameterVarianceChanged, ChangeClassification.Breaking,
                    $"Generic parameter '{newParam.Name}' variance changed from '{oldParam.Variance ?? "none"}' to '{newParam.Variance ?? "none"}'",
                    oldParam.Variance, newParam.Variance,
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }

            CompareTypeParameterConstraints(
                oldParam,
                newParam,
                ApiChangeSubject.Type(oldType, newType),
                changes);
        }
    }

    internal static void CompareMemberTypeParameterConstraints(
        ApiType oldType,
        ApiType newType,
        ApiMember oldMember,
        ApiMember newMember,
        List<ApiChange> changes)
    {
        IReadOnlyList<TypeParameter>? oldParameters =
            oldMember.SignatureModel?.TypeParameters;
        IReadOnlyList<TypeParameter>? newParameters =
            newMember.SignatureModel?.TypeParameters;
        if (oldParameters is null
            || newParameters is null
            || oldParameters.Count != newParameters.Count)
        {
            return;
        }

        ApiChangeSubject subject =
            ApiChangeSubject.Member(
                oldType,
                oldMember,
                newType,
                newMember);
        for (int index = 0; index < oldParameters.Count; index++)
        {
            CompareTypeParameterConstraints(
                oldParameters[index],
                newParameters[index],
                subject,
                changes);
        }
    }

    static void CompareTypeParameterConstraints(
        TypeParameter oldParameter,
        TypeParameter newParameter,
        ApiChangeSubject subject,
        List<ApiChange> changes)
    {
        var oldConstraints = new HashSet<string>(
            oldParameter.Constraints,
            StringComparer.Ordinal);
        var newConstraints = new HashSet<string>(
            newParameter.Constraints,
            StringComparer.Ordinal);

        List<string> added =
            [.. newConstraints.Except(oldConstraints)];
        List<string> removed =
            [.. oldConstraints.Except(newConstraints)];

        if (added.Count > 0)
        {
            changes.Add(
                new ApiChange(
                    ChangeKind.TypeParameterConstraintTightened,
                    ChangeClassification.Breaking,
                    $"Generic parameter '{newParameter.Name}' added constraints: "
                        + string.Join(", ", added),
                    NewValue: string.Join(", ", added),
                    Subject: subject));
        }

        if (removed.Count > 0)
        {
            changes.Add(
                new ApiChange(
                    ChangeKind.TypeParameterConstraintLoosened,
                    ChangeClassification.Additive,
                    $"Generic parameter '{newParameter.Name}' removed constraints: "
                        + string.Join(", ", removed),
                    OldValue: string.Join(", ", removed),
                    Subject: subject));
        }
    }

    internal static void CompareMemberModifiers(ApiType oldType, ApiType newType, ApiMember oldMember, ApiMember newMember, List<ApiChange> changes, bool compareTransitions)
    {
        if (compareTransitions)
        {
            CompareAccessibility(
                oldMember.Accessibility,
                newMember.Accessibility,
                ChangeKind.MemberAccessibilityChanged,
                $"Member '{oldMember.Name}'",
                ApiChangeSubject.Member(oldType, oldMember, newType, newMember),
                changes);
            CompareStatus(
                new ApiStatus(
                    oldMember.IsHidden,
                    oldMember.IsObsolete,
                    oldMember.ObsoleteMessage,
                    oldMember.ObsoleteIsError),
                new ApiStatus(
                    newMember.IsHidden,
                    newMember.IsObsolete,
                    newMember.ObsoleteMessage,
                    newMember.ObsoleteIsError),
                ChangeKind.MemberHiddenChanged,
                ChangeKind.MemberObsoleteChanged,
                $"Member '{oldMember.Name}'",
                ApiChangeSubject.Member(oldType, oldMember, newType, newMember),
                changes);
        }

        // Virtual removed
        if (oldMember.IsVirtual && !newMember.IsVirtual)
        {
            changes.Add(new ApiChange(ChangeKind.VirtualRemoved, ChangeClassification.Breaking,
                $"Member '{oldMember.Name}' is no longer virtual",
                GetMemberKey(oldMember).ToString(),
                GetMemberKey(newMember).ToString(),
                Subject: ApiChangeSubject.Member(oldType, oldMember, newType, newMember)));
        }

        // Abstract added
        if (!oldMember.IsAbstract && newMember.IsAbstract)
        {
            changes.Add(new ApiChange(ChangeKind.AbstractMemberAdded, ChangeClassification.Breaking,
                $"Member '{oldMember.Name}' was made abstract",
                GetMemberKey(oldMember).ToString(),
                GetMemberKey(newMember).ToString(),
                Subject: ApiChangeSubject.Member(oldType, oldMember, newType, newMember)));
        }

        // Enum value changed
        if (oldMember.EnumValue != null && newMember.EnumValue != null &&
            oldMember.EnumValue != newMember.EnumValue)
        {
            changes.Add(new ApiChange(ChangeKind.EnumValueChanged, ChangeClassification.PotentiallyBreaking,
                $"Enum member '{oldMember.Name}' value changed from {oldMember.EnumValue} to {newMember.EnumValue}",
                oldMember.EnumValue.Value.ToString(), newMember.EnumValue.Value.ToString(),
                Subject: ApiChangeSubject.Member(oldType, oldMember, newType, newMember)));
        }
    }

    /// <summary>
    /// Reports an accessibility move (docs/design/api-population-scope.md): every move between
    /// two known accessibilities is a change, Breaking when external reach narrows, Additive
    /// when it widens, and NonBreaking when it is unchanged, as <c>internal</c> to
    /// <c>private</c> is. On the API axis a move that leaves the axis is a population
    /// difference, which surfaces as a removal or an addition instead; under include-all both
    /// sides are admitted, so <c>public</c> to <c>internal</c> is a Breaking move. Runs only
    /// where <see cref="ComparesTransitions"/> holds.
    /// </summary>
    static void CompareAccessibility(
        string? oldAccessibility,
        string? newAccessibility,
        ChangeKind kind,
        string subjectText,
        ApiChangeSubject subject,
        List<ApiChange> changes)
    {
        string oldText = string.IsNullOrEmpty(oldAccessibility) ? "public" : oldAccessibility;
        string newText = string.IsNullOrEmpty(newAccessibility) ? "public" : newAccessibility;
        if (ExternalReach(oldAccessibility) is not { } oldReach
            || ExternalReach(newAccessibility) is not { } newReach
            || string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            return;
        }

        ChangeClassification classification = ClassifyReachMove(oldReach, newReach);
        string verb = classification switch
        {
            ChangeClassification.Breaking => "narrowed",
            ChangeClassification.Additive => "widened",
            _ => "changed",
        };
        changes.Add(new ApiChange(
            kind,
            classification,
            $"{subjectText} accessibility {verb} from '{oldText}' to '{newText}'",
            oldText,
            newText,
            Subject: subject));
    }

    /// <summary>
    /// How a move between two external reaches classifies: Breaking when reach narrows,
    /// Additive when it widens, and NonBreaking when it is unchanged.
    /// </summary>
    static ChangeClassification ClassifyReachMove(int oldReach, int newReach)
        => newReach < oldReach ? ChangeClassification.Breaking
            : newReach > oldReach ? ChangeClassification.Additive
            : ChangeClassification.NonBreaking;

    /// <summary>
    /// Classifies a matched Member's signature change (docs/design/api-population-scope.md):
    /// Breaking, unless the two signatures differ only in how they spell their accessors'
    /// accessibility, which is relative to the property's own. Then each accessor's own
    /// external reach decides, as it does for an accessibility move: Breaking when any
    /// accessor's reach narrows, Additive when reach only widens, and NonBreaking when no
    /// accessor's reach changes.
    /// </summary>
    internal static ChangeClassification ClassifySignatureChange(ApiMember oldMember, ApiMember newMember)
        => ClassifyAccessorSpellingChange(oldMember, newMember) ?? ChangeClassification.Breaking;

    /// <summary>
    /// The accessor reach classification when the two signatures differ only in their
    /// accessor accessibility spelling: the same accessors, in the same order and with the
    /// same accessor facts (<see cref="SameAccessorFacts"/>), the same declaration modifiers
    /// (<see cref="SameDeclarationModifiers"/>), and the same text before the accessor list,
    /// which each side's typed accessors reproduce. The signature text spells no declaration
    /// modifier and no <c>init</c>, so those facts are compared from the typed model. An
    /// accessor's own accessibility is its spelled accessibility, or its property's when it
    /// spells none. <see langword="null"/> for any other difference, including an added or
    /// removed accessor, or when either side's typed accessors are unavailable.
    /// </summary>
    static ChangeClassification? ClassifyAccessorSpellingChange(ApiMember oldMember, ApiMember newMember)
    {
        if (oldMember.SignatureModel?.Accessors is not { Count: > 0 } oldAccessors
            || newMember.SignatureModel?.Accessors is not { } newAccessors
            || oldAccessors.Count != newAccessors.Count
            || !SameDeclarationModifiers(oldMember, newMember)
            || SignatureBeforeAccessors(oldMember.Signature, oldAccessors) is not { } oldDeclaration
            || SignatureBeforeAccessors(newMember.Signature, newAccessors) is not { } newDeclaration
            || !string.Equals(oldDeclaration, newDeclaration, StringComparison.Ordinal))
        {
            return null;
        }

        ChangeClassification classification = ChangeClassification.NonBreaking;
        for (int index = 0; index < oldAccessors.Count; index++)
        {
            ApiAccessor oldAccessor = oldAccessors[index];
            ApiAccessor newAccessor = newAccessors[index];
            if (!SameAccessorFacts(oldAccessor, newAccessor)
                || ExternalReach(oldAccessor.Accessibility ?? oldMember.Accessibility) is not { } oldReach
                || ExternalReach(newAccessor.Accessibility ?? newMember.Accessibility) is not { } newReach)
            {
                return null;
            }

            switch (ClassifyReachMove(oldReach, newReach))
            {
                case ChangeClassification.Breaking:
                    classification = ChangeClassification.Breaking;
                    break;
                case ChangeClassification.Additive when classification == ChangeClassification.NonBreaking:
                    classification = ChangeClassification.Additive;
                    break;
            }
        }
        return classification;
    }

    /// <summary>
    /// Every declaration modifier the member model records, other than accessibility and
    /// hidden status, which their own rows report.
    /// </summary>
    static bool SameDeclarationModifiers(ApiMember oldMember, ApiMember newMember)
        => oldMember.IsStatic == newMember.IsStatic
            && oldMember.IsVirtual == newMember.IsVirtual
            && oldMember.IsAbstract == newMember.IsAbstract
            && oldMember.IsOverride == newMember.IsOverride
            && oldMember.IsSealed == newMember.IsSealed
            && oldMember.IsReadOnly == newMember.IsReadOnly
            && oldMember.IsUnsafe == newMember.IsUnsafe
            && oldMember.IsExplicitInterfaceImplementation == newMember.IsExplicitInterfaceImplementation;

    /// <summary>
    /// The accessor facts the model records that do not follow from its accessibility: its
    /// kind, its structural return (an <c>init</c> setter's carries <c>IsExternalInit</c>), and
    /// its <c>readonly</c> and explicit-implementation facts. Its
    /// <see cref="ApiAccessor.DeclarationModifiersMatchProperty"/> is not compared: Roslyn
    /// emits a <c>private</c> accessor of a virtual property as a non-virtual method, so that
    /// fact follows the accessor's accessibility, and the member's own modifiers
    /// (<see cref="SameDeclarationModifiers"/>) already carry the declaration.
    /// </summary>
    static bool SameAccessorFacts(ApiAccessor oldAccessor, ApiAccessor newAccessor)
        => string.Equals(oldAccessor.Kind, newAccessor.Kind, StringComparison.Ordinal)
            && string.Equals(
                oldAccessor.StructuralReturnType,
                newAccessor.StructuralReturnType,
                StringComparison.Ordinal)
            && oldAccessor.IsReadOnly == newAccessor.IsReadOnly
            && oldAccessor.IsExplicitInterfaceImplementation == newAccessor.IsExplicitInterfaceImplementation;

    /// <summary>
    /// The signature text before its accessor list, such as <c>int Value</c> for
    /// <c>int Value { get; protected set; }</c>, when the signature ends with the list its
    /// typed accessors spell; otherwise <see langword="null"/>.
    /// </summary>
    static string? SignatureBeforeAccessors(string? signature, List<ApiAccessor> accessors)
    {
        if (signature is null)
            return null;

        var list = new System.Text.StringBuilder(" {");
        foreach (ApiAccessor accessor in accessors)
        {
            list.Append(' ');
            if (accessor.Accessibility is { } accessibility)
                list.Append(accessibility).Append(' ');
            list.Append(accessor.Kind).Append(';');
        }
        list.Append(" }");
        string spelled = list.ToString();
        return signature.EndsWith(spelled, StringComparison.Ordinal)
            ? signature[..^spelled.Length]
            : null;
    }

    /// <summary>One subject's API status facts (docs/design/api-status-analysis.md#status-facts).</summary>
    readonly record struct ApiStatus(
        bool IsHidden,
        bool IsObsolete,
        string? ObsoleteMessage,
        bool ObsoleteIsError);

    /// <summary>
    /// Reports a subject's API status transitions (docs/design/api-status-analysis.md): one
    /// hidden row when <c>EditorBrowsable(Never)</c> is added or removed, and one obsolete row
    /// when <c>[Obsolete]</c> is added or removed or its error flag or message changes. Each
    /// is NonBreaking, except an obsolete row that adds <c>[Obsolete]</c>, as a warning or an
    /// error, or turns its warning into an error: a caller that builds with warnings as errors,
    /// or any caller of an error, stops compiling, so it is Breaking. Runs only where
    /// <see cref="ComparesTransitions"/> holds.
    /// </summary>
    static void CompareStatus(
        ApiStatus oldStatus,
        ApiStatus newStatus,
        ChangeKind hiddenKind,
        ChangeKind obsoleteKind,
        string subjectText,
        ApiChangeSubject subject,
        List<ApiChange> changes)
    {
        if (oldStatus.IsHidden != newStatus.IsHidden)
        {
            changes.Add(new ApiChange(
                hiddenKind,
                ChangeClassification.NonBreaking,
                newStatus.IsHidden
                    ? $"{subjectText} was hidden"
                    : $"{subjectText} is no longer hidden",
                HiddenText(oldStatus.IsHidden),
                HiddenText(newStatus.IsHidden),
                ApiChangeCategory.Status,
                subject));
        }

        string? oldMessage = oldStatus.IsObsolete ? oldStatus.ObsoleteMessage : null;
        string? newMessage = newStatus.IsObsolete ? newStatus.ObsoleteMessage : null;
        bool oldIsError = oldStatus.IsObsolete && oldStatus.ObsoleteIsError;
        bool newIsError = newStatus.IsObsolete && newStatus.ObsoleteIsError;
        if (oldStatus.IsObsolete == newStatus.IsObsolete
            && oldIsError == newIsError
            && string.Equals(oldMessage, newMessage, StringComparison.Ordinal))
        {
            return;
        }

        string description =
            !oldStatus.IsObsolete
                ? newIsError ? "was made obsolete as an error" : "was made obsolete"
            : !newStatus.IsObsolete ? "is no longer obsolete"
            : oldIsError != newIsError
                ? newIsError
                    ? "obsolete warning became an error"
                    : "obsolete error became a warning"
            : "obsolete message changed";
        string oldText = ObsoleteText(oldStatus.IsObsolete, oldMessage, oldIsError);
        string newText = ObsoleteText(newStatus.IsObsolete, newMessage, newIsError);
        changes.Add(new ApiChange(
            obsoleteKind,
            (newStatus.IsObsolete && !oldStatus.IsObsolete) || (newIsError && !oldIsError)
                ? ChangeClassification.Breaking
                : ChangeClassification.NonBreaking,
            $"{subjectText} {description}, from '{oldText}' to '{newText}'",
            oldText,
            newText,
            ApiChangeCategory.Status,
            subject));
    }

    static string HiddenText(bool isHidden)
        => isHidden ? "hidden" : "visible";

    /// <summary>
    /// The C# spelling of one side's obsolete status, such as <c>[Obsolete("m", true)]</c>,
    /// or <c>not obsolete</c>.
    /// </summary>
    static string ObsoleteText(bool isObsolete, string? message, bool isError)
    {
        if (!isObsolete)
            return "not obsolete";
        if (message is null)
            return isError ? "[Obsolete(null, true)]" : "[Obsolete]";
        string literal = "\"" + message
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        return isError ? $"[Obsolete({literal}, true)]" : $"[Obsolete({literal})]";
    }

    /// <summary>
    /// The external reach of an accessibility, widest first: which callers in other
    /// assemblies, the public contract, a declaration reaches. <c>protected internal</c>
    /// reaches the same ones <c>protected</c> does; the callers it adds are in its own
    /// assembly and its friends. <c>internal</c>, <c>private protected</c>, and
    /// <c>private</c> reach none.
    /// <see langword="null"/> for an unrecognized accessibility.
    /// </summary>
    static int? ExternalReach(string? accessibility) => accessibility switch
    {
        null or "" or "public" => 2,
        "protected internal" or "protected" => 1,
        "internal" or "private protected" or "private" => 0,
        _ => null,
    };

    private static MemberKey GetMemberKey(ApiMember member)
    {
        // Use signature when available (methods, properties), else fall back to kind + name
        return new MemberKey(
            member.Signature ?? $"{member.Kind}:{member.Name}",
            member.SignatureDecodeStatus);
    }

    internal static void CompareAttributes(
        IEnumerable<string> oldAttributes,
        IEnumerable<string> newAttributes,
        List<ApiChange> changes,
        ChangeKind addedKind,
        ChangeKind removedKind,
        string subject,
        ApiChangeSubject structuredSubject)
    {
        var oldSet = new HashSet<string>(oldAttributes, StringComparer.Ordinal);
        var newSet = new HashSet<string>(newAttributes, StringComparer.Ordinal);

        foreach (var removed in oldSet.Except(newSet).Order(StringComparer.Ordinal))
        {
            changes.Add(new ApiChange(
                removedKind,
                ChangeClassification.PotentiallyBreaking,
                $"{subject} attribute '{removed}' was removed",
                OldValue: removed,
                Category: ApiChangeCategory.Attribute,
                Subject: structuredSubject));
        }

        foreach (var added in newSet.Except(oldSet).Order(StringComparer.Ordinal))
        {
            changes.Add(new ApiChange(
                addedKind,
                ChangeClassification.PotentiallyBreaking,
                $"{subject} attribute '{added}' was added",
                NewValue: added,
                Category: ApiChangeCategory.Attribute,
                Subject: structuredSubject));
        }
    }

    internal static bool IncludesSignature(ApiDiffOptions options)
        => options.Scope.HasFlag(ApiDiffScope.Signature);

    internal static bool IncludesAttributes(ApiDiffOptions options)
        => options.Scope.HasFlag(ApiDiffScope.Attributes);
}
