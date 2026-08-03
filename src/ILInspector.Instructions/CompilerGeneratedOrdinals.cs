using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;

using ILInspector.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// A two-sided correspondence over Roslyn compiler-generated members whose mangled
/// names embed a containing-type <em>member</em> ordinal.
/// </summary>
/// <remarks>
/// <para>
/// Roslyn names a local function <c>&lt;M&gt;g__L|N_K</c>, a lambda
/// <c>&lt;M&gt;b__N_K</c>, and an iterator or async state machine
/// <c>&lt;M&gt;d__N</c>, where <c>N</c> is the ordinal of a member of the
/// containing type. Because <c>N</c> counts members — fields and properties and nested
/// types included — any change to the containing type's member population renumbers it.
/// A comparison that reconstructs one side's type from a different member population
/// therefore sees a different <c>N</c> for the same construct, and reports an operand
/// difference for two bodies that are otherwise identical.
/// </para>
/// <para>
/// The correspondence is computed from <b>both</b> readers together and is deliberately
/// not expressible as a per-side rewrite. A per-side eligibility test is resolver-local:
/// a member that is unique on one side but ambiguous on the other would fold on one side
/// only, turning an exact comparison into an operand difference — the comparison would
/// <i>manufacture</i> a difference that neither assembly contains. Requiring a key to
/// resolve to exactly one member on each side makes folding symmetric, so the worst case
/// is that a pair does not fold and the original mangled names are compared, which is
/// precisely the un-normalized behavior.
/// </para>
/// <para>
/// That symmetry property is enforced by four controls, each tamper-verified against the
/// deletion of the individual check it covers:
/// <c>UniqueAgainstAmbiguous_DoesNotFoldOntoAnArbitraryCounterpart</c> and its mirror
/// <c>AmbiguousAgainstUnique_DoesNotFoldOntoAnArbitraryCounterpart</c> discriminate the
/// two sides — they use a candidate whose sides share <em>no</em> ordinal, so consulting
/// the wrong side's index yields a visibly wrong counterpart rather than the same answer.
/// <c>UniqueAgainstAmbiguous_DoesNotManufactureADifference</c> and its mirror pin the
/// user-visible outcome. Deleting <em>either</em> ambiguity check alone fails one of the
/// first two; the earlier controls did not discriminate, because a first-seen-wins index
/// returns the same handle from either side when the sides share their first ordinal.
/// </para>
/// <para>
/// Eligibility is gated on <c>CompilerGeneratedAttribute</c> rather than on name shape.
/// The mangled forms are unspellable in C# but not in IL, so an untrusted assembly can
/// declare a type literally named <c>&lt;Foo&gt;d__5</c>; without the attribute check such
/// a type would be folded together with an unrelated one. Local-function methods and
/// state-machine and display-class types carry the attribute directly. Lambda methods
/// do not: Roslyn marks their containing <c>&lt;&gt;c</c> and leaves them unmarked, so
/// eligibility accepts a member whose <em>declaring type</em> carries the attribute —
/// see <c>TryEligibleName</c>, whose remarks give the measurement and the two controls
/// that hold both directions of it. The method-side rule is
/// enforced by <c>NameShapeAlone_DoesNotFold</c>, which declares a <em>method</em> with a
/// hand-written mangled name and no attribute and asserts it keeps its ordinal;
/// <c>TypeNameShapeAlone_DoesNotFold</c> asserts the same outcome for a type, though it
/// pins the outcome rather than a single call site, for the reason its own remarks give.
/// </para>
/// <para>
/// <b>That gate raises the cost of a collision; it is not a security boundary.</b>
/// <c>CompilerGeneratedAttribute</c> is publicly applicable, and an assembly may declare
/// its own — the assembly that owns the framework definition necessarily does, which is
/// why a <c>MethodDefinition</c> constructor is accepted here. So an assembly that has
/// been built to do so can present a hand-written member as generated. The residue is
/// bounded rather than open: folding additionally requires the containing-method name,
/// the local-function name, and the slot ordinal to agree, and requires the key to be
/// unique on <em>both</em> sides, so a forged attribute cannot equate members that differ
/// in anything but the member ordinal. Do not read this gate as authenticating provenance.
/// </para>
/// <para>
/// That bound is only real because the key's flattening is injective. When the separator
/// was spellable it was not, and a forged attribute could equate members of two different
/// types. The gate for this paragraph's claim is
/// <c>KeySeparatorCannotBeSpelledByAMetadataName</c>, which drives the assertion from
/// <see cref="KeySeparator"/> itself and so fails for <em>any</em> spellable separator.
/// <c>ForgedKeySegmentation_DoesNotFoldAcrossDeclaringTypes</c> exhibits the concrete
/// historical shape but does not gate the general claim: its forged name is written
/// against the old <c>.</c>/<c>+</c>/<c>::</c> joining, so changing the separator to an
/// arbitrary spellable character leaves it passing. Measured, not assumed — with the
/// separator set to <c>'@'</c>, <c>KeySeparatorCannotBeSpelledByAMetadataName</c> is the
/// only test in this assembly that fails.
/// </para>
/// <para>
/// Anonymous shapes (<c>&lt;&gt;c__DisplayClassN_K</c>, <c>&lt;&gt;9__N_K</c>) are excluded:
/// they carry no containing-method name, so <c>N</c> is their only discriminator and an
/// ordinal-free key would collide across unrelated closures. Note this is a statement
/// about the <em>key</em>, not about safety — a colliding key is ambiguous and therefore
/// refused, so the cost is a missed fold. The lambda form <c>&lt;M&gt;b__N_K</c> is not
/// anonymous in this sense and <em>is</em> owned: it carries its containing method's
/// name, so its ordinal-free key discriminates exactly as the local-function form's does.
/// Enforced by <c>AnonymousShapes_NeverFold</c>, with <c>LambdaShape_Folds</c> pinning
/// the distinction from the other side.
/// </para>
/// <para>
/// <b>Known gap — generic declaring types do not fold.</b> The correspondence is keyed on
/// <see cref="MethodDefinitionHandle"/> and <see cref="TypeDefinitionHandle"/>, so it is
/// consulted only where an operand resolves to a definition in this assembly. A member of
/// a <em>generic</em> type is referenced through a <c>MemberReference</c> whose parent is a
/// <c>TypeSpecification</c>, and the instantiated type name is produced by the signature
/// decoder rather than by definition formatting. Both paths bypass this correspondence, so
/// <c>C&lt;T&gt;</c>'s local functions and state machines still compare with their ordinals
/// intact. Measured, not inferred: a local function in a generic type still reports
/// <c>call ... C`1&lt;!0&gt;::&lt;M&gt;g__L|0_0</c> against <c>|3_0</c>, and a generic
/// iterator still reports <c>newobj ... C`1+&lt;Iter&gt;d__1&lt;!0&gt;</c> against
/// <c>d__4</c>, with this normalization enabled. This is an incompleteness, not an
/// unsoundness — such a pair simply does not fold, which is the un-normalized behavior.
/// <b>It is deliberately not covered by a test here</b>, because the synthetic images this
/// assembly's controls build are non-generic; treating the absence of a failing control as
/// evidence of coverage would be wrong. Tracked by issue #3583, whose acceptance
/// criteria include the generic fixture this assembly cannot currently build.
/// </para>
/// <para>
/// <b>Three branches in the type-key construction are likewise unverified.</b> The null
/// prefix returned when the declaring chain cannot be walked, the <c>consumed == 0</c>
/// rejection, and the placement of the namespace on the outermost segment only all need a
/// <em>nested</em> type — and, for the first two, one nested more deeply than
/// <c>MetadataSafetyPolicy.MaxRelationshipNodes</c> allows. The synthetic images here
/// declare only top-level types, so every control passes through those branches
/// identically whether or not they are present. Tracked by issue #3588. Accepting an
/// empty prefix in place of the null one would be a soundness loss, not a completeness
/// one: two types whose chains could not be walked would key alike.
/// </para>
/// <para>
/// <b>Three further branches are unverified because another branch masks them, not
/// because no fixture reaches them.</b> Type eligibility is computed once when indexing a
/// type and again when that type appears in a key prefix, and the second call keeps an
/// unattributed name raw on its own; the cached enclosing-type elision is duplicated by
/// the fallback beside it; and the constructor-kind default arm needs an attribute
/// constructor that is neither a member reference nor a method definition. Each is
/// redundant with a covered branch <em>today</em>, so deleting one changes nothing
/// observable — which is exactly why a later edit could remove the survivor without any
/// control noticing. Distinguishing the first two needs the same nested-type fixture as
/// the branches above; all three are tracked by issue #3588.
/// </para>
/// </remarks>
public sealed class CompilerGeneratedOrdinalCorrespondence
{
    /// <summary>The identity correspondence: nothing folds.</summary>
    public static readonly CompilerGeneratedOrdinalCorrespondence Empty =
        new(new Dictionary<MethodDefinitionHandle, string>(), new Dictionary<TypeDefinitionHandle, string>());

