using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

/// <summary>A method definition with its declaring type, as the source yields it.</summary>
public readonly struct MethodDefinitionRow(
    MetadataReader reader,
    TypeDefinitionHandle typeHandle,
    TypeDefinition type,
    MethodDefinitionHandle methodHandle,
    MethodDefinition method)
{
    public MetadataReader Reader => reader;

    public TypeDefinitionHandle TypeHandle => typeHandle;

    public TypeDefinition Type => type;

    public MethodDefinitionHandle MethodHandle => methodHandle;

    public MethodDefinition Method => method;

    public int Token => MetadataTokens.GetToken(methodHandle);
}

/// <summary>
/// The method-definition population as an NLinq source: every method, type by
/// type, in metadata order. Its fold is two nested loops, so a pipeline that
/// ends in a fold runs as a hand-written traversal would.
/// </summary>
/// <remarks>
/// The source lives here rather than beside the method-definition owner
/// because it implements NLinq's enumerator contract, which only this
/// test-infrastructure fixture may reference.
/// </remarks>
public struct MethodDefinitionRows : IEnumerator<MethodDefinitionRows, MethodDefinitionRow>
{
    readonly MetadataReader _reader;
    TypeDefinitionHandleCollection.Enumerator _types;
    MethodDefinitionHandleCollection.Enumerator _methods;
    TypeDefinitionHandle _typeHandle;
    TypeDefinition _type;
    bool _inType;

    public MethodDefinitionRows(MetadataReader reader)
    {
        _reader = reader;
        _types = reader.TypeDefinitions.GetEnumerator();
    }

    public MethodDefinitionRow TryGetNext(out bool hasMore)
    {
        while (true)
        {
            if (_inType && _methods.MoveNext())
            {
                hasMore = true;
                MethodDefinitionHandle handle = _methods.Current;
                return new(_reader, _typeHandle, _type, handle, _reader.GetMethodDefinition(handle));
            }

            if (!_types.MoveNext())
            {
                hasMore = false;
                return default;
            }

            _typeHandle = _types.Current;
            _type = _reader.GetTypeDefinition(_typeHandle);
            _methods = _type.GetMethods().GetEnumerator();
            _inType = true;
        }
    }

    static TAcc IEnumerator<MethodDefinitionRows, MethodDefinitionRow>.Fold<TAcc, TFunc>(
        scoped ref MethodDefinitionRows @this,
        TAcc accumulator,
        TFunc func)
    {
        MetadataReader reader = @this._reader;

        // Finish the type a partial pull left open, then fold the remaining
        // types with two nested loops.
        if (@this._inType)
        {
            while (@this._methods.MoveNext())
            {
                MethodDefinitionHandle handle = @this._methods.Current;
                accumulator = func.Invoke(
                    accumulator,
                    new(reader, @this._typeHandle, @this._type, handle, reader.GetMethodDefinition(handle)));
            }

            @this._inType = false;
        }

        while (@this._types.MoveNext())
        {
            TypeDefinitionHandle typeHandle = @this._types.Current;
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            foreach (MethodDefinitionHandle handle in type.GetMethods())
                accumulator = func.Invoke(accumulator, new(reader, typeHandle, type, handle, reader.GetMethodDefinition(handle)));
        }

        return accumulator;
    }
}
