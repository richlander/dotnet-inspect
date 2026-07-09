using System.Collections.Immutable;

using ILInspector.Evidence;

namespace ILInspector.Metadata;

/// <summary>
/// Adapts an API surface onto the domain-free evidence substrate. The rung-0 occurrence unit is a
/// member declaration, matched as an unordered identity set by the canonical metadata member
/// signature; matched members are then checked for API facets outside that identity.
/// </summary>
public static class MetadataEvidence
{
    /// <summary>The evidence descriptor for a single API member occurrence.</summary>
    public static readonly EvidenceDescriptor MemberDescriptor = new("api.member", "API member");

    static readonly EvidenceMatchOptions MemberSetOptions = new(EvidenceStreamKind.IdentitySet);
    static readonly ApiDiffOptions DiffOptions = new(ApiDiffScope.All);

    public static MetadataEvidenceResult Compare(
        ApiSurface oldSurface,
        ApiSurface newSurface,
        EvidenceSubject subject,
        int acceptanceThreshold = 100)
    {
        if (oldSurface is null)
            return MetadataEvidenceResult.Failed("old API surface is null");
        if (newSurface is null)
            return MetadataEvidenceResult.Failed("new API surface is null");
        if (subject is null)
            return MetadataEvidenceResult.Failed("evidence subject is null");

        ImmutableArray<EvidenceOccurrence> oldStream;
        ImmutableArray<EvidenceOccurrence> newStream;
        ApiDiff apiDiff;
        EvidenceMatch correspondence;

        try
        {
            oldStream = BuildOccurrences(oldSurface);
            newStream = BuildOccurrences(newSurface);
            apiDiff = ApiDiffAnalyzer.Compare(oldSurface, newSurface, DiffOptions);
            correspondence = EvidenceMatcher.Match(oldStream, newStream, MemberSetOptions);
        }
        catch (ArgumentException ex)
        {
            return MetadataEvidenceResult.Failed(ex.Message);
        }

        var rows = EvidenceFold.ToRows(correspondence, oldStream, newStream, subject, MemberDescriptor, acceptanceThreshold);
        rows = ApplyApiFacetChanges(rows, oldStream, newStream, apiDiff);
        return new MetadataEvidenceResult(rows, correspondence, oldStream, newStream, apiDiff, Failure: null);
    }

