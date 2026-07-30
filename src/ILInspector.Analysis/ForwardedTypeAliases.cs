using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis;

/// <summary>
/// The assembly spellings that denote the same type as a target's declaring type once type
/// forwarding is followed.
///
/// <para><b>Why this is not part of <see cref="TypeRef"/> identity.</b> A <c>TypeRef</c> records
/// what a call site <em>says</em>: the assembly the compiler bound against, which is routinely a
/// facade. <see cref="TypeRef.Equals"/> canonicalizes a fixed alias set of core-library facade
/// names, and that covers only the corelib case — measured against the shared framework, the alias
/// set covers 4 forwarder pairs while 294 pairs (nearly six thousand forwarded types) fall outside
/// it, including chains such as <c>System.Xml</c> to <c>System.Xml.ReaderWriter</c> to
/// <c>System.Private.Xml</c>. Widening <c>TypeRef</c> identity instead would make a type's identity
/// depend on which assemblies happened to be readable, and would change displayed assembly names.
/// Correspondence is a separate concern from identity, so it is computed here and consulted
/// explicitly (#3419).</para>
///
/// <para><b>Evidence-based, not name-based.</b> An alias is recorded only when an assembly in the
/// scope actually carries an <c>ExportedType</c> forwarder for the target type that reaches the
/// target's defining assembly. A facade outside the scope is never guessed at; it simply yields no
/// alias, which is the same answer callers get today.</para>
///
/// <para><b>One object, two consumers.</b> <see cref="MemberPattern.MatchesCrossAssembly"/> and
/// <see cref="CallerScopeTypeFilter"/> must stay exactly as permissive as each other, or the
/// prefilter rules out assemblies the matcher would have matched. They consult this same instance
/// rather than each deriving the answer, so they cannot drift.</para>
/// </summary>
public sealed class ForwardedTypeAliases
{
    // Matches TypeForwardResolver.DefaultMaxHops: the same bound on the same kind of chain.
    const int MaxHops = 8;

    // An evidence token is 8 bytes, so a one-byte value cannot collide with a real one. This marks
    // a spelling two different assemblies claim: it can never be verified against either.
    static readonly byte[] AmbiguousSpelling = [0];

    /// <summary>
    /// The identity of the assembly that supplied a spelling's forwarder evidence, in the parts a
    /// reference can be checked against.
    ///
    /// <para><b>Version is compared by roll-forward direction, not by equality.</b> A reference's
    /// version is not the definition's — binding rolls forward, and reference assemblies routinely
    /// record <c>0.0.0.0</c> — so equality is the wrong test. Measured over the shared framework,
    /// ASP.NET Core and this repository's own build output (2,267 assemblies, 16,294 references
    /// resolving to a definition on disk): 713 references named a version <em>below</em> the
    /// definition, and <b>0</b> named one above. Requiring equality would have declined all 713,
    /// including <c>mscorlib</c> — the canonical forwarding facade — whose reference to
    /// <c>System.Private.CoreLib</c> reads <c>0.0.0.0</c> against a definition of <c>9.0.0.0</c>.
    /// Requiring <c>reference &lt;= evidence</c> declines none of them, and is what the loader
    /// itself permits: a reference can bind to a same-or-newer definition, never to an older one.
    /// </para>
    ///
    /// <para>Ignoring version entirely was the previous rule, and it fabricated: a v1 facade
    /// forwarding <c>Widget</c> vouched for a caller built against an unrelated v2 assembly of the
    /// same name that defined <c>Widget</c> itself, reporting a call to the v2 type as a call to
    /// the target (executed in review of <c>b18e5009</c>). Version is the only discriminator left
    /// when both assemblies are unsigned, which is the common shape outside the framework.</para>
    ///
    /// <para>Culture is compared for equality: it disagreed <b>0</b> times in the same corpus, and
    /// a culture-specific assembly is a different assembly (found in review of
    /// <c>372be6d1</c>).</para>
    ///
    /// <para><paramref name="Version"/> is the highest version observed for the spelling and
    /// <paramref name="ObservedVersions"/> is every version observed for it. Signed evidence uses
    /// the highest — roll-forward admits everything at or below it — while unsigned evidence needs
    /// the whole set, because it matches exactly. Collapsing unsigned evidence to the highest
    /// dropped genuine callers: with unsigned v1 and v2 files both forwarding the type, a caller
    /// referencing v1 exactly was refused, so <em>adding</em> valid v2 evidence removed a real
    /// caller (executed in review of <c>a749cd4d</c>).</para>
    /// </summary>
    readonly record struct EvidenceIdentity(
        byte[] Token,
        string Culture,
        Version Version,
        ImmutableArray<Version> ObservedVersions)
    {
        internal EvidenceIdentity(byte[] token, string culture, Version version)
            : this(token, culture, version, [version])
        {
        }

        /// <summary>This identity widened to also account for <paramref name="version"/>.</summary>
        internal EvidenceIdentity Observing(Version version)
            => ObservedVersions.Contains(version)
                ? this with { Version = version > Version ? version : Version }
                : this with
                {
                    Version = version > Version ? version : Version,
                    ObservedVersions = ObservedVersions.Add(version),
                };
    }

    /// <summary>
    /// One assembly's forwarder for the target type: who declared it, where it points, that
    /// assembly's own identity, and the reference row naming the assembly it points at.
    /// </summary>
    readonly record struct ForwarderEdge(
        string Assembly,
        string Target,
        EvidenceIdentity Identity,
        AssemblyReferenceSpelling TargetReference);

    readonly HashSet<string> _aliases;
    readonly HashSet<string> _rawSpellings;
    readonly Dictionary<string, EvidenceIdentity> _spellingTokens;
    readonly Dictionary<string, string> _canonicalByRaw;

    // Raw spellings this image named but could not verify. Withdrawing them from _rawSpellings is
    // not enough: the matcher compares canonicalized names, so a withdrawn corelib facade spelling
    // is readmitted by any verified sibling in the same bucket. Kept so DenotesSameType can refuse
    // the spelling the TypeRef actually went through.
    readonly HashSet<string> _withdrawnSpellings;

