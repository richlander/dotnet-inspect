using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Generates custom-attribute constructor signatures and matching value blobs
/// over the grammar <see cref="CustomAttributeValueGuard"/> walks, so the guard
/// can be compared against SRM's decoder on shapes nobody thought of.
/// </summary>
/// <remarks>
/// <para>
/// The generator is what makes the comparison two-sided. SRM does not expose
/// its cursor and the guard's approval is not evidence of where the guard
/// landed, so neither implementation can witness the other's boundary. The
/// generator can: it emits a well-formed blob of a length it chose, and that
/// length is ground truth both walkers must agree with.
/// </para>
/// <para>
/// Shapes are emitted from a seeded <see cref="Random"/> so a failure is
/// reproducible from its seed alone, and a failing seed can be pinned as an
/// ordinary case.
/// </para>
/// </remarks>
static class CustomAttributeDifferentialOracle
{
    /// <summary>A generated fixed-argument shape and the bytes it encodes to.</summary>
    internal abstract record Shape
    {
        /// <summary>Writes this shape's constructor parameter type.</summary>
        public abstract void WriteSignature(SignatureTypeEncoder encoder, Context context);

        /// <summary>Writes this shape's value-blob bytes.</summary>
        public abstract void WriteValue(BlobBuilder value, Context context);

        /// <summary>A stable description used in assertion messages.</summary>
        public abstract override string ToString();
    }

    /// <summary>Handles the generated image publishes to the shapes writing into it.</summary>
    internal sealed record Context(
        TypeDefinitionHandle EnumType,
        string EnumSerializedName,
        PrimitiveTypeCode EnumUnderlying,
        TypeReferenceHandle SystemType)
    {
        /// <summary>
        /// Every inline element-type byte the value blob actually carried.
        /// Whether a given byte is emitted depends on a shape's *position*, not
        /// on the shape alone: a boxed array spells its element type even when
        /// the array is empty, while an <c>object[]</c> spells its elements only
        /// when it has some. Inferring that from the shape tree would drift from
        /// what was written, so the writer records it instead.
        /// </summary>
        public HashSet<byte> InlineElementTypes { get; } = [];

        /// <summary>Every primitive an argument value actually carried.</summary>
        public HashSet<PrimitiveTypeCode> Primitives { get; } = [];

        /// <summary>Every SerString form a string argument actually took.</summary>
        public HashSet<SerStringForm> StringForms { get; } = [];
    }

    /// <summary>
    /// The SerString encodings a string argument can take. These are distinct
    /// encodings rather than stylistic variants: null is the single byte
    /// <c>0xFF</c>, empty is the single byte <c>0x00</c>, and a payload of 128
    /// bytes or more forces a multi-byte compressed length prefix.
    /// </summary>
    internal enum SerStringForm
    {
        Null,
        Empty,
        SingleByteLength,
        MultiByteLength,
    }

    internal sealed record PrimitiveShape(PrimitiveTypeCode Code) : Shape
    {
        public override void WriteSignature(SignatureTypeEncoder encoder, Context context)
            => WritePrimitiveSignature(encoder, Code);

        public override void WriteValue(BlobBuilder value, Context context)
            => WriteRecordedPrimitiveValue(value, Code, context);

        public override string ToString() => Code.ToString();
    }

    internal sealed record StringShape(string? Value) : Shape
    {
        public override void WriteSignature(SignatureTypeEncoder encoder, Context context)
            => encoder.String();

        public override void WriteValue(BlobBuilder value, Context context)
            => WriteRecordedString(value, Value, context);

        public override string ToString() => Value is null ? "string(null)" : $"string(\"{Value}\")";
    }

    /// <summary>An <c>SZARRAY</c>; a negative count is the null-array encoding.</summary>
    internal sealed record ArrayShape(Shape Element, int Count) : Shape
    {
        public override void WriteSignature(SignatureTypeEncoder encoder, Context context)
            => Element.WriteSignature(encoder.SZArray(), context);

        public override void WriteValue(BlobBuilder value, Context context)
        {
            value.WriteInt32(Count);
            for (int index = 0; index < Count; index++)
                Element.WriteValue(value, context);
        }

