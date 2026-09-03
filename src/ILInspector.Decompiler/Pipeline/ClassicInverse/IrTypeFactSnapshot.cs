namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// An immutable snapshot of one function's type facts, captured by
/// <see cref="IrFunction.CaptureTypeFacts"/>.
/// <para>
/// A cross-method raise that publishes a detached plan cannot retain the source
/// <see cref="IrFunction"/> — the caller may mutate or discard it before the
/// plan is applied. The facts themselves are already immutable collections, so
/// this record carries them by value.
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
    IReadOnlySet<TypeDefinitionIdentity> InequalityOperatorFreeTypes);
