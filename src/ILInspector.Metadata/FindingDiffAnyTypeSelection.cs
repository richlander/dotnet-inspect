using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public static partial class FindingDiffAny
{
    internal sealed partial class Side
    {
        // A side that admits one selected Type reads no unit table, so the
        // Type keys two rows share are found on demand, from the bytes the
        // keys spell, when a reference first needs to reject one.
        bool _selectedOnly;
        HashSet<int>? _sharedKeyRows;

        /// <summary>
        /// The unit of one selected row, read as <see cref="ReadUnits"/> reads
        /// it without keying any other row: the Type and the generated Types it
        /// owns. Null when the row is generated code or beneath it, which no
        /// Type level reports. A row whose key another row shares, or whose
        /// name or declaring chain cannot be read, is a malformed unit.
        /// </summary>
        public Unit? SelectedUnit(TypeDefinitionHandle handle)
        {
            _selectedOnly = true;
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            try
            {
                if (!TryDeclaringChain(handle, chain, out int length))
                    return MalformedUnit(handle);
                for (int i = 0; i < length; i++)
                {
                    if (IsGeneratedName(Md.GetTypeDefinition(chain[i])))
                        return null;
                }
                if (EscapedName(handle) is not { } identifier || SharesTypeKey(handle))
                    return MalformedUnit(handle);

                // A generated Type nested in this one belongs to it with every
                // Type beneath it; a declared nested Type is its own unit.
                var generated = new List<TypeDefinitionHandle>();
                var visited = new HashSet<TypeDefinitionHandle>();
                foreach (TypeDefinitionHandle nested in NestedIn(handle))
                {
                    if (IsGeneratedName(Md.GetTypeDefinition(nested)))
                        AddGenerated(nested, generated, visited, length + 1);
                }
                generated.Sort(static (x, y) => MetadataTokens.GetRowNumber(x).CompareTo(MetadataTokens.GetRowNumber(y)));
                return new Unit(DisplayName(handle), identifier, handle, generated);
            }
            catch (Exception ex) when (IsMalformed(ex))
            {
                Abandon();
                return MalformedUnit(handle);
            }
        }

        Unit MalformedUnit(TypeDefinitionHandle handle)
            => new(SafeName(handle), $"!{MetadataTokens.GetToken(handle):X8}", handle, [], Malformed: true);

        // ReadUnits attributes a row through its declaring chain, once, so a
        // row belongs beneath the one parent GetDeclaringType names. The
        // NestedClass table can list a row under several enclosing rows, or
        // under one parent more than once; following only the declaring parent
        // and skipping a visited row makes the walk a tree that visits each row
        // once. A row deeper than the chain bound is its own malformed unit
        // there, so it is not attributed here either.
        void AddGenerated(TypeDefinitionHandle type, List<TypeDefinitionHandle> generated,
            HashSet<TypeDefinitionHandle> visited, int depth)
        {
            if (depth > MetadataSafetyPolicy.MaxRelationshipNodes || !visited.Add(type))
                return;
            generated.Add(type);
            foreach (TypeDefinitionHandle nested in NestedIn(type))
                AddGenerated(nested, generated, visited, depth + 1);
        }

        IEnumerable<TypeDefinitionHandle> NestedIn(TypeDefinitionHandle parent)
        {
            foreach (TypeDefinitionHandle nested in Md.GetTypeDefinition(parent).GetNestedTypes())
            {
                if (Md.GetTypeDefinition(nested).GetDeclaringType() == parent)
                    yield return nested;
            }
        }

        /// <summary>
        /// Whether another row spells this row's Type key, as
        /// <see cref="ReadUnits"/> would find while keying every row: two
        /// well-formed declared rows whose root namespaces and declaring-chain
        /// names are equal byte for byte.
        /// </summary>
        bool SharesTypeKey(TypeDefinitionHandle handle)
            => (_sharedKeyRows ??= SharedKeyRows()).Contains(MetadataTokens.GetRowNumber(handle));

        // One pass over the TypeDef table that hashes stored name bytes and
        // decodes nothing. Each row's group is its enclosing row's group and
        // its own name, plus the namespace for a root, which is what a Type
        // key spells; only groups of two or more are checked for spellings.
        HashSet<int> SharedKeyRows()
        {
            int count = Md.TypeDefinitions.Count;
            var groups = new int[count + 1];
            var representative = new List<int> { 0 };
            var size = new List<int> { 0 };
            var next = new List<int> { 0 };
            var buckets = new Dictionary<(int Enclosing, int Hash), int>();
            Span<TypeDefinitionHandle> chain =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            for (int row = 1; row <= count; row++)
            {
                if (groups[row] != 0)
                    continue;
                try
                {
                    if (!TryDeclaringChain(MetadataTokens.TypeDefinitionHandle(row), chain, out int length))
                    {
                        groups[row] = -1;
                        continue;
                    }
                    int enclosing = 0;
                    for (int i = 0; i < length; i++)
                    {
                        int current = MetadataTokens.GetRowNumber(chain[i]);
                        if (groups[current] == 0)
                            groups[current] = Group(chain[i], enclosing, root: i == 0);
                        enclosing = groups[current];
                        if (enclosing < 0)
                        {
                            // Generated or unreadable: never a keyed unit, and
                            // nor is any Type beneath it.
                            for (int j = i + 1; j < length; j++)
                                groups[MetadataTokens.GetRowNumber(chain[j])] = -1;
                            break;
                        }
                    }
                }
                catch (Exception ex) when (IsMalformed(ex))
                {
                    // The scan builds no key, so a key build it interrupts
                    // stays open for the reference that asked.
                    groups[row] = -1;
                }
            }

            var shared = new HashSet<int>();
            for (int row = 1; row <= count; row++)
            {
                if (groups[row] > 0 && size[groups[row]] > 1 && Spells(row))
                    shared.Add(row);
            }
            // A row whose spelling fails is a malformed unit that keys no
            // Type, so a group it leaves with one spelled row is not shared.
            var spelled = new Dictionary<int, int>();
            foreach (int row in shared)
                spelled[groups[row]] = spelled.GetValueOrDefault(groups[row]) + 1;
            shared.RemoveWhere(row => spelled[groups[row]] < 2);
            return shared;

            int Group(TypeDefinitionHandle handle, int enclosing, bool root)
            {
                TypeDefinition definition = Md.GetTypeDefinition(handle);
                if (IsGeneratedName(definition) || !Names.IsWellFormed(definition.Name)
                    || (root && !definition.Namespace.IsNil && !Names.IsWellFormed(definition.Namespace)))
                {
                    return -1;
                }
                ReadOnlySpan<byte> name = Names.Bytes(definition.Name);
                bool nilSpace = !root || definition.Namespace.IsNil;
                ReadOnlySpan<byte> space = nilSpace ? default : Names.Bytes(definition.Namespace);
                var hash = new HashCode();
                hash.AddBytes(name);
                hash.Add(nilSpace);
                hash.AddBytes(space);
                (int, int) key = (enclosing, hash.ToHashCode());
                for (int group = buckets.GetValueOrDefault(key); group != 0; group = next[group])
                {
                    TypeDefinition other = Md.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(representative[group]));
                    if (!Names.Bytes(other.Name).SequenceEqual(name))
                        continue;
                    if (root && (other.Namespace.IsNil != nilSpace
                        || (!nilSpace && !Names.Bytes(other.Namespace).SequenceEqual(space))))
                    {
                        continue;
                    }
                    size[group]++;
                    return group;
                }
                int created = representative.Count;
                representative.Add(MetadataTokens.GetRowNumber(handle));
                size.Add(1);
                next.Add(buckets.GetValueOrDefault(key));
                buckets[key] = created;
                return created;
            }

            bool Spells(int row)
            {
                try
                {
                    return EscapedName(MetadataTokens.TypeDefinitionHandle(row)) is not null;
                }
                catch (Exception ex) when (IsMalformed(ex))
                {
                    return false;
                }
            }
        }
    }
}
