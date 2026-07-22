using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

using SrmConstant = System.Reflection.Metadata.Constant;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recovers the two facts the optional-argument elision pass consumes for a
/// same-assembly <c>MethodDefinition</c> callee: each parameter's declared C#
/// default (<c>[Optional]</c> + a <c>Constant</c> row) and how many trailing
/// arguments may be dropped without a competing overload rebinding the shortened
/// call.
///
/// <para>Optional parameters are a C# concept the IL type system erases — the
/// compiler bakes the default into every call site (<c>ldnull</c>/<c>ldc</c>).
/// Sourcing the default from metadata (mirroring the declaration-signature path
/// in <c>ApiSurfaceExtractor</c>) keeps the decompiler from re-deriving its own
/// view of the signature.</para>
///
/// <para>The overload guardrail is exact and local. Because the elision pass
/// keeps only calls whose retained arguments already match their parameter types
/// (identity conversions), the callee is the best match on those arguments, so a
/// same-named sibling can rebind the shortened call only by <em>tying</em> the
/// callee — sharing its leading parameter types. <see cref="SafeTrailingElidableCount"/>
/// counts the trailing defaulted run for which no such leading-signature
/// duplicate exists among the callee's rebind candidates; any same-named generic
/// sibling declines the whole callee (a generic candidate can unify with the
/// retained arguments and win the fewer-declared-parameters tie-break), as does
/// any candidate carrying <c>[OverloadResolutionPriority]</c> (which reorders
/// applicable candidates ahead of betterness). For a non-extension callee the
/// candidate set is the declaring type's own methods (static calls are fully
/// qualified; instance calls are pinned by the pass-time receiver guard). An
/// extension callee renders in receiver syntax, so its candidates are
/// <em>every</em> extension method of the same name in the assembly, and elision
/// additionally requires the receiver type to live in another assembly (a
/// same-assembly instance method would beat the extension) and the manifest to
/// carry no code-bearing netmodules (whose extensions the scan cannot see).
/// Extensions and instance methods in referenced assemblies are the residual the
/// recompile/corpus fidelity gate backstops.</para>
/// </summary>
internal static class OptionalArgumentFacts
{
    /// <summary>
    /// Stamps <see cref="MethodRef.ParameterDefaults"/> and
    /// <see cref="MethodRef.SafeTrailingElidableCount"/> onto <paramref name="callee"/>
    /// when the callee has an overload-safe trailing run of defaulted parameters;
    /// returns the callee unchanged otherwise.
    /// </summary>
    internal static MethodRef Stamp(MetadataSource source, MethodDefinitionHandle handle, MethodRef callee)
    {
        try
        {
            int n = callee.ParameterTypes.Length;
            if (n == 0)
                return callee;

            var reader = source.Reader;
            var method = reader.GetMethodDefinition(handle);

            var defaults = ReadDefaults(reader, method, n);
            if (defaults.IsDefaultOrEmpty)
                return callee;

            int safe = SafeTrailingElidableCount(reader, method, handle, callee, defaults);
            if (safe == 0)
                return callee;

            return callee with { ParameterDefaults = defaults, SafeTrailingElidableCount = safe };
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException)
        {
            return callee;
        }
    }

    /// <summary>
    /// Per-parameter defaults aligned 1:1 with the callee parameters. A C# default
    /// is <c>[Optional]</c> together with a <c>Constant</c> row (<c>HasDefault</c>);
    /// attributed decimal/DateTime defaults carry no <c>Constant</c> and are left
    /// as "no default" (their call sites are <c>newobj</c>, never a plain constant,
    /// so the pass would not match them anyway). Returns <see langword="default"/>
    /// when no parameter carries a C# default.
    /// </summary>
    static ImmutableArray<ParameterDefault> ReadDefaults(MetadataReader reader, MethodDefinition method, int parameterCount)
    {
        var defaults = new ParameterDefault[parameterCount];
        bool any = false;
        foreach (var handle in method.GetParameters())
        {
            var parameter = reader.GetParameter(handle);
            int index = parameter.SequenceNumber - 1;  // sequence 0 is the return parameter
            if (index < 0 || index >= parameterCount)
                continue;
            const ParameterAttributes optionalWithDefault = ParameterAttributes.Optional | ParameterAttributes.HasDefault;
            if ((parameter.Attributes & optionalWithDefault) != optionalWithDefault)
                continue;
            var constantHandle = parameter.GetDefaultValue();
            if (constantHandle.IsNil)
                continue;
            // An undecodable constant type must not become a spurious null default
            // (which would then match a null/0 argument); leave the parameter as
            // "no default" so the pass keeps the explicit argument.
            if (!TryReadConstantValue(reader, reader.GetConstant(constantHandle), out var value))
                continue;
            defaults[index] = new ParameterDefault(true, value);
            any = true;
        }
        return any ? ImmutableArray.Create(defaults) : default;
    }

