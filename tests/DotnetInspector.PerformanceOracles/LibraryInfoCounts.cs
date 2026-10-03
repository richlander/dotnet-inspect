using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

/// <summary>
/// The Library Info Count rows this scorecard asks together. Every lane reads
/// metadata tables and custom-attribute constructor names; none decodes IL.
/// </summary>
public enum LibraryInfoLane
{
    AsyncMethods,
    ExtensionMethods,
    UnionTypes,
}

/// <summary>One lane's count; a multi-question answer is one row per lane in lane order.</summary>
public readonly record struct LaneCount(LibraryInfoLane Lane, int Count)
{
    public override string ToString() => $"{Lane}={Count}";
}

/// <summary>
/// The lanes' predicates, each the same test the product path for that row
/// applies, through product-owned predicates. Only the read machinery
/// differs between columns.
/// </summary>
public static class LibraryInfoLanes
{
    public static IReadOnlyList<LibraryInfoLane> Lanes { get; } =
        [LibraryInfoLane.AsyncMethods, LibraryInfoLane.ExtensionMethods, LibraryInfoLane.UnionTypes];

    /// <summary>Union Types: a TypeDef carrying <c>[Union]</c>, as <c>UnionTypeScanner</c> selects it.</summary>
    public static bool IsUnionType(MetadataReader reader, TypeDefinition type) =>
        AttributeReader.HasAttribute(reader, type.GetCustomAttributes(), KnownAttributeNames.UnionAttribute);

    /// <summary>
    /// Extension Methods, type half: a static (sealed abstract) TypeDef carrying
    /// <c>[Extension]</c> and not hidden, as <c>ExtensionMethodScanner.FindAllExtensions</c>
    /// selects it with <c>includeAll: false</c>.
    /// </summary>
    public static bool IsExtensionType(MetadataReader reader, TypeDefinition type)
    {
        TypeAttributes attributes = type.Attributes;
        if ((attributes & TypeAttributes.Sealed) == 0 || (attributes & TypeAttributes.Abstract) == 0)
            return false;
        return AttributeReader.HasExtensionAttribute(reader, type.GetCustomAttributes())
            && !AttributeReader.HasHiddenAttribute(reader, type.GetCustomAttributes());
    }

    /// <summary>
    /// Extension Methods, method half: a public static method carrying
    /// <c>[Extension]</c>, not hidden, whose signature decodes with at least
    /// one parameter. Extension properties (C# 14 extension blocks) are the
    /// row's other half and are not a lane here: their selection walks
    /// nested marker types through a private product path.
    /// </summary>
    public static bool IsExtensionMethod(MetadataReader reader, TypeDefinition type, MethodDefinition method)
    {
        MethodAttributes attributes = method.Attributes;
        if ((attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            return false;
        if ((attributes & MethodAttributes.Static) == 0)
            return false;
        if (!AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes()))
            return false;
        if (AttributeReader.HasHiddenAttribute(reader, method.GetCustomAttributes()))
            return false;
        GenericContext context = GenericContext.ForMethod(reader, type, method);
        return GuardedSignatureText.MethodText(reader, method, context).TryGetValue(out MethodSignature<string> signature)
            && signature.ParameterTypes.Length > 0;
    }

    /// <summary>Async Methods: the gate, then the async tests, as the Planner's Count terminal applies them.</summary>
    public static bool IsAsyncMethod(MethodClassificationCallState state, TypeDefinition type, MethodDefinition method) =>
        MethodClassificationGate.TypeInScope(state.Reader, type)
        && MethodClassificationGate.MethodInScope(state.Reader, method)
        && MethodClassificationTests.Classify(ClassificationPopulation.Async, state, method) is not null;
}

/// <summary>
/// A pinned asset the Library Info scorecard runs over: the image bytes as a
/// <see cref="PEReader"/> for the comparator columns, and an inspection
/// session over the same file for the production (Old) column. Both are
/// opened once, outside every timed cell.
/// </summary>
public sealed class LibraryInfoAsset(string path) : IDisposable
{
    public PEReader Reader { get; } = new(System.Collections.Immutable.ImmutableArray.Create(File.ReadAllBytes(path)));