    ForwardedTypeAliases(
        HashSet<string> aliases,
        HashSet<string> rawSpellings,
        Dictionary<string, EvidenceIdentity> spellingTokens,
        Dictionary<string, string> canonicalByRaw,
        HashSet<string> withdrawnSpellings)
    {
        _aliases = aliases;
        _rawSpellings = rawSpellings;
        _spellingTokens = spellingTokens;
        _canonicalByRaw = canonicalByRaw;
        _withdrawnSpellings = withdrawnSpellings;
    }

    /// <summary>No aliases: every comparison falls back to plain identity.</summary>
    public static ForwardedTypeAliases None { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, EvidenceIdentity>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public bool IsEmpty => _aliases.Count == 0;

    /// <summary>
    /// Whether an assembly spells its name as one of the facades that forward the type.
    ///
    /// <para>This is a membership test rather than an exposed collection on purpose. Handing out
    /// the backing sets let a consumer downcast <c>IReadOnlyCollection&lt;string&gt;</c> to the
    /// <see cref="HashSet{T}"/> behind it and mutate alias state — including the shared
    /// <see cref="None"/> singleton, which is process-wide — and a mutation also defeats the
    /// reference-identity memoization in <c>MethodBodyInspectionSession.ApplicableAliases</c>,
    /// which is sound only because instances never change. Both were demonstrated in review of
    /// <c>372be6d1</c>; the properties had no product consumers, so they are gone rather than
    /// hardened.</para>
    ///
    /// <para>Raw spellings are the facade names exactly as their assemblies spell them, which is
    /// not a convenience: the canonical alias set collapses the five core-library facade spellings
    /// onto one name, and <em>every</em> managed assembly references a core-library facade, so an
    /// assembly-level filter keyed on the canonical name would select the entire scope and undo the
    /// prefiltering it is part of. Only assemblies that actually carry the forwarder appear
    /// here.</para>
    /// </summary>
    public bool IncludesRawSpelling(string assembly) => _rawSpellings.Contains(assembly);

    /// <summary>
    /// The aliases that are actually applicable to one caller image: those whose facade the image
    /// references under a strong-name identity matching the assembly that supplied the forwarder
    /// evidence. Returns <see cref="None"/> when the image verifies none of them.
    ///
    /// <para><b>Why per image, and why here.</b> An alias is a bare assembly <em>name</em>, because
    /// that is all a <see cref="TypeRef"/> records. Two different assemblies can share a simple name
    /// and differ in strong-name identity, so a forwarder read from one of them must not be applied
    /// to a caller that bound against the other: that reports a call to an unrelated type as a call
    /// to the target — a fabricated caller, which is worse than a missing one.</para>
    ///
    /// <para>Whether an alias applies is therefore a property of the <em>caller image</em>, not of
    /// the call site, and the image's <c>AssemblyRef</c> table is the only place the identity still
    /// exists. Restricting once per image is what lets the prefilter and the matcher enforce the
    /// same rule from the same primitive: an earlier revision checked identity inside
    /// <see cref="CallerScopeTypeFilter"/> alone, which left every path that reaches the matcher
    /// without passing the prefilter — a reused shared scope, most of all — able to fabricate a
    /// caller (found in review of #3419).</para>
    ///
    /// <para><b>An alias fires only when identity is verified.</b> That is the opposite of the rule
    /// for ordinary identity matching, and deliberately so. Aliasing is additive: declining to
    /// apply one restores the behavior callers had before #3419, which is a display gap. Applying
    /// one wrongly invents an edge. So unknown, unverifiable, or ambiguous identity declines.</para>
    ///
    /// <para><b>Every reference to a spelling must verify, not merely one.</b> A
    /// <see cref="TypeRef"/> records only a name, so when an image holds two <c>AssemblyRef</c> rows
    /// that spell one name under different keys, nothing downstream can tell which row a given row
    /// resolved through. Admitting the spelling because <em>some</em> row verified would let a
    /// genuine reference vouch for a call made through the impostor beside it (found in review of
    /// <c>7181e795</c>). One row that disagrees, or that cannot be checked at all, therefore
    /// withdraws the spelling for the whole image.</para>
    ///
    /// <para><b>Withdrawing a spelling and withdrawing its canonical bucket are different acts.</b>
    /// The first costs only that spelling; the second lands on every spelling that canonicalizes
    /// with it, including verified ones. So a row that merely cannot be checked — retargetable, or
    /// checked against ambiguous evidence — withdraws its own spelling but not the bucket. Only a
    /// row that is checkable and disagrees does both (found in review of <c>372be6d1</c>, where a
    /// retargetable <c>mscorlib</c> beside a verified corelib facade dropped that facade's genuine
    /// callers).</para>
    /// </summary>
    public ForwardedTypeAliases RestrictedTo(MetadataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (_aliases.Count == 0)
            return this;

        var spellings = ImmutableArray.CreateBuilder<AssemblyReferenceSpelling>();
        foreach (var handle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(handle);
            spellings.Add(new AssemblyReferenceSpelling(
                reader.GetString(reference.Name),
                [.. reader.GetBlobContent(reference.PublicKeyOrToken)],
                reference.Flags,
                reference.Culture.IsNil ? "" : reader.GetString(reference.Culture),
                reference.Version));
        }

        return RestrictedTo(spellings.ToImmutable());
    }

    /// <summary>
    /// <see cref="RestrictedTo(MetadataReader)"/> over an image's already-read <c>AssemblyRef</c>
    /// rows, for callers holding a snapshot rather than an open reader.
    /// </summary>
    public ForwardedTypeAliases RestrictedTo(ImmutableArray<AssemblyReferenceSpelling> references)
    {
        if (_aliases.Count == 0)
            return this;

        var verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contradicted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unusable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references)
        {
            if (!_rawSpellings.Contains(reference.Name))
                continue;

            switch (VerdictFor(reference.Name, reference))
            {
                case ReferenceVerdict.Verified:
                    verified.Add(reference.Name);
                    break;
                case ReferenceVerdict.Contradicted:
                    contradicted.Add(reference.Name);
                    break;
                default:
                    unusable.Add(reference.Name);
                    break;
            }
        }

        // A spelling is admitted only if every row naming it was checkable and agreed. One row that
        // disagreed, or that could not be checked at all, leaves the image unable to say which row
        // a given TypeRef resolved through — and a TypeRef records only the name.
        verified.ExceptWith(contradicted);
        verified.ExceptWith(unusable);

        if (verified.Count == _rawSpellings.Count)
            return this;
        if (verified.Count == 0)
            return None;

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spellingTokens = new Dictionary<string, EvidenceIdentity>(StringComparer.OrdinalIgnoreCase);
        var canonicalByRaw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in verified)
        {
            if (!_canonicalByRaw.TryGetValue(raw, out string? canonical))
                continue;
            aliases.Add(canonical);
            canonicalByRaw[raw] = canonical;
            if (_spellingTokens.TryGetValue(raw, out EvidenceIdentity identity))
                spellingTokens[raw] = identity;
        }

