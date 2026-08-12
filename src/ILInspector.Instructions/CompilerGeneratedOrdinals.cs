using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

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
/// The correspondence keys are produced by <see cref="MethodStructuralSignature"/> and
/// <see cref="TypeStructuralSignature"/>. Their tagged, length-prefixed grammar preserves
/// segment boundaries even when metadata names contain punctuation that was significant
/// to the former display-shaped key. Gated by
/// <c>Build_LengthPrefixesEveryNameSegmentation</c> and
/// <c>ForgedKeySegmentation_DoesNotFoldAcrossDeclaringTypes</c>.
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
/// A generated method whose signature itself names another ordinal-bearing generated
/// TypeDef does not fold that referenced name. Name substitutions are intentionally
/// limited to the method and its declaring chain; substituting a parameter or constraint
/// type independently on each side would bypass the two-sided type correspondence. An
/// unverified probe shows the operand renderer exposing the same referenced-type ordinal
/// today, making this a missed fold rather than a hidden difference; no test gates that
/// renderer dependency yet.
/// </para>
/// <para>
/// Nested generated-type correspondence still needs a dedicated end-to-end fixture. The
/// shared structural codec walks and encodes the complete declaring chain, but this
/// suite's synthetic generated types are top-level. Nested override and malformed-chain
/// integration coverage remains tracked by #3588.
/// </para>
/// </remarks>
public sealed class CompilerGeneratedOrdinalCorrespondence
{
    /// <summary>The identity correspondence: nothing folds.</summary>
    public static readonly CompilerGeneratedOrdinalCorrespondence Empty =
        new(new Dictionary<MethodDefinitionHandle, string>(),
            new Dictionary<TypeDefinitionHandle, string>(),
            new Dictionary<FieldDefinitionHandle, string>());

    /// <summary>
    /// The separator in the synthetic cache-field comparison name.
    /// </summary>
    /// <remarks>
    /// A cache field takes its sibling method's comparison name. NUL keeps that
    /// composition distinct from every raw metadata field name.
    /// </remarks>
    internal const char CacheFieldNameSeparator = '\0';

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
    readonly Dictionary<FieldDefinitionHandle, string> _fields;

    CompilerGeneratedOrdinalCorrespondence(
        Dictionary<MethodDefinitionHandle, string> methods,
        Dictionary<TypeDefinitionHandle, string> types,
        Dictionary<FieldDefinitionHandle, string> fields)
    {
        _methods = methods;
        _types = types;
        _fields = fields;
    }

    /// <summary>The ordinal-free name to compare this method under, when it has one.</summary>
    public bool TryGetMethodName(MethodDefinitionHandle handle, out string name)
        => _methods.TryGetValue(handle, out name!);

    /// <summary>The ordinal-free name to compare this type under, when it has one.</summary>
    public bool TryGetTypeName(TypeDefinitionHandle handle, out string name)
        => _types.TryGetValue(handle, out name!);

    /// <summary>The ordinal-free name to compare this field under, when it has one.</summary>
    public bool TryGetFieldName(FieldDefinitionHandle handle, out string name)
        => _fields.TryGetValue(handle, out name!);

