using System.Globalization;
using System.Text;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

/// <summary>The role of one identified part of a printed member declaration.</summary>
public enum CSharpDeclarationPartKind
{
    /// <summary>One attribute, keyed by its metadata Type full name.</summary>
    Attribute,

    /// <summary>The return attribute list, keyed <c>return</c>.</summary>
    ReturnAttributes,

    /// <summary>One modifier, keyed by its keyword.</summary>
    Modifier,

    /// <summary>The return, property, field, or event type, keyed <c>type</c>.</summary>
    ReturnType,

    /// <summary>The member name, keyed <c>name</c>.</summary>
    Name,

    /// <summary>One parameter, keyed by its zero-based position.</summary>
    Parameter,

    /// <summary>One accessor, keyed by its kind (<c>get</c>, <c>set</c>, <c>init</c>).</summary>
    Accessor,

    /// <summary>One <c>where</c> clause, keyed by the type parameter it constrains.</summary>
    Constraint,
}

/// <summary>
/// One identified part of a printed member declaration. Parts correspond
/// across two versions by <see cref="Kind"/>, <see cref="Key"/>, and
/// <see cref="Occurrence"/>, the part's position among parts with the same
/// kind and key. <see cref="Start"/> and <see cref="Length"/> locate the part
/// in <see cref="CSharpDeclarationParts.Text"/>.
/// </summary>
public sealed record CSharpDeclarationPart(
    CSharpDeclarationPartKind Kind,
    string Key,
    int Occurrence,
    int Start,
    int Length);

/// <summary>
/// A printed member declaration and its identified parts, which come from the
/// model as the declaration is composed, never from reading the text back.
/// When the parts cannot be identified, or their concatenation disagrees with
/// the printed declaration, <see cref="Parts"/> is null and
/// <see cref="UnidentifiedReason"/> says why; the text is still the printed
/// declaration.
/// </summary>
public sealed record CSharpDeclarationParts(
    string Text,
    IReadOnlyList<CSharpDeclarationPart>? Parts,
    string? UnidentifiedReason)
{
    public bool IsIdentified => Parts is not null;

    internal static CSharpDeclarationParts Unidentified(string text, string reason)
        => new(text, null, reason);
}

/// <summary>
/// A piece of composed declaration text. A tagged segment is an identified
/// part; an untagged one is fixed punctuation or whitespace between parts.
/// </summary>
readonly record struct DeclarationSegment(
    string Text,
    CSharpDeclarationPartKind? Kind = null,
    string? Key = null)
{
    public const string NameKey = "name";
    public const string ReturnTypeKey = "type";

    public DeclarationSegment WithText(string text) => this with { Text = text };

    public static string Concat(IEnumerable<DeclarationSegment> segments)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
            builder.Append(segment.Text);
        return builder.ToString();
    }
}

internal static partial class CSharpDeclarationWriter
{
    /// <summary>
    /// Renders a member declaration with its identified parts. The text is
    /// exactly what <see cref="RenderMemberDeclaration"/> prints for the same
    /// member and options.
    /// </summary>
    public static CSharpDeclarationParts RenderMemberDeclarationParts(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        options ??= new CSharpDeclarationOptions();
        member = ExplicitAccessorShape(type, member);
        var plan = MemberPlanInputs.Collect(type, member, options).CreatePlan(options);
        return RenderPlannedMemberDeclarationParts(type, member, options, plan);
    }

    /// <summary>
    /// Renders a member's baseline and current declarations with one
    /// type-name plan made from both, so a Type is spelled the same way on each
    /// side. A missing side (an added or removed member) renders as null, and
    /// the plan then comes from the present side alone.
    /// </summary>
    public static (CSharpDeclarationParts? Baseline, CSharpDeclarationParts? Current) RenderMemberDeclarationPair(
        ApiType? baselineType,
        ApiMember? baselineMember,
        ApiType? currentType,
        ApiMember? currentMember,
        CSharpDeclarationOptions? options = null)
    {
        if ((baselineType is null) != (baselineMember is null))
            throw new ArgumentException("A baseline member needs its type.", nameof(baselineMember));
        if ((currentType is null) != (currentMember is null))
            throw new ArgumentException("A current member needs its type.", nameof(currentMember));
        if (baselineMember is null && currentMember is null)
            throw new ArgumentException("A pair needs at least one member.");

        options ??= new CSharpDeclarationOptions();
        ApiMember? baseline = baselineMember is null ? null : ExplicitAccessorShape(baselineType!, baselineMember);
        ApiMember? current = currentMember is null ? null : ExplicitAccessorShape(currentType!, currentMember);
        MemberPlanInputs? inputs = null;
        if (baseline is not null)
            inputs = MemberPlanInputs.Collect(baselineType!, baseline, options);
        if (current is not null)
        {
            var currentInputs = MemberPlanInputs.Collect(currentType!, current, options);
            inputs = inputs is null ? currentInputs : inputs.Union(currentInputs);
        }
        var plan = inputs!.CreatePlan(options);
        return (
            baseline is null ? null : RenderPlannedMemberDeclarationParts(baselineType!, baseline, options, plan),
            current is null ? null : RenderPlannedMemberDeclarationParts(currentType!, current, options, plan));
    }