    public AssemblyInspectionSession Session { get; } = AssemblyInspectionSession.Open(path);

    public void Dispose()
    {
        Session.Dispose();
        Reader.Dispose();
    }
}

/// <summary>
/// The multi-question Count scorecard's comparator columns: three independent
/// LINQ pipelines, three independent NLinq folds (the oracle), and one
/// hand-fused NLinq fold that answers every lane in one pass (the ceiling).
/// Each column admits the image as the product scanners do, then reads.
/// </summary>
public static class LibraryInfoCounts
{
    public const string OracleName = "NLinq ×3";

    public static IReadOnlyList<ScorecardAsset<LibraryInfoAsset>> LoadAssets(IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> names = ScorecardAssetNames.FromPaths(paths);
        var assets = new List<ScorecardAsset<LibraryInfoAsset>>(paths.Count);
        for (int i = 0; i < paths.Count; i++)
            assets.Add(new(names[i], new LibraryInfoAsset(paths[i])));
        return assets;
    }

    public static string RowText(LaneCount row) => row.ToString();

    public static ScorecardColumn<LibraryInfoAsset, LaneCount> LinqColumn() =>
        new("LINQ ×3", (closing, asset) => Answer(closing, Linq(Admit(asset))));

    public static ScorecardColumn<LibraryInfoAsset, LaneCount> NLinqColumn() =>
        new(OracleName, (closing, asset) => Answer(closing, NLinqIndependent(Admit(asset))));

    public static ScorecardColumn<LibraryInfoAsset, LaneCount> FusedColumn() =>
        new("NLinq fused", (closing, asset) => Answer(closing, Fused(Admit(asset))));

    /// <summary>Lane counts as one answer: a row per lane, in lane order.</summary>
    public static ScorecardAnswer<LaneCount> Answer(ScorecardClosing closing, LaneCounts counts)
    {
        if (closing != ScorecardClosing.Count)
            throw new ArgumentOutOfRangeException(nameof(closing), "The Library Info scorecard asks Count only.");
        return ScorecardAnswer<LaneCount>.OfRows(counts.ToRows());
    }

    static MetadataReader Admit(LibraryInfoAsset asset)
    {
        if (!MetadataFormatAdmission.AdmitImage(asset.Reader))
            throw new BadImageFormatException("The scorecard asset is not an admitted managed image.");
        return MetadataFormatAdmission.GetMetadataReader(asset.Reader);
    }

    /// <summary>Three idiomatic streaming System.Linq pipelines, one per lane.</summary>
    public static LaneCounts Linq(MetadataReader reader)
    {
        var state = new MethodClassificationCallState(reader);
        int async = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Where(type => MethodClassificationGate.TypeInScope(reader, type))
            .SelectMany(type => type.GetMethods().Select(handle => (Type: type, Method: reader.GetMethodDefinition(handle))))
            .Count(row => MethodClassificationGate.MethodInScope(reader, row.Method)
                && MethodClassificationTests.Classify(ClassificationPopulation.Async, state, row.Method) is not null);
        int extension = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Where(type => LibraryInfoLanes.IsExtensionType(reader, type))
            .SelectMany(type => type.GetMethods().Select(handle => (Type: type, Method: reader.GetMethodDefinition(handle))))
            .Count(row => LibraryInfoLanes.IsExtensionMethod(reader, row.Type, row.Method));
        int union = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Count(type => LibraryInfoLanes.IsUnionType(reader, type));
        return new(async, extension, union);
    }