    /// <summary>
    /// The length of the trailing run of defaulted parameters that is also
    /// overload-safe. The elision pass keeps a call only when every retained
    /// argument's type equals its parameter type (an identity conversion), so the
    /// callee is the best possible match on those arguments. A same-named sibling
    /// can therefore rebind the shortened call only if it <em>ties</em> the callee
    /// on the retained arguments — i.e. its leading parameter types are identical
    /// to the callee's. (Any sibling that differs at a leading position forces a
    /// non-identity conversion there and loses to the callee's exact match; a
    /// generic sibling loses the non-generic tie-break; a <c>params</c> sibling
    /// loses normal-vs-expanded.) So a shortened arity is unsafe exactly when some
    /// sibling with at least that many parameters shares the callee's leading
    /// parameter types.
    /// </summary>
    static int SafeTrailingElidableCount(
        MetadataReader reader,
        MethodDefinition method,
        MethodDefinitionHandle handle,
        MethodRef callee,
        ImmutableArray<ParameterDefault> defaults)
    {
        int n = callee.ParameterTypes.Length;

        int trailingDefaults = 0;
        for (int k = 1; k <= n; k++)
        {
            if (!defaults[n - k].HasDefault)
                break;
            trailingDefaults = k;
        }
        if (trailingDefaults == 0)
            return 0;

        // A generic declaring type or generic method makes cross-instantiation
        // reasoning unsound here; decline in v1.
        if (callee.DeclaringType.Kind == TypeRefKind.GenericInstance
            || !callee.TypeArguments.IsDefaultOrEmpty
            || method.GetGenericParameters().Count > 0)
            return 0;

        List<SiblingSignature> siblings;
        if (MethodDefinitionFacts.HasExtensionAttribute(reader, method))
        {
            // An extension call renders in receiver syntax (r.M(args)), so the
            // recompiler resolves M across every extension class in scope plus the
            // receiver type's own instance methods — not just the callee's
            // declaring class. Bound what we can prove locally: (1) the receiver
            // type must live in another assembly, so no same-assembly instance
            // method can capture the shortened call, and (2) no same-assembly
            // extension named M may tie the callee's leading signature. Extensions
            // and instance methods in referenced assemblies stay the residual the
            // corpus fidelity gate backstops.
            // The assembly-wide extension scan below reads only this manifest
            // module's TypeDefinitions. In a multi-module assembly a competing
            // extension can live in a linked netmodule the scan never sees (a
            // same-assembly steal, not the accepted cross-assembly residual), so
            // decline extension elision whenever the manifest carries code-bearing
            // module files.
            if (!ReceiverIsCrossAssembly(reader, callee.ParameterTypes[0]) || AssemblyHasMetadataModules(reader))
                return 0;
            siblings = AssemblyExtensionSiblings(reader, callee.Name);
        }
        else
        {
            // A non-extension call resolves within the callee's declaring type:
            // static calls are fully qualified, and instance calls use the
            // receiver's static type, which the pass-time receiver guard pins to
            // the declaring type. Its own methods are the only rebind candidates.
            siblings = DeclaringTypeSiblings(reader, method, callee.Name);
        }

        // A same-named generic sibling can unify with the retained arguments and
        // win the "fewer declared parameters" tie-break over an optional-using
        // callee (verified: Pick(int, int = 0) loses Pick(v) to Pick<T>(T)). And
        // [OverloadResolutionPriority] reorders applicable candidates ahead of
        // betterness, so even a differently-typed sibling can steal the shortened
        // call. The leading-signature identity check below models neither, so
        // decline the whole callee when any candidate carries priority, or when
        // any non-self sibling shares the name and is generic.
        foreach (var sibling in siblings)
        {
            if (sibling.HasPriority)
                return 0;
            if (sibling.Handle != handle && sibling.IsGeneric)
                return 0;
        }

        int safe = 0;
        for (int k = 1; k <= trailingDefaults; k++)
        {
            if (!AritySafe(callee, siblings, handle, n - k))
                break;
            safe = k;
        }
        return safe;
    }