        public override string ToString()
            => Count < 0 ? $"{Element}[null]" : $"{Element}[{Count}]";
    }

    /// <summary>A boxed argument: an <c>object</c> parameter carrying an inline element type.</summary>
    internal sealed record BoxedShape(Shape Inner) : Shape
    {
        public override void WriteSignature(SignatureTypeEncoder encoder, Context context)
            => encoder.Object();

        public override void WriteValue(BlobBuilder value, Context context)
        {
            // An `object` fixed argument carries its FieldOrPropType byte
            // directly. ELEMENT_TYPE_BOXED (0x51) is a *nested* marker, not the
            // prefix for this case; emitting it here would generate a
            // non-canonical blob.
            WriteInlineElementType(value, Inner, context);
            Inner.WriteValue(value, context);
        }

        public override string ToString() => $"boxed({Inner})";
    }

    /// <summary>
    /// A <c>System.Type</c> argument: the value blob carries the serialized type
    /// name, and the boxed spelling is <c>0x50</c>.
    /// </summary>
    internal sealed record SystemTypeShape(string TypeName) : Shape
    {
        public override void WriteSignature(SignatureTypeEncoder encoder, Context context)
            => encoder.Type(context.SystemType, isValueType: false);

        public override void WriteValue(BlobBuilder value, Context context)
            => value.WriteSerializedString(TypeName);

        public override string ToString() => $"Type(\"{TypeName}\")";
    }

    /// <summary>An enum spelled as <c>VALUETYPE</c> plus a coded handle.</summary>
    internal sealed record EnumHandleShape : Shape
    {
        public override void WriteSignature(SignatureTypeEncoder encoder, Context context)
            => encoder.Type(context.EnumType, isValueType: true);

        public override void WriteValue(BlobBuilder value, Context context)
            => WritePrimitiveValue(value, context.EnumUnderlying);

        public override string ToString() => "enum(handle)";
    }

    /// <summary>Writes the inline element-type byte a boxed value carries before its data.</summary>
    /// <remarks>
    /// The byte is computed once, then both written and recorded. Recording it
    /// from a second switch would let the two drift: a writer emitting some
    /// other byte would still be recorded as this one, and the coverage gate
    /// would claim a spelling that never appeared in any blob. The switch below
    /// therefore handles only the trailing payload some spellings carry.
    /// </remarks>
    static void WriteInlineElementType(BlobBuilder value, Shape shape, Context context)
    {
        byte elementType = InlineElementTypeByte(shape);
        value.WriteByte(elementType);
        context.InlineElementTypes.Add(elementType);

        switch (shape)
        {
            case ArrayShape array:
                WriteInlineElementType(value, array.Element, context);
                break;
            case EnumHandleShape:
                value.WriteSerializedString(context.EnumSerializedName);
                break;
        }
    }

    /// <summary>
    /// The single source for the inline element-type byte <paramref name="shape"/>
    /// is spelled with. <c>0x51</c> is correct only here, where `object` appears
    /// as a *nested* element type; a top-level `object` argument writes its
    /// FieldOrPropType directly with no <c>0x51</c> prefix.
    /// </summary>
    static byte InlineElementTypeByte(Shape shape) => shape switch
    {
        PrimitiveShape primitive => ElementTypeByte(primitive.Code),
        StringShape => 0x0e,
        SystemTypeShape => 0x50,
        ArrayShape => 0x1d,
        BoxedShape => 0x51,
        EnumHandleShape => 0x55,
        _ => throw new InvalidOperationException(
            $"{shape.GetType().Name} has no inline element-type spelling."),
    };

