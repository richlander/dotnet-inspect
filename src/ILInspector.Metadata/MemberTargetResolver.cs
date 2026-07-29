using System.Text;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public enum MemberTargetKind
{
    Unknown,
    Constructor,
    Finalizer,
    Field,
    Property,
    Method,
    Operator,
    ExplicitInterfaceImplementation,
    ExtensionMethod,
    Event
}

public enum MemberTargetDiagnosticKind
{
    MissingMember,
    AmbiguousMember,
    DigestNotFound,
    DigestAmbiguous,
    OverloadOutOfRange,
    ConflictingSelectors
}

public sealed record MemberTargetSelector(
    string RequestedText,
    string Name,
    int? OverloadIndex = null,
    string? DigestPrefix = null,
    string? Kind = null,
    int? GenericArity = null)
{
    public string NormalizedSelector
    {
        get
        {
            var kindPrefix = Kind switch
            {
                "operator" => "operator:",
                "explicit-interface-implementation" => "explicit:",
                "extension-method" => "extension:",
                _ => ""
            };
            var suffix = DigestPrefix is { Length: > 0 }
                ? $"~{DigestPrefix}"
                : OverloadIndex is { } index ? $":{index}" : "";
            return $"{kindPrefix}{Name}{suffix}";
        }
    }

    public static MemberTargetSelector Parse(string value)
    {
        var requested = value.Trim();
        var work = requested;
        string? kind = null;

        foreach (var prefix in (ReadOnlySpan<string>)["operator:", "explicit:", "extension:"])
        {
            if (!work.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            kind = NormalizeKind(prefix[..^1]);
            work = work[prefix.Length..];
            break;
        }

        var (digestHead, digest) = SplitDigest(work);
        var (overloadHead, overloadIndex) = FqnParser.TrySplitOverload(digestHead);
        var genericArity = TryGetGenericArity(overloadHead);
        var name = TypeMatcher.NormalizeMemberName(overloadHead);
        return new MemberTargetSelector(
            requested,
            name,
            overloadIndex,
            digest,
            kind,
            genericArity);
    }

    public static string? NormalizeKindQualifier(string value)
        => value.ToLowerInvariant() switch
        {
            "operator" => "operator",
            "explicit" => "explicit-interface-implementation",
            "extension" => "extension-method",
            _ => null
        };

    static string NormalizeKind(string value)
        => NormalizeKindQualifier(value) ?? value.ToLowerInvariant();

    static (string Head, string? Digest) SplitDigest(string value)
    {
        var tilde = value.LastIndexOf('~');
        if (tilde <= 0 || tilde == value.Length - 1)
            return (value, null);

        var digest = value[(tilde + 1)..];
        if (!digest.All(Uri.IsHexDigit))
            return (value, null);

        return (value[..tilde], digest.ToLowerInvariant());
    }

    static int? TryGetGenericArity(string value)
    {
        var angleStart = value.IndexOf('<');
        if (angleStart <= 0)
            return null;

        var angleEnd = value.LastIndexOf('>');
        if (angleEnd <= angleStart || angleEnd != value.Length - 1)
            return null;

        var arguments = value.AsSpan((angleStart + 1)..angleEnd);
        if (arguments.IsEmpty || arguments.IsWhiteSpace())
            return 0;

        var count = 1;
        var depth = 0;
        foreach (var ch in arguments)
        {
            if (ch == '<')
                depth++;
            else if (ch == '>')
                depth--;
            else if (ch == ',' && depth == 0)
                count++;
        }

        return count;
    }
}

public sealed record BodyTarget(
    string DeclaringType,
    string CanonicalSignature,
    int? MetadataToken = null,
    int? DeclaringOverloadIndex = null);

public sealed record ResolvedMemberTarget(
    ApiType ApiType,
    ApiMemberHandle ApiMember,
    MemberAnchor Anchor,
    string RequestedText,
    string NormalizedSelector,
    int SelectorIndex,
    int DeclaringOverloadIndex,
    int? OverloadIndex,
    string? DigestPrefix,
    int? GenericArity,
    MemberTargetKind Kind,
    BodyTarget? Body);

public sealed record MemberTargetCandidate(
    ApiMember Member,
    MemberAnchor Anchor,
    int SelectorIndex,
    int DeclaringOverloadIndex)
{
    public string SelectorName => ApiMemberIdentity.GetMemberSelectorName(Member);
    public string Selector => SelectorIndex > 1 ? $"{SelectorName}:{SelectorIndex}" : SelectorName;
    public string StableSelector => Anchor.StableSelector;
    public string CanonicalSignature => Anchor.CanonicalSignature;
}

public sealed record MemberTargetDiagnostic(
    MemberTargetDiagnosticKind Kind,
    string Message,
    IReadOnlyList<MemberTargetCandidate> Candidates)
{
    /// <summary>
    /// Composes the diagnostic, candidate list included, as one message.
    /// </summary>
    /// <remarks>
    /// This layer owns metadata facts, not presentation, so it formats rather
    /// than writes. The previous shape took a <see cref="TextWriter"/> and
    /// wrote to it, which put a stderr diagnostic outside the CLI's single
    /// writer and outside the source scan that pins it (issue #3319).
    /// </remarks>
    public string FormatMessage()
    {
        if (Candidates.Count == 0)
            return Message;

        var message = new StringBuilder(Message);
        foreach (var candidate in Candidates)
            message.AppendLine().Append($"  {candidate.StableSelector}  {candidate.CanonicalSignature}");

        return message.ToString();
    }
}

public sealed record MemberTargetResolution(
    ResolvedMemberTarget? Target,
    MemberTargetDiagnostic? Diagnostic,
    IReadOnlyList<MemberTargetCandidate> Candidates)
{
    public bool Found => Target is not null;
}

public static class MemberTargetResolver
{
    public static IReadOnlyList<MemberTargetCandidate> GetCandidates(
        ApiType type,
        MemberTargetSelector selector,
        IReadOnlyCollection<string>? kindFilter = null)
    {
        var declaringMembers = type.Members
            .Where(member => TypeMatcher.MatchesMemberName(member.Name, selector.Name));

        if (selector.Kind is { Length: > 0 })
            declaringMembers = declaringMembers.Where(member => string.Equals(member.Kind, selector.Kind, StringComparison.OrdinalIgnoreCase));

        if (kindFilter is { Count: > 0 })
            declaringMembers = declaringMembers.Where(member => kindFilter.Contains(member.Kind));

        var declaring = declaringMembers.ToList();
        var selected = declaring.AsEnumerable();
        if (selector.GenericArity is { } genericArity)
            selected = selected.Where(member => (member.SignatureModel?.TypeParameters.Count ?? 0) == genericArity);

        var display = selected
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ThenBy(ApiMemberIdentity.GetMemberSignatureSortKey, StringComparer.Ordinal)
            .ToList();

        var selectorIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        List<MemberTargetCandidate> candidates = [];
        foreach (var member in display)
        {
            var selectorName = ApiMemberIdentity.GetMemberSelectorName(member);
            selectorIndices.TryGetValue(selectorName, out var selectorIndex);
            selectorIndex++;
            selectorIndices[selectorName] = selectorIndex;

            candidates.Add(new MemberTargetCandidate(
                member,
                ApiMemberIdentity.GetMemberAnchor(type, member),
                selectorIndex,
                declaring.IndexOf(member) + 1));
        }

        return candidates;
    }

    public static MemberTargetResolution Resolve(
        ApiType type,
        MemberTargetSelector selector,
        IReadOnlyCollection<string>? kindFilter = null)
    {
        var candidates = GetCandidates(type, selector, kindFilter);
        if (candidates.Count == 0)
        {
            return Failure(MemberTargetDiagnosticKind.MissingMember,
                $"Error: No members matched selector '{selector.RequestedText}'.",
                candidates);
        }

        if (selector.OverloadIndex.HasValue && selector.DigestPrefix is { Length: > 0 })
        {
            return Failure(MemberTargetDiagnosticKind.ConflictingSelectors,
                "Error: digest selector cannot be combined with --index/Name:N.",
                candidates);
        }

        MemberTargetCandidate selected;
        int? accessorOrdinal = null;
        if (selector.DigestPrefix is { Length: > 0 } digest)
        {
            var matches = candidates
                .Where(candidate => candidate.Anchor.Fingerprint.StartsWith(digest, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                return Failure(MemberTargetDiagnosticKind.DigestNotFound,
                    $"Error: No overload of {selector.Name} matches digest '{digest}'. Use one of the stable {selector.Name}~<digest> selectors.",
                    candidates);
            }

            if (matches.Count > 1)
            {
                return Failure(MemberTargetDiagnosticKind.DigestAmbiguous,
                    $"Error: Digest '{digest}' is ambiguous. Use a longer digest prefix:",
                    matches);
            }

            selected = matches[0];
        }
        else if (selector.OverloadIndex is { } index)
        {
            // A property or event exposes no overloads of its own, but its accessors are
            // addressable through the overload-index path (issue #3265): Name:1 = the
            // getter/adder, Name:2 = the setter/remover. This applies only when the name
            // resolves to a single property/event; overloaded indexers keep normal overload
            // selection and default to the getter.
            var accessorCount = candidates.Count == 1 ? AccessorCount(candidates[0].Member) : 0;
            var maxIndex = Math.Max(candidates.Count, accessorCount);
            if (index < 1 || index > maxIndex)
            {
                return Failure(MemberTargetDiagnosticKind.OverloadOutOfRange,
                    $"Error: {selector.Name}:{index} is out of range. Use {selector.Name}:1 through {selector.Name}:{maxIndex}.",
                    candidates);
            }

            if (accessorCount > 0)
            {
                selected = candidates[0];
                accessorOrdinal = index;
            }
            else
            {
                selected = candidates[index - 1];
            }
        }
        else
        {
            if (candidates.Count > 1)
            {
                return Failure(MemberTargetDiagnosticKind.AmbiguousMember,
                    $"Error: Member selector '{selector.RequestedText}' is ambiguous. Use one of the stable {selector.Name}~<digest> selectors, or {selector.Name}:1 through {selector.Name}:{candidates.Count}.",
                    candidates);
            }

            selected = candidates[0];
        }

        var anchor = selected.Anchor;
        var member = selected.Member;
        // A property/event addresses its body through an accessor ordinal (1 = getter/adder,
        // 2 = setter/remover), defaulting to the getter/adder; a method uses its own overload
        // position. This ordinal flows to the body sections as the selected overload index.
        var bodyOverloadIndex = AccessorCount(member) > 0
            ? accessorOrdinal ?? 1
            : selected.DeclaringOverloadIndex;
        var body = CreateBodyTarget(type, member, anchor, bodyOverloadIndex);
        var handle = ApiMemberIdentity.CreateHandle(type, member);
        return new MemberTargetResolution(
            new ResolvedMemberTarget(
                type,
                handle,
                anchor,
                selector.RequestedText,
                selector.NormalizedSelector,
                selected.SelectorIndex,
                selected.DeclaringOverloadIndex,
                selector.OverloadIndex,
                selector.DigestPrefix,
                selector.GenericArity,
                ToTargetKind(member.Kind),
                body),
            null,
            candidates);

        MemberTargetResolution Failure(
            MemberTargetDiagnosticKind kind,
            string message,
            IReadOnlyList<MemberTargetCandidate> diagnosticCandidates)
            => new(null, new MemberTargetDiagnostic(kind, message, diagnosticCandidates), candidates);
    }

    static BodyTarget? CreateBodyTarget(
        ApiType apiType,
        ApiMember member,
        MemberAnchor anchor,
        int declaringOverloadIndex)
    {
        var apiTypeName = MetadataTypeNameFormatter.FormatFullName(apiType);
        var declaringType = string.IsNullOrWhiteSpace(member.DeclaringType)
            ? apiTypeName
            : member.DeclaringType!;

        if (declaringType == apiTypeName
            && member.MetadataToken is null
            && member.GetterToken is null
            && member.SetterToken is null
            && member.AdderToken is null
            && member.RemoverToken is null
            && member.DeclaringOverloadIndex is null)
        {
            return null;
        }

        return new BodyTarget(
            declaringType,
            anchor.CanonicalSignature,
            member.MetadataToken ?? SelectAccessorToken(member, declaringOverloadIndex),
            member.DeclaringOverloadIndex ?? declaringOverloadIndex);
    }

    /// <summary>
    /// The MethodDef token of the accessor addressed by the selected ordinal (1 =
    /// getter/adder, 2 = setter/remover), counting only accessors that exist so a
    /// write-only property's sole accessor is ordinal 1. Returns <see langword="null"/>
    /// for a member with no accessor tokens. Mirrors the accessor ordering the body
    /// sections synthesize (issue #3265), so <see cref="BodyTarget.MetadataToken"/> names
    /// the same accessor the body render selects by ordinal.
    /// </summary>
    static int? SelectAccessorToken(ApiMember member, int? ordinal)
    {
        int?[] tokens = member.Kind switch
        {
            "property" => [member.GetterToken, member.SetterToken],
            "event" => [member.AdderToken, member.RemoverToken],
            _ => []
        };
        var present = tokens.Where(token => token is not null).ToArray();
        if (present.Length == 0)
            return null;
        var index = (ordinal ?? 1) - 1;
        return index >= 0 && index < present.Length ? present[index] : present[0];
    }

    /// <summary>
    /// The number of body-backed accessors a member exposes: get/set for a property or
    /// indexer, add/remove for an event. Zero for methods, fields, and any member with no
    /// accessor tokens. Used to address accessors through the overload-index path (#3265).
    /// </summary>
    static int AccessorCount(ApiMember member)
        => member.Kind switch
        {
            "property" => (member.GetterToken is null ? 0 : 1) + (member.SetterToken is null ? 0 : 1),
            "event" => (member.AdderToken is null ? 0 : 1) + (member.RemoverToken is null ? 0 : 1),
            _ => 0
        };

    static MemberTargetKind ToTargetKind(string kind)
        => kind switch
        {
            "constructor" => MemberTargetKind.Constructor,
            "finalizer" => MemberTargetKind.Finalizer,
            "field" => MemberTargetKind.Field,
            "property" => MemberTargetKind.Property,
            "method" => MemberTargetKind.Method,
            "operator" => MemberTargetKind.Operator,
            "explicit-interface-implementation" => MemberTargetKind.ExplicitInterfaceImplementation,
            "extension-method" => MemberTargetKind.ExtensionMethod,
            "event" => MemberTargetKind.Event,
            _ => MemberTargetKind.Unknown
        };
}