    /// <summary>
    /// A same-named overload candidate: its handle (to exclude the callee itself),
    /// its decoded parameter types (leading-signature comparison), whether it is
    /// generic (a generic sibling declines the whole callee), and whether it
    /// carries <c>[OverloadResolutionPriority]</c> (any priority candidate declines).
    /// </summary>
    readonly record struct SiblingSignature(MethodDefinitionHandle Handle, ImmutableArray<TypeRef> ParameterTypes, bool IsGeneric, bool HasPriority);

    static readonly List<SiblingSignature> NoSiblings = [];

    // The assembly-wide extension index is a full metadata scan; cache it per
    // reader so a corpus run pays for it once per assembly.
    static readonly ConditionalWeakTable<MetadataReader, Dictionary<string, List<SiblingSignature>>> ExtensionSiblingCache = new();

    static List<SiblingSignature> DeclaringTypeSiblings(MetadataReader reader, MethodDefinition self, string name)
    {
        var siblings = new List<SiblingSignature>();
        var declaringType = reader.GetTypeDefinition(self.GetDeclaringType());
        var typeScope = new GenericScope(MethodDefinitionFacts.GenericParameterNames(reader, declaringType.GetGenericParameters()), []);
        foreach (var handle in declaringType.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (!string.Equals(reader.GetString(method.Name), name, StringComparison.Ordinal))
                continue;
            siblings.Add(new SiblingSignature(
                handle,
                GuardedDecode.MethodSignature(reader, method, typeScope).ParameterTypes,
                method.GetGenericParameters().Count > 0,
                MethodDefinitionFacts.HasOverloadResolutionPriorityAttribute(reader, method)));
        }
        return siblings;
    }

    /// <summary>
    /// Every extension method named <paramref name="name"/> anywhere in the
    /// assembly — the candidate set a shortened extension call's receiver-syntax
    /// overload resolution draws from. Built once per reader and cached.
    /// </summary>
    static List<SiblingSignature> AssemblyExtensionSiblings(MetadataReader reader, string name)
    {
        var index = ExtensionSiblingCache.GetValue(reader, BuildExtensionSiblingIndex);
        return index.TryGetValue(name, out var siblings) ? siblings : NoSiblings;
    }