    /// <summary>Three NLinq count folds, one per lane, each over its population source.</summary>
    public static LaneCounts NLinqIndependent(MetadataReader reader)
    {
        int async = new MethodDefinitionRows(reader)
            .Where<MethodDefinitionRows, MethodDefinitionRow, AsyncSelect>(new(new MethodClassificationCallState(reader)))
            .CountFold<Filter<MethodDefinitionRow, MethodDefinitionRows, AsyncSelect>, MethodDefinitionRow>();
        int extension = new MethodDefinitionRows(reader)
            .Where<MethodDefinitionRows, MethodDefinitionRow, ExtensionSelect>(default)
            .CountFold<Filter<MethodDefinitionRow, MethodDefinitionRows, ExtensionSelect>, MethodDefinitionRow>();
        int union = new TypeDefinitionRows(reader)
            .Where<TypeDefinitionRows, TypeDefinitionRow, UnionSelect>(default)
            .CountFold<Filter<TypeDefinitionRow, TypeDefinitionRows, UnionSelect>, TypeDefinitionRow>();
        return new(async, extension, union);
    }

    /// <summary>One fold over the TypeDef source answering every lane; each MethodDef row is read once.</summary>
    public static LaneCounts Fused(MetadataReader reader) =>
        new TypeDefinitionRows(reader)
            .Fold<TypeDefinitionRows, TypeDefinitionRow, LaneCounts, FusedFold>(default, new FusedFold(new MethodClassificationCallState(reader)));

    struct AsyncSelect(MethodClassificationCallState state) : IFunc<MethodDefinitionRow, bool>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Invoke(MethodDefinitionRow row) => LibraryInfoLanes.IsAsyncMethod(state, row.Type, row.Method);
    }

    /// <summary>
    /// The extension-method lane over the flat MethodDef source. The type half
    /// of the predicate is decided once per type, as the product path decides
    /// it, by remembering the last type seen.
    /// </summary>
    struct ExtensionSelect : IFunc<MethodDefinitionRow, bool>
    {
        TypeDefinitionHandle _lastType;
        bool _lastTypeSelected;
        bool _seen;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Invoke(MethodDefinitionRow row)
        {
            if (!_seen || _lastType != row.TypeHandle)
            {
                _seen = true;
                _lastType = row.TypeHandle;
                _lastTypeSelected = LibraryInfoLanes.IsExtensionType(row.Reader, row.Type);
            }

            return _lastTypeSelected && LibraryInfoLanes.IsExtensionMethod(row.Reader, row.Type, row.Method);
        }
    }

    struct UnionSelect : IFunc<TypeDefinitionRow, bool>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Invoke(TypeDefinitionRow row) => LibraryInfoLanes.IsUnionType(row.Reader, row.Type);
    }

    /// <summary>The hand-fused fold: every lane's test against one read of each type and each method.</summary>
    struct FusedFold(MethodClassificationCallState state) : IFunc<LaneCounts, TypeDefinitionRow, LaneCounts>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly LaneCounts Invoke(LaneCounts accumulator, TypeDefinitionRow row)
        {
            MetadataReader reader = row.Reader;
            TypeDefinition type = row.Type;
            int async = accumulator.AsyncMethods;
            int extension = accumulator.ExtensionMethods;
            int union = accumulator.UnionTypes;

            if (LibraryInfoLanes.IsUnionType(reader, type))
                union++;
            bool asyncScope = MethodClassificationGate.TypeInScope(reader, type);
            bool extensionType = LibraryInfoLanes.IsExtensionType(reader, type);
            if (asyncScope || extensionType)
            {
                foreach (MethodDefinitionHandle handle in type.GetMethods())
                {
                    MethodDefinition method = reader.GetMethodDefinition(handle);
                    if (asyncScope
                        && MethodClassificationGate.MethodInScope(reader, method)
                        && MethodClassificationTests.Classify(ClassificationPopulation.Async, state, method) is not null)
                    {
                        async++;
                    }

                    if (extensionType && LibraryInfoLanes.IsExtensionMethod(reader, type, method))
                        extension++;
                }
            }

            return new(async, extension, union);
        }
    }
}

/// <summary>Every lane's count from one answer.</summary>
public readonly record struct LaneCounts(int AsyncMethods, int ExtensionMethods, int UnionTypes)
{
    public IReadOnlyList<LaneCount> ToRows() =>
    [
        new(LibraryInfoLane.AsyncMethods, AsyncMethods),
        new(LibraryInfoLane.ExtensionMethods, ExtensionMethods),
        new(LibraryInfoLane.UnionTypes, UnionTypes),
    ];
}
