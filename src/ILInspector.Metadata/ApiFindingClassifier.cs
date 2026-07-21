using ILInspector.Findings;

namespace ILInspector.Metadata;

/// <summary>
/// Produces an <see cref="ApiDiff"/> from the Finding lane's own type and member
/// correspondence (<see cref="FindingComparison{T}"/> over <see cref="ApiTypeHandle"/> and
/// <see cref="ApiMemberHandle"/>) instead of <see cref="ApiDiffAnalyzer"/>'s independent
/// type-name/member-signature walk. Type-level classification reuses
/// <see cref="ApiDiffAnalyzer.CompareTypeFacetsOnly"/> unchanged (type identity is the same
/// full-name key in both lanes). Member-level classification is intentionally sourced from
/// the Finding lane's richer identity/soft-key matching rather than
/// <see cref="ApiDiffAnalyzer"/>'s bespoke signature-then-(Name,Kind) matcher -- see
/// <see cref="ApiDiffNormalization"/> for the explicit, opt-in policy this introduces for
/// fuzzy-matched (soft-key) member pairs.
/// </summary>
public static class ApiFindingClassifier
{
    public static ApiDiff Classify(
        FindingComparison<ApiTypeHandle> types,
        FindingComparison<ApiMemberHandle> members,
        ApiSurface oldSurface,
        ApiSurface newSurface,
        ApiDiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(oldSurface);
        ArgumentNullException.ThrowIfNull(newSurface);
        ArgumentNullException.ThrowIfNull(options);

        var inspectionFailures = BuildInspectionFailures(oldSurface, newSurface);

        // Matching never ran on at least one side; there is nothing to classify beyond the
        // structured inspection failures themselves (mirrors the legacy analyzer's own
        // identity-incomplete skip, but as a full bail-out rather than a per-type skip since
        // the Finding lane doesn't expose a partial-comparison shape).
        if (types.Value is not FindingComparison<ApiTypeHandle>.Complete typesComplete
            || members.Value is not FindingComparison<ApiMemberHandle>.Complete membersComplete)
        {
            return new ApiDiff { InspectionFailures = inspectionFailures };
        }

        var changesByType = new Dictionary<string, List<ApiChange>>(StringComparer.Ordinal);
        var wholeTypeTransition = BuildWholeTypeTransitionSets(typesComplete);
        bool oldIdentityIncomplete = oldSurface.InspectionFailures.Count > 0;
        bool newIdentityIncomplete = newSurface.InspectionFailures.Count > 0;

        ClassifyTypes(typesComplete, options, oldIdentityIncomplete, newIdentityIncomplete, changesByType);
        ClassifyMembers(membersComplete, options, wholeTypeTransition, changesByType);

        List<TypeDiff> typeDiffs = [];
        int totalBreaking = 0;
        int totalAdditive = 0;
        int totalPotentiallyBreaking = 0;

        foreach (var typeName in changesByType.Keys.Order(StringComparer.Ordinal))
        {
            var changes = changesByType[typeName];
            if (changes.Count == 0)
                continue;

            var diff = new TypeDiff(typeName, changes);
            typeDiffs.Add(diff);
            totalBreaking += diff.BreakingCount;
            totalAdditive += diff.AdditiveCount;
            totalPotentiallyBreaking += diff.PotentiallyBreakingCount;
        }

        return new ApiDiff
        {
            TypeDiffs = typeDiffs,
            InspectionFailures = inspectionFailures,
            TotalBreaking = totalBreaking,
            TotalAdditive = totalAdditive,
            TotalPotentiallyBreaking = totalPotentiallyBreaking,
        };
    }