    /// <summary>
    /// Builds the correspondence for each side. A member folds only when its ordinal-free
    /// key resolves to exactly one eligible member on <b>both</b> sides.
    /// </summary>
    /// <remarks>
    /// The safety of <see cref="OrdinalPlaceholder"/> and
    /// <see cref="CacheFieldNameSeparator"/> rests
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
        foreach (var (declaringType, oldGroup) in oldIndex.MethodGroups)
        {
            if (!newIndex.MethodGroups.TryGetValue(
                    declaringType,
                    out var newGroup))
            {
                continue;
            }

            foreach (var (signature, oldSignatureGroup) in oldGroup.Signatures)
            {
                if (!newGroup.Signatures.TryGetValue(
                        signature,
                        out var newSignatureGroup))
                {
                    continue;
                }

                foreach (var (localKey, handle) in oldSignatureGroup.Methods)
                {
                    if (oldSignatureGroup.AmbiguousMethods.Contains(localKey)
                        || newSignatureGroup.AmbiguousMethods.Contains(localKey)
                        || !newSignatureGroup.Methods.TryGetValue(
                            localKey,
                            out var counterpart))
                    {
                        continue;
                    }

                    oldMethods[handle] = oldIndex.MethodNames[handle];
                    newMethods[counterpart] = newIndex.MethodNames[counterpart];
                }
            }
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

        // A cache field is named for whatever its sibling lambda method resolved to, so
        // the two agree by construction rather than by a second key that could disagree
        // with the first. The composition is what makes the field safe in both
        // directions. When the sibling folded, both sides spell the field with the
        // sibling's elided name and the field folds with it. When the sibling did not --
        // because its key was ambiguous, or because the two sides' lambdas belong to
        // different containing methods -- both sides spell the field with the sibling's
        // *raw* name, which still differs exactly when the siblings differ.
        //
        // Falling back to the field's own raw name instead would be unsound, and this is
        // the one place where declining to fold is not automatically the safe answer: two
        // unrelated closures can carry the identical field name `<>9__0_0` while their
        // siblings are `<Run>b__0_0` and `<Other>b__0_0`, so a raw fallback reports Exact
        // for a real difference. Gated by
        // LambdaCacheFieldsOfDifferentContainingMethods_DoNotFold, which fails with a raw
        // fallback and passes with this composition.
        var oldFields = Resolve(oldReader, oldIndex, oldMethods);
        var newFields = Resolve(newReader, newIndex, newMethods);

        static Dictionary<FieldDefinitionHandle, string> Resolve(
            MetadataReader reader,
            SideIndex index,
            Dictionary<MethodDefinitionHandle, string> resolvedMethods)
        {
            var named = new Dictionary<FieldDefinitionHandle, string>();
            var siblingNames =
                new Dictionary<MethodDefinitionHandle, string>();
            foreach (var (fieldHandle, sibling) in index.FieldSiblings)
            {
                if (!siblingNames.TryGetValue(
                        sibling,
                        out string? composedName))
                {
                    string siblingName =
                        resolvedMethods.TryGetValue(
                            sibling,
                            out var elided)
                            ? elided
                            : MetadataSafetyPolicy.ReadStructuralString(
                                reader,
                                reader.GetMethodDefinition(sibling).Name);
                    composedName =
                        LambdaCacheFieldPrefix
                        + CacheFieldNameSeparator
                        + siblingName;
                    siblingNames.Add(sibling, composedName);
                }

                // CacheFieldNameSeparator, not bare concatenation, and for the reason spelled out on
                // OrdinalPlaceholder: this string is substituted into the compared operand
                // text, so it shares a namespace with every raw name that text can carry.
                // Without an unspellable mark the composition is forgeable -- a field
                // actually named `<>9__<Run>b__0_0` renders identically to the surrogate
                // built for `<>9__0_0` whose sibling is `<Run>b__0_0`, and a real
                // difference reports Exact. The folded branch already carries NUL inside
                // OrdinalPlaceholder; it is the raw branch that is otherwise spellable.
                //
                // Two gates, because one claim is concrete and the other general.
                // LambdaCacheFieldName_CannotBeForgedByARawFieldName pins that *this*
                // composition is not bare concatenation, and fails if the separator is
                // dropped. It does not pin that the separator is unspellable -- give
                // CacheFieldNameSeparator a spellable value and that test still passes, because its
                // forged name is written against NUL. The unspellability itself is
                // CacheFieldNameSeparatorCannotBeSpelledByAMetadataName, which drives its assertion
                // from the constant and so fails for any spellable value.
                named[fieldHandle] = composedName;
            }

            return named;
        }

        if (oldMethods.Count == 0 && oldTypes.Count == 0 && oldFields.Count == 0)
            return (Empty, Empty);

        return (new CompilerGeneratedOrdinalCorrespondence(oldMethods, oldTypes, oldFields),
                new CompilerGeneratedOrdinalCorrespondence(newMethods, newTypes, newFields));
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
    /// for is disowned, so two corresponding members keep their different raw ordinals
    /// and report a false-positive operand difference.
    ///
    /// Found by adversarial review (round 12); gated by
    /// GeneratedNameWhoseContainingNameIsItselfGenerated_IsStillOwned.
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
    /// difference.
    /// </remarks>
    internal enum GeneratedNameKind
    {
        /// <summary>A name read from a type definition.</summary>
        Type,

        /// <summary>A name read from a method definition or reference.</summary>
        Method,

    }

    internal static string? TryElideOrdinal(string name, GeneratedNameKind kind)
        => TryElideOrdinal(name, kind, workBudget: null);

    static string? TryElideOrdinal(
        string name,
        GeneratedNameKind kind,
        StructuralSignatureWorkBudget? workBudget)
    {
        if (name.Length < 4 || name[0] != '<')
            return null;

        // A generated name folds only when it names the construct it belongs to. The
        // anonymous shapes — `<>c`, `<>c__DisplayClassN_K`, `<>9__N_K` — open with an
        // empty pair of brackets, so their ordinal is their only discriminator and
        // eliding it would merge unrelated closures. Depth matching still reports 1 for
        // those, so they are rejected here exactly as before.
        //
        // `<>9__N_K` is refused here like the others, and is still folded -- but by the
        // field loop, through the sibling lambda method it pairs with, never on its own
        // name. Nothing about that fold passes through this function.
        int close = FindClosingAngle(name);
        if (close <= 1)
            return null;

        var containing = name.AsSpan(0, close + 1);
        var rest = name.AsSpan(close + 1);

        if (rest.StartsWith("d__", StringComparison.Ordinal))
        {
            if (kind != GeneratedNameKind.Type
                || !IsCanonicalOrdinal(rest[3..]))
            {
                return null;
            }
            workBudget?.Charge(name.Length);
            return $"{containing}d__{OrdinalPlaceholder}";
        }

        if (kind == GeneratedNameKind.Type)
            return null;

        // The lambda form `<M>b__N_K` differs from the local-function form only in
        // carrying no local name, so it has no separator and its ordinals start
        // immediately. Both are methods, and both spell `N` as the containing type's
        // member index, which the harness's rebuilt type skeleton renumbers.
        if (rest.StartsWith("b__", StringComparison.Ordinal))
        {
            if (ElideScopeOrdinal(rest[3..]) is not { } lambdaOrdinals)
                return null;
            workBudget?.Charge(name.Length);
            return $"{containing}b__{lambdaOrdinals}";
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

        if (ElideScopeOrdinal(
                rest[(bar + 1)..]) is not { } localOrdinals)
        {
            return null;
        }
        workBudget?.Charge(name.Length);
        return $"{containing}g__{local}|{localOrdinals}";
    }

    internal const string LambdaCacheFieldPrefix = "<>9__";

    /// <summary>
    /// The raw <c>N_K</c> tail of Roslyn's lambda cache field <c>&lt;&gt;9__N_K</c>, or
    /// null when the name is not that form.
    /// </summary>
    /// <remarks>
    /// This field is the one owned form that cannot fold on its own name. It opens with
    /// an empty pair of brackets, so after eliding the renumbered scope ordinal <c>N</c>
    /// nothing distinguishes one containing method's cache slot from another's:
    /// <c>&lt;&gt;9__0_0</c> and <c>&lt;&gt;9__1_0</c> would both spell
    /// <c>&lt;&gt;9__#_0</c>, and two unrelated closures would merge. What makes it
    /// foldable is that Roslyn emits it in lockstep with a sibling lambda method
    /// <c>&lt;Name&gt;b__N_K</c> on the same type, whose containing name supplies exactly
    /// the discriminator the field lacks. The tail returned here is what pairs the two.
    /// <para>
    /// Measured rather than assumed: a build of a type with lambdas in seven methods --
    /// plain, overloaded, capturing, async and nested -- emits eight
    /// <c>&lt;&gt;9__N_K</c> fields on <c>&lt;&gt;c</c> and exactly eight matching
    /// <c>&lt;Name&gt;b__N_K</c> methods, a 1:1 pairing with no unmatched member on
    /// either side. Pinned by <c>LambdaCacheFields_PairOneToOneWithTheirLambdaMethods</c>.
    /// </para>
    /// <para>
    /// The tail is accepted exactly when <see cref="ElideScopeOrdinal"/> accepts it, so
    /// the field grammar cannot drift from the method grammar it pairs against. That
    /// also excludes the neighbouring generated fields Roslyn puts on these types --
    /// <c>&lt;&gt;9</c> (the singleton, no tail), <c>&lt;&gt;1__state</c>,
    /// <c>&lt;&gt;t__builder</c> and the awaiter slots <c>&lt;&gt;u__N</c>, whose ordinal
    /// discriminates a real slot and which have no sibling method to pair with. Gated by
    /// <c>NeighbouringGeneratedFields_AreNotMistakenForLambdaCaches</c>.
    /// </para>
    /// </remarks>
    internal static string? TryLambdaCacheFieldTail(string name)
    {
        if (name is null || !name.StartsWith(LambdaCacheFieldPrefix, StringComparison.Ordinal))
            return null;

        string tail = name[LambdaCacheFieldPrefix.Length..];
        return ElideScopeOrdinal(tail) is not null ? tail : null;
    }

    /// <summary>
    /// The raw <c>N_K</c> tail of a lambda method <c>&lt;Name&gt;b__N_K</c>, or null when
    /// the name is not that form. This is the key side of the pairing described on
    /// <see cref="TryLambdaCacheFieldTail"/>.
    /// </summary>
    internal static string? TryLambdaMethodTail(string name)
    {
        if (name is null || name.Length < 4 || name[0] != '<')
            return null;

        int close = FindClosingAngle(name);
        if (close <= 1)
            return null;

        var rest = name.AsSpan(close + 1);
        if (!rest.StartsWith("b__", StringComparison.Ordinal))
            return null;

        string tail = rest[3..].ToString();
        return ElideScopeOrdinal(tail) is not null ? tail : null;
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

        public required Dictionary<StructuralTypeKey, MethodGroup> MethodGroups { get; init; }
        public required Dictionary<MethodDefinitionHandle, string> MethodNames { get; init; }
        public required Dictionary<StructuralTypeKey, TypeDefinitionHandle> Types { get; init; }
        public required HashSet<StructuralTypeKey> AmbiguousTypes { get; init; }
        public required Dictionary<TypeDefinitionHandle, string> TypeNames { get; init; }
        public required Dictionary<FieldDefinitionHandle, MethodDefinitionHandle> FieldSiblings { get; init; }

        public bool IsEmpty => MethodGroups.Count == 0 && Types.Count == 0 && FieldSiblings.Count == 0;

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
                    MethodGroups = [],
                    MethodNames = [],
                    Types = [],
                    AmbiguousTypes = [],
                    TypeNames = [],
                    FieldSiblings = [],
                };
            }
        }