    static ImmutableArray<EvidenceOccurrence> BuildOccurrences(ApiSurface surface)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceOccurrence>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (type, member) in EnumerateMembers(surface))
        {
            var handle = ApiMemberIdentity.CreateHandle(type, member);
            var key = handle.CanonicalSignature ?? handle.Identity;
            if (!seen.Add(key))
                continue;

            builder.Add(new EvidenceOccurrence(
                key,
                ScopeKey: MetadataTypeNameFormatter.FormatFullName(type),
                Payload: handle));
        }

        return builder.ToImmutable();
    }

    static IEnumerable<(ApiType Type, ApiMember Member)> EnumerateMembers(ApiSurface surface)
    {
        foreach (var type in surface.Types)
        {
            foreach (var member in type.Members)
            {
                if (!MemberFilters.IsCompilerGenerated(member.Name))
                    yield return (type, member);
            }
        }
    }

    static ImmutableArray<EvidenceRow> ApplyApiFacetChanges(
        ImmutableArray<EvidenceRow> rows,
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream,
        ApiDiff apiDiff)
    {
        var apiChangesByKey = BuildApiChangeMap(apiDiff);
        var builder = ImmutableArray.CreateBuilder<EvidenceRow>(rows.Length);
        foreach (var row in rows)
        {
            if (row.Kind != EvidenceRowKind.Present || row.Anchor.OldIndex < 0 || row.Anchor.NewIndex < 0)
            {
                builder.Add(row);
                continue;
            }

            var oldHandle = (ApiMemberHandle?)oldStream[row.Anchor.OldIndex].Payload;
            var newHandle = (ApiMemberHandle?)newStream[row.Anchor.NewIndex].Payload;
            if (oldHandle is null || newHandle is null)
            {
                builder.Add(row);
                continue;
            }

            var details = new List<string>();
            if (apiChangesByKey.TryGetValue(row.Anchor.IdentityKey, out var apiChanges))
                details.AddRange(apiChanges.Select(FormatApiChange));
            AddFacetDetails(oldHandle.Member, newHandle.Member, details);

            if (details.Count == 0)
            {
                builder.Add(row);
                continue;
            }

            builder.Add(row with
            {
                Kind = EvidenceRowKind.Changed,
                Detail = string.Join("; ", details.Distinct(StringComparer.Ordinal))
            });
        }

        return builder.MoveToImmutable();
    }

    static Dictionary<string, List<ApiChange>> BuildApiChangeMap(ApiDiff apiDiff)
    {
        var changesByKey = new Dictionary<string, List<ApiChange>>(StringComparer.Ordinal);
        foreach (var change in apiDiff.TypeDiffs.SelectMany(typeDiff => typeDiff.Changes))
        {
            if (change.Subject?.Kind != ApiChangeSubjectKind.Member)
                continue;

            var oldKey = change.Subject.OldMember?.CanonicalSignature;
            var newKey = change.Subject.NewMember?.CanonicalSignature;
            if (oldKey is null || newKey is null || oldKey != newKey)
                continue;

            if (!changesByKey.TryGetValue(newKey, out var changes))
            {
                changes = [];
                changesByKey.Add(newKey, changes);
            }

            changes.Add(change);
        }

        return changesByKey;
    }

    static string FormatApiChange(ApiChange change)
    {
        var detail = $"{change.Category}: {change.Kind}";
        if (!string.IsNullOrEmpty(change.OldValue) || !string.IsNullOrEmpty(change.NewValue))
            detail += $" ({FormatValue(change.OldValue)} -> {FormatValue(change.NewValue)})";
        return detail;
    }

    static void AddFacetDetails(ApiMember oldMember, ApiMember newMember, List<string> details)
    {
        AddFacetDetail(details, "signature", oldMember.Signature, newMember.Signature);
        AddFacetDetail(details, "return type", oldMember.ReturnType, newMember.ReturnType);
        AddFacetDetail(details, "accessibility", NormalizeAccessibility(oldMember.Accessibility), NormalizeAccessibility(newMember.Accessibility));
        AddFacetDetail(details, "static", oldMember.IsStatic ? "static" : "instance", newMember.IsStatic ? "static" : "instance");
        AddFacetDetail(details, "virtual", oldMember.IsVirtual, newMember.IsVirtual);
        AddFacetDetail(details, "abstract", oldMember.IsAbstract, newMember.IsAbstract);
        AddFacetDetail(details, "override", oldMember.IsOverride, newMember.IsOverride);
        AddFacetDetail(details, "sealed", oldMember.IsSealed, newMember.IsSealed);
        AddFacetDetail(details, "readonly", oldMember.IsReadOnly, newMember.IsReadOnly);
        AddFacetDetail(details, "const", oldMember.IsConst, newMember.IsConst);
        AddFacetDetail(details, "unsafe", oldMember.IsUnsafe, newMember.IsUnsafe);
        AddFacetDetail(details, "extension", oldMember.IsExtension, newMember.IsExtension);
        AddFacetDetail(details, "extended type", oldMember.ExtendedType, newMember.ExtendedType);
        AddFacetDetail(details, "obsolete", oldMember.IsObsolete, newMember.IsObsolete);
        AddFacetDetail(details, "obsolete message", oldMember.ObsoleteMessage, newMember.ObsoleteMessage);
    }

    static void AddFacetDetail(List<string> details, string name, bool oldValue, bool newValue)
    {
        if (oldValue != newValue)
            details.Add($"{name}: {oldValue.ToString().ToLowerInvariant()} -> {newValue.ToString().ToLowerInvariant()}");
    }

    static void AddFacetDetail(List<string> details, string name, string? oldValue, string? newValue)
    {
        if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            details.Add($"{name}: {FormatValue(oldValue)} -> {FormatValue(newValue)}");
    }

    static string NormalizeAccessibility(string? accessibility)
        => string.IsNullOrWhiteSpace(accessibility) ? "public" : accessibility!;

    static string FormatValue(string? value)
        => string.IsNullOrEmpty(value) ? "none" : value;
}

/// <summary>The outcome of a <see cref="MetadataEvidence.Compare"/> call.</summary>
public sealed record MetadataEvidenceResult(
    ImmutableArray<EvidenceRow> Rows,
    EvidenceMatch Match,
    ImmutableArray<EvidenceOccurrence> OldOccurrences,
    ImmutableArray<EvidenceOccurrence> NewOccurrences,
    ApiDiff ApiDiff,
    string? Failure)
{
    /// <summary>True when the member set is exact under the fidelity fold.</summary>
    public bool IsExact => Failure is null && EvidenceEquivalence.Exact.IsEquivalent(Rows);

    public static MetadataEvidenceResult Failed(string failure)
        => new([], new EvidenceMatch([], []), [], [], new ApiDiff(), failure);
}