    static void WritePrimitiveSignature(SignatureTypeEncoder encoder, PrimitiveTypeCode code)
    {
        switch (code)
        {
            case PrimitiveTypeCode.Boolean: encoder.Boolean(); break;
            case PrimitiveTypeCode.Char: encoder.Char(); break;
            case PrimitiveTypeCode.SByte: encoder.SByte(); break;
            case PrimitiveTypeCode.Byte: encoder.Byte(); break;
            case PrimitiveTypeCode.Int16: encoder.Int16(); break;
            case PrimitiveTypeCode.UInt16: encoder.UInt16(); break;
            case PrimitiveTypeCode.Int32: encoder.Int32(); break;
            case PrimitiveTypeCode.UInt32: encoder.UInt32(); break;
            case PrimitiveTypeCode.Int64: encoder.Int64(); break;
            case PrimitiveTypeCode.UInt64: encoder.UInt64(); break;
            case PrimitiveTypeCode.Single: encoder.Single(); break;
            case PrimitiveTypeCode.Double: encoder.Double(); break;
            default:
                throw new InvalidOperationException($"{code} is not a generated primitive.");
        }
    }

    /// <summary>
    /// Writes a string argument and records the form it took. The write and the
    /// record derive from the same payload, so a writer that ignored the shape's
    /// value could not leave the coverage gate still claiming the forms the
    /// shape tree describes.
    /// </summary>
    static void WriteRecordedString(BlobBuilder value, string? payload, Context context)
    {
        context.StringForms.Add(payload switch
        {
            null => SerStringForm.Null,
            { Length: 0 } => SerStringForm.Empty,
            { Length: < 128 } => SerStringForm.SingleByteLength,
            _ => SerStringForm.MultiByteLength,
        });

        value.WriteSerializedString(payload);
    }

    /// <summary>
    /// Writes a primitive argument value and records which primitive it was.
    /// Enum values deliberately do not route through here: their width is
    /// asserted separately, and crediting them as primitives would let an
    /// enum-only corpus satisfy the primitive coverage assertion.
    /// </summary>
    static void WriteRecordedPrimitiveValue(
        BlobBuilder value,
        PrimitiveTypeCode code,
        Context context)
    {
        context.Primitives.Add(code);
        WritePrimitiveValue(value, code);
    }

    static void WritePrimitiveValue(BlobBuilder value, PrimitiveTypeCode code)
    {
        switch (code)
        {
            case PrimitiveTypeCode.Boolean: value.WriteBoolean(true); break;
            case PrimitiveTypeCode.Char: value.WriteUInt16('x'); break;
            case PrimitiveTypeCode.SByte: value.WriteSByte(-1); break;
            case PrimitiveTypeCode.Byte: value.WriteByte(2); break;
            case PrimitiveTypeCode.Int16: value.WriteInt16(-3); break;
            case PrimitiveTypeCode.UInt16: value.WriteUInt16(4); break;
            case PrimitiveTypeCode.Int32: value.WriteInt32(-5); break;
            case PrimitiveTypeCode.UInt32: value.WriteUInt32(6); break;
            case PrimitiveTypeCode.Int64: value.WriteInt64(-7); break;
            case PrimitiveTypeCode.UInt64: value.WriteUInt64(8); break;
            case PrimitiveTypeCode.Single: value.WriteSingle(9.5f); break;
            case PrimitiveTypeCode.Double: value.WriteDouble(10.5d); break;
            default:
                throw new InvalidOperationException($"{code} is not a generated primitive.");
        }
    }

    static byte ElementTypeByte(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.Boolean => 0x02,
        PrimitiveTypeCode.Char => 0x03,
        PrimitiveTypeCode.SByte => 0x04,
        PrimitiveTypeCode.Byte => 0x05,
        PrimitiveTypeCode.Int16 => 0x06,
        PrimitiveTypeCode.UInt16 => 0x07,
        PrimitiveTypeCode.Int32 => 0x08,
        PrimitiveTypeCode.UInt32 => 0x09,
        PrimitiveTypeCode.Int64 => 0x0a,
        PrimitiveTypeCode.UInt64 => 0x0b,
        PrimitiveTypeCode.Single => 0x0c,
        PrimitiveTypeCode.Double => 0x0d,
        _ => throw new InvalidOperationException($"{code} has no element-type byte."),
    };