    static void ClassifyTypes(
        FindingComparison<ApiTypeHandle>.Complete types,
        ApiDiffOptions options,
        bool oldIdentityIncomplete,
        bool newIdentityIncomplete,
        Dictionary<string, List<ApiChange>> changesByType)
    {
        foreach (var pair in types.Pairs)
        {
            switch ((object)pair.Value)
            {
                case PairFinding<ApiTypeHandle>.Added added:
                    // Mirrors the legacy analyzer's own oldIdentityIncomplete skip: if the old
                    // surface failed to resolve identity for some type(s), a type that's
                    // "only in new" may really be a type whose old-side identity just failed
                    // to decode, not a genuine addition.
                    if (ApiDiffAnalyzer.IncludesSignature(options) && !oldIdentityIncomplete)
                    {
                        Bucket(changesByType, added.New.Payload.TypeFullName).Add(new ApiChange(
                            ChangeKind.TypeAdded,
                            ChangeClassification.Additive,
                            $"Type '{added.New.Payload.TypeFullName}' was added",
                            Subject: ApiChangeSubject.Type(null, added.New.Payload.Type)));
                    }
                    break;

                case PairFinding<ApiTypeHandle>.Removed removed:
                    // Mirrors the legacy analyzer's own newIdentityIncomplete skip (see
                    // ApiSurfaceRelationshipFailureTests.ApiDiff_IncompleteNewIdentityDoesNotClaimOldTypeWasRemoved):
                    // a type that's "only in old" may really be a type whose new-side identity
                    // just failed to decode (e.g. a metadata cycle), not a genuine removal.
                    if (ApiDiffAnalyzer.IncludesSignature(options) && !newIdentityIncomplete)
                    {
                        Bucket(changesByType, removed.Old.Payload.TypeFullName).Add(new ApiChange(
                            ChangeKind.TypeRemoved,
                            ChangeClassification.Breaking,
                            $"Type '{removed.Old.Payload.TypeFullName}' was removed",
                            Subject: ApiChangeSubject.Type(removed.Old.Payload.Type, null)));
                    }
                    break;

                case PairFinding<ApiTypeHandle>.Present present:
                    Bucket(changesByType, present.New.Payload.TypeFullName).AddRange(
                        ApiDiffAnalyzer.CompareTypeFacetsOnly(present.Old.Payload.Type, present.New.Payload.Type, options));
                    break;

                case PairFinding<ApiTypeHandle>.Changed changed:
                    Bucket(changesByType, changed.New.Payload.TypeFullName).AddRange(
                        ApiDiffAnalyzer.CompareTypeFacetsOnly(changed.Old.Payload.Type, changed.New.Payload.Type, options));
                    break;
            }
        }
    }

    /// <summary>
    /// Type full names present only on one side (a whole-type addition/removal). A member
    /// belonging to one of these types is already covered by the type-level change and must
    /// not also be reported as its own member-level addition/removal -- mirroring the legacy
    /// analyzer, whose per-type walk never descends into <c>CompareMembers</c> for a type
    /// that was itself added or removed.
    /// </summary>
    readonly record struct WholeTypeTransitionSets(HashSet<string> Added, HashSet<string> Removed);

    static WholeTypeTransitionSets BuildWholeTypeTransitionSets(FindingComparison<ApiTypeHandle>.Complete types)
    {
        var added = new HashSet<string>(StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in types.Pairs)
        {
            switch ((object)pair.Value)
            {
                case PairFinding<ApiTypeHandle>.Added a:
                    added.Add(a.New.Payload.TypeFullName);
                    break;
                case PairFinding<ApiTypeHandle>.Removed r:
                    removed.Add(r.Old.Payload.TypeFullName);
                    break;
            }
        }
        return new WholeTypeTransitionSets(added, removed);
    }