    /// <summary>
    /// The separator between key segments. It is NUL for the same reason the ordinal
    /// placeholder ends in one: no metadata name can contain it.
    /// </summary>
    /// <remarks>
    /// A key is a sequence of segments — namespace, each enclosing type, the member name —
    /// flattened to a string. With a spellable separator that flattening is not injective:
    /// a method named <c>&lt;M&gt;g__L::&lt;N&gt;g__X|3_0</c> on type <c>C</c> and a method
    /// named <c>&lt;N&gt;g__X|7_0</c> on a type named <c>C::&lt;M&gt;g__L</c> produce the
    /// same key from different segmentations. Both are unique on their own side, so both
    /// pass the two-sided ambiguity check and fold onto each other — and because the
    /// rendered operand concatenates the same way, the two genuinely different targets
    /// render identically and a real difference is hidden. Separating with NUL makes the
    /// flattening injective, because no segment can contain the separator. That general
    /// property is pinned by <c>KeySeparatorCannotBeSpelledByAMetadataName</c>, which
    /// drives its assertion from this constant and so fails for any spellable value.
    /// <c>ForgedKeySegmentation_DoesNotFoldAcrossDeclaringTypes</c> pins only the concrete
    /// historical <c>.</c>/<c>+</c>/<c>::</c> shape and does not fire for an arbitrary
    /// spellable separator.
    /// <para>
    /// Keys are never rendered — only <c>MethodNames</c> and <c>TypeNames</c> reach the
    /// compared text — so this costs nothing in output.
    /// </para>
    /// </remarks>
    internal const char KeySeparator = '\0';

