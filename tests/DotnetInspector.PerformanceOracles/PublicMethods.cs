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

/// <summary>
/// The method-definition postcard population: public, non-accessor methods on
/// types whose name does not start with <c>&lt;</c>, each projected to a
/// <see cref="MethodTextRow"/>. It carries the standing columns every
/// enablement over this population is scored against: the NLinq oracle and an
/// idiomatic streaming System.Linq pipeline. Enablements register their own
/// Before and After columns beside them.
/// </summary>
public static class PublicMethods
{
    public static bool IsSelected(MetadataReader reader, TypeDefinition type, MethodDefinition method)
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

    /// <summary>The NLinq oracle: each closing as an NLinq pipeline, as an NLinq author writes it.</summary>
    public static PostcardColumn<PEReader, MethodTextRow> NLinqColumn(PostcardShape shape) =>
        new("NLinq", (closing, pe) => NLinqAnswer(closing, pe.GetMetadataReader(), shape));

    /// <summary>An idiomatic streaming System.Linq pipeline: SelectMany, Where, Select, then the closing.</summary>
    public static PostcardColumn<PEReader, MethodTextRow> LinqColumn(PostcardShape shape) =>
        new("LINQ", (closing, pe) => LinqAnswer(closing, pe.GetMetadataReader(), shape));

    static PostcardAnswer<MethodTextRow> NLinqAnswer(PostcardClosing closing, MetadataReader reader, PostcardShape shape)
    {
        var selected = new MethodDefinitionRows(reader).Where<MethodDefinitionRows, MethodDefinitionRow, Selected>(default);
        switch (closing)
        {
            case PostcardClosing.Exists:
                return PostcardAnswer<MethodTextRow>.OfExists(
                    new MethodDefinitionRows(reader).Any<MethodDefinitionRows, MethodDefinitionRow, Selected>(default));
            case PostcardClosing.Count:
                return PostcardAnswer<MethodTextRow>.OfCount(
                    selected.CountFold<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow>());
            case PostcardClosing.Rows:
                return PostcardAnswer<MethodTextRow>.OfRows(
                    selected
                        .Select<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow, MethodTextRow, ToText>(default)
                        .ToList<Map<MethodDefinitionRow, MethodTextRow, Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, ToText>, MethodTextRow>());
            case PostcardClosing.Head:
            {
                var rows = selected
                    .Select<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow, MethodTextRow, ToText>(default)
                    .Take<Map<MethodDefinitionRow, MethodTextRow, Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, ToText>, MethodTextRow>(shape.N);
                var list = new List<MethodTextRow>(shape.N);
                while (true)
                {
                    MethodTextRow row = rows.TryGetNext(out bool hasMore);
                    if (!hasMore)
                        return PostcardAnswer<MethodTextRow>.OfRows(list);
                    list.Add(row);
                }
            }

            case PostcardClosing.Tail:
            {
                List<MethodDefinitionRow> last = selected
                    .TakeLast<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow>(shape.N);
                var list = new List<MethodTextRow>(last.Count);
                foreach (MethodDefinitionRow row in last)
                    list.Add(Project(row.Reader, row.Type, row.Method));
                return PostcardAnswer<MethodTextRow>.OfRows(list);
            }

            case PostcardClosing.Window:
                // Skip before Select, so skipped rows are never projected; a
                // strict take reports a window that runs out as a failure.
                return selected
                        .Skip<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow>(shape.WindowSkip)
                        .Select<SkipEnumerator<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow>, MethodDefinitionRow, MethodTextRow, ToText>(default)
                        .TryTakeExactly<Map<MethodDefinitionRow, MethodTextRow, SkipEnumerator<Filter<MethodDefinitionRow, MethodDefinitionRows, Selected>, MethodDefinitionRow>, ToText>, MethodTextRow>(shape.WindowTake, out List<MethodTextRow> window)
                    ? PostcardAnswer<MethodTextRow>.OfRows(window)
                    : PostcardAnswer<MethodTextRow>.OfWindowFailure();
            default:
                throw new ArgumentOutOfRangeException(nameof(closing));
        }
    }

    static PostcardAnswer<MethodTextRow> LinqAnswer(PostcardClosing closing, MetadataReader reader, PostcardShape shape)
    {
        IEnumerable<MethodTextRow> rows = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .SelectMany(type => type.GetMethods().Select(handle => (Type: type, Method: reader.GetMethodDefinition(handle))))
            .Where(m => IsSelected(reader, m.Type, m.Method))
            .Select(m => Project(reader, m.Type, m.Method));
        return closing switch
        {
            PostcardClosing.Exists => PostcardAnswer<MethodTextRow>.OfExists(rows.Any()),
            PostcardClosing.Count => PostcardAnswer<MethodTextRow>.OfCount(rows.Count()),
            PostcardClosing.Head => PostcardAnswer<MethodTextRow>.OfRows(rows.Take(shape.N).ToList()),
            PostcardClosing.Tail => PostcardAnswer<MethodTextRow>.OfRows(rows.TakeLast(shape.N).ToList()),
            PostcardClosing.Rows => PostcardAnswer<MethodTextRow>.OfRows(rows.ToList()),
            PostcardClosing.Window => rows.Skip(shape.WindowSkip).Take(shape.WindowTake).ToList() is { } window
                && window.Count == shape.WindowTake
                    ? PostcardAnswer<MethodTextRow>.OfRows(window)
                    : PostcardAnswer<MethodTextRow>.OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };
    }

    struct Selected : IFunc<MethodDefinitionRow, bool>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Invoke(MethodDefinitionRow row) => IsSelected(row.Reader, row.Type, row.Method);
    }

    struct ToText : IFunc<MethodDefinitionRow, MethodTextRow>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MethodTextRow Invoke(MethodDefinitionRow row) => Project(row.Reader, row.Type, row.Method);
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