    static void ClassifyMembers(
        FindingComparison<ApiMemberHandle>.Complete members,
        ApiDiffOptions options,
        WholeTypeTransitionSets wholeTypeTransition,
        Dictionary<string, List<ApiChange>> changesByType)
    {
        bool normalizeProjection = options.Normalization.HasFlag(ApiDiffNormalization.NormalizeMemberProjection);

        // A member producer can emit more than one identity atom for the same physical old
        // (or new) member to enable soft-key projection matching (e.g. an extension method
        // gets both its own hard "extension:" selector atom -- unmatched, becoming Removed --
        // and a receiver-projected atom that fuzzy-matches a real instance method, becoming
        // Changed). Both atoms share the same content CanonicalSignature. Without
        // deduplicating by canonical signature, the same physical member would be reported
        // twice: once via the Removed/Added atom and again via the fuzzy Changed pair.
        //
        // Deliberately keyed on the full CanonicalSignature string rather than Fingerprint:
        // Fingerprint is a 10-hex-char (40-bit) truncated SHA256 digest, so at real-corpus
        // member counts an unrelated member's fingerprint can collide with a fuzzy-matched
        // member's fingerprint (birthday bound ~2^20 members for a 50% collision chance) and
        // silently suppress that unrelated member's genuine Added/Removed change. The full
        // signature string carries no such collision risk and is already available on the
        // payload at zero extra cost.
        var oldSignaturesInFuzzyMatch = new HashSet<string>(StringComparer.Ordinal);
        var newSignaturesInFuzzyMatch = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in members.Pairs)
        {
            if (pair.Value is IMatchedPairFinding { Match: not null } && pair is PairFinding<ApiMemberHandle>.Changed fuzzy)
            {
                if (fuzzy.Old.Payload.CanonicalSignature is { } oldSignature)
                    oldSignaturesInFuzzyMatch.Add(oldSignature);
                if (fuzzy.New.Payload.CanonicalSignature is { } newSignature)
                    newSignaturesInFuzzyMatch.Add(newSignature);
            }
        }