    /// <summary>
    /// The text standing in for an elided member ordinal. It ends in NUL, which no
    /// metadata name can contain.
    /// </summary>
    /// <remarks>
    /// The elided form is substituted into the compared operand text, so it shares a
    /// namespace with every name that text can contain — not only the type and method
    /// definitions this correspondence indexes, but member references, type references,
    /// fields, and anything a future operand formatter renders. Enumerating those and
    /// declining to fold on a match is a guess at a list; a member literally named
    /// <c>&lt;M&gt;g__L|#_0</c> is unspellable in C# but legal in metadata, and one that
    /// the list missed would render identically to a folded <c>&lt;M&gt;g__L|3_0</c>, so a
    /// body that changed which of the two it calls would read as unchanged.
    /// <para>
    /// Ending the placeholder in NUL removes the list. Names reach the compared text
    /// through <see cref="MetadataReader.GetString(StringHandle)"/>, and the
    /// <c>#Strings</c> heap is NUL-terminated, so a name read back can never contain NUL
    /// however the assembly was written — a type emitted as <c>A\0B</c> reads back as
    /// <c>A</c>. The elided form therefore cannot equal any name, and no enumeration has
    /// to be kept complete. This holds for the default string decoder; see the precondition
    /// on <see cref="Build"/> for the one way a caller can defeat it. Pinned by
    /// <c>PlaceholderCannotBeSpelledByAMetadataName</c>, which derives its assertion from
    /// this constant so a spellable placeholder fails it, with the attack it defeats pinned
    /// by <c>PlaceholderCollidingName_DoesNotHideARealTargetChange</c> and its member
    /// reference and type-name siblings.
    /// </para>
    /// <para>
    /// The argument rests on the compared text being compared <em>ordinally</em>, and that
    /// is a real dependency rather than a formality: NUL is collation-ignorable under ICU,
    /// so <c>string.Compare</c>, <c>IndexOf</c>, <c>StartsWith</c> and <c>EndsWith</c> —
    /// all culture-sensitive by default — report <c>&lt;M&gt;g__L|#_0</c> and
    /// <c>&lt;M&gt;g__L|#\0_0</c> as equal. A single culture-sensitive comparison anywhere
    /// on this path would restore the collision the placeholder exists to prevent. The
    /// comparison that matters is <c>IlBodyDiff.CanonicalEquals</c>, which compares
    /// <see cref="IlOperandIdentity"/> by record equality, and the key lookups here, which
    /// use <see cref="StringComparer.Ordinal"/> explicitly. Making
    /// <c>CanonicalEquals</c> culture-sensitive fails exactly two controls:
    /// <c>CollationCollidingName_DoesNotHideARealTargetChange</c>, which forges the
    /// NUL-free name <c>&lt;M&gt;g__L|#_0</c> a hostile assembly can actually spell, and
    /// <c>PlaceholderCollidingTypeName_DoesNotHideARealTargetChange</c>, whose placeholder
    /// is last in the name so heap truncation happens to leave the collating prefix. The
    /// two NUL-bearing <em>method</em> controls cannot gate this: the heap truncates their
    /// forgeries to <c>&lt;M&gt;g__L|#</c>, which is not collation-equal to the elided
    /// <c>&lt;M&gt;g__L|#\0_0</c> either.
    /// </para>
    /// <para>
    /// The NUL is invisible where a folded name reaches output, which happens only when a
    /// row differs for some other reason; the visible <c>#</c> is what a reader sees.
    /// </para>
    /// </remarks>
    internal const string OrdinalPlaceholder = "#\0";

    readonly Dictionary<MethodDefinitionHandle, string> _methods;
    readonly Dictionary<TypeDefinitionHandle, string> _types;

    CompilerGeneratedOrdinalCorrespondence(
        Dictionary<MethodDefinitionHandle, string> methods,
        Dictionary<TypeDefinitionHandle, string> types)
    {
        _methods = methods;
        _types = types;
    }

    /// <summary>The ordinal-free name to compare this method under, when it has one.</summary>
    public bool TryGetMethodName(MethodDefinitionHandle handle, out string name)
        => _methods.TryGetValue(handle, out name!);

    /// <summary>The ordinal-free name to compare this type under, when it has one.</summary>
    public bool TryGetTypeName(TypeDefinitionHandle handle, out string name)
        => _types.TryGetValue(handle, out name!);

