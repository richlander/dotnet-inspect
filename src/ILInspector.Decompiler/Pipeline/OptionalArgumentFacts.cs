using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

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
/// duplicate exists on the declaring type; a generic callee declines to zero. The
/// recompile/corpus fidelity gate is the empirical backstop for any residual
/// overload-resolution miss.</para>
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
            defaults[index] = new ParameterDefault(true, ReadConstantValue(reader, reader.GetConstant(constantHandle)));
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
        if (callee.DeclaringType.Kind == TypeRefKind.GenericInstance || !callee.TypeArguments.IsDefaultOrEmpty)
            return 0;

        var siblings = SiblingOverloads(reader, method, handle, callee.Name);

        int safe = 0;
        for (int k = 1; k <= trailingDefaults; k++)
        {
            if (!AritySafe(callee, siblings, n - k))
                break;
            safe = k;
        }
        return safe;
    }

    static List<ImmutableArray<TypeRef>> SiblingOverloads(MetadataReader reader, MethodDefinition self, MethodDefinitionHandle selfHandle, string name)
    {
        var siblings = new List<ImmutableArray<TypeRef>>();
        var declaringType = reader.GetTypeDefinition(self.GetDeclaringType());
        var typeScope = new GenericScope(MethodDefinitionFacts.GenericParameterNames(reader, declaringType.GetGenericParameters()), []);
        foreach (var handle in declaringType.GetMethods())
        {
            if (handle == selfHandle)
                continue;
            var method = reader.GetMethodDefinition(handle);
            if (!string.Equals(reader.GetString(method.Name), name, StringComparison.Ordinal))
                continue;
            siblings.Add(GuardedDecode.MethodSignature(reader, method, typeScope).ParameterTypes);
        }
        return siblings;
    }

    /// <summary>
    /// Whether dropping the callee down to <paramref name="arity"/> leading
    /// arguments is safe: no sibling with at least that many parameters shares the
    /// callee's first <paramref name="arity"/> parameter types. A sibling that
    /// differs at any leading position cannot rebind (the callee's identity match
    /// on the retained arguments is strictly better), so only a leading-signature
    /// duplicate blocks.
    /// </summary>
    static bool AritySafe(MethodRef callee, List<ImmutableArray<TypeRef>> siblings, int arity)
    {
        foreach (var sibling in siblings)
        {
            if (sibling.Length < arity)
                continue;  // too few parameters to tie the callee at this arity
            bool sharesLeadingSignature = true;
            for (int i = 0; i < arity; i++)
            {
                if (!sibling[i].Equals(callee.ParameterTypes[i]))
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

    static object? ReadConstantValue(MetadataReader reader, SrmConstant constant)
    {
        var blob = reader.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Boolean => blob.ReadBoolean(),
            ConstantTypeCode.Char => blob.ReadChar(),
            ConstantTypeCode.SByte => blob.ReadSByte(),
            ConstantTypeCode.Byte => blob.ReadByte(),
            ConstantTypeCode.Int16 => blob.ReadInt16(),
            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
            ConstantTypeCode.Int32 => blob.ReadInt32(),
            ConstantTypeCode.UInt32 => blob.ReadUInt32(),
            ConstantTypeCode.Int64 => blob.ReadInt64(),
            ConstantTypeCode.UInt64 => blob.ReadUInt64(),
            ConstantTypeCode.Single => blob.ReadSingle(),
            ConstantTypeCode.Double => blob.ReadDouble(),
            ConstantTypeCode.String => blob.ReadUTF16(blob.Length),
            ConstantTypeCode.NullReference => null,
            _ => null,
        };
    }
}