        static SideIndex CreateCore(MetadataReader reader)
        {
            var methodGroups = new Dictionary<StructuralTypeKey, MethodGroup>();
            var methodNames = new Dictionary<MethodDefinitionHandle, string>();
            var types = new Dictionary<StructuralTypeKey, TypeDefinitionHandle>();
            var ambiguousTypes = new HashSet<StructuralTypeKey>();
            var typeNames = new Dictionary<TypeDefinitionHandle, string>();
            var fieldSiblings = new Dictionary<FieldDefinitionHandle, MethodDefinitionHandle>();
            var workBudget = new StructuralSignatureWorkBudget();
            var typeNameElisions = new Dictionary<StringHandle, string?>();
            var methodNameElisions =
                new Dictionary<StringHandle, (string? Elided, string? SiblingTail)>();
            var fieldTails = new Dictionary<StringHandle, string?>();

            string DecodeName(StringHandle handle)
            {
                string name =
                    MetadataSafetyPolicy.ReadStructuralString(
                        reader,
                        handle);
                workBudget.Charge(name.Length);
                return name;
            }

            // Name discovery is separate because the shared key builder consumes a
            // complete handle-to-name map rather than owning generated-name policy.
            // Parsing and materializing an elision is work even when the definition
            // ultimately lacks ownership, so it is charged before the attribute gate.
            // Failing the optional normalization whole-side is the safe outcome when
            // attacker-controlled name work exhausts the shared budget.
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                if (!reader.StringComparer.StartsWith(type.Name, "<"))
                    continue;
                if (!typeNameElisions.TryGetValue(
                        type.Name,
                        out string? elidedType))
                {
                    elidedType = TryElideOrdinal(
                        DecodeName(type.Name),
                        GeneratedNameKind.Type,
                        workBudget);
                    typeNameElisions.Add(type.Name, elidedType);
                }
                if (TryEligibleName(
                        reader,
                        elidedType,
                        type.GetCustomAttributes(),
                        declaringTypeIsGenerated:
                            false) is { } eligibleTypeName)
                {
                    typeNames[typeHandle] = eligibleTypeName;
                }
            }

