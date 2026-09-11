using System.Reflection.Metadata;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Classifies primary-image types whose generated provenance is inherited from
/// the type or one of its enclosing types.
/// </summary>
internal sealed class LibraryBodyGeneratedProvenanceClassifier
{
    readonly MetadataReader _reader;
    readonly Func<CustomAttributeHandleCollection, bool>
        _hasGeneratedCodeAttribute;
    readonly Action<TypeDefinitionHandle>? _typeClassified;
    readonly Dictionary<TypeDefinitionHandle, bool>
        _sourceGeneratedTypes = new();

    internal LibraryBodyGeneratedProvenanceClassifier(
        MetadataReader reader,
        Func<CustomAttributeHandleCollection, bool>
            hasGeneratedCodeAttribute,
        Action<TypeDefinitionHandle>? typeClassified)
    {
        _reader = reader;
        _hasGeneratedCodeAttribute = hasGeneratedCodeAttribute;
        _typeClassified = typeClassified;
    }

    internal bool IsSourceGeneratedTypeOrEnclosing(
        TypeDefinitionHandle handle)
    {
        if (_sourceGeneratedTypes.TryGetValue(handle, out bool cached))
            return cached;

        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        int count = 0;
        TypeDefinitionHandle current = handle;
        bool inherited = false;
        while (!current.IsNil)
        {
            if (_sourceGeneratedTypes.TryGetValue(
                    current,
                    out inherited))
            {
                break;
            }
            for (int i = 0; i < count; i++)
            {
                if (chain[i] == current)
                {
                    inherited = true;
                    goto CacheChain;
                }
            }
            if (count == chain.Length)
            {
                inherited = true;
                goto CacheChain;
            }

            chain[count++] = current;
            try
            {
                current = _reader.GetTypeDefinition(current)
                    .GetDeclaringType();
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                inherited = true;
                goto CacheChain;
            }
        }

    CacheChain:
        for (int i = count - 1; i >= 0; i--)
        {
            TypeDefinitionHandle candidate = chain[i];
            if (!inherited)
            {
                _typeClassified?.Invoke(candidate);
                inherited = _hasGeneratedCodeAttribute(
                    _reader.GetTypeDefinition(candidate)
                        .GetCustomAttributes());
            }
            _sourceGeneratedTypes[candidate] = inherited;
            if (inherited)
            {
                for (int j = i - 1; j >= 0; j--)
                    _sourceGeneratedTypes[chain[j]] = true;
                return true;
            }
        }
        return inherited;
    }
}
