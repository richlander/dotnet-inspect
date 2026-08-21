using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins every place in <c>ILInspector.Decompiler</c> that participates in
/// core-library identity trust: the methods that obtain a
/// <see cref="MetadataReader"/>, and the methods that grant identity through
/// <c>CoreLibraryIdentityTrust</c>.
/// <para>
/// <c>PlantedCoreLibraryIdentityTests.EveryPublicFactory_ClassifiesTheReaderItCreates</c>
/// enumerates <em>factory signatures</em>, and three consecutive review rounds
/// on PR #4428 escaped it along a different cosmetic dimension each time: the
/// method name, then the declared return type, then visibility and <c>Task</c>
/// wrapping (issue #4464). A signature has unboundedly many such dimensions, so
/// patching one per round does not converge.
/// </para>
/// <para>
/// This gate reads the compiled IL instead, and keys on the operation that
/// actually produces the thing trust attaches to. Core-library identity is
/// recorded per <see cref="MetadataReader"/> instance, and this assembly creates
/// one only by calling <c>GetMetadataReader</c> or by constructing a reader
/// directly — so a site is visible whatever its method is called, however it is
/// declared, and whether or not its result is wrapped. Both directions fail: an
/// unpinned site is an unreviewed way to obtain a reader, and a pinned site that
/// stops obtaining or granting is a stale entry.
/// </para>
/// <para>
/// Grants are recognised by the primitive, not by the call surface. Core-library
/// identity <em>is</em> membership in the <c>s_trusted</c> table, so
/// <see cref="TrustTableAccess_IsConfinedToItsPinnedMembers"/> pins every method
/// in the assembly whose IL reaches that field, whatever it is called and
/// whatever type declares it. A call into <c>CoreLibraryIdentityTrust</c> is
/// still reported as a grant unless its full signature appears in
/// <see cref="s_nonGrantingTrustMembers"/>, and
/// <see cref="TrustTypeMembers_AreClassified"/> requires the type to account for
/// every member it declares and to declare no nested types.
/// </para>
/// <para>
/// That structure is the answer to #4464 rather than one more patch. Rounds 3
/// and 4 escaped the call-surface scan four times — a member named
/// <c>Classify</c>, a nested <c>Helper</c> reaching the table directly, a
/// granting static constructor, and a <c>MayMint(MetadataReader)</c> overload
/// inheriting a bare-name exemption. Each was a fresh cosmetic dimension, which
/// is exactly the series that has no end. The field is not a dimension: a grant
/// written as ordinary code has to name it, so within direct IL access the pin
/// is complete by construction rather than by enumerating spellings.
/// </para>
/// <para>
/// <strong>That completeness is bounded to direct IL access, and to the grant.</strong>
/// Reflection over the field emits no <c>ldsfld</c>, so a reflective mutation
/// reaches the table without naming it here. And nothing in this class watches
/// the <em>consumer</em>: making <c>MayMintCoreLibraryIdentity</c> return
/// <see langword="true"/> unconditionally, having <c>TypeRefDecoder.CanonicalSelf</c>
/// mint without consulting trust at all, or introducing a second trust store
/// confers identity while adding no referent to <c>s_trusted</c>, and every one
/// of those leaves these tests green. They are not unguarded — they are
/// <c>PlantedCoreLibraryIdentityTests</c>'s property, and round 5 of PR #4469
/// confirmed each of those three tampers fails three of its tests
/// (<c>PlantedPlatformKey</c>, <c>DiscoveredSibling</c>, and
/// <c>PlantedSibling</c>). The division is deliberate: this gate asks where
/// readers come from and what reaches the trust table, and that suite asks
/// whether the resulting identity is deserved.
/// </para>
/// <para>
/// The scan sees <em>creation</em>, not receipt. A method handed a reader —
/// through a delegate, an interface, or a reflective invoke whose IL never
/// mentions <c>GetMetadataReader</c> — does not appear here, and round 2 of PR
/// #4469 demonstrated exactly that. It stays sound for two reasons worth stating
/// rather than assuming. A reader created outside this assembly was never
/// classified, and unclassified means no core-library identity, so laundering one
/// inward loses the privilege rather than gaining it. And the grant itself is a
/// direct call into <c>CoreLibraryIdentityTrust</c> at five sites, every one of
/// which this scan reports, so a reflectively-obtained reader still cannot be
/// granted identity invisibly. What reflection defeats is the completeness of the
/// acquisition inventory, which is a review convenience, not the trust boundary.
/// </para>
/// <para>
/// Being listed here is not approval. <see cref="TrustRole.GrantsIdentity"/>
/// records whether a site classifies the reader it obtains, and most sites
/// deliberately do not: an unclassified reader loses core-library identity,
/// which is the fail-closed half of the design that
/// <c>CoreLibraryIdentityTrust</c> documents. What the pin buys is that adding
/// either kind of site forces someone to say which it is.
/// </para>
/// <para>
/// <strong>What this gate does not cover.</strong> It answers "where do readers
/// come from, and which of them are classified", not "is each grant deserved". A
/// method that passed a discovered path into the raw-path designation overload
/// would launder discovery into designation while neither obtaining a reader nor
/// granting identity itself, so it would not appear here. That property belongs
/// to provenance, and <c>PlantedCoreLibraryIdentityTests</c> gates it — see
/// <c>DiscoveredSibling_IsDenied</c> and
/// <c>PlantedSibling_OpenedThroughMetadataSource_LosesCoreLibraryIdentity</c>.
/// The bound is stated deliberately: review of PR #4469 found an earlier,
/// unbounded phrasing of this claim to be false.
/// </para>
/// </summary>
public sealed class ReaderConstructionSiteTests
{
    /// <summary>
    /// The trust type itself. Its own helpers call the grant they implement, and
    /// reporting them would pin the mechanism as one of its own consumers.
    /// Compared by exact type identity: a <c>StartsWith</c> prefix test on the
    /// display name was escaped in review by declaring a namespace with this
    /// name.
    /// </summary>
    /// <summary>
    /// The field that <em>is</em> the trust. Core-library identity is exactly
    /// membership in this table, so every IL reference to it in the assembly is
    /// pinned by <see cref="TrustTableAccess_IsConfinedToItsPinnedMembers"/>.
    /// </summary>
    const string TrustTableFieldName = "s_trusted";