            var structuralKeys =
                new StructuralSignatureBuilder(
                    reader,
                    typeNames,
                    workBudget);
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);

                if (typeNames.ContainsKey(typeHandle))
                {
                    Add(
                        types,
                        ambiguousTypes,
                        structuralKeys.BuildTypeKey(typeHandle),
                        typeHandle);
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

                // Per type for the same reason the mark is: a cache field pairs only with
                // a lambda method on its own type, and a map shared across types would
                // let a field borrow an unrelated type's method name. Gated by
                // LambdaCacheFields_DoNotPairAcrossTypes.
                Dictionary<string, MethodDefinitionHandle>? lambdaSiblings = null;
                HashSet<string>? ambiguousSiblings = null;

                foreach (var methodHandle in type.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if (!reader.StringComparer.StartsWith(method.Name, "<"))
                        continue;
                    if (!methodNameElisions.TryGetValue(
                            method.Name,
                            out var nameInfo))
                    {
                        string name = DecodeName(method.Name);
                        string? candidateElision = TryElideOrdinal(
                            name,
                            GeneratedNameKind.Method,
                            workBudget);
                        nameInfo = (
                            candidateElision,
                            candidateElision is null
                                ? null
                                : TryLambdaMethodTail(name));
                        methodNameElisions.Add(
                            method.Name,
                            nameInfo);
                    }
                    if (nameInfo.Elided is not { } elided)
                        continue;

                    typeIsGenerated ??= HasCompilerGeneratedAttribute(reader, type.GetCustomAttributes());
                    if (TryEligibleName(
                            reader,
                            elided,
                            method.GetCustomAttributes(),
                            declaringTypeIsGenerated:
                                typeIsGenerated.Value) is null)
                        continue;

                    StructuralMethodKey key =
                        structuralKeys.BuildMethodKey(method, elided);
                    methodNames[methodHandle] = elided;

                    // The pairing side of the lambda cache field. Recorded only for
                    // methods that were themselves found eligible, so a field can never
                    // borrow a name the method path refused to fold.
                    if (nameInfo.SiblingTail is { } siblingTail)
                    {
                        lambdaSiblings ??= new Dictionary<string, MethodDefinitionHandle>(StringComparer.Ordinal);
                        if (!lambdaSiblings.TryAdd(siblingTail, methodHandle))
                            (ambiguousSiblings ??= new HashSet<string>(StringComparer.Ordinal)).Add(siblingTail);
                    }

                    if (!methodGroups.TryGetValue(
                            key.DeclaringType,
                            out var group))
                    {
                        group = new MethodGroup();
                        methodGroups.Add(key.DeclaringType, group);
                    }
                    if (!group.Signatures.TryGetValue(
                            key.Component.Signature,
                            out var signatureGroup))
                    {
                        signatureGroup = new SignatureGroup();
                        group.Signatures.Add(
                            key.Component.Signature,
                            signatureGroup);
                    }
                    Add(
                        signatureGroup.Methods,
                        signatureGroup.AmbiguousMethods,
                        key.Component.LocalKey,
                        methodHandle);
                }

                if (lambdaSiblings is null)
                    continue;

                foreach (var fieldHandle in type.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    if (!reader.StringComparer.StartsWith(
                            field.Name,
                            LambdaCacheFieldPrefix))
                    {
                        continue;
                    }
                    if (!fieldTails.TryGetValue(
                            field.Name,
                            out string? tail))
                    {
                        tail = TryLambdaCacheFieldTail(
                            DecodeName(field.Name));
                        fieldTails.Add(field.Name, tail);
                    }
                    if (tail is null)
                        continue;

                    // Fail closed on an ambiguous pairing. Roslyn does not emit two lambda
                    // methods sharing a tail on one type -- the scope ordinal is the
                    // containing member index, so a shared tail implies a shared containing
                    // method and hence a shared name -- but hand-written or hostile IL can,
                    // and then the field has no single sibling to take its name from.
                    if (ambiguousSiblings?.Contains(tail) == true
                        || !lambdaSiblings.TryGetValue(tail, out var sibling))
                    {
                        continue;
                    }

                    typeIsGenerated ??= HasCompilerGeneratedAttribute(reader, type.GetCustomAttributes());
                    if (!typeIsGenerated.Value
                        && !HasCompilerGeneratedAttribute(reader, field.GetCustomAttributes()))
                    {
                        continue;
                    }

                    // Only the pairing is recorded here. What the field is *called* is
                    // decided in Build, from whatever the method correspondence resolved
                    // its sibling to, so the field inherits the method's two-sided
                    // guarantee rather than repeating it on a weaker key of its own.
                    fieldSiblings[fieldHandle] = sibling;
                }
            }

            return new SideIndex
            {
                MethodGroups = methodGroups,
                MethodNames = methodNames,
                Types = types,
                AmbiguousTypes = ambiguousTypes,
                TypeNames = typeNames,
                FieldSiblings = fieldSiblings,
            };
        }

        static void Add<TKey, THandle>(
            Dictionary<TKey, THandle> index,
            HashSet<TKey> ambiguous,
            TKey key,
            THandle handle)
            where TKey : notnull
        {
            if (!index.TryAdd(key, handle))
                ambiguous.Add(key);
        }

        internal sealed class MethodGroup
        {
            internal Dictionary<StructuralEncodedSignature, SignatureGroup>
                Signatures { get; } = [];
        }

        internal sealed class SignatureGroup
        {
            internal Dictionary<StructuralMethodLocalKey, MethodDefinitionHandle>
                Methods { get; } = [];

            internal HashSet<StructuralMethodLocalKey> AmbiguousMethods { get; } = [];
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
            string? elidedName,
            CustomAttributeHandleCollection attributes,
            bool declaringTypeIsGenerated)
        {
            if (elidedName is null)
                return null;
            return declaringTypeIsGenerated || HasCompilerGeneratedAttribute(reader, attributes)
                ? elidedName
                : null;
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
                    return Matches(
                        reader,
                        typeRef.Namespace,
                        typeRef.Name);
                case HandleKind.MethodDefinition:
                    var ctor = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                    var typeDef = reader.GetTypeDefinition(ctor.GetDeclaringType());
                    return Matches(
                        reader,
                        typeDef.Namespace,
                        typeDef.Name);
                default:
                    return false;
            }

            static bool Matches(
                MetadataReader reader,
                StringHandle namespaceHandle,
                StringHandle nameHandle)
                => reader.StringComparer.Equals(
                        nameHandle,
                        "CompilerGeneratedAttribute")
                    && reader.StringComparer.Equals(
                        namespaceHandle,
                        "System.Runtime.CompilerServices");
        }
    }
}