    static readonly PrimitiveTypeCode[] s_primitives =
    [
        PrimitiveTypeCode.Boolean,
        PrimitiveTypeCode.Char,
        PrimitiveTypeCode.SByte,
        PrimitiveTypeCode.Byte,
        PrimitiveTypeCode.Int16,
        PrimitiveTypeCode.UInt16,
        PrimitiveTypeCode.Int32,
        PrimitiveTypeCode.UInt32,
        PrimitiveTypeCode.Int64,
        PrimitiveTypeCode.UInt64,
        PrimitiveTypeCode.Single,
        PrimitiveTypeCode.Double,
    ];

    /// <summary>
    /// Produces one fixed-argument shape.
    /// </summary>
    /// <remarks>
    /// The custom-attribute grammar admits an <c>SZARRAY</c> of scalars or of
    /// <c>object</c>, but not an array of arrays: ECMA-335 §II.23.3 allows one
    /// <c>SZARRAY</c> prefix before an <c>Elem</c>, and <c>Elem</c> does not
    /// itself include <c>SZARRAY</c>. Jagged arrays therefore have no spelling
    /// here and SRM refuses them outright, so generating them would compare the
    /// two walkers on inputs neither is contracted to agree about. An
    /// <c>object[]</c> is a different case and is legal: the array's declared
    /// element type is <c>object</c>, and each element carries its own
    /// <c>FieldOrPropType</c> byte. Array elements are therefore leaves or
    /// boxed leaves — never boxed arrays, which would reintroduce nesting.
    /// </remarks>
    internal static Shape NextShape(Random random)
    {
        return random.Next(0, 4) switch
        {
            0 => NextLeafShape(random, "s"),
            1 => NextArrayShape(random, "s"),
            2 => new BoxedShape(NextLeafShape(random, "b")),
            _ => new BoxedShape(NextArrayShape(random, "b")),
        };
    }

    /// <summary>A scalar: the only thing a boxed value may directly contain.</summary>
    static Shape NextLeafShape(Random random, string prefix)
        => random.Next(0, 4) switch
        {
            0 => new PrimitiveShape(s_primitives[random.Next(s_primitives.Length)]),
            1 => new StringShape(NextString(random, prefix)),
            2 => new SystemTypeShape(s_typeNames[random.Next(s_typeNames.Length)]),
            _ => new EnumHandleShape(),
        };

    /// <summary>
    /// A SerString payload. The three interesting forms are distinct encodings,
    /// not stylistic variants: null is the single byte <c>0xFF</c>, empty is the
    /// single byte <c>0x00</c>, and a string of 128 bytes or more forces a
    /// multi-byte compressed length prefix rather than the one-byte form every
    /// short string takes.
    /// </summary>
    static string? NextString(Random random, string prefix)
        => random.Next(8) switch
        {
            0 => null,
            1 => string.Empty,
            2 => new string('w', 128 + random.Next(8)),
            _ => $"{prefix}{random.Next(100)}",
        };

    /// <summary>The ECMA standard public key token that core-library references carry.</summary>
    static readonly byte[] s_ecmaPublicKeyToken = [0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a];

    static readonly string[] s_typeNames =
    [
        "System.Int32",
        "System.String",
        "Samples.E",
        "System.Collections.Generic.List`1[[System.Int32]]",
    ];

    /// <summary>
    /// An array of scalars, or an <c>object[]</c> whose elements are boxed
    /// leaves; a negative count is the null-array encoding.
    /// </summary>
    static ArrayShape NextArrayShape(Random random, string prefix)
    {
        Shape leaf = NextLeafShape(random, prefix);
        Shape element = random.Next(3) == 0 ? new BoxedShape(leaf) : leaf;
        return new ArrayShape(element, random.Next(6) == 0 ? -1 : random.Next(0, 4));
    }