    /// <summary>
    /// Builds the correspondence for each side. A member folds only when its ordinal-free
    /// key resolves to exactly one eligible member on <b>both</b> sides.
    /// </summary>
    /// <remarks>
    /// The safety of <see cref="OrdinalPlaceholder"/> and <see cref="KeySeparator"/> rests
    /// on a name never containing NUL, which follows from the <c>#Strings</c> heap being
    /// NUL-terminated — but only for the default string decoder. A custom
    /// <c>MetadataStringDecoder</c> may return whatever it likes, including a name that
    /// spells the elided form, which would let a raw name impersonate a folded one.
    /// <para>
    /// Rather than assume the precondition, this checks it: a reader carrying any other
    /// decoder folds nothing. The decoder is chosen by the caller constructing the reader
    /// and never by the assembly being read, so this is not a defense against untrusted
    /// input — it is what keeps the NUL argument true for every caller, including callers
    /// outside this repository, instead of only for this repository's own call sites.
    /// Gated by <c>NonDefaultStringDecoder_FoldsNothing</c>, with
    /// <c>AStringDecoderCanReturnANameContainingNul</c> pinning that the hazard is real
    /// and <c>DefaultStringDecoder_StillFolds</c> pinning that the check does not simply
    /// disable folding.
    /// </para>
    /// </remarks>
    public static (CompilerGeneratedOrdinalCorrespondence Old, CompilerGeneratedOrdinalCorrespondence New) Build(
        MetadataReader oldReader,
        MetadataReader newReader)
    {
        ArgumentNullException.ThrowIfNull(oldReader);
        ArgumentNullException.ThrowIfNull(newReader);

        // The NUL unspellability argument holds for the default decoder only. Fold nothing
        // rather than fold unsoundly.
        if (!ReferenceEquals(oldReader.UTF8Decoder, MetadataStringDecoder.DefaultUTF8)
            || !ReferenceEquals(newReader.UTF8Decoder, MetadataStringDecoder.DefaultUTF8))
        {
            return (Empty, Empty);
        }

        var oldIndex = SideIndex.For(oldReader);
        var newIndex = SideIndex.For(newReader);
        if (oldIndex.IsEmpty || newIndex.IsEmpty)
            return (Empty, Empty);

        var oldMethods = new Dictionary<MethodDefinitionHandle, string>();
        var newMethods = new Dictionary<MethodDefinitionHandle, string>();
        foreach (var (key, handle) in oldIndex.Methods)
        {
            if (oldIndex.AmbiguousMethods.Contains(key)
                || newIndex.AmbiguousMethods.Contains(key)
                || !newIndex.Methods.TryGetValue(key, out var counterpart))
            {
                continue;
            }

            oldMethods[handle] = oldIndex.MethodNames[handle];
            newMethods[counterpart] = newIndex.MethodNames[counterpart];
        }

        var oldTypes = new Dictionary<TypeDefinitionHandle, string>();
        var newTypes = new Dictionary<TypeDefinitionHandle, string>();
        foreach (var (key, handle) in oldIndex.Types)
        {
            if (oldIndex.AmbiguousTypes.Contains(key)
                || newIndex.AmbiguousTypes.Contains(key)
                || !newIndex.Types.TryGetValue(key, out var counterpart))
            {
                continue;
            }

            oldTypes[handle] = oldIndex.TypeNames[handle];
            newTypes[counterpart] = newIndex.TypeNames[counterpart];
        }

        if (oldMethods.Count == 0 && oldTypes.Count == 0)
            return (Empty, Empty);

        return (new CompilerGeneratedOrdinalCorrespondence(oldMethods, oldTypes),
                new CompilerGeneratedOrdinalCorrespondence(newMethods, newTypes));
    }

