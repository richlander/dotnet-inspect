using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// An immutable snapshot of one function's type facts, captured by
/// <see cref="IrFunction.CaptureTypeFacts"/>.
/// <para>
/// A cross-method raise that publishes a detached plan cannot retain the source
/// <see cref="IrFunction"/> — the caller may mutate or discard it before the
/// plan is applied. Capture copies every collection, including nested enum
/// maps, so callers cannot mutate the published snapshot through an interface
/// backed by a mutable collection.
/// </para>
/// </summary>
internal sealed record IrTypeFactSnapshot(
    IReadOnlyDictionary<TypeRef, TypeShape> TypeShapes,
    IReadOnlyDictionary<TypeRef, TypeDefinitionIdentity> TypeFactIdentities,
    IReadOnlySet<TypeRef> AmbiguousTypeFacts,
    IReadOnlyDictionary<TypeRef, IReadOnlyDictionary<long, string>> EnumMembers,
    IReadOnlyDictionary<TypeRef, TypeRef> EnumUnderlyingTypes,
    IReadOnlySet<TypeRef> CollectionInitializerTypes,
    IReadOnlySet<TypeRef> UnionTypes,
    IReadOnlySet<TypeRef> ByRefLikeTypes,
    IReadOnlySet<TypeRef> InterfaceTypes,
    IReadOnlySet<TypeDefinitionIdentity> EqualityOperatorFreeTypes,
    IReadOnlySet<TypeDefinitionIdentity> InequalityOperatorFreeTypes)
{
    internal string Signature => string.Join(
        "|",
        [
            Map(TypeShapes, static value => value.ToString()),
            Map(TypeFactIdentities, Identity),
            Set(AmbiguousTypeFacts, Type),
            Map(
                EnumMembers,
                static members => string.Join(
                    ",",
                    members.OrderBy(static pair => pair.Key)
                        .Select(pair =>
                            $"{pair.Key}={Part(pair.Value)}"))),
            Map(EnumUnderlyingTypes, Type),
            Set(CollectionInitializerTypes, Type),
            Set(UnionTypes, Type),
            Set(ByRefLikeTypes, Type),
            Set(InterfaceTypes, Type),
            Set(EqualityOperatorFreeTypes, Identity),
            Set(InequalityOperatorFreeTypes, Identity),
        ]);

    static string Map<TValue>(
        IReadOnlyDictionary<TypeRef, TValue> map,
        Func<TValue, string> value)
        => string.Join(
            ",",
            map.Select(pair =>
                    $"{Type(pair.Key)}={value(pair.Value)}")
                .Order(StringComparer.Ordinal));

    static string Set<T>(
        IEnumerable<T> values,
        Func<T, string> value)
        => string.Join(
            ",",
            values.Select(value).Order(StringComparer.Ordinal));

    static string Identity(TypeDefinitionIdentity identity)
    {
        string definitionName =
            $"{Part(identity.DefinitionName.Namespace)}"
            + string.Join(
                "",
                identity.DefinitionName.Segments.Select(Part));
        string assembly = identity.ResolutionAssembly is null
            ? "-"
            : string.Join(
                "",
                [
                    Part(identity.ResolutionAssembly.Name),
                    Part(identity.ResolutionAssembly.Version?.ToString() ?? ""),
                    Part(identity.ResolutionAssembly.Culture ?? ""),
                    Part(identity.ResolutionAssembly.PublicKeyToken ?? ""),
                ]);
        return $"{Type(identity.Definition)}:{definitionName}:{assembly}";
    }

    static string Type(TypeRef type)
        => string.Join(
            ":",
            [
                type.Kind.ToString(),
                Part(type.Assembly),
                Part(type.Namespace),
                Part(type.Name),
                type.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture),
                type.GenericParameterIndex.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                Part(type.GenericParameterName),
                Part(type.UnsupportedReason),
                Part(type.CallingConvention),
                type.ArrayShapeIsExact ? "1" : "0",
                type.FunctionPointerSignatureIsExact ? "1" : "0",
                type.DefinitionModuleVersionId?.ToString("D") ?? "-",
                type.DefinitionHandle.IsNil
                    ? "-"
                    : MetadataTokens.GetToken(type.DefinitionHandle)
                        .ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                type.ElementType is null ? "-" : Type(type.ElementType),
                string.Join(",", type.TypeArguments.Select(Type)),
                string.Join(",", type.FunctionPointerParameterRefKinds),
            ]);

    static string Part(string value) => $"{value.Length}:{value}";
}
