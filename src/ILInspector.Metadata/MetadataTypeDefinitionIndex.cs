using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// Indexes exact TypeDef identities from one metadata image in one bounded
/// pass while sharing enclosing-type identity between nested definitions.
/// </summary>
/// <remarks>
/// Gated by
/// <c>TypeDefinitionIndex_VisitsDefinitionsOnceAndQueriesExactNames</c>,
/// <c>TypeDefinitionIndex_DeepSharedAncestryAllocatesLinearly</c>,
/// <c>TypeDefinitionIndex_DuplicateNamesAllocateLinearly</c>, and
/// <c>TypeDefinitionIndex_RejectsCumulativeNameWorkBeyondBudget</c>.
/// </remarks>
public sealed class MetadataTypeDefinitionIndex
{
    readonly IReadOnlyDictionary<NodeKey, int> nodesByKey;
    readonly IReadOnlyList<IndexedNode> nodes;

    MetadataTypeDefinitionIndex(
        IReadOnlyDictionary<NodeKey, int> nodesByKey,
        IReadOnlyList<IndexedNode> nodes)
    {
        this.nodesByKey = nodesByKey;
        this.nodes = nodes;
    }

    public static MetadataTypeDefinitionIndex Create(
        MetadataReader reader)
    {
        try
        {
            return Create(reader, definitionVisited: null);
        }
        catch (MetadataTypeDefinitionIndexBudgetException ex)
        {
            throw new BadImageFormatException(ex.Message, ex);
        }
    }

    internal static MetadataTypeDefinitionIndex Create(
        MetadataReader reader,
        Action<TypeDefinitionHandle>? definitionVisited)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int rowCount = reader.GetTableRowCount(TableIndex.TypeDef);
        var nodeByRow = new int[rowCount + 1];
        var nodesByKey = new Dictionary<NodeKey, int>();
        var nodes = new List<MutableIndexedNode>
        {
            default,
        };
        long remainingWork =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars;
        Span<TypeDefinitionHandle> path =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            definitionVisited?.Invoke(handle);
            int row = MetadataTokens.GetRowNumber(handle);
            if (nodeByRow[row] != 0)
                continue;

            int count = 0;
            TypeDefinitionHandle current = handle;
            int parentNode = 0;
            while (!current.IsNil)
            {
                int currentRow = MetadataTokens.GetRowNumber(current);
                if ((uint)currentRow >= (uint)nodeByRow.Length)
                    throw MalformedIndex();
                if (nodeByRow[currentRow] is int existing
                    && existing != 0)
                {
                    parentNode = existing;
                    break;
                }
                for (int i = 0; i < count; i++)
                {
                    if (path[i] == current)
                        throw MalformedIndex();
                }
                if (count == path.Length)
                    throw MalformedIndex();

                path[count++] = current;
                try
                {
                    current = reader.GetTypeDefinition(current)
                        .GetDeclaringType();
                }
                catch (Exception ex)
                    when (ex is BadImageFormatException
                        or ArgumentOutOfRangeException)
                {
                    throw MalformedIndex(ex);
                }
            }

            int parentDepth = parentNode == 0
                ? 0
                : nodes[parentNode].Depth;
            if (parentDepth + count
                > MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                throw MalformedIndex();
            }

