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

    readonly HashSet<string> _aliases;
    readonly HashSet<string> _rawSpellings;
    readonly Dictionary<string, byte[]> _spellingTokens;

    ForwardedTypeAliases(
        HashSet<string> aliases,
        HashSet<string> rawSpellings,
        Dictionary<string, byte[]> spellingTokens)
    {
        _aliases = aliases;
        _rawSpellings = rawSpellings;
        _spellingTokens = spellingTokens;
    }

    /// <summary>No aliases: every comparison falls back to plain identity.</summary>
    public static ForwardedTypeAliases None { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase));

    public bool IsEmpty => _aliases.Count == 0;

    /// <summary>The recorded alias spellings, for diagnostics and tests.</summary>
    public IReadOnlyCollection<string> Aliases => _aliases;

    /// <summary>
    /// The facade names exactly as their assemblies spell them, for consumers that compare raw
    /// <c>AssemblyRef</c> names rather than <see cref="TypeRef"/> identities.
    ///
    /// <para>This is not a convenience: <see cref="Aliases"/> is canonicalized, and the
    /// canonicalization collapses the five core-library facade spellings onto one name. When
    /// <c>netstandard</c> is the facade that forwards a type, its canonical alias is
    /// <c>corelib</c> — and <em>every</em> managed assembly references a core-library facade, so an
    /// assembly-level filter keyed on the canonical name would select the entire scope and undo the
    /// prefiltering it is part of. The raw spellings are precise: only assemblies that actually
    /// carry the forwarder appear here.</para>
    /// </summary>
    public IReadOnlyCollection<string> RawSpellings => _rawSpellings;

    /// <summary>Whether an assembly spells its name as one of the facades that forward the type.</summary>
    public bool IncludesRawSpelling(string assembly) => _rawSpellings.Contains(assembly);

    /// <summary>
    /// Whether a reference to <paramref name="rawSpelling"/> carrying
    /// <paramref name="referenceToken"/> is a reference to the very assembly that supplied the
    /// forwarder evidence for this alias.
    ///
    /// <para><b>Why this exists.</b> An alias is a bare assembly <em>name</em>, because that is all
    /// a <see cref="TypeRef"/> records. Two different assemblies can share a simple name and differ
    /// in strong-name identity, and without this check a forwarder read from one of them is applied
    /// to a caller that bound against the other — reporting a call to an unrelated type as a call to
    /// the target. That is a fabricated caller, which is worse than a missing one, and it is a
    /// regression this change would otherwise introduce (found in review of #3419).</para>
    ///
    /// <para>Unrecorded spellings answer <see langword="true"/>: there is no evidence to contradict,
    /// and this must never be the reason a candidate is ruled out.</para>
    ///
    /// <para>An absent token on <em>either</em> side also answers <see langword="true"/>. Only a
    /// present-and-different token is evidence of a different assembly; a missing one is merely
    /// unknown, and narrowing on unknown input is how a prefilter silently drops real callers. This
    /// check exists to reject a demonstrated collision, not to require strong naming.</para>
    /// </summary>
    public bool EvidenceIdentityAgrees(string rawSpelling, ReadOnlySpan<byte> referenceToken)
    {
        if (referenceToken.IsEmpty)
            return true;

        if (!_spellingTokens.TryGetValue(rawSpelling, out byte[]? evidenceToken)
            || evidenceToken.Length == 0)
        {
            return true;
        }

        return referenceToken.SequenceEqual(evidenceToken);
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
    /// </summary>
    public bool Includes(string assembly) => _aliases.Contains(assembly);

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
            && aliases.Includes(candidate.Assembly);
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
        => ForTarget(openDeclaringType, scopeAssemblyPaths, seedSpellings: null);

    /// <summary>
    /// The demand-driven form. <paramref name="seedSpellings"/> names the assembly spellings a
    /// caller could actually have written — its own <c>AssemblyRef</c> entries — so only those
    /// files are opened, and then only the files their forwarder chains actually point at.
    ///
    /// <para>This is what keeps the sweep off the hot path. A framework directory holds hundreds of
    /// assemblies and a caller can name only the handful it references; opening the rest cannot
    /// change an answer, because an alias is consulted solely for a spelling some caller wrote.
    /// Chains are still followed to the end, so an intermediate facade that no caller names is
    /// opened when — and only when — a chain reaches it.</para>
    ///
    /// <para>A null <paramref name="seedSpellings"/> means the callers' spellings could not be
    /// enumerated, and every supplied path is read. That is the sound direction: a narrower sweep
    /// on unknown input would silently drop aliases.</para>
    /// </summary>
    public static ForwardedTypeAliases ForTarget(
        TypeRef openDeclaringType,
        IEnumerable<string> scopeAssemblyPaths,
        IReadOnlySet<string>? seedSpellings)
    {
        ArgumentNullException.ThrowIfNull(openDeclaringType);
        ArgumentNullException.ThrowIfNull(scopeAssemblyPaths);

        // Only a plain type definition has an assembly-qualified identity to alias. Anything else
        // is not a shape the matcher's declaring type takes.
        if (openDeclaringType.Kind != TypeRefKind.Definition)
            return None;

        string fullName = openDeclaringType.Namespace.Length == 0
            ? openDeclaringType.Name
            : $"{openDeclaringType.Namespace}.{openDeclaringType.Name}";

        var paths = scopeAssemblyPaths.ToList();

        // File name to path, for following a chain to its next hop without reopening the world.
        // A file whose name differs from its assembly name is simply not found here, which costs an
        // alias and never invents one. Tracked as #3479; indexing by metadata identity instead would
        // mean opening every evidence file, which is exactly what the seeded walk avoids.
        var pathsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
            pathsByName.TryAdd(Path.GetFileNameWithoutExtension(path), path);

        var frontier = seedSpellings is null
            ? paths
            : paths.Where(p => seedSpellings.Contains(Path.GetFileNameWithoutExtension(p))).ToList();

        // assembly name -> the assembly its forwarder for this type points at. Both sides are
        // canonicalized, because the matcher compares TypeRef.Assembly values that already are.
        // That collapses the five core-library facade spellings onto one key, so a target outside
        // the core library is reachable through whichever of them forwards the type. The
        // imprecision is inherited from TypeRef identity rather than introduced here: those names
        // are already indistinguishable to the matcher.
        var forwardsTo = new Dictionary<string, string>(StringComparer.Ordinal);
        var rawByCanonical = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var tokensBySpelling = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var probed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int hop = 0; hop <= MaxHops && frontier.Count > 0; hop++)
        {
            var next = new List<string>();
            foreach (string path in frontier)
            {
                if (!probed.Add(path))
                    continue;

                if (ReadForwarder(path, fullName) is not { } edge)
                    continue;

                string canonical = TypeRef.CanonicalAssembly(edge.Assembly);

                // First writer wins; two files claiming the same assembly name are not
                // distinguishable here, and picking either cannot make the filter narrower than
                // the matcher.
                forwardsTo.TryAdd(canonical, TypeRef.CanonicalAssembly(edge.Target));

                if (!rawByCanonical.TryGetValue(canonical, out var raw))
                    rawByCanonical[canonical] = raw = [];
                raw.Add(edge.Assembly);

                // Two files claiming one simple name cannot both answer for it. Recording only the
                // first would let the loser's callers be validated against the winner's key, so an
                // ambiguous spelling is marked unusable (empty token never matches a real one).
                if (tokensBySpelling.TryGetValue(edge.Assembly, out byte[]? seen)
                    && !seen.AsSpan().SequenceEqual(edge.Token))
                {
                    tokensBySpelling[edge.Assembly] = [0];
                }
                else
                {
                    tokensBySpelling.TryAdd(edge.Assembly, edge.Token);
                }

                // Follow the chain: the assembly this one forwards to may itself be a facade that
                // no caller names, and dropping it would break a multi-hop chain.
                if (pathsByName.TryGetValue(edge.Target, out string? nextPath)
                    && !probed.Contains(nextPath))
                {
                    next.Add(nextPath);
                }
            }

            frontier = next;
        }

        if (forwardsTo.Count == 0)
            return None;

        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var rawSpellings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spellingTokens = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string spelling in forwardsTo.Keys)
        {
            // A spelling that already equals the target is not an alias; identity answers it.
            if (spelling != openDeclaringType.Assembly
                && ReachesTarget(spelling, openDeclaringType.Assembly, forwardsTo))
            {
                aliases.Add(spelling);
                foreach (string raw in rawByCanonical[spelling])
                {
                    rawSpellings.Add(raw);
                    if (tokensBySpelling.TryGetValue(raw, out byte[]? token))
                        spellingTokens[raw] = token;
                }
            }
        }

        return aliases.Count == 0
            ? None
            : new ForwardedTypeAliases(aliases, rawSpellings, spellingTokens);
    }

    /// <summary>
    /// Whether following this type's forwarder chain from <paramref name="spelling"/> arrives at
    /// <paramref name="targetAssembly"/>. Bounded and cycle-guarded: a forwarder chain in hostile
    /// or merely broken metadata can loop.
    /// </summary>
    static bool ReachesTarget(
        string spelling,
        string targetAssembly,
        Dictionary<string, string> forwardsTo)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { spelling };
        string current = spelling;

        for (int hop = 0; hop < MaxHops; hop++)
        {
            if (!forwardsTo.TryGetValue(current, out string? next))
                return false;

            // The chain terminates wherever the forwarder points, whether or not that assembly is
            // itself in the scope. Both sides are already canonicalized, so a forwarder into a
            // core-library facade name compares equal to a target whose own identity canonicalized
            // the same way.
            if (next == targetAssembly)
                return true;

            if (!visited.Add(next))
                return false;

            current = next;
        }

        return false;
    }

    /// <summary>
    /// The assembly's own name and the assembly its forwarder for <paramref name="fullName"/>
    /// points at, or null when it carries no such forwarder. Reads metadata only; an unreadable
    /// image contributes no alias, which leaves the matcher exactly where it is today.
    /// </summary>
    static (string Assembly, string Target, byte[] Token)? ReadForwarder(string path, string fullName)
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

            string assembly = reader.GetString(reader.GetAssemblyDefinition().Name);
            byte[] token = PublicKeyTokenOf(
                reader.GetBlobContent(reader.GetAssemblyDefinition().PublicKey).AsSpan());

            foreach (var handle in reader.ExportedTypes)
            {
                var exported = reader.GetExportedType(handle);
                if (!exported.IsForwarder)
                    continue;
                // A nested forwarded type's implementation is the enclosing ExportedType, not an
                // AssemblyReference, so Outer+Inner forwarders are not recognized here. That loses an
                // alias and never invents one; tracked as #3480.
                if (exported.Implementation.Kind != HandleKind.AssemblyReference)
                    continue;

                string ns = reader.GetString(exported.Namespace);
                string name = reader.GetString(exported.Name);
                string candidate = ns.Length == 0 ? name : $"{ns}.{name}";
                if (candidate != fullName)
                    continue;

                var target = reader.GetAssemblyReference(
                    (AssemblyReferenceHandle)exported.Implementation);
                return (assembly, reader.GetString(target.Name), token);
            }

            return null;
        }
        catch (Exception ex) when (ex is BadImageFormatException
                                      or IOException
                                      or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