    static CSharpDeclarationParts RenderPlannedMemberDeclarationParts(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions options,
        TypeNamePlan plan)
    {
        string text = RenderPlannedMemberDeclaration(type, member, options, plan);
        if (!TryComposeDeclarationSegments(type, member, options, out var segments, out string? reason))
            return CSharpDeclarationParts.Unidentified(text, reason);

        var builder = new StringBuilder(text.Length);
        var parts = new List<CSharpDeclarationPart>();
        var occurrences = new Dictionary<(CSharpDeclarationPartKind, string), int>();
        foreach (var segment in segments)
        {
            string spelled = plan.Apply(segment.Text);
            if (segment.Kind is { } kind)
            {
                string key = segment.Key!;
                occurrences.TryGetValue((kind, key), out int occurrence);
                occurrences[(kind, key)] = occurrence + 1;
                parts.Add(new CSharpDeclarationPart(kind, key, occurrence, builder.Length, spelled.Length));
            }
            builder.Append(spelled);
        }
        if (options.TerminateMemberDeclaration && NeedsTerminator(builder.ToString()))
            builder.Append(';');

        string composed = builder.ToString();
        return composed == text
            ? new CSharpDeclarationParts(text, parts, null)
            : CSharpDeclarationParts.Unidentified(
                text,
                "the composed parts disagree with the printed declaration");
    }

    /// <summary>
    /// Composes the declaration <see cref="RenderMemberDeclarationCore"/>
    /// prints as tagged segments, applying the same signature rewrites to each
    /// segment. Declines, with a reason, for every shape the core prints from
    /// compatibility text or rewrites as a whole.
    /// </summary>
    static bool TryComposeDeclarationSegments(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions options,
        out List<DeclarationSegment> segments,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? reason)
    {
        segments = [];
        reason = null;
        var safety = CSharpMemorySafetySpelling.Member(
            type,
            member,
            options.MemorySafetyLanguage,
            options.IsExtern,
            options.ForceUnsafe,
            isStandaloneMember: true);
        if (safety.Failure is { } failure)
        {
            reason = failure;
            return false;
        }
        if (CSharpMemorySafetySpelling.IsStandaloneEnumMember(type, member, isStandaloneMember: true))
        {
            reason = "an enum member is printed as a name and value";
            return false;
        }
        if (options.AbbreviateSignature)
        {
            reason = "an abbreviated signature is rewritten as a whole";
            return false;
        }
        if (member.IsFinalizer
            || member.Name.StartsWith("op_", StringComparison.Ordinal)
            || member.IsExtension)
        {
            reason = "printed from compatibility text";
            return false;
        }

        List<DeclarationSegment> signature;
        if (member.Kind == "field" && member.Signature == null && !string.IsNullOrWhiteSpace(member.ReturnType))
        {
            signature =
            [
                new(member.ReturnType, CSharpDeclarationPartKind.ReturnType, DeclarationSegment.ReturnTypeKey),
                new(" "),
                new(member.Name, CSharpDeclarationPartKind.Name, DeclarationSegment.NameKey),
            ];
        }
        else if (!TryComposeSignatureModel(
                type,
                member,
                options,
                methodParameters: null,
                parameterNames: null,
                out signature))
        {
            reason = "printed from compatibility text";
            return false;
        }
        if (signature.Count == 0)
        {
            reason = "the declaration has no signature";
            return false;
        }

        var knownIdentifiers = type.TypeParameters
            .Concat(member.SignatureModel?.TypeParameters ?? [])
            .Select(parameter => parameter.Name)
            .ToArray();
        // RenderMemberDeclarationCore escapes the name for these kinds, once each.
        bool escapeMemberName = member.Kind is "method" or "extension-method"
            or "explicit-interface-implementation" or "property" or "field" or "event";
        bool preserveQualifiedIndexerKeyword = IsExplicitInterfaceProperty(member)
            && member.SignatureModel?.MemberName == "this[]";
        for (int index = 0; index < signature.Count; index++)
        {
            var segment = signature[index];
            string text = EscapeKnownIdentifiers(segment.Text, knownIdentifiers);
            if (escapeMemberName && segment.Kind == CSharpDeclarationPartKind.Name)
                text = EscapeMemberNameInSignature(text, member.Name);
            // The pass keeps a qualified indexer's `this` only when `[` follows it,
            // and that bracket is the next segment. Escape with it attached; the
            // pass never rewrites `[`, so removing it again is exact.
            bool bracketFollows = preserveQualifiedIndexerKeyword
                && index + 1 < signature.Count
                && signature[index + 1].Text.StartsWith('[');
            text = bracketFollows
                ? EscapeQualifiedKeywordSegments(text + "[", preserveQualifiedIndexerKeyword)[..^1]
                : EscapeQualifiedKeywordSegments(text, preserveQualifiedIndexerKeyword);
            signature[index] = segment.WithText(text);
        }
        if (IsEvent(member)
            && !DeclarationSegment.Concat(signature).StartsWith("event ", StringComparison.Ordinal))
        {
            signature.Insert(0, new("event "));
        }

        var attributeLines = MemberAttributeLines(type, member, options, out bool hasLayoutAttribute);
        if (attributeLines.Any(static line => line.Key is null))
        {
            reason = "the attribute Types are not recorded";
            return false;
        }
        string separator = AttributeLineSeparator(options, hasLayoutAttribute);
        foreach (var line in attributeLines)
        {
            segments.Add(line);
            segments.Add(new(separator));
        }

        var modifiers = MemberModifiers(type, member, options, safety.Modifier);
        foreach (var modifier in modifiers)
        {
            segments.Add(new(modifier, CSharpDeclarationPartKind.Modifier, modifier));
            segments.Add(new(" "));
        }
        segments.AddRange(signature);
        return true;
    }