        foreach (var pair in members.Pairs)
        {
            switch ((object)pair.Value)
            {
                case PairFinding<ApiMemberHandle>.Added added:
                    if (ApiDiffAnalyzer.IncludesSignature(options)
                        && !IsCompilerGeneratedMember(added.New.Payload)
                        && !wholeTypeTransition.Added.Contains(added.New.Payload.TypeFullName)
                        && !(added.New.Payload.CanonicalSignature is { } addedSignature && newSignaturesInFuzzyMatch.Contains(addedSignature)))
                    {
                        Bucket(changesByType, added.New.Payload.TypeFullName).Add(new ApiChange(
                            ChangeKind.MemberAdded,
                            MemberAddedClassification(added.New.Payload),
                            $"Member '{added.New.Payload.MemberName}' was added",
                            Subject: ApiChangeSubject.Member(null, null, added.New.Payload.Type, added.New.Payload.Member)));
                    }
                    break;

                case PairFinding<ApiMemberHandle>.Removed removed:
                    if (ApiDiffAnalyzer.IncludesSignature(options)
                        && !IsCompilerGeneratedMember(removed.Old.Payload)
                        && !wholeTypeTransition.Removed.Contains(removed.Old.Payload.TypeFullName)
                        && !(removed.Old.Payload.CanonicalSignature is { } removedSignature && oldSignaturesInFuzzyMatch.Contains(removedSignature)))
                    {
                        Bucket(changesByType, removed.Old.Payload.TypeFullName).Add(new ApiChange(
                            ChangeKind.MemberRemoved,
                            ChangeClassification.Breaking,
                            $"Member '{removed.Old.Payload.MemberName}' was removed",
                            Subject: ApiChangeSubject.Member(removed.Old.Payload.Type, removed.Old.Payload.Member, null, null)));
                    }
                    break;

                case PairFinding<ApiMemberHandle>.Present present:
                    if (!IsCompilerGeneratedMember(present.Old.Payload))
                        ClassifyMatchedMember(present.Old.Payload, present.New.Payload, options, changesByType);
                    break;

                case PairFinding<ApiMemberHandle>.Changed changed:
                    if (IsCompilerGeneratedMember(changed.Old.Payload) || IsCompilerGeneratedMember(changed.New.Payload))
                        break;

                    var match = ((IMatchedPairFinding)pair.Value).Match;
                    if (match is null)
                    {
                        // Matched on the hard (identity-set) tier; the fold-in step
                        // recorded a Detail but classification is unaffected by that.
                        ClassifyMatchedMember(changed.Old.Payload, changed.New.Payload, options, changesByType);
                    }
                    else if (normalizeProjection)
                    {
                        ClassifyNormalizedProjection(changed.Old.Payload, changed.New.Payload, options, changesByType);
                    }
                    else
                    {
                        // Strict default: a fuzzy/soft-key correspondence (e.g. an extension
                        // method's static/instance projection) is not assumed to be the same
                        // member. Report it the way the legacy analyzer's own matcher would --
                        // as an unrelated removal and addition -- unless the caller explicitly
                        // opts into ApiDiffNormalization.NormalizeMemberProjection.
                        if (ApiDiffAnalyzer.IncludesSignature(options))
                        {
                            if (!wholeTypeTransition.Removed.Contains(changed.Old.Payload.TypeFullName))
                            {
                                Bucket(changesByType, changed.Old.Payload.TypeFullName).Add(new ApiChange(
                                    ChangeKind.MemberRemoved,
                                    ChangeClassification.Breaking,
                                    $"Member '{changed.Old.Payload.MemberName}' was removed",
                                    Subject: ApiChangeSubject.Member(changed.Old.Payload.Type, changed.Old.Payload.Member, null, null)));
                            }
                            if (!wholeTypeTransition.Added.Contains(changed.New.Payload.TypeFullName))
                            {
                                Bucket(changesByType, changed.New.Payload.TypeFullName).Add(new ApiChange(
                                    ChangeKind.MemberAdded,
                                    MemberAddedClassification(changed.New.Payload),
                                    $"Member '{changed.New.Payload.MemberName}' was added",
                                    Subject: ApiChangeSubject.Member(null, null, changed.New.Payload.Type, changed.New.Payload.Member)));
                            }
                        }
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Legacy <c>ApiDiffAnalyzer.CompareMembers</c> drops compiler-generated members (backing
    /// fields, enum <c>value__</c> slots, closure/state-machine artifacts -- see
    /// <see cref="MemberFilters.IsCompilerGenerated"/>) via its own <c>FilterMembers</c> before
    /// matching, since they are not part of the authored API surface. The Finding lane's member
    /// producer deliberately does not apply this heuristic itself (it is a raw, unfiltered
    /// census -- see <c>MetadataFindingsTests.ApiSurfaceMembersAreNotFilteredByNameHeuristics</c>),
    /// so classification is where this policy must be applied instead.
    /// </summary>
    static bool IsCompilerGeneratedMember(ApiMemberHandle handle)
        => MemberFilters.IsCompilerGenerated(handle.MemberName);

    /// <summary>
    /// Adding a member to an interface is potentially breaking (existing implementers must
    /// add it too); adding a member to a class/struct/enum is purely additive. Mirrors the
    /// legacy analyzer's own <c>newType.Kind == "interface"</c> check in <c>CompareMembers</c>.
    /// </summary>
    static ChangeClassification MemberAddedClassification(ApiMemberHandle newHandle)
        => newHandle.Type.Kind == "interface"
            ? ChangeClassification.PotentiallyBreaking
            : ChangeClassification.Additive;

    static void ClassifyMatchedMember(
        ApiMemberHandle oldHandle,
        ApiMemberHandle newHandle,
        ApiDiffOptions options,
        Dictionary<string, List<ApiChange>> changesByType)
    {
        var changes = Bucket(changesByType, newHandle.TypeFullName);
        if (ApiDiffAnalyzer.IncludesSignature(options))
        {
            // Identity/fingerprint matching is keyed on CanonicalSignature, which omits the
            // return type (see MemberAnchor.ComputeFingerprint). Two hard-matched members can
            // therefore still differ in raw Signature text (return type changed, e.g.
            // `void Changed()` -> `int Changed()`) -- a real signature change the identity key
            // itself can't see. Mirrors the legacy analyzer's own (Name, Kind)-fallback
            // MemberSignatureChanged report for this case.
            AddSignatureChangeIfDifferent(oldHandle, newHandle, changes);
            ApiDiffAnalyzer.CompareMemberModifiers(oldHandle.Type, newHandle.Type, oldHandle.Member, newHandle.Member, changes);
        }
        if (ApiDiffAnalyzer.IncludesAttributes(options))
        {
            ApiDiffAnalyzer.CompareAttributes(
                oldHandle.Member.Attributes,
                newHandle.Member.Attributes,
                changes,
                ChangeKind.MemberAttributeAdded,
                ChangeKind.MemberAttributeRemoved,
                $"Member '{oldHandle.MemberName}'",
                ApiChangeSubject.Member(oldHandle.Type, oldHandle.Member, newHandle.Type, newHandle.Member));
        }
    }

    /// <summary>
    /// <see cref="ApiDiffNormalization.NormalizeMemberProjection"/>: a fuzzy-matched member
    /// pair is the same conceptual member observed under two projections. Runs the same
    /// facet-level checks as a hard match, plus an explicit signature-changed fact when the
    /// two projections' raw signatures differ (the only way a fuzzy match can carry a real
    /// signature delta, since it wasn't discovered by an exact signature/identity key).
    /// </summary>
    static void ClassifyNormalizedProjection(
        ApiMemberHandle oldHandle,
        ApiMemberHandle newHandle,
        ApiDiffOptions options,
        Dictionary<string, List<ApiChange>> changesByType)
    {
        var changes = Bucket(changesByType, newHandle.TypeFullName);
        if (ApiDiffAnalyzer.IncludesSignature(options))
        {
            AddSignatureChangeIfDifferent(oldHandle, newHandle, changes);
            ApiDiffAnalyzer.CompareMemberModifiers(oldHandle.Type, newHandle.Type, oldHandle.Member, newHandle.Member, changes);
        }
        if (ApiDiffAnalyzer.IncludesAttributes(options))
        {
            ApiDiffAnalyzer.CompareAttributes(
                oldHandle.Member.Attributes,
                newHandle.Member.Attributes,
                changes,
                ChangeKind.MemberAttributeAdded,
                ChangeKind.MemberAttributeRemoved,
                $"Member '{oldHandle.MemberName}'",
                ApiChangeSubject.Member(oldHandle.Type, oldHandle.Member, newHandle.Type, newHandle.Member));
        }
    }

    /// <summary>
    /// Reports a <see cref="ChangeKind.MemberSignatureChanged"/> fact for a matched member pair
    /// (hard-identity or normalized-projection) whose raw signature text differs, matching the
    /// legacy analyzer's classification for its own (Name, Kind)-fallback pairing.
    /// </summary>
    static void AddSignatureChangeIfDifferent(ApiMemberHandle oldHandle, ApiMemberHandle newHandle, List<ApiChange> changes)
    {
        if (string.Equals(oldHandle.Member.Signature, newHandle.Member.Signature, StringComparison.Ordinal))
            return;

        changes.Add(new ApiChange(
            ChangeKind.MemberSignatureChanged,
            ChangeClassification.Breaking,
            $"Member '{oldHandle.MemberName}' signature changed from '{oldHandle.Member.Signature}' to '{newHandle.Member.Signature}'",
            oldHandle.Member.Signature,
            newHandle.Member.Signature,
            Subject: ApiChangeSubject.Member(oldHandle.Type, oldHandle.Member, newHandle.Type, newHandle.Member)));
    }

    static List<ApiChange> Bucket(Dictionary<string, List<ApiChange>> changesByType, string typeFullName)
    {
        if (!changesByType.TryGetValue(typeFullName, out var changes))
        {
            changes = [];
            changesByType[typeFullName] = changes;
        }
        return changes;
    }

    static List<ApiDiffInspectionFailure> BuildInspectionFailures(ApiSurface oldSurface, ApiSurface newSurface)
        =>
        [
            .. oldSurface.InspectionFailures.Select(failure =>
                new ApiDiffInspectionFailure(
                    "old",
                    failure.Operation,
                    failure.SubjectToken,
                    failure.Mechanism,
                    failure.Kind,
                    failure.Detail)),
            .. newSurface.InspectionFailures.Select(failure =>
                new ApiDiffInspectionFailure(
                    "new",
                    failure.Operation,
                    failure.SubjectToken,
                    failure.Mechanism,
                    failure.Kind,
                    failure.Detail)),
        ];
}
