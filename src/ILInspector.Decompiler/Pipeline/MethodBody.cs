using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

public enum HandlerKind { Catch, Filter, Finally, Fault }

/// <summary>An exception region, fully materialized (no metadata handles).</summary>
public sealed record HandlerRegion(
    HandlerKind Kind,
    int TryOffset,
    int TryLength,
    int HandlerOffset,
    int HandlerLength,
    int FilterOffset,
    TypeRef? CatchType);

/// <summary>
/// A method body as plain data: no metadata handles, no lifetime — safe to
/// hold after its <see cref="MetadataSource"/> is disposed.
/// </summary>
public sealed record MethodBody(
    ImmutableArray<byte> IL,
    int MaxStack,
    ImmutableArray<TypeRef> Locals,
    ImmutableArray<string?> LocalNames,
    ImmutableArray<HandlerRegion> Handlers,
    bool SkipLocalsInit = false);

/// <summary>A parameter: name, symbolic type, and whether metadata declares an optional default.</summary>
public sealed record Parameter(string Name, TypeRef Type, bool HasDefault = false);

/// <summary>A method signature with symbolic types throughout.</summary>
public sealed record MethodSignature(
    TypeRef ReturnType,
    ImmutableArray<Parameter> Parameters,
    bool HasThis,
    int GenericParameterCount)
{
    /// <summary>
    /// The method's own generic parameter names (e.g. <c>T</c>), when known; empty
    /// otherwise. A method type parameter shares the declaration space with the
    /// declaring type's members, so it shadows a same-named static member — an
    /// unqualified call to that member would bind to the type parameter (CS0119).
    /// </summary>
    public ImmutableArray<string> GenericParameterNames { get; init; } = [];
}

/// <summary>
/// The importer's output: declaring type, name, signature, and body — all
/// materialized, valid after the source is disposed.
/// </summary>
public sealed record ImportedMethod(
    TypeRef DeclaringType,
    string Name,
    MethodSignature Signature,
    MethodBody Body,
    MetadataFactState CompilerGenerated = MetadataFactState.Unknown,
    MetadataFactState DeclaringTypeCompilerGenerated = MetadataFactState.Unknown,
    MetadataFactState IsRuntimeAsync = MetadataFactState.Unknown,
    int MetadataToken = 0);