    /// <summary>
    /// The inputs a member declaration's type-name plan is made from. Two
    /// members' inputs union, so one plan can spell both sides of a pair.
    /// </summary>
    sealed record MemberPlanInputs(
        HashSet<string> References,
        HashSet<string> ShadowingNames,
        string DeclaredTypeName,
        List<ApiTypeReferenceIdentity> NestedTypeIdentities,
        HashSet<string> QualificationOnlyReferences,
        HashSet<string> ShortenableReferences,
        HashSet<string> ValueReferences,
        HashSet<string> SynthesizedAttributeReferences)
    {
        public static MemberPlanInputs Collect(
            ApiType type,
            ApiMember member,
            CSharpDeclarationOptions options)
        {
            ApiMember? signatureMember = options.IncludeSignatureAttributes ? member : null;
            var attributeReferences = CollectAttributeTypeReferences(member.Attributes, signatureMember)
                .ToHashSet(StringComparer.Ordinal);
            var attributeValueReferences = CollectAttributeValueTypeReferences(
                    member.Attributes,
                    signatureMember)
                .ToHashSet(StringComparer.Ordinal);
            var qualificationOnlyAttributeReferences = (options.IncludeSignatureAttributes
                    ? CollectQualificationOnlyAttributeTypeReferences(member.Attributes, member)
                    : CollectDeclaredAttributeTypeReferences(member.Attributes))
                .Concat(CollectExplicitInterfaceTypeReferences(member))
                .ToHashSet(StringComparer.Ordinal);
            var synthesizedAttributeReferences = member.IsObsolete && options.IncludeObsoleteAttribute
                ? new HashSet<string>(["System.Obsolete"], StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            var memberReferences = CollectMemberTypeReferences(
                    member,
                    options.IncludeSignatureAttributes)
                .ToHashSet(StringComparer.Ordinal);
            var explicitInterfaceReferences = CollectExplicitInterfaceTypeReferences(member)
                .ToHashSet(StringComparer.Ordinal);
            var references = memberReferences
                .Concat(attributeReferences)
                .Concat(explicitInterfaceReferences)
                .Concat(synthesizedAttributeReferences)
                .ToHashSet(StringComparer.Ordinal);
            return new MemberPlanInputs(
                references,
                CollectShadowingNames(type, [member]),
                CSharpFormatter.StripArity(type.Name),
                [.. MemberTypeReferenceIdentities(member)],
                qualificationOnlyAttributeReferences,
                memberReferences.Except(explicitInterfaceReferences).ToHashSet(StringComparer.Ordinal),
                attributeValueReferences,
                synthesizedAttributeReferences);
        }

        public MemberPlanInputs Union(MemberPlanInputs other)
            => new(
                [.. References, .. other.References],
                [.. ShadowingNames, .. other.ShadowingNames],
                other.DeclaredTypeName,
                [.. NestedTypeIdentities, .. other.NestedTypeIdentities],
                [.. QualificationOnlyReferences, .. other.QualificationOnlyReferences],
                [.. ShortenableReferences, .. other.ShortenableReferences],
                [.. ValueReferences, .. other.ValueReferences],
                [.. SynthesizedAttributeReferences, .. other.SynthesizedAttributeReferences]);

        public TypeNamePlan CreatePlan(CSharpDeclarationOptions options)
            => TypeNamePlan.Create(
                References,
                options,
                ShadowingNames,
                DeclaredTypeName,
                TypedNestedTypeRoots(NestedTypeIdentities),
                QualificationOnlyReferences,
                ShortenableReferences,
                ValueReferences,
                SynthesizedAttributeReferences);
    }
}