    /// <summary>
    /// Rewrites a Roslyn mangled name to its ordinal-free form, or returns null when the
    /// name is not one of the recognized ordinal-bearing shapes.
    /// </summary>
    /// <summary>
    /// Finds the <c>&gt;</c> that closes the name's leading <c>&lt;</c>, matching nesting
    /// rather than taking the first one.
    /// </summary>
    /// <remarks>
    /// The containing name in a generated name may itself be generated, so the first
    /// <c>&gt;</c> can close an inner name instead of the containing one. Taking it
    /// splits the name in the wrong place and this method then fails to recognize a form
    /// it owns: <c>&lt;&lt;Run&gt;b__0_0&gt;g__Local|0_0</c> parsed at the first angle
    /// yields a remainder of <c>b__0_0&gt;g__Local|0_0</c>, which matches neither
    /// <c>d__</c> nor <c>g__</c>, so a local function the correspondence is responsible
    /// for is disowned. Two consequences, both bad: the correspondence stops folding a
    /// name it should fold, and — because IlBodyDiff asks this same predicate which names
    /// are the correspondence's — the per-side rewrite is handed a name it should never
    /// have been handed and folds it on weaker evidence.
    ///
    /// Found by adversarial review (round 12); gated by
    /// GeneratedNameWhoseContainingNameIsItselfGenerated_IsStillOwned and, end-to-end,
    /// by CompilerGeneratedCorrespondence_KeepsTheRewriteOffAnOwnedNameNestedInsideAnother.
    /// The scan is a single left-to-right pass over the name, so a hostile name cannot
    /// amplify it (see docs/design/untrusted-data-threat-model.md).
    /// </remarks>
    static int FindClosingAngle(string name)
    {
        int depth = 0;
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '<')
            {
                depth++;
            }
            else if (name[i] == '>' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Which metadata entity a generated name was read from.
    /// </summary>
    /// <remarks>
    /// Each owned form belongs to exactly one kind: Roslyn emits
    /// <c>&lt;M&gt;d__N</c> as a state-machine <em>type</em>, and both
    /// <c>&lt;M&gt;g__L|N_K</c> and <c>&lt;M&gt;b__N_K</c> as <em>methods</em>
    /// — a local function and a lambda respectively. A name carrying one form
    /// on the other kind is not a shape any compiler produces, so nothing
    /// relates the two sides' ordinals and folding them would mask a real
    /// difference. This mirrors the rule
    /// <see cref="IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals"/>
    /// already applies to non-canonical ordinals.
    /// </remarks>
    internal enum GeneratedNameKind
    {
        /// <summary>A name read from a type definition.</summary>
        Type,

        /// <summary>A name read from a method definition or reference.</summary>
        Method,

        /// <summary>
        /// Either kind. Used only by the ownership guard, which asks whether a
        /// name belongs to this correspondence at all so the per-side rewrite
        /// can keep off it. Answering broadly there costs at most a false
        /// positive, while answering narrowly would hand an owned form to a
        /// weaker folder.
        /// </summary>
        Any,
    }

    internal static string? TryElideOrdinal(string name, GeneratedNameKind kind)
    {
        if (name.Length < 4 || name[0] != '<')
            return null;

        // A generated name folds only when it names the construct it belongs to. The
        // anonymous shapes — `<>c`, `<>c__DisplayClassN_K`, `<>9__N_K` — open with an
        // empty pair of brackets, so their ordinal is their only discriminator and
        // eliding it would merge unrelated closures. Depth matching still reports 1 for
        // those, so they are rejected here exactly as before.
        int close = FindClosingAngle(name);
        if (close <= 1)
            return null;

        var containing = name.AsSpan(0, close + 1);
        var rest = name.AsSpan(close + 1);

        if (rest.StartsWith("d__", StringComparison.Ordinal))
        {
            return kind != GeneratedNameKind.Method && IsCanonicalOrdinal(rest[3..])
                ? $"{containing}d__{OrdinalPlaceholder}"
                : null;
        }

        if (kind == GeneratedNameKind.Type)
            return null;

        // The lambda form `<M>b__N_K` differs from the local-function form only in
        // carrying no local name, so it has no separator and its ordinals start
        // immediately. Both are methods, and both spell `N` as the containing type's
        // member index, which the harness's rebuilt type skeleton renumbers.
        if (rest.StartsWith("b__", StringComparison.Ordinal))
        {
            return ElideScopeOrdinal(rest[3..]) is { } lambdaOrdinals
                ? $"{containing}b__{lambdaOrdinals}"
                : null;
        }

        if (!rest.StartsWith("g__", StringComparison.Ordinal))
            return null;

        // Split at the last separator, not the first. Roslyn emits exactly one — a local
        // function nested in another local function is still named after the *outermost*
        // method, so `<M>g__Inner|0_1`, not `<M>g__Outer|Inner|0_1`. Measured against the
        // compiler rather than assumed: see MultiplePipesInAGeneratedName_SplitAtTheLast,
        // which pins this choice for a hand-written IL name that does carry two.
        int bar = rest.LastIndexOf('|');
        if (bar < 3)
            return null;

        var local = rest[3..bar];
        if (local.IsEmpty)
            return null;

        return ElideScopeOrdinal(rest[(bar + 1)..]) is { } localOrdinals
            ? $"{containing}g__{local}|{localOrdinals}"
            : null;
    }

    /// <summary>
    /// Elides the scope ordinal from the <c>N_K</c> tail both method forms end in,
    /// yielding <c>#_K</c>, or returns null when the tail is not that shape.
    /// </summary>
    /// <remarks>
    /// The scope ordinal <c>N</c> is the containing type's member index, which the
    /// harness's rebuilt type skeleton renumbers and which is therefore not evidence.
    /// The slot ordinal <c>K</c> distinguishes closures within one containing method
    /// and is preserved, so two lambdas of the same method still differ.
    /// <para>
    /// Shared by the lambda and local-function forms because their tails are the same
    /// grammar; keeping one parser keeps the two from drifting apart, which would show
    /// up as one form accepting an ordinal the other rejects. Gated on the lambda side
    /// by <c>LambdaOrdinalTails_AreHeldToTheCanonicalShape</c>.
    /// </para>
    /// </remarks>
    static string? ElideScopeOrdinal(ReadOnlySpan<char> ordinals)
    {
        int underscore = ordinals.IndexOf('_');
        if (underscore <= 0)
            return null;

        var scope = ordinals[..underscore];
        var slot = ordinals[(underscore + 1)..];
        return IsCanonicalOrdinal(scope) && IsCanonicalOrdinal(slot)
            ? $"{OrdinalPlaceholder}_{slot}"
            : null;
    }

    /// <summary>
    /// Reports whether <paramref name="value"/> is an ordinal exactly as Roslyn
    /// spells one.
    /// </summary>
    /// <remarks>
    /// Roslyn formats these indices with an invariant <see cref="int"/>
    /// conversion, so <c>01</c> and a value past <see cref="int.MaxValue"/> are
    /// not forms it emits. Accepting them would let <c>&lt;M&gt;d__01</c> and
    /// <c>&lt;M&gt;d__1</c> key alike and fold, which is a masked difference
    /// between two names no compiler produced and that nothing else relates.
    /// Requiring the canonical encoding costs at most a false positive on such
    /// a name, which is the safe direction.
    /// <para>
    /// This is deliberately the same rule the per-side rewrite applies in
    /// <c>IlBodyDiff.SynthesizedOrdinals</c>. The two mechanisms partition the
    /// generated name space, so a name either mechanism would refuse must not
    /// become foldable by arriving at the other one.
    /// </para>
    /// <para>
    /// Gated by <c>CompilerGeneratedOrdinalTests.NonCanonicalOrdinals_DoNotFold</c>
    /// against both forms and both of the local-function indices.
    /// </para>
    /// </remarks>
    static bool IsCanonicalOrdinal(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return false;

        // A padded ordinal is rejected here, before the parse, so the range rule
        // below is reached only by an unpadded value and stays independently
        // observable.
        if (value.Length > 1 && value[0] == '0')
            return false;

        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// One assembly's eligible compiler-generated members, indexed by ordinal-free key.
    /// Cached per reader: the fidelity loop compares many methods against the same
    /// original assembly, and re-enumerating its metadata for each one would make the
    /// comparison quadratic in the assembly's member count.
    /// </summary>
    sealed class SideIndex
    {
        static readonly ConditionalWeakTable<MetadataReader, SideIndex> s_cache = new();

        public required Dictionary<string, MethodDefinitionHandle> Methods { get; init; }
        public required HashSet<string> AmbiguousMethods { get; init; }
        public required Dictionary<MethodDefinitionHandle, string> MethodNames { get; init; }
        public required Dictionary<string, TypeDefinitionHandle> Types { get; init; }
        public required HashSet<string> AmbiguousTypes { get; init; }
        public required Dictionary<TypeDefinitionHandle, string> TypeNames { get; init; }

        public bool IsEmpty => Methods.Count == 0 && Types.Count == 0;

        public static SideIndex For(MetadataReader reader)
            => s_cache.GetValue(reader, static r => Create(r));

        /// <summary>
        /// Builds the index, or yields an empty one when the metadata cannot be read.
        /// </summary>
        /// <remarks>
        /// Failure is whole-index rather than per-row on purpose. This index's guarantee is
        /// that a key resolves to exactly one member; a member skipped because its row is
        /// malformed is a member that cannot witness an ambiguity, so per-row recovery
        /// could fold two members that a complete read would have kept apart. Malformed
        /// metadata is also reachable from parts of the assembly the comparison itself
        /// never touches — enumerating every type is this type's own added exposure — so
        /// it must not turn a comparison that would have succeeded into a thrown exception.
        /// Declining to fold restores the un-normalized comparison. Enforced by
        /// <c>MalformedUnrelatedMetadata_FailsClosedRatherThanThrowing</c>.
        /// </remarks>
        static SideIndex Create(MetadataReader reader)
        {
            try
            {
                return CreateCore(reader);
            }
            catch (BadImageFormatException)
            {
                return new SideIndex
                {
                    Methods = [],
                    AmbiguousMethods = [],
                    MethodNames = [],
                    Types = [],
                    AmbiguousTypes = [],
                    TypeNames = [],
                };
            }
        }

        static SideIndex CreateCore(MetadataReader reader)
        {
            var methods = new Dictionary<string, MethodDefinitionHandle>(StringComparer.Ordinal);
            var ambiguousMethods = new HashSet<string>(StringComparer.Ordinal);
            var methodNames = new Dictionary<MethodDefinitionHandle, string>();
            var types = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
            var ambiguousTypes = new HashSet<string>(StringComparer.Ordinal);
            var typeNames = new Dictionary<TypeDefinitionHandle, string>();

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);

                string? typeKeyPrefix = TypeKeyPrefix(reader, typeHandle, typeNames);
                if (typeKeyPrefix is null)
                    continue;

                if (TryEligibleName(reader, reader.GetString(type.Name), type.GetCustomAttributes(), GeneratedNameKind.Type, declaringTypeIsGenerated: false) is { } elidedType)
                {
                    typeNames[typeHandle] = elidedType;
                    Add(types, ambiguousTypes, typeKeyPrefix, typeHandle);
                }

                // Computed on demand, and scoped to this type. Two separate properties,
                // each with its own gate, because one tamper does not expose both:
                //
                //   On demand. Only a name that could be owned needs the declaring type's
                //   mark, so an assembly carrying a malformed attribute row on a type this
                //   index would otherwise never inspect keeps folding rather than failing
                //   the whole index closed. Gated by
                //   MalformedAttributeOnUnrelatedType_IsSkippedByTheOnDemandRead, which
                //   fails if the read becomes unconditional.
                //
                //   Per type. A type that earns the mark must not lend it to the next type
                //   indexed, or an unmarked type's members inherit an ownership they did
                //   not earn and fold. Gated by
                //   TheDeclaringTypeMark_DoesNotCarryToTheNextType, which fails if this
                //   declaration moves out of the type loop. The malformed-row test above
                //   does not catch that: with the cache shared, the first type resolves it
                //   and the malformed row is never reached.
                //
                // The ??= additionally avoids re-reading for a second eligible member on
                // the same type. That part is an optimization only and nothing gates it:
                // the read is pure over the same rows, so reading twice returns the same
                // answer and throws in the same cases, and dropping the caching leaves the
                // suite green. It is not load-bearing for either property above.
                bool? typeIsGenerated = null;

                foreach (var methodHandle in type.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    string methodName = reader.GetString(method.Name);
                    if (TryElideOrdinal(methodName, GeneratedNameKind.Method) is null)
                        continue;

                    typeIsGenerated ??= HasCompilerGeneratedAttribute(reader, type.GetCustomAttributes());
                    if (TryEligibleName(reader, methodName, method.GetCustomAttributes(), GeneratedNameKind.Method, declaringTypeIsGenerated: typeIsGenerated.Value) is not { } elided)
                        continue;

                    // A generic method's own constraints; the declaring chain's are
                    // refused in TypeKeyPrefix. Splitting the two keeps either check
                    // observable when the other is removed.
                    if (HasConstrainedGenericParameters(reader, method.GetGenericParameters()))
                        continue;

                    methodNames[methodHandle] = elided;
                    // A method records its arity twice, in GenericParam and in its
                    // signature, and nothing in the format ties the two together. The
                    // operand renderer reads the signature, so a well-formed generic
                    // method still differs after its name folds; this term carries the
                    // case where the signature says non-generic and the renderer therefore
                    // spells no arity tick. Gated by
                    // MethodsWhoseSignatureHidesTheirArity_DoNotFold, not by the
                    // Roslyn-shaped control next to it.
                    Add(
                        methods,
                        ambiguousMethods,
                        typeKeyPrefix + KeySeparator + elided + KeySeparator +
                            method.GetGenericParameters().Count,
                        methodHandle);
                }
            }

            return new SideIndex
            {
                Methods = methods,
                AmbiguousMethods = ambiguousMethods,
                MethodNames = methodNames,
                Types = types,
                AmbiguousTypes = ambiguousTypes,
                TypeNames = typeNames,
            };
        }

        static void Add<THandle>(
            Dictionary<string, THandle> index,
            HashSet<string> ambiguous,
            string key,
            THandle handle)
        {
            if (!index.TryAdd(key, handle))
                ambiguous.Add(key);
        }

        /// <summary>
        /// The declaring-type path a member is keyed under, with each enclosing segment
        /// itself elided when it is an eligible generated name — otherwise a state machine
        /// nested in a renumbered type would key differently on the two sides.
        /// </summary>
        /// <remarks>
        /// The nesting chain comes from the shared bounded traversal rather than a local
        /// recursion, so a cyclic or pathologically deep declaring-type chain in an
        /// untrusted assembly is rejected under the same policy the rest of the metadata
        /// layer applies. A rejected chain yields no key, so the type and its methods are
        /// simply not folded.
        /// </remarks>
        static string? TypeKeyPrefix(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            Dictionary<TypeDefinitionHandle, string> typeNames)
        {
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                    reader,
                    handle,
                    chain,
                    out int consumed,
                    out _,
                    out _)
                || consumed == 0)
            {
                return null;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < consumed; i++)
            {
                var type = reader.GetTypeDefinition(chain[i]);

                // A generic constraint anywhere in the declaring chain distinguishes the
                // members below it, and neither this key nor the rendered operand carries
                // it: the operand spells a call target as `C::<M>g__L|#_0()`, which names
                // the declaring type but none of its constraints. A local function is not
                // itself generic, so checking only the member's own generic parameters
                // leaves it folding across a `where T : class` / `where T : struct`
                // difference on the type that declares it. Refusing the whole prefix
                // refuses the type and every method keyed beneath it in one place.
                // Gated by LocalFunctionsInsideDifferentlyConstrainedTypes_DoNotFold.
                if (HasConstrainedGenericParameters(reader, type.GetGenericParameters()))
                    return null;

                string name = typeNames.TryGetValue(chain[i], out var elided)
                    ? elided
                    : TryEligibleName(reader, reader.GetString(type.Name), type.GetCustomAttributes(), GeneratedNameKind.Type, declaringTypeIsGenerated: false)
                        ?? reader.GetString(type.Name);

                if (i == 0)
                    builder.Append(reader.GetString(type.Namespace));

                // Generic arity is part of a type's identity but is not reliably part of
                // its name. The `N suffix is a language convention, not a runtime rule, so
                // an untrusted assembly may declare a generic type whose name omits it and
                // whose elided form therefore collides with a different arity's. Key on the
                // declared count so two arities can never share a slot.
                builder.Append(KeySeparator).Append(name)
                    .Append(KeySeparator).Append(type.GetGenericParameters().Count);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Reports the elided form of an eligible generated name, or null when the name
        /// is not an owned shape or the member is not marked compiler-generated.
        /// </summary>
        /// <remarks>
        /// <paramref name="declaringTypeIsGenerated"/> carries the mark down one level,
        /// and is the difference between owning local functions and owning lambdas.
        /// Roslyn marks a generated <em>type</em> and then leaves its members unmarked,
        /// because the type-level mark already says it: measured on a Release build,
        /// <c>&lt;&gt;c</c> carries <c>CompilerGeneratedAttribute</c> while every
        /// <c>&lt;M&gt;b__N_K</c> and <c>&lt;&gt;9__N_K</c> inside it carries none. A
        /// local function is the other shape — it sits on the user's own unmarked type,
        /// so it carries the mark itself. Asking only the member would therefore decline
        /// every lambda, and asking only the type would decline every local function.
        /// <para>
        /// Types are asked about themselves alone: every call site that passes a type
        /// kind passes <c>false</c> here, because Roslyn marks every generated type it
        /// emits, so a type has no mark it needs to inherit.
        /// <c>TypeNameShapeAlone_DoesNotFold</c> pins that an unmarked type does not
        /// fold on its name; nothing here would let it inherit one, and widening the
        /// inheritance to types would need a nested-type fixture to gate, which the
        /// class remarks already track as an unverified branch.
        /// Gated by <c>UnmarkedMemberOfAGeneratedType_Folds</c> for the inheritance and
        /// <c>UnmarkedMemberOfAnUnmarkedType_DoesNotFold</c> for its negative.
        /// </para>
        /// <para>
        /// The parameter has no default. Every call site states its answer, so adding
        /// one — the field index the next slice needs — is a decision rather than an
        /// omission that compiles.
        /// </para>
        /// </remarks>
        static string? TryEligibleName(
            MetadataReader reader,
            string name,
            CustomAttributeHandleCollection attributes,
            GeneratedNameKind kind,
            bool declaringTypeIsGenerated)
        {
            if (TryElideOrdinal(name, kind) is not { } elided)
                return null;
            return declaringTypeIsGenerated || HasCompilerGeneratedAttribute(reader, attributes)
                ? elided
                : null;
        }

        /// <summary>
        /// Reports whether any generic parameter carries a constraint, either as a
        /// <see cref="GenericParameterAttributes"/> flag or as a
        /// <c>GenericParamConstraint</c> row.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Constraints distinguish two otherwise identical members, and neither the
        /// correspondence key nor the rendered operand carries them: the key is a name
        /// and an arity, and the operand spells a call target as
        /// <c>call void C::&lt;M&gt;g__L|#_0`1()</c>, which has no syntax for
        /// <c>where T : class</c>. Folding a <c>class</c>-constrained member onto a
        /// <c>struct</c>-constrained one would therefore report <c>Exact</c> for a
        /// genuine difference.
        /// </para>
        /// <para>
        /// This declines the fold rather than extending the key, because enumerating
        /// discriminators by hand is what left arity, the instance bit and the module
        /// scope out of it in turn (#3681). Refusing is fail-closed and costs nothing
        /// measurable — the fidelity corpus retires the same 68 rows with and without
        /// this check.
        /// </para>
        /// <para>
        /// It has exactly two callers, owning disjoint halves of the question, so that
        /// neither can mask the other's removal: <see cref="TypeKeyPrefix"/> asks it of
        /// every type in a declaring chain, and the method loop asks it of a method's
        /// own generic parameters. A local function is not itself generic, so the method
        /// check alone would leave it folding across a constraint difference on the type
        /// that declares it; a generated type reaches only the chain check, because a
        /// null prefix skips the type before its name is ever considered.
        /// </para>
        /// </remarks>
        static bool HasConstrainedGenericParameters(
            MetadataReader reader,
            GenericParameterHandleCollection genericParameters)
        {
            foreach (var handle in genericParameters)
            {
                var parameter = reader.GetGenericParameter(handle);
                if (parameter.Attributes != GenericParameterAttributes.None)
                    return true;
                if (parameter.GetConstraints().Count > 0)
                    return true;
            }
            return false;
        }

        static bool HasCompilerGeneratedAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
        {
            foreach (var handle in attributes)
            {
                if (IsCompilerGenerated(reader, reader.GetCustomAttribute(handle)))
                    return true;
            }
            return false;
        }

        static bool IsCompilerGenerated(MetadataReader reader, CustomAttribute attribute)
        {
            switch (attribute.Constructor.Kind)
            {
                case HandleKind.MemberReference:
                    var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                    if (member.Parent.Kind != HandleKind.TypeReference)
                        return false;
                    var typeRef = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                    return Matches(reader.GetString(typeRef.Namespace), reader.GetString(typeRef.Name));
                case HandleKind.MethodDefinition:
                    var ctor = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                    var typeDef = reader.GetTypeDefinition(ctor.GetDeclaringType());
                    return Matches(reader.GetString(typeDef.Namespace), reader.GetString(typeDef.Name));
                default:
                    return false;
            }

            static bool Matches(string ns, string name)
                => name == "CompilerGeneratedAttribute"
                    && ns == "System.Runtime.CompilerServices";
        }
    }
}