    /// <summary>A generated image and the ground truth the generator knows about it.</summary>
    internal sealed class Case(
        byte[] image,
        IReadOnlyList<Shape> shapes,
        int valueLength,
        PrimitiveTypeCode enumUnderlying,
        int seed,
        IReadOnlySet<byte> inlineElementTypes,
        IReadOnlySet<PrimitiveTypeCode> primitives,
        IReadOnlySet<SerStringForm> stringForms) : IDisposable
    {
        /// <summary>The inline element-type bytes this blob actually carried.</summary>
        public IReadOnlySet<byte> InlineElementTypes => inlineElementTypes;

        /// <summary>The primitives this blob actually carried.</summary>
        public IReadOnlySet<PrimitiveTypeCode> Primitives => primitives;

        /// <summary>The SerString forms this blob actually carried.</summary>
        public IReadOnlySet<SerStringForm> StringForms => stringForms;

        readonly PEReader _peReader = new(new MemoryStream(image, writable: false));

        public MetadataReader Reader => _peReader.GetMetadataReader();

        /// <summary>The exact value-blob length the generator emitted.</summary>
        public int ValueLength => valueLength;

        public IReadOnlyList<Shape> Shapes => shapes;

        public PrimitiveTypeCode EnumUnderlying => enumUnderlying;

        public int Seed => seed;

        public CustomAttribute Attribute
        {
            get
            {
                foreach (var handle in Reader.CustomAttributes)
                    return Reader.GetCustomAttribute(handle);
                throw new InvalidOperationException("The generated image has no custom attribute.");
            }
        }

        public string Describe()
            => $"seed {seed}, enum {enumUnderlying}, args [{string.Join(", ", shapes)}]";

        public void Dispose() => _peReader.Dispose();
    }

    /// <summary>Builds one generated image from a seed.</summary>
    internal static Case Generate(int seed, int? trailingGarbageBytes = null)
    {
        var random = new Random(seed);
        PrimitiveTypeCode enumUnderlying = random.Next(2) == 0
            ? PrimitiveTypeCode.Int32
            : PrimitiveTypeCode.Int64;

        int parameterCount = random.Next(0, 4);
        var shapes = new List<Shape>(parameterCount);
        for (int index = 0; index < parameterCount; index++)
            shapes.Add(NextShape(random));

        return Build(shapes, enumUnderlying, seed, trailingGarbageBytes);
    }

    /// <summary>Builds an image for an explicit shape list, for pinned cases.</summary>
    internal static Case Build(
        IReadOnlyList<Shape> shapes,
        PrimitiveTypeCode enumUnderlying,
        int seed = 0,
        int? trailingGarbageBytes = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Generated.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Generated"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.Sha1);

        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        // System.Enum and System.Type are ECMA special types, and real metadata
        // scopes them to the core library rather than to whichever assembly
        // declares the attribute. Both walkers happen to flatten these to names,
        // so scoping them to `Other` would still decode — and would quietly make
        // a green result depend on that flattening instead of on the corpus
        // being well-formed. Referencing real System.Runtime identity keeps the
        // premise the corpus claims.
        AssemblyReferenceHandle systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(8, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(s_ecmaPublicKeyToken),
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));

        // The local enum both spellings resolve through.
        var fieldSignature = new BlobBuilder();
        WritePrimitiveSignature(
            new BlobEncoder(fieldSignature).FieldSignature(),
            enumUnderlying);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle enumType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        TypeReferenceHandle systemType = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Type"));

        var context = new Context(enumType, "Samples.E", enumUnderlying, systemType);

        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                shapes.Count,
                returnType => returnType.Void(),
                parameters =>
                {
                    foreach (var shape in shapes)
                        shape.WriteSignature(parameters.AddParameter().Type(), context);
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        foreach (var shape in shapes)
            shape.WriteValue(value, context);
        value.WriteUInt16(0);

        // The length the generator intends both walkers to agree on. Trailing
        // garbage is deliberately excluded: neither walker should reach it.
        int valueLength = value.Count;
        for (int index = 0; index < (trailingGarbageBytes ?? 0); index++)
            value.WriteByte(0xcc);

        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));

        return new Case(
            Serialize(metadata),
            shapes,
            valueLength,
            enumUnderlying,
            seed,
            context.InlineElementTypes,
            context.Primitives,
            context.StringForms);
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder());
        var peImage = new BlobBuilder();
        peBuilder.Serialize(peImage);
        return peImage.ToArray();
    }
}
