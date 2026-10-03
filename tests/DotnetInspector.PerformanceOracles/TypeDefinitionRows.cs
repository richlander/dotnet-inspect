using System.Reflection.Metadata;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

/// <summary>A type definition with its handle, as the source yields it.</summary>
public readonly struct TypeDefinitionRow(
    MetadataReader reader,
    TypeDefinitionHandle handle,
    TypeDefinition type)
{
    public MetadataReader Reader => reader;

    public TypeDefinitionHandle Handle => handle;

    public TypeDefinition Type => type;
}

/// <summary>
/// The type-definition population as an NLinq source: every TypeDef row in
/// metadata order. Its fold is one loop, so a pipeline that ends in a fold
/// runs as a hand-written traversal would.
/// </summary>
/// <remarks>
/// The source lives here rather than beside the type-definition owner
/// because it implements NLinq's enumerator contract, which only this
/// test-infrastructure fixture may reference.
/// </remarks>
public struct TypeDefinitionRows : IEnumerator<TypeDefinitionRows, TypeDefinitionRow>
{
    readonly MetadataReader _reader;
    TypeDefinitionHandleCollection.Enumerator _types;

    public TypeDefinitionRows(MetadataReader reader)
    {
        _reader = reader;
        _types = reader.TypeDefinitions.GetEnumerator();
    }

    public TypeDefinitionRow TryGetNext(out bool hasMore)
    {
        if (!_types.MoveNext())
        {
            hasMore = false;
            return default;
        }

        hasMore = true;
        TypeDefinitionHandle handle = _types.Current;
        return new(_reader, handle, _reader.GetTypeDefinition(handle));
    }

    static TAcc IEnumerator<TypeDefinitionRows, TypeDefinitionRow>.Fold<TAcc, TFunc>(
        scoped ref TypeDefinitionRows @this,
        TAcc accumulator,
        TFunc func)
    {
        MetadataReader reader = @this._reader;
        while (@this._types.MoveNext())
        {
            TypeDefinitionHandle handle = @this._types.Current;
            accumulator = func.Invoke(accumulator, new(reader, handle, reader.GetTypeDefinition(handle)));
        }

        return accumulator;
    }
}
