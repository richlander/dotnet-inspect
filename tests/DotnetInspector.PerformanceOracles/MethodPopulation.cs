using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

/// <summary>A method as a text row: its name, declaring type, and signature.</summary>
public readonly record struct MethodTextRow(string Name, string DeclaringType, string Signature)
{
    public override string ToString() => $"{Name}|{DeclaringType}|{Signature}";
}

/// <summary>Which method definitions a population selects.</summary>
public interface IMethodSelection
{
    bool IsSelected(MetadataReader reader, TypeDefinition type, MethodDefinition method);
}

/// <summary>Public, non-accessor methods on types whose name does not start with <c>&lt;</c>.</summary>
public struct PublicMethodSelection : IMethodSelection
{
    public readonly bool IsSelected(MetadataReader reader, TypeDefinition type, MethodDefinition method)
    {
        if (reader.StringComparer.StartsWith(type.Name, "<"))
            return false;
        if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            return false;
        StringHandle name = method.Name;
        return !(reader.StringComparer.StartsWith(name, "get_")
            || reader.StringComparer.StartsWith(name, "set_")
            || reader.StringComparer.StartsWith(name, "add_")
            || reader.StringComparer.StartsWith(name, "remove_"));
    }
}

/// <summary>
/// The public-methods scorecard population: the default method-definition
/// population the scorecard tool runs.
/// </summary>
public static class PublicMethods
{
    public static ScorecardColumn<PEReader, MethodTextRow> NLinqColumn(ScorecardShape shape) =>
        MethodPopulation<PublicMethodSelection>.NLinqColumn(shape);

    public static ScorecardColumn<PEReader, MethodTextRow> LinqColumn(ScorecardShape shape) =>
        MethodPopulation<PublicMethodSelection>.LinqColumn(shape);

    public static string RowText(MethodTextRow row) => MethodPopulation.RowText(row);

    public static IReadOnlyList<ScorecardAsset<PEReader>> LoadAssets(IReadOnlyList<string> paths) =>
        MethodPopulation.LoadAssets(paths);
}

/// <summary>
/// A method-definition population's shared pieces: the text-row projection,
/// the row text used for display, and asset loading.
/// </summary>
public static class MethodPopulation
{
    public static MethodTextRow Project(MetadataReader reader, TypeDefinition type, MethodDefinition method)
    {
        string name = reader.GetString(method.Name);
        string ns = reader.GetString(type.Namespace);
        string declaring = ns.Length == 0 ? reader.GetString(type.Name) : ns + "." + reader.GetString(type.Name);
        string signature;
        try
        {
            MethodSignature<string> decoded = method.DecodeSignature(SignatureText.Instance, null);
            var text = new StringBuilder();
            text.Append(decoded.ReturnType).Append(' ').Append(name).Append('(');
            for (int i = 0; i < decoded.ParameterTypes.Length; i++)
            {
                if (i > 0)
                    text.Append(", ");
                text.Append(decoded.ParameterTypes[i]);
            }

            signature = text.Append(')').ToString();
        }
        catch (BadImageFormatException)
        {
            signature = name + "(...)";
        }

        return new(name, declaring, signature);
    }

    public static string RowText(MethodTextRow row) => row.ToString();

    /// <summary>
    /// Reads each assembly into memory, so timing excludes file I/O, and names
    /// it for display with <see cref="ScorecardAssetNames.FromPaths"/>. The
    /// caller disposes the readers.
    /// </summary>
    public static IReadOnlyList<ScorecardAsset<PEReader>> LoadAssets(IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> names = ScorecardAssetNames.FromPaths(paths);
        var assets = new List<ScorecardAsset<PEReader>>(paths.Count);
        for (int i = 0; i < paths.Count; i++)
            assets.Add(new(names[i], new PEReader(System.Collections.Immutable.ImmutableArray.Create(File.ReadAllBytes(paths[i])))));
        return assets;
    }

    sealed class SignatureText : ISignatureTypeProvider<string, object?>
    {
        public static SignatureText Instance { get; } = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.IntPtr => "nint",
            PrimitiveTypeCode.UIntPtr => "nuint",
            _ => typeCode.ToString(),
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";

        public string GetByReferenceType(string elementType) => "ref " + elementType;

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(", ", typeArguments) + ">";

        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;
    }
}

