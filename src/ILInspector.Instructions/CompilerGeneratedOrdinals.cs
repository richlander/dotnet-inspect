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
/// Roslyn names a local function <c>&lt;M&gt;g__L|N_K</c> and an iterator or async
/// state machine <c>&lt;M&gt;d__N</c>, where <c>N</c> is the ordinal of a member of the
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
/// state-machine and display-class types all carry the attribute. Enforced by
/// <c>NameShapeAlone_DoesNotFold</c>, which declares a type with a hand-written mangled
/// name and no attribute and asserts it keeps its ordinal.
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
/// Anonymous shapes (<c>&lt;&gt;c__DisplayClassN_K</c>, <c>&lt;&gt;9__N_K</c>) are excluded:
/// they carry no containing-method name, so <c>N</c> is their only discriminator and
/// folding it would merge unrelated closures.
/// Enforced by <c>AnonymousShapes_NeverFold</c> and <c>LambdaShape_IsOutOfScope</c>.
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
/// </remarks>
public sealed class CompilerGeneratedOrdinalCorrespondence
{
    /// <summary>The identity correspondence: nothing folds.</summary>
    public static readonly CompilerGeneratedOrdinalCorrespondence Empty =
        new(new Dictionary<MethodDefinitionHandle, string>(), new Dictionary<TypeDefinitionHandle, string>());

    /// <summary>The character standing in for an elided member ordinal.</summary>
    internal const string OrdinalPlaceholder = "#";

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
    public static (CompilerGeneratedOrdinalCorrespondence Old, CompilerGeneratedOrdinalCorrespondence New) Build(
        MetadataReader oldReader,
        MetadataReader newReader)
    {
        ArgumentNullException.ThrowIfNull(oldReader);
        ArgumentNullException.ThrowIfNull(newReader);

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

            if (CollidesWithARawName(oldIndex, newIndex, oldIndex.MethodNames[handle])
                || CollidesWithARawName(oldIndex, newIndex, newIndex.MethodNames[counterpart]))
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

            if (CollidesWithARawName(oldIndex, newIndex, oldIndex.TypeNames[handle])
                || CollidesWithARawName(oldIndex, newIndex, newIndex.TypeNames[counterpart]))
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
    /// Whether the elided form is spelled by some member's <em>raw</em> name on either
    /// side, in which case folding onto it is unsafe.
    /// </summary>
    /// <remarks>
    /// The elided form is substituted into the compared text, so it shares a namespace
    /// with every raw name in either assembly. A member literally named
    /// <c>&lt;M&gt;g__L|#_0</c> is unspellable in C# but legal in metadata, and it would
    /// render identically to a folded <c>&lt;M&gt;g__L|3_0</c> — so a body that changed
    /// which of the two it calls would read as unchanged. Declining the fold restores the
    /// un-normalized comparison, which is the behavior the rest of this type promises.
    /// </remarks>
    static bool CollidesWithARawName(SideIndex oldIndex, SideIndex newIndex, string elided)
        => oldIndex.RawNames.Contains(elided) || newIndex.RawNames.Contains(elided);

    /// <summary>
    /// Rewrites a Roslyn mangled name to its ordinal-free form, or returns null when the
    /// name is not one of the recognized ordinal-bearing shapes.
    /// </summary>
    internal static string? TryElideOrdinal(string name)
    {
        if (name.Length < 4 || name[0] != '<')
            return null;

        // A generated name folds only when it names the construct it belongs to. The
        // anonymous shapes — `<>c`, `<>c__DisplayClassN_K`, `<>9__N_K` — open with an
        // empty pair of brackets, so their ordinal is their only discriminator and
        // eliding it would merge unrelated closures.
        int close = name.IndexOf('>', 1);
        if (close <= 1)
            return null;

        var containing = name.AsSpan(0, close + 1);
        var rest = name.AsSpan(close + 1);

        if (rest.StartsWith("d__", StringComparison.Ordinal))
        {
            return AllDigits(rest[3..])
                ? $"{containing}d__{OrdinalPlaceholder}"
                : null;
        }

        if (!rest.StartsWith("g__", StringComparison.Ordinal))
            return null;

        int bar = rest.LastIndexOf('|');
        if (bar < 3)
            return null;

        var local = rest[3..bar];
        var ordinals = rest[(bar + 1)..];
        int underscore = ordinals.IndexOf('_');
        if (local.IsEmpty || underscore <= 0)
            return null;

        var scope = ordinals[..underscore];
        var slot = ordinals[(underscore + 1)..];
        if (!AllDigits(scope) || !AllDigits(slot))
            return null;

        // The scope ordinal `N` is the unstable member index; the slot ordinal `K`
        // distinguishes local functions within one containing method and is preserved.
        return $"{containing}g__{local}|{OrdinalPlaceholder}_{slot}";
    }

    static bool AllDigits(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return false;
        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }
        return true;
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

        /// <summary>Every type and method name in this assembly, verbatim.</summary>
        public required HashSet<string> RawNames { get; init; }

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
                    RawNames = [],
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
            var rawNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                rawNames.Add(reader.GetString(type.Name));

                string? typeKeyPrefix = TypeKeyPrefix(reader, typeHandle, typeNames);
                foreach (var methodHandle in type.GetMethods())
                    rawNames.Add(reader.GetString(reader.GetMethodDefinition(methodHandle).Name));

                if (typeKeyPrefix is null)
                    continue;

                if (TryEligibleName(reader, reader.GetString(type.Name), type.GetCustomAttributes()) is { } elidedType)
                {
                    typeNames[typeHandle] = elidedType;
                    Add(types, ambiguousTypes, typeKeyPrefix, typeHandle);
                }

                foreach (var methodHandle in type.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if (TryEligibleName(reader, reader.GetString(method.Name), method.GetCustomAttributes()) is not { } elided)
                        continue;

                    methodNames[methodHandle] = elided;
                    Add(methods, ambiguousMethods, $"{typeKeyPrefix}::{elided}", methodHandle);
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
                RawNames = rawNames,
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
                string name = typeNames.TryGetValue(chain[i], out var elided)
                    ? elided
                    : TryEligibleName(reader, reader.GetString(type.Name), type.GetCustomAttributes())
                        ?? reader.GetString(type.Name);

                if (i == 0)
                {
                    string ns = reader.GetString(type.Namespace);
                    if (ns.Length != 0)
                        builder.Append(ns).Append('.');
                }
                else
                {
                    builder.Append('+');
                }

                builder.Append(name);
            }

            return builder.ToString();
        }

        static string? TryEligibleName(
            MetadataReader reader,
            string name,
            CustomAttributeHandleCollection attributes)
        {
            if (TryElideOrdinal(name) is not { } elided)
                return null;
            return HasCompilerGeneratedAttribute(reader, attributes) ? elided : null;
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
