using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Proves that the chain charges the spellability decode owes are actually
/// paid, by reading what the ledger consumed.
///
/// The sibling syntax gates in <c>SignatureSpellabilityBudgetBoundaryTests</c>
/// prove that a charge is *spelled* at every read site. That is not the same
/// property, and the difference has been exploited repeatedly: a charge can be
/// spelled correctly and still never run, whether it sits behind a guard that
/// is false for every real chain length, inside an uninvoked local function, or
/// in a branch the traversal cannot reach. Two successive generations of the
/// syntax gate were broken that way by reviewers, and a third hole was found by
/// mutation testing.
///
/// No input can expose a missing chain charge through the decode's own
/// behaviour. The chain charges are bounded far below the metadata-work ceiling
/// -- that headroom is deliberate, and is what makes the budget safe for real
/// assemblies -- so removing them never turns an accepted signature into a
/// rejected one. Reading the consumed total is therefore the only evidence that
/// distinguishes a charge which ran from one which merely exists.
/// </summary>
public sealed class SignatureSpellabilityChainChargeTests
{
    [Fact]
    public void TypeDefinitionDecode_ChargesItsDeclaringChainOnce()
    {
        TypeDefinitionHandle leaf = default;
        MetadataReader reader = BuildAssembly(metadata =>
        {
            TypeDefinitionHandle outer = AddType(metadata, "N", "Outer");
            TypeDefinitionHandle mid = AddType(metadata, null, "Mid");
            leaf = AddType(metadata, null, "Leaf");
            metadata.AddNestedType(mid, outer);
            metadata.AddNestedType(leaf, mid);
        });

        var provider = new SignatureOccurrenceProvider();
        provider.GetTypeFromDefinition(reader, leaf, rawTypeKind: 0);

        // ChargeName charges the namespace plus every segment; the declaring
        // chain adds one unit per node walked. "N" + Outer + Mid + Leaf is
        // 1 + 5 + 3 + 4 characters, over a chain of 3 nodes.
        const long NameCharacters = 1 + 5 + 3 + 4;
        const long ChainNodes = 3;
        Assert.Equal(
            NameCharacters + ChainNodes,
            provider.ConsumedMetadataWork);
    }

    [Fact]
    public void TypeReferenceDecode_ChargesBothWalksOfItsResolutionScope()
    {
        // GetTypeFromReference walks the resolution scope twice: once inside
        // the name read, and once more to recover the terminal. Both walks are
        // real work, so both are charged. Comparing two chains that differ by a
        // single node isolates that multiplicity from every other charge --
        // the scope projection and its assembly-name charges are identical on
        // both sides and cancel.
        (long Shallow, long Deep) consumed = ConsumeReferenceChains();

        const long NameCharacterDelta = 4; // the extra "Deep" segment
        const long ChainNodeDelta = 1;
        const long WalksPerRead = 2;

        Assert.Equal(
            NameCharacterDelta + (ChainNodeDelta * WalksPerRead),
            consumed.Deep - consumed.Shallow);
    }

    static (long Shallow, long Deep) ConsumeReferenceChains()
    {
        TypeReferenceHandle inner = default;
        TypeReferenceHandle deep = default;
        MetadataReader reader = BuildAssembly(metadata =>
        {
            AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
                metadata.GetOrAddString("Ext"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            TypeReferenceHandle root = metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Root"));
            inner = metadata.AddTypeReference(
                root,
                default,
                metadata.GetOrAddString("Inner"));
            deep = metadata.AddTypeReference(
                inner,
                default,
                metadata.GetOrAddString("Deep"));
        });

        // Separate providers, because the projection caches would otherwise
        // make the second decode free.
        var shallowProvider = new SignatureOccurrenceProvider();
        shallowProvider.GetTypeFromReference(reader, inner, rawTypeKind: 0);

        var deepProvider = new SignatureOccurrenceProvider();
        deepProvider.GetTypeFromReference(reader, deep, rawTypeKind: 0);

        return (
            shallowProvider.ConsumedMetadataWork,
            deepProvider.ConsumedMetadataWork);
    }

    static TypeDefinitionHandle AddType(
        MetadataBuilder metadata,
        string? @namespace,
        string name) =>
        metadata.AddTypeDefinition(
            @namespace is null
                ? TypeAttributes.NestedPublic
                : TypeAttributes.Public,
            @namespace is null
                ? default
                : metadata.GetOrAddString(@namespace),
            metadata.GetOrAddString(name),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

    static MetadataReader BuildAssembly(Action<MetadataBuilder> addRows)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        addRows(metadata);

        var root = new MetadataRootBuilder(metadata, suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(image, 0, 0);
        return MetadataReaderProvider
            .FromMetadataImage(ImmutableArray.Create(image.ToArray()))
            .GetMetadataReader();
    }
}