            for (int i = count - 1; i >= 0; i--)
            {
                TypeDefinitionHandle definitionHandle = path[i];
                TypeDefinition definition;
                try
                {
                    definition = reader.GetTypeDefinition(
                        definitionHandle);
                }
                catch (Exception ex)
                    when (ex is BadImageFormatException
                        or ArgumentOutOfRangeException)
                {
                    throw MalformedIndex(ex);
                }

                string name = ReadBounded(
                    reader,
                    definition.Name,
                    ref remainingWork);
                string @namespace = parentNode == 0
                    ? ReadBounded(
                        reader,
                        definition.Namespace,
                        ref remainingWork,
                        allowEmpty: true)
                    : "";
                var key = new NodeKey(
                    parentNode,
                    @namespace,
                    name);
                if (nodesByKey.TryGetValue(
                        key,
                        out int existingNode))
                {
                    MutableIndexedNode existing = nodes[existingNode];
                    existing.AdditionalHandles ??= [];
                    existing.AdditionalHandles.Add(
                        definitionHandle);
                    nodes[existingNode] = existing;
                    parentNode = existingNode;
                }
                else
                {
                    parentNode = nodes.Count;
                    nodesByKey.Add(key, parentNode);
                    nodes.Add(new(
                        definitionHandle,
                        nodes[key.Parent].Depth + 1));
                }
                nodeByRow[
                    MetadataTokens.GetRowNumber(definitionHandle)] =
                        parentNode;
            }
        }

        var immutableNodes = new IndexedNode[nodes.Count];
        for (int i = 1; i < nodes.Count; i++)
        {
            MutableIndexedNode node = nodes[i];
            ImmutableArray<TypeDefinitionHandle> handles;
            if (node.AdditionalHandles is null)
            {
                handles = [node.FirstHandle];
            }
            else
            {
                var builder =
                    ImmutableArray.CreateBuilder<TypeDefinitionHandle>(
                        node.AdditionalHandles.Count + 1);
                builder.Add(node.FirstHandle);
                builder.AddRange(node.AdditionalHandles);
                handles = builder.MoveToImmutable();
            }

            immutableNodes[i] = new(
                handles,
                node.Depth,
                Ambiguous: node.AdditionalHandles is not null);
        }

        return new(nodesByKey, immutableNodes);
    }

    public bool TryGetUniqueDefinition(
        MetadataTypeDefinitionName name,
        out TypeDefinitionHandle handle)
        => TryGetDefinition(
            name,
            out handle,
            out _);

    internal bool TryGetDefinition(
        MetadataTypeDefinitionName name,
        out TypeDefinitionHandle handle,
        out bool ambiguous)
    {
        bool found = TryGetDefinitions(
            name,
            out ImmutableArray<TypeDefinitionHandle> handles,
            out ambiguous);
        handle = found && !ambiguous
            ? handles[0]
            : default;
        return found && !ambiguous;
    }

    internal bool TryGetDefinitions(
        MetadataTypeDefinitionName name,
        out ImmutableArray<TypeDefinitionHandle> handles,
        out bool ambiguous)
    {
        ArgumentNullException.ThrowIfNull(name);
        ambiguous = false;
        int parentNode = 0;
        for (int i = 0; i < name.Segments.Length; i++)
        {
            var key = new NodeKey(
                parentNode,
                i == 0 ? name.Namespace : "",
                name.Segments[i]);
            if (!nodesByKey.TryGetValue(key, out parentNode))
            {
                handles = [];
                return false;
            }
            if (nodes[parentNode].Ambiguous)
                ambiguous = true;
        }

        IndexedNode definition = nodes[parentNode];
        handles = definition.Handles;
        return true;
    }

    static string ReadBounded(
        MetadataReader reader,
        StringHandle handle,
        ref long remainingWork,
        bool allowEmpty = false)
    {
        try
        {
            int charge = reader.GetBlobReader(handle).Length;
            if ((!allowEmpty && charge == 0)
                || charge > MetadataSafetyPolicy.MaxTypeNameCharacters)
            {
                throw MalformedIndex();
            }
            remainingWork -= Math.Max(charge, 1);
            if (remainingWork < 0)
            {
                throw new MetadataTypeDefinitionIndexBudgetException(
                    "The TypeDef name index exceeded its structural-name "
                    + "work budget.");
            }
            return reader.GetString(handle);
        }
        catch (Exception ex)
            when (ex is ArgumentOutOfRangeException)
        {
            throw MalformedIndex(ex);
        }
    }

    static BadImageFormatException MalformedIndex(
        Exception? inner = null) =>
        new("A TypeDef name could not be indexed.", inner);

    readonly record struct NodeKey(
        int Parent,
        string Namespace,
        string Name);

    readonly record struct IndexedNode(
        ImmutableArray<TypeDefinitionHandle> Handles,
        int Depth,
        bool Ambiguous);

    record struct MutableIndexedNode(
        TypeDefinitionHandle FirstHandle,
        int Depth)
    {
        internal List<TypeDefinitionHandle>? AdditionalHandles
        {
            get;
            set;
        }
    }
}

internal sealed class MetadataTypeDefinitionIndexBudgetException(
    string message) : BadImageFormatException(message);