    static Dictionary<string, List<SiblingSignature>> BuildExtensionSiblingIndex(MetadataReader reader)    {
        var index = new Dictionary<string, List<SiblingSignature>>(StringComparer.Ordinal);
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            // The compiler marks an extension method's declaring class with
            // [Extension] as well; skip classes that hold no extensions.
            if (!ILInspector.Metadata.AttributeReader.HasExtensionAttribute(reader, type.GetCustomAttributes()))
                continue;
            var typeScope = new GenericScope(MethodDefinitionFacts.GenericParameterNames(reader, type.GetGenericParameters()), []);
            foreach (var handle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(handle);
                if (!MethodDefinitionFacts.HasExtensionAttribute(reader, method))
                    continue;
                string name = reader.GetString(method.Name);
                if (!index.TryGetValue(name, out var siblings))
                {
                    siblings = [];
                    index[name] = siblings;
                }
                siblings.Add(new SiblingSignature(
                    handle,
                    GuardedDecode.MethodSignature(reader, method, typeScope).ParameterTypes,
                    method.GetGenericParameters().Count > 0,
                    MethodDefinitionFacts.HasOverloadResolutionPriorityAttribute(reader, method)));
            }
        }
        return index;
    }

    /// <summary>
    /// Whether the manifest assembly links code-bearing module files (netmodules).
    /// The extension scan reads only this reader's <see cref="MetadataReader.TypeDefinitions"/>,
    /// so a competing extension in another module of the same assembly is invisible
    /// to it; the extension path declines outright in that case.
    /// </summary>
    static bool AssemblyHasMetadataModules(MetadataReader reader)
    {
        if (!reader.IsAssembly)
            return false;
        foreach (var handle in reader.AssemblyFiles)
            if (reader.GetAssemblyFile(handle).ContainsMetadata)
                return true;
        return false;
    }

    /// <summary>
    /// Whether an extension callee's receiver type is rooted in another assembly.
    /// A same-assembly receiver could declare an instance method (possibly
    /// inherited) that captures the shortened call — instance methods beat
    /// extensions — and clearing its full method set is not cheap, so decline. A
    /// receiver rooted elsewhere has its instance methods there too, out of this
    /// assembly's reach. Arrays and generic instances are cleared through their
    /// element type.
    /// </summary>
    static bool ReceiverIsCrossAssembly(MetadataReader reader, TypeRef receiver)
    {
        string? rootAssembly = RootAssembly(receiver);
        if (string.IsNullOrEmpty(rootAssembly))
            return false;
        string current = reader.IsAssembly
            ? TypeRefDecoder.Canonical(reader.GetString(reader.GetAssemblyDefinition().Name))
            : "";
        return !string.Equals(rootAssembly, current, StringComparison.Ordinal);
    }

    static string? RootAssembly(TypeRef type) => type.Kind switch
    {
        TypeRefKind.Definition => type.Assembly,
        TypeRefKind.GenericInstance or TypeRefKind.SzArray or TypeRefKind.Array
            or TypeRefKind.Pointer or TypeRefKind.ByRef or TypeRefKind.Pinned
            => type.ElementType is { } element ? RootAssembly(element) : null,
        _ => null,
    };

    /// <summary>
    /// Whether dropping the callee down to <paramref name="arity"/> leading
    /// arguments is safe: no candidate (other than the callee itself) with at
    /// least that many parameters shares the callee's first <paramref name="arity"/>
    /// parameter types. A candidate that differs at any leading position cannot
    /// rebind (the callee's identity match on the retained arguments is strictly
    /// better), so only a leading-signature duplicate blocks.
    /// </summary>
    static bool AritySafe(MethodRef callee, List<SiblingSignature> siblings, MethodDefinitionHandle selfHandle, int arity)
    {
        foreach (var sibling in siblings)
        {
            if (sibling.Handle == selfHandle)
                continue;  // the callee cannot rebind to itself
            if (sibling.ParameterTypes.Length < arity)
                continue;  // too few parameters to tie the callee at this arity
            bool sharesLeadingSignature = true;
            for (int i = 0; i < arity; i++)
            {
                if (!sibling.ParameterTypes[i].Equals(callee.ParameterTypes[i]))
                {
                    sharesLeadingSignature = false;
                    break;
                }
            }
            if (sharesLeadingSignature)
                return false;  // the sibling ties the callee and could rebind
        }
        return true;
    }

    static bool TryReadConstantValue(MetadataReader reader, SrmConstant constant, out object? value)
    {
        var blob = reader.GetBlobReader(constant.Value);
        switch (constant.TypeCode)
        {
            case ConstantTypeCode.Boolean: value = blob.ReadBoolean(); return true;
            case ConstantTypeCode.Char: value = blob.ReadChar(); return true;
            case ConstantTypeCode.SByte: value = blob.ReadSByte(); return true;
            case ConstantTypeCode.Byte: value = blob.ReadByte(); return true;
            case ConstantTypeCode.Int16: value = blob.ReadInt16(); return true;
            case ConstantTypeCode.UInt16: value = blob.ReadUInt16(); return true;
            case ConstantTypeCode.Int32: value = blob.ReadInt32(); return true;
            case ConstantTypeCode.UInt32: value = blob.ReadUInt32(); return true;
            case ConstantTypeCode.Int64: value = blob.ReadInt64(); return true;
            case ConstantTypeCode.UInt64: value = blob.ReadUInt64(); return true;
            case ConstantTypeCode.Single: value = blob.ReadSingle(); return true;
            case ConstantTypeCode.Double: value = blob.ReadDouble(); return true;
            case ConstantTypeCode.String: value = blob.ReadUTF16(blob.Length); return true;
            case ConstantTypeCode.NullReference: value = null; return true;
            // Invalid / unrecognized (e.g. an attributed decimal/DateTime default
            // never has a Constant row, so this is not reached for well-formed
            // metadata): decline rather than record a guessed value.
            default: value = null; return false;
        }
    }
}
