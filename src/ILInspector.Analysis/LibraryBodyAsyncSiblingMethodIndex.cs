using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Publishes one immutable method-name index per reader-relative type for
/// async-sibling candidate and dispatch analysis.
/// <c>AsyncSiblingMethodIndex_ConcurrentReadsBuildTypeOnce</c> gates
/// synchronized single-publication behavior.
/// </summary>
internal sealed class LibraryBodyAsyncSiblingMethodIndex(
    Action<MetadataReader, MethodDefinitionHandle>? methodScanned = null)
{
    readonly object _gate = new();
    readonly Dictionary<
        MetadataReader,
        Dictionary<
            TypeDefinitionHandle,
            IReadOnlyDictionary<
                string,
                ImmutableArray<MethodDefinitionHandle>>>>
        _methodsByName =
            new(ReferenceEqualityComparer.Instance);
    readonly Action<MetadataReader, MethodDefinitionHandle>?
        _methodScanned = methodScanned;

    internal IReadOnlyDictionary<
        string,
        ImmutableArray<MethodDefinitionHandle>>
        MethodsByName(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle)
    {
        lock (_gate)
        {
            if (!_methodsByName.TryGetValue(
                    reader,
                    out Dictionary<
                        TypeDefinitionHandle,
                        IReadOnlyDictionary<
                            string,
                            ImmutableArray<MethodDefinitionHandle>>>?
                        byType))
            {
                byType = [];
                _methodsByName.Add(reader, byType);
            }
            if (byType.TryGetValue(typeHandle, out var methods))
                return methods;

            var builders = new Dictionary<
                string,
                ImmutableArray<MethodDefinitionHandle>.Builder>(
                    StringComparer.Ordinal);
            foreach (MethodDefinitionHandle methodHandle
                in reader.GetTypeDefinition(typeHandle).GetMethods())
            {
                _methodScanned?.Invoke(
                    reader,
                    methodHandle);
                string name = reader.GetString(
                    reader.GetMethodDefinition(methodHandle).Name);
                if (!builders.TryGetValue(name, out var named))
                {
                    named = ImmutableArray.CreateBuilder<
                        MethodDefinitionHandle>();
                    builders.Add(name, named);
                }
                named.Add(methodHandle);
            }

            var result = new Dictionary<
                string,
                ImmutableArray<MethodDefinitionHandle>>(
                    builders.Count,
                    StringComparer.Ordinal);
            foreach (var pair in builders)
                result.Add(pair.Key, pair.Value.ToImmutable());
            byType.Add(typeHandle, result);
            return result;
        }
    }
}