        // A canonical alias a contradicted spelling also maps to cannot be admitted either: the
        // spellings are indistinguishable to the matcher once canonicalized, so keeping it would
        // readmit the row that was just refused. Only contradicted spellings do this — a spelling
        // that merely could not be checked has no business withdrawing a verified sibling's bucket.
        // This covers spellings that supplied forwarder evidence; the wider imprecision of
        // canonicalization itself is #3485.
        foreach (string raw in contradicted)
        {
            if (_canonicalByRaw.TryGetValue(raw, out string? canonical))
                aliases.Remove(canonical);
        }

        // A spelling that failed for either reason is refused by name, so that a TypeRef which went
        // through it is not readmitted by a verified sibling that shares its canonical bucket.
        // Bucket removal above cannot express this: it would take the verified sibling down too,
        // which is exactly the regression the Contradicted/Indeterminate split exists to avoid.
        var withdrawn = new HashSet<string>(contradicted, StringComparer.OrdinalIgnoreCase);
        withdrawn.UnionWith(unusable);

        return aliases.Count == 0
            ? None
            : new ForwardedTypeAliases(aliases, verified, spellingTokens, canonicalByRaw, withdrawn);
    }

    /// <summary>
    /// What one <c>AssemblyRef</c> row says about the spelling it names.
    ///
    /// <para>Refutation is why this is not a <see cref="bool"/>. Before refutation existed, every
    /// non-verifying answer meant one thing — "this row does not vouch for the spelling" — and
    /// collapsing them cost nothing, because the spelling simply stayed out of the verified set.
    /// Refutation gave one of those answers a second and far stronger meaning: it withdraws the
    /// canonical bucket for the whole image, which lands on <em>other</em> spellings. Only a row
    /// that can be checked and disagrees has earned that (found in review of <c>372be6d1</c>).</para>
    /// </summary>
    enum ReferenceVerdict
    {
        /// <summary>This row denotes the very assembly that supplied the evidence.</summary>
        Verified,

        /// <summary>This row is checkable and denotes a different assembly.</summary>
        Contradicted,

        /// <summary>
        /// This row cannot be checked either way — it declares its identity substitutable, or the
        /// evidence it would be checked against is itself ambiguous.
        /// </summary>
        Indeterminate,
    }

    /// <summary>
    /// What an <c>AssemblyRef</c> to <paramref name="rawSpelling"/> says about the assembly that
    /// supplied this alias's forwarder evidence.
    ///
    /// <para>The reference blob is a full public key rather than a token when
    /// <see cref="AssemblyFlags.PublicKey"/> is set, and is reduced before comparison — comparing a
    /// 160-byte key against an 8-byte token would reject every reference that spells its identity
    /// that way, dropping genuine callers.</para>
    ///
    /// <para>A retargetable reference declares its identity substitutable, so its token is not the
    /// definition's; an ambiguous spelling was claimed by two differently signed assemblies, so no
    /// reference can be checked against it. Neither is evidence of disagreement, so neither
    /// refutes. An unsigned evidence assembly, by contrast, <em>can</em> answer: a reference
    /// carrying a token could not have bound to it, so that pair genuinely disagrees.</para>
    /// </summary>
    ReferenceVerdict VerdictFor(
        string rawSpelling,
        AssemblyReferenceSpelling reference)
        => _spellingTokens.TryGetValue(rawSpelling, out EvidenceIdentity evidence)
            ? VerdictFor(reference, evidence)
            : ReferenceVerdict.Indeterminate;

    /// <summary>
    /// The same question with the evidence supplied directly, for the edges that are not a caller's
    /// reference to a facade.
    ///
    /// <para>Both halves of the forwarder graph ask it. A caller's <c>AssemblyRef</c> to a facade is
    /// one edge; the facade's own <c>AssemblyRef</c> to the assembly it forwards <em>to</em> is the
    /// other, and until review of <c>a749cd4d</c> only the first was ever checked. A facade that
    /// forwarded the type to a different assembly of the same name as the target still vouched for
    /// the caller, so the tool reported a call to the target that the caller never made. Routing
    /// both through this one method is what keeps the two edges from drifting apart again.</para>
    /// </summary>
    static ReferenceVerdict VerdictFor(
        AssemblyReferenceSpelling reference,
        EvidenceIdentity evidence)
    {
        if (evidence.Token.AsSpan().SequenceEqual(AmbiguousSpelling))
            return ReferenceVerdict.Indeterminate;

        if ((reference.Flags & AssemblyFlags.Retargetable) != 0)
            return ReferenceVerdict.Indeterminate;

        if (!string.Equals(reference.Culture, evidence.Culture, StringComparison.OrdinalIgnoreCase))
            return ReferenceVerdict.Contradicted;

        // Version rule, and why it depends on signing. For a strong-named assembly the token
        // already proves the publisher, and binding to a same-or-newer definition of one strong
        // name is exactly what the loader does — requiring equality there would decline the
        // canonical facade itself (mscorlib references System.Private.CoreLib at 0.0.0.0 against a
        // definition of 9.0.0.0). For an unsigned assembly the token proves nothing, anyone can
        // produce the name, and no binding policy ties an unsigned reference to a later file, so
        // version is the only discriminator left and "could roll forward" is not evidence that it
        // did (executed in review of 984454a3: an unsigned v3 forwarder vouched for a caller that
        // referenced v2, which may define the type itself).
        //
        // Measured over the shared framework, ASP.NET Core and this repository's build output
        // (16,294 references resolving to a definition on disk): 712 legitimate roll-forwards are
        // strong-named and exactly 1 is unsigned. So this costs one miss and closes the
        // fabrication.
        //
        // Unsigned evidence matches any version actually observed for the spelling, not just the
        // highest. Comparing against the highest alone meant a second, newer unsigned file
        // suppressed the older one's genuine callers (executed in review of a749cd4d).
        bool evidenceIsSigned = evidence.Token.Length > 0;
        bool versionAgrees = evidenceIsSigned
            ? reference.Version <= evidence.Version
            : !evidence.ObservedVersions.IsDefaultOrEmpty
                && evidence.ObservedVersions.Contains(reference.Version);

        if (!versionAgrees)
            return ReferenceVerdict.Contradicted;

        ReadOnlySpan<byte> referenceToken = (reference.Flags & AssemblyFlags.PublicKey) != 0
            ? PublicKeyTokenOf(reference.PublicKeyOrToken.AsSpan())
            : reference.PublicKeyOrToken.AsSpan();

        return referenceToken.SequenceEqual(evidence.Token)
            ? ReferenceVerdict.Verified
            : ReferenceVerdict.Contradicted;
    }

    /// <summary>
    /// The public key token an assembly reference would carry for an assembly whose full public
    /// key is <paramref name="publicKey"/>: the low 8 bytes of its SHA-1, reversed. An unsigned
    /// assembly has an empty token, which compares equal only to another empty token.
    /// </summary>
    static byte[] PublicKeyTokenOf(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.IsEmpty)
            return [];

        Span<byte> hash = stackalloc byte[20];
        System.Security.Cryptography.SHA1.HashData(publicKey, hash);

        var token = new byte[8];
        for (int i = 0; i < 8; i++)
            token[i] = hash[hash.Length - 1 - i];

        return token;
    }

    /// <summary>
    /// Whether <paramref name="assembly"/> is a facade spelling that forwards the target type to
    /// the target's defining assembly. Already-equal spellings are not aliases and are not
    /// recorded here; identity comparison answers those.
    ///
    /// <para>Assembly names compare case-insensitively, as the CLR compares them
    /// (<see cref="System.Reflection.AssemblyName.ReferenceMatchesDefinition"/> matches
    /// <c>contoso.facade</c> to <c>Contoso.Facade</c>). The alias sets were ordinal while the
    /// identity verification beside them was case-insensitive, so a reference that differed only
    /// in case verified and was then refused by the lookup, dropping a genuine caller (found in
    /// review of <c>984454a3</c>).</para>
    /// </summary>
    public bool Includes(string assembly) => _aliases.Contains(assembly);

    /// <summary>
    /// <see cref="Includes(string)"/> for a candidate whose pre-canonical spelling is known.
    ///
    /// <para>Canonicalization collapses the five core-library facade spellings onto one name, so
    /// <see cref="Includes(string)"/> alone cannot tell a verified spelling from a withdrawn one in
    /// the same bucket: an image referencing a retargetable <c>mscorlib</c> beside a verified
    /// <c>netstandard</c> had its <c>mscorlib</c> withdrawal silently undone, and a
    /// <c>TypeRef</c> that went through the retargetable row matched anyway (executed in review of
    /// <c>b18e5009</c>). Withdrawing the bucket instead is not an option — that takes the verified
    /// sibling down with it, which is the regression the <c>Contradicted</c>/<c>Indeterminate</c>
    /// split exists to prevent — so the spelling is refused by name here, where it is still
    /// known.</para>
    ///
    /// <para>A candidate with no raw spelling is not special-cased. An earlier revision skipped the
    /// refusal when <paramref name="rawSpelling"/> was empty, reading it as "nothing known, so do
    /// not refuse" — a permissive default in a check whose entire purpose is to refuse, and the
    /// shape every defect in this file has taken (raised in review of <c>984454a3</c>). An empty
    /// spelling simply is not in the withdrawn set, so the refusal is a no-op for it either way,
    /// and a <see cref="TypeRef"/> with no assembly name also has no canonical one to match.</para>
    /// </summary>
    public bool Includes(string assembly, string rawSpelling)
        => _aliases.Contains(assembly) && !_withdrawnSpellings.Contains(rawSpelling);

    /// <summary>
    /// Whether <paramref name="candidate"/> denotes the same type as <paramref name="target"/>,
    /// by identity or by a recorded facade spelling. This is the single definition of that
    /// question: <see cref="MemberPattern.MatchesCrossAssembly"/> and
    /// <see cref="CallerScopeTypeFilter"/> both call it rather than each deriving it, which is how
    /// the prefilter is kept from ruling out an assembly the matcher would have matched.
    ///
    /// The alias branch only ever admits candidates that agree with the target on everything
    /// except which assembly spells the type.
    /// </summary>
    public static bool DenotesSameType(TypeRef candidate, TypeRef target, ForwardedTypeAliases? aliases)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(target);

        if (candidate.Equals(target))
            return true;

        if (aliases is null || aliases.IsEmpty)
            return false;

        return candidate.Kind == TypeRefKind.Definition
            && target.Kind == TypeRefKind.Definition
            && string.Equals(candidate.Namespace, target.Namespace, StringComparison.Ordinal)
            && string.Equals(candidate.Name, target.Name, StringComparison.Ordinal)
            && aliases.Includes(candidate.Assembly, candidate.RawAssembly);
    }

    /// <summary>
    /// Computes the aliases for <paramref name="openDeclaringType"/> by reading the
    /// <c>ExportedType</c> table of each assembly in <paramref name="scopeAssemblyPaths"/>.
    ///
    /// Only forwarder rows for this one type are considered, and a chain is followed through other
    /// scope members so a facade that forwards to another facade is still recognized. The terminal
    /// assembly does not need to be in the scope: it is compared by canonicalized name, so a
    /// forwarder into the core library resolves without opening it.
    /// </summary>
    public static ForwardedTypeAliases ForTarget(
        TypeRef openDeclaringType,
        IEnumerable<string> scopeAssemblyPaths)
        => ForTarget(openDeclaringType, targetAssemblyPath: null, scopeAssemblyPaths, seedSpellings: null);

    /// <summary>
    /// The demand-driven form. <paramref name="seedSpellings"/> names the assembly spellings a
    /// caller could actually have written — its own <c>AssemblyRef</c> entries — so only those
    /// files' forwarder tables are read, and then only the files their chains actually point at.
    ///
    /// <para>This is what keeps the sweep off the hot path. A framework directory holds hundreds of
    /// assemblies and a caller can name only the handful it references; reading the rest cannot
    /// change an answer, because an alias is consulted solely for a spelling some caller wrote.
    /// Chains are still followed to the end, so an intermediate facade that no caller names is
    /// read when — and only when — a chain reaches it.</para>
    ///
    /// <para>Seeding narrows which <em>forwarder tables</em> are read, never which identities are
    /// known: every path is still censused for the name it claims. Narrowing the identities too is
    /// what let the seeded walk admit an alias the unseeded walk refused.</para>
    ///
    /// <para>A null <paramref name="seedSpellings"/> means the callers' spellings could not be
    /// enumerated, and every supplied path is read. That is the sound direction: a narrower sweep
    /// on unknown input would silently drop aliases.</para>
    /// </summary>
    public static ForwardedTypeAliases ForTarget(
        TypeRef openDeclaringType,
        IEnumerable<string> scopeAssemblyPaths,
        IReadOnlySet<string>? seedSpellings)
        => ForTarget(openDeclaringType, targetAssemblyPath: null, scopeAssemblyPaths, seedSpellings);

    /// <summary>
    /// The full form. <paramref name="targetAssemblyPath"/> is the file that defines the target
    /// type, and supplying it is what lets the terminal hop of a forwarder chain be verified: the
    /// chain claims to arrive at that assembly, and only its real <c>AssemblyDef</c> can say
    /// whether it did. Without it the identity is taken from the census, and when the census cannot
    /// name it unambiguously no alias is produced at all.
    ///
    /// <para>A supplied path is authoritative even when it cannot be read: the census is consulted
    /// only when no path was supplied. Falling back to the census for an unreadable supplied path
    /// meant that <em>losing</em> the authoritative identity produced a <em>wider</em> answer than
    /// having it, admitting a rival the readable path refuses.</para>
    /// </summary>
    public static ForwardedTypeAliases ForTarget(
        TypeRef openDeclaringType,
        string? targetAssemblyPath,
        IEnumerable<string> scopeAssemblyPaths,
        IReadOnlySet<string>? seedSpellings)
    {
        ArgumentNullException.ThrowIfNull(openDeclaringType);
        ArgumentNullException.ThrowIfNull(scopeAssemblyPaths);

        // Only a plain type definition has an assembly-qualified identity to alias. Anything else
        // is not a shape the matcher's declaring type takes.
        if (openDeclaringType.Kind != TypeRefKind.Definition)
            return None;

        // Normalized once, up front, so that two spellings of one path are one file everywhere
        // below: the census counts claimants by path, and `dir\x.dll` and `dir\.\x.dll` counted as
        // two files contesting one name, which made the target's identity unknowable and dropped
        // every genuine caller (executed in review of d6405614).
        var paths = scopeAssemblyPaths.Select(Normalize).ToList();

        // Every path's assembly name and AssemblyDef identity, read without touching the
        // ExportedType table. Indexing evidence by the identity a file *claims* rather than by its
        // file name is what makes the ambiguity check trustworthy: the seeded frontier used to
        // filter on file name, so a second file claiming an admitted spelling under a different
        // file name was never opened and never contradicted anything, and the seeded walk admitted
        // an alias the unseeded walk correctly refused (executed in review of a749cd4d).
        //
        // Measured warm over the shared framework (301 files): the census costs 237 ms against
        // 368 ms for the full forwarder read it protects, so seeding still earns its keep — it
        // skips the 9,267-row ExportedType scan, which is the part that actually costs. This also
        // retires the file-name assumption tracked as #3479.
        var census = new Dictionary<string, List<(string Path, EvidenceIdentity Identity)>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (IdentityOf(path) is not { } claimed)
                continue;

            if (!census.TryGetValue(claimed.Name, out var claimants))
                census[claimed.Name] = claimants = [];

            // Deduplicated, because the same file listed twice is one claimant. Counting it twice
            // made a single-claimant name look contested and dropped a genuine alias (executed in
            // review of 7572838c). Paths are normalized above, so two spellings of one file compare
            // equal here.
            if (!claimants.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))
                claimants.Add((path, claimed.Identity));
        }

        var frontier = seedSpellings is null
            ? paths
            : census
                .Where(entry => seedSpellings.Contains(entry.Key))
                .SelectMany(entry => entry.Value.Select(claimant => claimant.Path))
                .ToList();

        // Who the target assembly actually is. The terminal hop of a forwarder chain claims to
        // point at it, and that claim is only checkable against a real identity. The inspected
        // library supplies it on the product path; otherwise the census can name it, but only when
        // exactly one file claims it — two claimants is the ambiguity this whole file is about.
        string targetAssemblyName = openDeclaringType.RawAssembly.Length > 0
            ? openDeclaringType.RawAssembly
            : openDeclaringType.Assembly;

        EvidenceIdentity? targetIdentity = null;
        if (targetAssemblyPath is not null)
        {
            // An explicitly supplied target is authoritative, including when it cannot be read.
            // Falling through to the census then meant that LOSING the authoritative identity
            // STRENGTHENED the result, admitting a rival the readable path refuses — information
            // loss must never widen an answer (executed in review of 7572838c).
            if (IdentityOf(targetAssemblyPath) is { } supplied)
            {
                targetAssemblyName = supplied.Name;
                targetIdentity = supplied.Identity;
            }
        }
        else if (census.TryGetValue(targetAssemblyName, out var targetClaimants)
            && targetClaimants.Count == 1)
        {
            targetIdentity = targetClaimants[0].Identity;
        }

        // raw assembly spelling -> the assemblies its forwarder for this type points at, each
        // canonicalized because the matcher compares TypeRef.Assembly values that already are.
        //
        // The KEY is deliberately raw. Canonicalization collapses the five core-library facade
        // spellings onto one name, so keying this on the canonical name made a failing edge on one
        // of them withdraw its siblings: a stray `mscorlib` forwarding the type somewhere
        // unverifiable removed the genuine `netstandard` alias sharing its bucket, and a caller was
        // dropped (executed in review of 7572838c). This is the same lesson the caller side learned
        // in review of b18e5009 — withdraw the spelling, never the bucket — arriving on the definer
        // side one round later. Canonicalization happens once, below, after pruning.
        //
        // The VALUE is a set because two spellings that canonicalize together may legitimately
        // forward to different assemblies, and collapsing to whichever was read first made
        // reachability depend on enumeration order.
        var targetsByRaw = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var tokensBySpelling = new Dictionary<string, EvidenceIdentity>(StringComparer.OrdinalIgnoreCase);
        var probed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Every edge read, kept so the definer side can be verified once the identity of every
        // evidence file is known. It cannot be verified while reading: the file an edge points at
        // may not have been opened yet, and deciding on partial evidence is what made the result
        // depend on enumeration order.
        var edges = new List<ForwarderEdge>();

        // Spellings under which some file DEFINES the type itself. A file that defines the type
        // contradicts a same-named file that forwards it exactly as a rival forwarder does: a
        // caller naming that spelling may be bound to the definition, in which case it is not a
        // caller of the target at all. Recording only forwarder rows left such a twin invisible,
        // so it contradicted nothing and its callers were reported against the target (executed in
        // review of 7572838c, with two indistinguishable `Contoso.Facade` files).
        var definingSpellings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int hop = 0; hop <= MaxHops && frontier.Count > 0; hop++)
        {
            // A set, because one file's rows and the files claiming what they point at multiply:
            // every row enqueued every claimant of its target, so a file carrying many rows for one
            // type queued rows × claimants entries before the next hop deduplicated them. Row
            // counts and scope size are both attacker-controlled, so that product is unbounded work
            // on hostile input (executed in review of 57187942).
            var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in frontier)
            {
                if (!probed.Add(path))
                    continue;

                if (ProbeForType(path, openDeclaringType.Namespace, openDeclaringType.Name) is not { } probe)
                    continue;

                if (probe.DeclaresType)
                    definingSpellings.Add(probe.Assembly);

                foreach (var edge in probe.Edges)
                {
                    edges.Add(edge);

                    // Follow the chain: the assembly this one forwards to may itself be a facade
                    // that no caller names, and dropping it would break a multi-hop chain.
                    foreach (var claimant in census.TryGetValue(edge.Target, out var hops) ? hops : [])
                    {
                        if (!probed.Contains(claimant.Path))
                            next.Add(claimant.Path);
                    }
                }
            }

            frontier = [.. next];
        }

        // Now that every claimed identity is known, fold the edges together. Two files claiming one
        // simple name cannot both answer for it: recording only the first would let the loser's
        // callers be validated against the winner's key, so an ambiguous spelling is marked
        // unusable (a one-byte token never matches a real one). Differing only in version is not
        // ambiguity — both files are the same identity and both forward the type — so those
        // versions are merged instead.
        foreach (var edge in edges)
        {
            string target = TypeRef.CanonicalAssembly(edge.Target);

            // Keyed on the raw spelling, so a disagreement is attributed to the spelling whose two
            // files actually disagree rather than to everything that canonicalizes alongside it.
            bool retargets = targetsByRaw.TryGetValue(edge.Assembly, out var already)
                && !already.Contains(target);

            if (retargets)
            {
                tokensBySpelling[edge.Assembly] =
                    new EvidenceIdentity(AmbiguousSpelling, "", new Version(0, 0, 0, 0));
            }
            else if (!tokensBySpelling.ContainsKey(edge.Assembly))
            {
                // Identity comes from the CENSUS, not from this edge. Deriving it from the files
                // that forward the type left a same-named twin that does not forward it invisible,
                // so it contradicted nothing — and a caller that really binds to the twin's own
                // definition was reported as calling the target (executed in review of 7572838c).
                // The census sees every claimant, whatever it contains.
                tokensBySpelling[edge.Assembly] = CensusIdentity(census, edge.Assembly, edge.Identity);
            }

            if (!targetsByRaw.TryGetValue(edge.Assembly, out var targets))
                targetsByRaw[edge.Assembly] = targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            targets.Add(target);
        }

        // A spelling under which the type is both forwarded and defined answers for itself and for
        // the target at once, and nothing distinguishes which one a caller bound to. Marked
        // unusable rather than removed, so — like every other refusal here — it withdraws the
        // spelling and not the canonical bucket its siblings share.
        foreach (string spelling in definingSpellings)
        {
            if (targetsByRaw.ContainsKey(spelling))
            {
                tokensBySpelling[spelling] =
                    new EvidenceIdentity(AmbiguousSpelling, "", new Version(0, 0, 0, 0));
            }
        }

        // Verify the definer side of every edge. Until now only the caller's reference to a facade
        // was ever checked; the facade's own reference to the assembly it forwards *to* was taken
        // on its name. A facade forwarding the type to a different assembly that merely shares the
        // target's name therefore vouched for the caller, and the tool reported a call the caller
        // never made (executed in review of a749cd4d).
        //
        // Verification requires knowing who the far side really is, so an edge is kept only when
        // its target's identity is known and agrees. Indeterminate is not good enough here: on the
        // caller side an unverifiable row is merely unusable, but on this side admitting one
        // asserts a forwarding relationship that was never established. Refusing costs an alias
        // and never invents one.
        //
        // This is also what refuses an ambiguous *intermediate* hop, which an earlier revision
        // handled with a second mechanism beside this one. It does not need one: an alias can be
        // produced in exactly two ways, and the sentinel identity an ambiguous spelling carries
        // stops both. A caller naming the ambiguous spelling itself is refused by the caller-side
        // check, and anything reaching it through a chain must hold an edge whose TargetReference
        // is verified here against that same sentinel, which no reference matches. Removing the
        // duplicate left one enforcement path, so the order-independence test below gates it.
        //
        // Removal is by raw spelling. Removing the canonical bucket instead let a stray facade
        // withdraw a genuine sibling that merely canonicalized alongside it (see targetsByRaw).
        foreach (var edge in edges)
        {
            if (!targetsByRaw.ContainsKey(edge.Assembly))
                continue;

            EvidenceIdentity? far =
                targetIdentity is { } known
                && string.Equals(edge.Target, targetAssemblyName, StringComparison.OrdinalIgnoreCase)
                    ? known
                    : tokensBySpelling.TryGetValue(edge.Target, out EvidenceIdentity hop) ? hop : null;

            if (far is not { } evidence || VerdictFor(edge.TargetReference, evidence) != ReferenceVerdict.Verified)
                targetsByRaw.Remove(edge.Assembly);
        }

        if (targetsByRaw.Count == 0)
            return None;

        // Only now collapse the survivors onto canonical names, which is what the matcher compares.
        // Doing this before pruning is what made one spelling's failure everyone's failure.
        //
        // The merged map is the map of INTERMEDIATE hops only. Reachability is still asked of each
        // raw spelling from its own targets: seeding the walk with a canonical bucket's union let a
        // spelling that forwards the type somewhere else entirely be credited with a sibling's
        // target and vouch for callers it has nothing to do with (executed in review of 7572838c).
        // Merging is right for a hop, because the matcher already treats the five core-library
        // spellings as one assembly, and wrong for a seed, because the question there is what THIS
        // spelling forwards.
        var forwardsTo = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach ((string raw, var targets) in targetsByRaw)
        {
            string canonical = TypeRef.CanonicalAssembly(raw);

            if (!forwardsTo.TryGetValue(canonical, out var merged))
                forwardsTo[canonical] = merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            merged.UnionWith(targets);
        }

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rawSpellings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spellingTokens = new Dictionary<string, EvidenceIdentity>(StringComparer.OrdinalIgnoreCase);
        var canonicalByRaw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string raw, var seeds) in targetsByRaw)
        {
            string canonical = TypeRef.CanonicalAssembly(raw);

            // A spelling that already equals the target is not an alias; identity answers it.
            if (canonical == openDeclaringType.Assembly
                || !ReachesTarget(seeds, openDeclaringType.Assembly, forwardsTo))
            {
                continue;
            }

            aliases.Add(canonical);
            rawSpellings.Add(raw);
            canonicalByRaw[raw] = canonical;
            if (tokensBySpelling.TryGetValue(raw, out EvidenceIdentity identity))
                spellingTokens[raw] = identity;
        }

        // Nothing is withdrawn until an image's AssemblyRef table is consulted; RestrictedTo fills
        // this in per caller image.
        return aliases.Count == 0
            ? None
            : new ForwardedTypeAliases(
                aliases, rawSpellings, spellingTokens, canonicalByRaw,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether following this type's forwarder chain from <paramref name="seeds"/> — the
    /// assemblies one raw spelling forwards the type to — arrives at
    /// <paramref name="targetAssembly"/>. Bounded and cycle-guarded: a forwarder chain in hostile
    /// or merely broken metadata can loop.
    ///
    /// <para>A search rather than a walk, because canonicalization can merge two spellings that
    /// forward to different assemblies onto one key. Following only the first would make the
    /// answer depend on which file was read first.</para>
    ///
    /// <para>The seeds are one spelling's own targets, while <paramref name="forwardsTo"/> is the
    /// canonically merged map used from the second hop onward. Seeding from the merged map instead
    /// credited a spelling with a canonical sibling's target.</para>
    /// </summary>
    static bool ReachesTarget(
        IReadOnlyCollection<string> seeds,
        string targetAssembly,
        Dictionary<string, HashSet<string>> forwardsTo)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();

        foreach (string seed in seeds)
        {
            if (string.Equals(seed, targetAssembly, StringComparison.OrdinalIgnoreCase))
                return true;

            if (visited.Add(seed))
                pending.Enqueue(seed);
        }

        for (int hop = 1; hop < MaxHops && pending.Count > 0; hop++)
        {
            for (int width = pending.Count; width > 0; width--)
            {
                if (!forwardsTo.TryGetValue(pending.Dequeue(), out var nexts))
                    continue;

                foreach (string next in nexts)
                {
                    // The chain terminates wherever the forwarder points, whether or not that
                    // assembly is itself in the scope. Both sides are already canonicalized, so a
                    // forwarder into a core-library facade name compares equal to a target whose
                    // own identity canonicalized the same way. Compared case-insensitively like
                    // every other assembly-name comparison here, including this walk's own visited
                    // set — an ordinal comparison here silently dropped a caller whose forwarder
                    // spelled the definer's name in another case (executed in review of 7572838c).
                    if (string.Equals(next, targetAssembly, StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (visited.Add(next))
                        pending.Enqueue(next);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The name an assembly claims in its own <c>AssemblyDef</c>, or null when the file is not a
    /// readable managed assembly. Read on its own because the census needs the claimed identity of
    /// every candidate without paying for its <c>ExportedType</c> table.
    /// </summary>
    static string? AssemblyNameOf(string path) => IdentityOf(path)?.Name;

    /// <summary>
    /// The identity every file claiming <paramref name="spelling"/> agrees on, or the ambiguous
    /// sentinel when they do not. Versions are merged rather than treated as disagreement, because
    /// two builds of one unsigned assembly are the same identity; token and culture must match.
    ///
    /// <para>Deriving this from forwarder rows instead left a same-named twin that does not forward
    /// the type unseen, so it could not contradict anything.</para>
    /// </summary>
    static EvidenceIdentity CensusIdentity(
        Dictionary<string, List<(string Path, EvidenceIdentity Identity)>> census,
        string spelling,
        EvidenceIdentity fallback)
    {
        if (!census.TryGetValue(spelling, out var claimants) || claimants.Count == 0)
            return fallback;

        EvidenceIdentity agreed = claimants[0].Identity;
        for (int i = 1; i < claimants.Count; i++)
        {
            EvidenceIdentity other = claimants[i].Identity;
            bool sameIdentity =
                agreed.Token.AsSpan().SequenceEqual(other.Token)
                && string.Equals(agreed.Culture, other.Culture, StringComparison.OrdinalIgnoreCase);

            if (!sameIdentity)
                return new EvidenceIdentity(AmbiguousSpelling, "", new Version(0, 0, 0, 0));

            agreed = agreed.Observing(other.Version);
        }

        return agreed;
    }

    /// <summary>
    /// One file's path in a single spelling, so that two ways of naming it compare equal. A path
    /// this cannot resolve is returned unchanged: the walk opens it either way, and failing to
    /// normalize can only cost a deduplication, never invent a claimant.
    /// </summary>
    static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException
                                      or NotSupportedException
                                      or PathTooLongException
                                      or System.Security.SecurityException)
        {
            return path;
        }
    }

    /// <summary>
    /// The name and <c>AssemblyDef</c> identity an assembly claims for itself, or null when the
    /// file is not a readable managed assembly.
    /// </summary>
    static (string Name, EvidenceIdentity Identity)? IdentityOf(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return null;

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
                return null;

            var definition = reader.GetAssemblyDefinition();
            return (
                reader.GetString(definition.Name),
                new EvidenceIdentity(
                    PublicKeyTokenOf(reader.GetBlobContent(definition.PublicKey).AsSpan()),
                    definition.Culture.IsNil ? "" : reader.GetString(definition.Culture),
                    definition.Version));
        }
        catch (Exception ex) when (ex is BadImageFormatException
                                      or IOException
                                      or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// What one file has to say about the type named by <paramref name="ns"/> and
    /// <paramref name="name"/>: the assembly name it claims, <em>every</em> forwarder edge it
    /// carries for that type, and whether it <em>declares</em> the type itself.
    ///
    /// <para>All three answers come from the one read, because all three bear on the same question.
    /// A file that declares the type contradicts a same-named file that forwards it, and a walk that
    /// looked only for forwarders could not see that contradiction.</para>
    ///
    /// <para>Edges are returned whole rather than stopping at the first match: a file carrying two
    /// forwarder rows for one type disagrees with itself, and answering from whichever row the table
    /// listed first made the verdict depend on metadata row order (executed in review of d6405614).
    /// Reporting both lets the ordinary disagreement rule refuse the spelling, which is where that
    /// judgement already lives.</para>
    ///
    /// <para>The target's <c>AssemblyRef</c> row is returned whole rather than by name because the
    /// name alone cannot say <em>which</em> assembly of that name the forwarder meant, and that is
    /// the question the terminal hop has to answer.</para>
    ///
    /// <para>Reads metadata only; an unreadable image contributes nothing, which leaves the matcher
    /// exactly where it is today.</para>
    /// </summary>
    static (string Assembly, List<ForwarderEdge> Edges, bool DeclaresType)? ProbeForType(
        string path,
        string ns,
        string name)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return null;

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
                return null;

            var definition = reader.GetAssemblyDefinition();
            string assembly = reader.GetString(definition.Name);
            var identity = new EvidenceIdentity(
                PublicKeyTokenOf(reader.GetBlobContent(definition.PublicKey).AsSpan()),
                definition.Culture.IsNil ? "" : reader.GetString(definition.Culture),
                definition.Version);

            bool declaresType = false;
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);

                // Only top-level types: a nested type's namespace is its enclosing type's, so
                // comparing it against an assembly-qualified full name would match the wrong thing.
                if (type.IsNested)
                    continue;

                // Compared through the metadata string comparer rather than by materializing each
                // name, because this loop now runs over every probed file and a core library
                // declares thousands of types.
                if (reader.StringComparer.Equals(type.Name, name)
                    && reader.StringComparer.Equals(type.Namespace, ns))
                {
                    declaresType = true;
                    break;
                }
            }

            var edges = new List<ForwarderEdge>();
            foreach (var handle in reader.ExportedTypes)
            {
                var exported = reader.GetExportedType(handle);

                if (!reader.StringComparer.Equals(exported.Name, name)
                    || !reader.StringComparer.Equals(exported.Namespace, ns))
                {
                    continue;
                }

                if (!exported.IsForwarder)
                {
                    // A multi-module assembly declares its public types in netmodules and lists
                    // them here, implemented by an AssemblyFile. That row is a declaration, not a
                    // forward, and it contradicts a same-named facade exactly as a TypeDef does —
                    // a scan of TypeDef rows alone could not see it (executed in review of
                    // d6405614). Rows implemented by anything else say nothing about this file.
                    if (exported.Implementation.Kind == HandleKind.AssemblyFile)
                        declaresType = true;

                    continue;
                }

                // A nested forwarded type's implementation is the enclosing ExportedType, not an
                // AssemblyReference, so Outer+Inner forwarders are not recognized here. That loses an
                // alias and never invents one; tracked as #3480.
                if (exported.Implementation.Kind != HandleKind.AssemblyReference)
                    continue;

                var target = reader.GetAssemblyReference(
                    (AssemblyReferenceHandle)exported.Implementation);
                edges.Add(new ForwarderEdge(
                    assembly,
                    reader.GetString(target.Name),
                    identity,
                    new AssemblyReferenceSpelling(
                        reader.GetString(target.Name),
                        [.. reader.GetBlobContent(target.PublicKeyOrToken)],
                        target.Flags,
                        target.Culture.IsNil ? "" : reader.GetString(target.Culture),
                        target.Version)));
            }

            return (assembly, edges, declaresType);
        }
        catch (Exception ex) when (ex is BadImageFormatException
                                      or IOException
                                      or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