/// <summary>
/// A method-definition population selected by <typeparamref name="TSelection"/>,
/// each method projected to a <see cref="MethodTextRow"/>. It carries the
/// standard columns every enablement over the population is scored against:
/// the NLinq oracle and an idiomatic streaming System.Linq pipeline.
/// Enablements register their Old (Before) and Planner (After) columns beside
/// them.
/// </summary>
public static class MethodPopulation<TSelection>
    where TSelection : struct, IMethodSelection
{
    /// <summary>The NLinq oracle: each closing as an NLinq pipeline, as an NLinq author writes it.</summary>
    public static ScorecardColumn<PEReader, MethodTextRow> NLinqColumn(ScorecardShape shape) =>
        new("NLinq", (closing, pe) => NLinqAnswer(closing, pe.GetMetadataReader(), shape));

    /// <summary>An idiomatic streaming System.Linq pipeline: SelectMany, Where, Select, then the closing.</summary>
    public static ScorecardColumn<PEReader, MethodTextRow> LinqColumn(ScorecardShape shape) =>
        new("LINQ", (closing, pe) => LinqAnswer(closing, pe.GetMetadataReader(), shape));

    static ScorecardAnswer<MethodTextRow> NLinqAnswer(ScorecardClosing closing, MetadataReader reader, ScorecardShape shape)
    {
        var selected = new MethodDefinitionRows(reader).Where<MethodDefinitionRows, MethodDefinitionRow, Selected>(default);
        switch (closing)
        {
            case ScorecardClosing.Exists:
                return ScorecardAnswer<MethodTextRow>.OfExists(
                    new MethodDefinitionRows(reader).Any<MethodDefinitionRows, MethodDefinitionRow, Selected>(default));
            case ScorecardClosing.Count:
                return ScorecardAnswer<MethodTextRow>.OfCount(
                    selected.CountFold<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow>());
            case ScorecardClosing.Rows:
                return ScorecardAnswer<MethodTextRow>.OfRows(
                    selected
                        .Select<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow, MethodTextRow, ToText>(default)
                        .ToList<Map<MethodDefinitionRow, MethodTextRow, Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, ToText>, MethodTextRow>());
            case ScorecardClosing.Head:
            {
                var rows = selected
                    .Select<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow, MethodTextRow, ToText>(default)
                    .Take<Map<MethodDefinitionRow, MethodTextRow, Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, ToText>, MethodTextRow>(shape.N);
                var list = new List<MethodTextRow>(shape.N);
                while (true)
                {
                    MethodTextRow row = rows.TryGetNext(out bool hasMore);
                    if (!hasMore)
                        return ScorecardAnswer<MethodTextRow>.OfRows(list);
                    list.Add(row);
                }
            }

            case ScorecardClosing.Tail:
            {
                List<MethodDefinitionRow> last = selected
                    .TakeLast<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow>(shape.N);
                var list = new List<MethodTextRow>(last.Count);
                foreach (MethodDefinitionRow row in last)
                    list.Add(MethodPopulation.Project(row.Reader, row.Type, row.Method));
                return ScorecardAnswer<MethodTextRow>.OfRows(list);
            }

            case ScorecardClosing.Window:
                // Skip before Select, so skipped rows are never projected; a
                // strict take reports a window that runs out as a failure.
                return selected
                        .Skip<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow>(shape.WindowSkip)
                        .Select<SkipEnumerator<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow>, MethodDefinitionRow, MethodTextRow, ToText>(default)
                        .TryTakeExactly<Map<MethodDefinitionRow, MethodTextRow, SkipEnumerator<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow>, ToText>, MethodTextRow>(shape.WindowTake, out List<MethodTextRow> window)
                    ? ScorecardAnswer<MethodTextRow>.OfRows(window)
                    : ScorecardAnswer<MethodTextRow>.OfWindowFailure();
            default:
                throw new ArgumentOutOfRangeException(nameof(closing));
        }
    }

    static ScorecardAnswer<MethodTextRow> LinqAnswer(ScorecardClosing closing, MetadataReader reader, ScorecardShape shape)
    {
        IEnumerable<MethodTextRow> rows = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .SelectMany(type => type.GetMethods().Select(handle => (Type: type, Method: reader.GetMethodDefinition(handle))))
            .Where(m => default(TSelection).IsSelected(reader, m.Type, m.Method))
            .Select(m => MethodPopulation.Project(reader, m.Type, m.Method));
        return closing switch
        {
            ScorecardClosing.Exists => ScorecardAnswer<MethodTextRow>.OfExists(rows.Any()),
            ScorecardClosing.Count => ScorecardAnswer<MethodTextRow>.OfCount(rows.Count()),
            ScorecardClosing.Head => ScorecardAnswer<MethodTextRow>.OfRows(rows.Take(shape.N).ToList()),
            ScorecardClosing.Tail => ScorecardAnswer<MethodTextRow>.OfRows(rows.TakeLast(shape.N).ToList()),
            ScorecardClosing.Rows => ScorecardAnswer<MethodTextRow>.OfRows(rows.ToList()),
            ScorecardClosing.Window => rows.Skip(shape.WindowSkip).Take(shape.WindowTake).ToList() is { } window
                && window.Count == shape.WindowTake
                    ? ScorecardAnswer<MethodTextRow>.OfRows(window)
                    : ScorecardAnswer<MethodTextRow>.OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };
    }

    struct Selected : IFunc<MethodDefinitionRow, bool>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Invoke(MethodDefinitionRow row) => default(TSelection).IsSelected(row.Reader, row.Type, row.Method);
    }

    struct ToText : IFunc<MethodDefinitionRow, MethodTextRow>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MethodTextRow Invoke(MethodDefinitionRow row) => MethodPopulation.Project(row.Reader, row.Type, row.Method);
    }
}
