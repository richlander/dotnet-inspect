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
        PrimitiveTypeCode EnumUnderlying);

    internal sealed record PrimitiveShape(PrimitiveTypeCode Code) : Shape
    {
        public override void WriteSignature(SignatureTypeEncoder encoder, Context context)
            => WritePrimitiveSignature(encoder, Code);

        public override void WriteValue(BlobBuilder value, Context context)
            => WritePrimitiveValue(value, Code);

        public override string ToString() => Code.ToString();
    }

    internal sealed record StringShape(string? Value) : Shape
    {
        public override void WriteSignature(SignatureTypeEncoder encoder, Context context)
            => encoder.String();

        public override void WriteValue(BlobBuilder value, Context context)
            => value.WriteSerializedString(Value);

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
    static void WriteInlineElementType(BlobBuilder value, Shape shape, Context context)
    {
        switch (shape)
        {
            case PrimitiveShape primitive:
                value.WriteByte(ElementTypeByte(primitive.Code));
                break;
            case StringShape:
                value.WriteByte(0x0e);
                break;
            case ArrayShape array:
                value.WriteByte(0x1d);
                WriteInlineElementType(value, array.Element, context);
                break;
            case BoxedShape:
                throw new InvalidOperationException(
                    "A boxed value cannot itself be spelled as an inline element type.");
            case EnumHandleShape:
                value.WriteByte(0x55);
                value.WriteSerializedString(context.EnumSerializedName);
                break;
            default:
                throw new InvalidOperationException(
                    $"{shape.GetType().Name} has no inline element-type spelling.");
        }
    }

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
    /// The custom-attribute grammar admits an <c>SZARRAY</c> of scalars, not an
    /// array of arrays: a jagged array has no spelling here, and SRM refuses
    /// one outright. Generating those would compare the two walkers on inputs
    /// neither is contracted to agree about, so array elements are always
    /// leaves.
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

    /// <summary>A scalar: the only thing an array element or boxed value may be.</summary>
    static Shape NextLeafShape(Random random, string prefix)
        => random.Next(0, 3) switch
        {
            0 => new PrimitiveShape(s_primitives[random.Next(s_primitives.Length)]),
            1 => new StringShape(random.Next(4) == 0 ? null : $"{prefix}{random.Next(100)}"),
            _ => new EnumHandleShape(),
        };

    /// <summary>An array of scalars; a negative count is the null-array encoding.</summary>
    static ArrayShape NextArrayShape(Random random, string prefix)
        => new(
            NextLeafShape(random, prefix),
            random.Next(6) == 0 ? -1 : random.Next(0, 4));

    /// <summary>A generated image and the ground truth the generator knows about it.</summary>
    internal sealed class Case(
        byte[] image,
        IReadOnlyList<Shape> shapes,
        int valueLength,
        PrimitiveTypeCode enumUnderlying,
        int seed) : IDisposable
    {
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
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
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

        var context = new Context(enumType, "Samples.E", enumUnderlying);

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

        return new Case(Serialize(metadata), shapes, valueLength, enumUnderlying, seed);
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