    /// <summary>
    /// The static constructor that creates the trust table. It is the one method
    /// allowed to store the field, because it is the method that makes it.
    /// </summary>
    const string TrustTableInitializerKey = "Pipeline.CoreLibraryIdentityTrust..cctor()";

    /// <summary>
    /// Every method in <c>ILInspector.Decompiler</c> permitted to touch the trust
    /// table, keyed by full signature. Nothing else in the assembly may name the
    /// field at all — not a nested helper, not a static constructor, not another
    /// overload of an allow-listed name.
    /// </summary>
    static readonly ImmutableHashSet<string> s_trustTableAccessors =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Pipeline.CoreLibraryIdentityTrust.GrantCoreLibraryIdentity(MetadataReader)",
            "Pipeline.CoreLibraryIdentityTrust.MayMintCoreLibraryIdentity(MetadataReader)");

    const string TrustTypeFullName = "ILInspector.Decompiler.Pipeline.CoreLibraryIdentityTrust";

    /// <summary>
    /// The members of the trust type that answer a question without conferring
    /// anything. Every other member is treated as granting, so a new member is
    /// grant-relevant until someone classifies it here.
    /// </summary>
    /// <remarks>
    /// The polarity is deliberate and was itself a review finding. Round 3 of PR
    /// #4469 escaped a <c>StartsWith("Grant")</c> test by adding a member named
    /// <c>Classify</c> that forwarded to the grant, and calling it from a site
    /// pinned as acquisition-only: every test stayed green while every opened
    /// reader gained identity. Recognising grants by their name reproduced, on
    /// the grant half, the cosmetic non-convergence that issue #4464 exists to
    /// end — a name is one more unbounded dimension. Listing the members that do
    /// <em>not</em> grant is bounded, because <see cref="TrustTypeMembers_AreClassified"/>
    /// fails when the type gains a member this set does not name.
    /// </remarks>
    static readonly ImmutableHashSet<string> s_nonGrantingTrustMembers =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "MayMint(AssemblyResolutionProvenance)",
            "MayMintCoreLibraryIdentity(MetadataReader)");

    /// <summary>
    /// The members of the trust type that confer core-library identity. This set
    /// is not what <see cref="RoleOf"/> tests — it treats everything outside
    /// <see cref="s_nonGrantingTrustMembers"/> as granting — but naming it lets
    /// <see cref="TrustTypeMembers_AreClassified"/> require that every declared
    /// member be a deliberate entry on one side or the other.
    /// </summary>
    static readonly ImmutableHashSet<string> s_grantingTrustMembers =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "GrantCoreLibraryIdentity(MetadataReader)",
            "GrantIfEntitled(MetadataReader, AssemblyResolutionProvenance)");

    /// <summary>
    /// Whether a trust-relevant site obtains a reader, grants core-library
    /// identity, or does both.
    /// </summary>
    [Flags]
    enum TrustRole
    {
        None = 0,
        ObtainsReader = 1,
        GrantsIdentity = 2,
    }

    /// <summary>
    /// Every trust-relevant method in <c>ILInspector.Decompiler</c>, with the
    /// role it plays and why. Keys carry the parameter list, so overloads stay
    /// distinct. Update this table in the same commit that moves a site, and say
    /// in the reason which half of the design the new entry is on.
    /// </summary>
    static readonly ImmutableDictionary<string, (TrustRole Role, string Reason)> s_pinned =
        new Dictionary<string, (TrustRole, string)>(StringComparer.Ordinal)
        {
            ["Pipeline.MetadataContext.OpenDesignated(String)"] =
                (TrustRole.GrantsIdentity,
                 "A raw path is an explicit caller designation, so the reader it "
                 + "opens is trusted. This is the route that opens "
                 + "System.Private.CoreLib.dll from a dotnet/runtime build layout."),
            ["Pipeline.MetadataContext.OpenResolved(ResolvedAssemblyReference)"] =
                (TrustRole.GrantsIdentity,
                 "Discovery. Grants only when the acquisition entitles it, through "
                 + "CoreLibraryIdentityTrust.GrantIfEntitled."),
            ["Pipeline.OpenedAssembly.TryOpen(Func`1<Stream>)"] =
                (TrustRole.ObtainsReader,
                 "Obtains the reader but classifies nothing itself. Its callers "
                 + "decide: MetadataContext.OpenDesignated grants the reader this "
                 + "returns, because a raw path is a designation, while OpenResolved "
                 + "defers to provenance. Reaching it any other way leaves the "
                 + "reader unclassified, which fails closed."),
            ["Pipeline.MetadataSource.OpenCore(String, String, Boolean, IAssemblyReferenceResolver, MetadataContext)"] =
                (TrustRole.ObtainsReader | TrustRole.GrantsIdentity,
                 "The raw-path overload. Grants unconditionally, as an explicit "
                 + "caller designation."),
            ["Pipeline.MetadataSource.OpenCore(ResolvedAssemblyReference, String, Boolean, IAssemblyBindingPolicy, MetadataContext)"] =
                (TrustRole.ObtainsReader | TrustRole.GrantsIdentity,
                 "The discovery overload. Defers to GrantIfEntitled, so identity "
                 + "follows the reference's provenance and a planted sibling "
                 + "reached by resolution gets nothing."),
            ["Pipeline.MetadataSource.OpenFromPrefetchedImage(String, ImmutableArray`1<Byte>, String, IAssemblyReferenceResolver, MetadataContext)"] =
                (TrustRole.ObtainsReader | TrustRole.GrantsIdentity,
                 "Designation by path plus caller-supplied bytes; its one product "
                 + "caller, LibrarySections.ScanBodyShapes, passes the designated "
                 + "target's own path and image."),
            ["Pipeline.MetadataSource.PdbReader()"] =
                (TrustRole.ObtainsReader,
                 "A portable PDB reader, embedded or sidecar. Deliberately "
                 + "unclassified: a PDB carries no assembly identity, and this "
                 + "reader is a different instance from the assembly's, so it can "
                 + "never be mistaken for one. Surfaced only once the predicate "
                 + "widened from PEReader construction to reader acquisition, "
                 + "which is the point of that widening."),
            ["MemberBodyProducer.ComposeCore(ApiType, Func`1<ResolvedTypeDefinition>, Func`3<ResolvedTypeDefinition, MetadataContext, MetadataSource>, MetadataContext, PrinterOptions)"] =
                (TrustRole.ObtainsReader,
                 "Deliberately unclassified: opens the assembly a located type came "
                 + "from to read method bodies. This is the sibling path from issue "
                 + "#4411, and it must not mint core-library identity."),
            ["MemberBodyProducer.ComposeMemberCore(ApiType, ApiMember, Func`1<ResolvedTypeDefinition>, Func`3<ResolvedTypeDefinition, MetadataContext, MetadataSource>, MetadataContext, PrinterOptions, MemberRenderAttributeMode)"] =
                (TrustRole.ObtainsReader,
                 "Deliberately unclassified, for the same reason as ComposeCore: a "
                 + "sibling opened to read one member's body."),
            ["MemberBodyProducer.ComposeMembersBatch(ApiType, Func`1<ResolvedTypeDefinition>, Func`3<ResolvedTypeDefinition, MetadataContext, MetadataSource>, MetadataContext, MemberRenderAttributeMode)"] =
                (TrustRole.ObtainsReader,
                 "Deliberately unclassified, for the same reason as ComposeCore: a "
                 + "sibling opened to read a batch of bodies."),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    [Fact]
    public void TrustRelevantSites_MatchThePin()
    {
        var observed = ScanTrustRelevantSites();

        Assert.NotEmpty(observed);

        var expected = s_pinned.ToImmutableDictionary(e => e.Key, e => e.Value.Role);
        var differences = new List<string>();

        foreach (string site in observed.Keys.Union(expected.Keys).Order(StringComparer.Ordinal))
        {
            observed.TryGetValue(site, out TrustRole actual);
            expected.TryGetValue(site, out TrustRole pinned);
            if (actual == pinned)
                continue;

            differences.Add(
                actual == TrustRole.None
                    ? $"  {site}: pinned as {pinned}, but no longer obtains a reader "
                      + "or grants identity. Remove the stale entry."
                    : pinned == TrustRole.None
                        ? $"  {site}: {actual}, but is not pinned. Add it to s_pinned "
                          + "and state whether it may mint core-library identity."
                        : $"  {site}: pinned as {pinned}, observed {actual}.");
        }

        Assert.True(
            differences.Count == 0,
            "The core-library trust surface of ILInspector.Decompiler no longer "
            + "matches its pin. Every method that obtains a MetadataReader or "
            + "grants core-library identity has to be listed, because an "
            + "unreviewed one is an unreviewed way to obtain a reader:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, differences));
    }

    /// <summary>
    /// Non-vacuity gate for <see cref="TrustRelevantSites_MatchThePin"/>. That
    /// test compares a scan against a table, and it would pass just as happily if
    /// the scanner silently observed nothing — a broken token comparison or a
    /// renamed trust type would empty both sides of a set difference. This
    /// asserts the scanner still finds both things it looks for, so the pin
    /// cannot go quietly vacuous.
    /// </summary>
    [Fact]
    public void Scanner_ObservesBothAcquisitionAndGrantSites()
    {
        var observed = ScanTrustRelevantSites();

        Assert.Contains(observed, e => e.Value.HasFlag(TrustRole.ObtainsReader));
        Assert.Contains(observed, e => e.Value.HasFlag(TrustRole.GrantsIdentity));
    }

    /// <summary>
    /// Keeps <see cref="s_nonGrantingTrustMembers"/> honest by deriving the
    /// question from the type instead of restating it. A member added to
    /// <c>CoreLibraryIdentityTrust</c> is grant-relevant to
    /// <see cref="RoleOf"/> until it is named here, and this test says so out
    /// loud rather than letting the omission read as approval. Set equality, so
    /// a stale entry for a deleted member fails too.
    /// </summary>
    /// <remarks>
    /// Keyed by full signature, not by bare name. Round 4 of PR #4469 escaped a
    /// name-keyed version by adding a granting <c>MayMint(MetadataReader)</c>
    /// overload, which silently inherited the exemption belonging to the
    /// unrelated <c>MayMint(AssemblyResolutionProvenance)</c>.
    /// The trust type is also required to declare no nested types, because a
    /// nested helper can reach the private table while its call sites never name
    /// the trust type — the other round-4 escape. <c>.cctor</c> is skipped here
    /// and covered by <see cref="TrustTableAccess_IsConfinedToItsPinnedMembers"/>,
    /// which does not skip it.
    /// </remarks>
    [Fact]
    public void TrustTypeMembers_AreClassified()
    {
        using var stream = File.OpenRead(typeof(MetadataSource).Assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var declared = new HashSet<string>(StringComparer.Ordinal);
        var nested = new List<string>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (FullName(reader, handle) != TrustTypeFullName)
                continue;

            foreach (var nestedHandle in type.GetNestedTypes())
                nested.Add(FullName(reader, nestedHandle));

            foreach (var methodHandle in type.GetMethods())
            {
                var definition = reader.GetMethodDefinition(methodHandle);
                string name = reader.GetString(definition.Name);

                // The static constructor initializes the trust table and cannot be
                // called, so it is not a classification anyone has to make. It is
                // not thereby unexamined: TrustTableAccess_IsConfinedToItsPinnedMembers
                // scans it like any other method, so a .cctor that granted would
                // fail there.
                if (name == ".cctor")
                    continue;

                var signature = definition.DecodeSignature(
                    new SignatureTypeNames(), genericContext: null);
                declared.Add($"{name}({string.Join(", ", signature.ParameterTypes)})");
            }
        }

        Assert.NotEmpty(declared);

        Assert.True(
            nested.Count == 0,
            "CoreLibraryIdentityTrust declares a nested type. A nested type can "
            + "reach the private trust table while none of its call sites name "
            + "the trust type, so the grant would not appear on the trust "
            + "surface at all: "
            + Format(nested));

        var classified = s_grantingTrustMembers.Union(s_nonGrantingTrustMembers);

        Assert.True(
            declared.SetEquals(classified),
            "The members of CoreLibraryIdentityTrust no longer match their "
            + "classification. Every member has to be named as granting or as "
            + "non-granting, because RoleOf treats an unclassified one as a "
            + "grant and the pin will fail until someone decides which it is."
            + Environment.NewLine
            + $"  Declared but unclassified: {Format(declared.Except(classified))}"
            + Environment.NewLine
            + $"  Classified but not declared: {Format(classified.Except(declared))}");

        static string Format(IEnumerable<string> names)
        {
            string joined = string.Join(", ", names.Order(StringComparer.Ordinal));
            return joined.Length == 0 ? "(none)" : joined;
        }
    }

    /// <summary>
    /// Confines the grant primitive. Core-library identity is not conferred by
    /// calling something named like a grant — it is conferred by putting a reader
    /// into <c>s_trusted</c>, and read back by looking one up. So every method in
    /// the assembly whose IL names that field has to be pinned here, whatever it
    /// is called, whatever type declares it, and whether or not it is reachable
    /// through the trust type's public surface.
    /// </summary>
    /// <remarks>
    /// This is the test that makes the gate converge, and round 4 of PR #4469 is
    /// why it exists. Both reviewers escaped a scan that keyed on calls
    /// <em>into</em> <c>CoreLibraryIdentityTrust</c>: a nested
    /// <c>CoreLibraryIdentityTrust.Helper.Grant</c> mutating the table directly
    /// never names the trust type at its call site, and the type's own static
    /// constructor was skipped outright. Sol added a third — a granting
    /// <c>MayMint(MetadataReader)</c> overload, which inherited the exemption of
    /// the unrelated <c>MayMint</c> because the allow list was keyed on the bare
    /// name. Each was a fresh cosmetic dimension on the call surface, which is
    /// the non-convergence issue #4464 exists to end. The field is not a
    /// dimension: trust <em>is</em> membership in that table, so pinning its
    /// referents is complete by construction rather than by enumeration —
    /// for a grant written as ordinary code, which is the bound stated on this
    /// class. Reflection over the field emits no <c>ldsfld</c>, and a
    /// consumer-side grant reaches the table not at all;
    /// <c>PlantedCoreLibraryIdentityTests</c> owns both.
    /// </remarks>
    [Fact]
    public void TrustTableAccess_IsConfinedToItsPinnedMembers()
    {
        var observed = EnumerateMethods(typeof(MetadataSource).Assembly.Location)
            .Where(e => e.Method.ReachesTrustTable)
            .Select(e => e.Key)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            observed.SetEquals(s_trustTableAccessors),
            "The set of methods that touch the core-library trust table has "
            + "changed. Granting identity means putting a reader into that "
            + "table, so anything able to name the field can confer trust no "
            + "matter what it is called or where it is declared."
            + Environment.NewLine
            + $"  Touches the table but is not pinned: {Format(observed.Except(s_trustTableAccessors))}"
            + Environment.NewLine
            + $"  Pinned but no longer touches it: {Format(s_trustTableAccessors.Except(observed))}");

        static string Format(IEnumerable<string> keys)
        {
            string joined = string.Join(", ", keys.Order(StringComparer.Ordinal));
            return joined.Length == 0 ? "(none)" : joined;
        }
    }

    /// <summary>
    /// Guards the property that makes the pin an identity rather than a label:
    /// two distinct methods must never collapse onto one key. Review of PR #4469
    /// escaped an earlier version twice this way — by adding an overload, and by
    /// adding a local function whose lowered name was unmangled back to its
    /// enclosing method — with the new method silently inheriting the pinned
    /// approval of the one it collided with.
    /// </summary>
    [Fact]
    public void SiteKeys_AreUniquePerMethod()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (_, key) in EnumerateMethods(typeof(MetadataSource).Assembly.Location))
            counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;

        var collisions = counts
            .Where(e => e.Value > 1)
            .Select(e => $"  {e.Key} ({e.Value} methods)")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            collisions.Count == 0,
            "Distinct methods share a site key, so one could inherit another's "
            + "pinned approval:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, collisions));
    }

    /// <summary>
    /// Arms both halves of the acquisition predicate. Round 2 of PR #4469 found
    /// that the direct-construction branch asserted nothing: no production site
    /// constructs a <see cref="MetadataReader"/> from a pointer, so deleting that
    /// branch left every test green even though it is the branch that closes the
    /// unsafe-constructor escape. These specimens are never called; they exist so
    /// the compiled IL of this test assembly contains one instance of each shape,
    /// and <see cref="Scanner_DetectsBothAcquisitionShapes"/> fails if the
    /// scanner stops recognising either.
    /// </summary>
    static class AcquisitionSpecimens
    {
        internal static MetadataReader ThroughGetMetadataReader(ImmutableArray<byte> image)
            => MetadataReaderProvider.FromMetadataImage(image).GetMetadataReader();

        internal static unsafe MetadataReader ThroughConstructor(byte* metadata, int length)
            => new(metadata, length);
    }

    /// <summary>
    /// Non-vacuity gate for the acquisition predicate itself, as distinct from
    /// <see cref="Scanner_ObservesBothAcquisitionAndGrantSites"/>, which only
    /// proves the scan of the product assembly is not empty. Deleting either
    /// branch of the predicate fails this test.
    /// </summary>
    [Fact]
    public void Scanner_DetectsBothAcquisitionShapes()
    {
        const string SpecimenType = "Tests.ReaderConstructionSiteTests+AcquisitionSpecimens";

        var observed = EnumerateMethods(typeof(ReaderConstructionSiteTests).Assembly.Location)
            .Where(e => e.Key.StartsWith($"{SpecimenType}.", StringComparison.Ordinal))
            .ToDictionary(e => e.Key, e => e.Method.Role, StringComparer.Ordinal);

        Assert.Equal(
            TrustRole.ObtainsReader,
            observed[$"{SpecimenType}.ThroughGetMetadataReader(ImmutableArray`1<Byte>)"]);

        Assert.Equal(
            TrustRole.ObtainsReader,
            observed[$"{SpecimenType}.ThroughConstructor(Byte*, Int32)"]);
    }

    /// <summary>
    /// Reads the compiled <c>ILInspector.Decompiler</c> assembly and reports every
    /// method whose IL obtains a <see cref="MetadataReader"/> or calls into
    /// <c>CoreLibraryIdentityTrust</c> to grant identity.
    /// </summary>
    static ImmutableDictionary<string, TrustRole> ScanTrustRelevantSites()
    {
        var sites = new Dictionary<string, TrustRole>(StringComparer.Ordinal);

        foreach (var (method, key) in EnumerateMethods(typeof(MetadataSource).Assembly.Location))
        {
            if (method.Role == TrustRole.None || method.DeclaringTypeFullName == TrustTypeFullName)
                continue;

            Assert.False(
                sites.ContainsKey(key),
                $"Two trust-relevant methods share the site key '{key}'.");

            sites[key] = method.Role;
        }

        return sites.ToImmutableDictionary(StringComparer.Ordinal);
    }

    readonly record struct ScannedMethod(
        TrustRole Role,
        string DeclaringTypeFullName,
        bool ReachesTrustTable);

    static IEnumerable<(ScannedMethod Method, string Key)> EnumerateMethods(string assemblyPath)
    {
        Assert.True(
            File.Exists(assemblyPath),
            $"Cannot scan the decompiler assembly at '{assemblyPath}'.");

        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            byte[] il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() ?? [];
            if (il.Length == 0)
                continue;

            string declaringType = FullName(reader, method.GetDeclaringType());
            string key = SiteKey(reader, method, declaringType);
            yield return (
                new ScannedMethod(
                    RoleOf(reader, il),
                    declaringType,
                    ReachesTrustTable(reader, il, key)),
                key);
        }
    }

    /// <summary>
    /// Whether this method can reach the contents of the trust table. Loading the
    /// field, its address, or its token all count, because mutating the table
    /// requires getting hold of it first. Storing the field is initialization
    /// rather than reach, and is allowed only in the static constructor that
    /// creates the table — anywhere else, replacing the table wholesale would be
    /// a way to install a pre-populated one.
    /// </summary>
    static bool ReachesTrustTable(MetadataReader reader, byte[] il, string siteKey)
    {
        bool loads = false;
        bool stores = false;

        foreach (var instruction in InstructionDecoder.Decode(il))
        {
            if (instruction.Operand is not (OperandKind.InlineField or OperandKind.InlineTok))
                continue;

            var operand = MetadataTokens.EntityHandle((int)instruction.OperandValue);
            if (!TryDescribeField(reader, operand, out string declaringType, out string field))
                continue;

            if (declaringType != TrustTypeFullName || field != TrustTableFieldName)
                continue;

            if (instruction.OpCode is ILOpCode.Stsfld or ILOpCode.Stfld)
                stores = true;
            else
                loads = true;
        }

        return loads || (stores && siteKey != TrustTableInitializerKey);
    }

    static bool TryDescribeField(
        MetadataReader reader,
        EntityHandle handle,
        out string declaringType,
        out string field)
    {
        declaringType = "";
        field = "";

        switch (handle.Kind)
        {
            case HandleKind.FieldDefinition:
            {
                var definition = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                field = reader.GetString(definition.Name);
                declaringType = FullName(reader, definition.GetDeclaringType());
                return true;
            }
            case HandleKind.MemberReference:
            {
                var reference = reader.GetMemberReference((MemberReferenceHandle)handle);
                if (reference.GetKind() != MemberReferenceKind.Field)
                    return false;

                field = reader.GetString(reference.Name);
                declaringType = TypeNameOf(reader, reference.Parent);
                return true;
            }
            default:
                return false;
        }
    }

    static TrustRole RoleOf(MetadataReader reader, byte[] il)
    {
        TrustRole role = TrustRole.None;

        foreach (var instruction in InstructionDecoder.Decode(il))
        {
            if (instruction.Operand is not (OperandKind.InlineMethod or OperandKind.InlineTok))
                continue;

            var operand = MetadataTokens.EntityHandle((int)instruction.OperandValue);
            if (!TryDescribeMember(reader, operand, out string declaringType, out string member))
                continue;

            // Trust attaches to a MetadataReader instance, so key on the ways one
            // can be obtained rather than on PEReader, which is merely the usual
            // container. Review of PR #4469 escaped a PEReader-only scan through
            // MetadataReaderProvider and through the unsafe MetadataReader
            // constructor, neither of which constructs a PEReader.
            if (MemberName(member) == "GetMetadataReader")
                role |= TrustRole.ObtainsReader;

            if (instruction.OpCode == ILOpCode.Newobj
                && declaringType == "System.Reflection.Metadata.MetadataReader")
            {
                role |= TrustRole.ObtainsReader;
            }

            if (declaringType == TrustTypeFullName
                && !s_nonGrantingTrustMembers.Contains(member))
            {
                role |= TrustRole.GrantsIdentity;
            }
        }

        return role;
    }

    /// <summary>
    /// The bare name of a signature-qualified member key.
    /// </summary>
    static string MemberName(string member)
    {
        int paren = member.IndexOf('(', StringComparison.Ordinal);
        return paren < 0 ? member : member[..paren];
    }

    static bool TryDescribeMember(
        MetadataReader reader,
        EntityHandle handle,
        out string declaringType,
        out string member)
    {
        declaringType = "";
        member = "";

        switch (handle.Kind)
        {
            case HandleKind.MemberReference:
            {
                var reference = reader.GetMemberReference((MemberReferenceHandle)handle);
                member = reader.GetString(reference.Name);
                if (reference.GetKind() == MemberReferenceKind.Method)
                {
                    var signature = reference.DecodeMethodSignature(
                        new SignatureTypeNames(), genericContext: null);
                    member += $"({string.Join(", ", signature.ParameterTypes)})";
                }

                declaringType = TypeNameOf(reader, reference.Parent);
                return true;
            }
            case HandleKind.MethodDefinition:
            {
                var definition = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var signature = definition.DecodeSignature(
                    new SignatureTypeNames(), genericContext: null);
                member = reader.GetString(definition.Name)
                    + $"({string.Join(", ", signature.ParameterTypes)})";
                declaringType = FullName(reader, definition.GetDeclaringType());
                return true;
            }
            case HandleKind.MethodSpecification:
            {
                var specification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                return TryDescribeMember(reader, specification.Method, out declaringType, out member);
            }
            default:
                return false;
        }
    }

    static string TypeNameOf(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
            {
                var reference = reader.GetTypeReference((TypeReferenceHandle)handle);
                return Join(reader.GetString(reference.Namespace), reader.GetString(reference.Name));
            }
            case HandleKind.TypeDefinition:
                return FullName(reader, (TypeDefinitionHandle)handle);
            default:
                return "";
        }
    }

    static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        return type.IsNested
            ? $"{FullName(reader, type.GetDeclaringType())}+{name}"
            : Join(reader.GetString(type.Namespace), name);
    }

    static string Join(string @namespace, string name) =>
        string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";

    /// <summary>
    /// Identifies a site by declaring type, method name, and parameter types,
    /// with the root namespace trimmed for readability. The parameter list is
    /// what keeps overloads distinct, and
    /// <see cref="SiteKeys_AreUniquePerMethod"/> is the gate on that.
    /// Compiler-generated names are reported verbatim, so a local function or
    /// lambda that obtains a reader becomes its own site rather than inheriting
    /// the approval of the method it was lowered from. That makes the key
    /// sensitive to compiler-assigned ordinals, which is the intended trade: a
    /// trust-relevant site should not be compiler-generated, and if one ever is,
    /// it deserves the explicit pin and the review that churn forces.
    /// </summary>
    static string SiteKey(MetadataReader reader, MethodDefinition method, string declaringType)
    {
        const string RootNamespace = "ILInspector.Decompiler.";
        string type = declaringType.StartsWith(RootNamespace, StringComparison.Ordinal)
            ? declaringType[RootNamespace.Length..]
            : declaringType;

        var signature = method.DecodeSignature(SignatureTypeNames.Instance, genericContext: null);
        return $"{type}.{reader.GetString(method.Name)}({string.Join(", ", signature.ParameterTypes)})";
    }

    /// <summary>
    /// Spells signature types by simple name — enough to separate overloads in a
    /// site key and to stay readable in a failure message.
    /// </summary>
    sealed class SignatureTypeNames : ISignatureTypeProvider<string, object?>
    {
        internal static readonly SignatureTypeNames Instance = new();

        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[]";

        public string GetByReferenceType(string elementType) => $"{elementType}&";

        public string GetFunctionPointerType(MethodSignature<string> signature) => "method";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => $"{genericType}<{string.Join(", ", typeArguments)}>";

        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;

        public string GetPointerType(string elementType) => $"{elementType}*";

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSZArrayType(string elementType) => $"{elementType}[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
