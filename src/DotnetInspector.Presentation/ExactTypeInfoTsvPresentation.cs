using System.Buffers;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;

using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Presentation;

/// <summary>One complete Type Signature ready for direct TSV output.</summary>
public sealed record ExactTypeInfoTsvDocument(
    TypeSignature Signature,
    string Kind,
    IReadOnlyList<string> Modifiers,
    string Assembly,
    string Source);

/// <summary>Writes one complete Type Signature as UTF-8 TSV.</summary>
public static class ExactTypeInfoTsvPresentation
{
    /// <summary>Writes one complete document without creating a UTF-16 document.</summary>
    public static void Write(
        IBufferWriter<byte> output,
        ExactTypeInfoTsvDocument document,
        bool showHeader)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Signature);
        ArgumentNullException.ThrowIfNull(document.Kind);
        ArgumentNullException.ThrowIfNull(document.Modifiers);
        ArgumentNullException.ThrowIfNull(document.Assembly);
        ArgumentNullException.ThrowIfNull(document.Source);

        if (showHeader)
            WriteBytes(output, "field\tvalue\n"u8);

        WriteBytes(output, "Type\t"u8);
        WriteSubject(output, document.Signature);
        WriteByte(output, (byte)'\n');

        WriteRow(output, "Kind"u8, document.Kind);
        if (document.Modifiers.Count > 0)
        {
            WriteBytes(output, "Modifiers\t"u8);
            for (int index = 0;
                index < document.Modifiers.Count;
                index++)
            {
                if (index > 0)
                    WriteBytes(output, ", "u8);
                WriteString(output, document.Modifiers[index]);
            }
            WriteByte(output, (byte)'\n');
        }
        if (document.Signature.BaseType is { } baseType)
        {
            WriteBytes(output, "Base\t"u8);
            WriteType(
                output,
                baseType.Type,
                document.Signature.TypeParameters,
                DynamicProjection.Create(
                    baseType.Type,
                    baseType.DynamicFlags));
            WriteByte(output, (byte)'\n');
        }
        if (!document.Signature.TypeParameters.IsEmpty)
        {
            WriteBytes(output, "Type Parameters\t"u8);
            WriteTypeParameters(output, document.Signature);
            WriteByte(output, (byte)'\n');
        }
        if (document.Signature.InterfaceCount > 0)
        {
            WriteCountRow(
                output,
                "Interfaces"u8,
                document.Signature.InterfaceCount);
        }
        if (document.Signature.NestedTypeCount > 0)
        {
            WriteCountRow(
                output,
                "Nested Types"u8,
                document.Signature.NestedTypeCount);
        }
        WriteRow(output, "Library"u8, document.Assembly);
        WriteRow(output, "Source"u8, document.Source);
    }

    private static void WriteSubject(
        IBufferWriter<byte> output,
        TypeSignature signature)
    {
        WriteNamespace(
            output,
            signature.SubjectName.Namespace);
        if (signature.SubjectName.Namespace.Length > 0)
            WriteByte(output, (byte)'.');

        int parameterIndex = 0;
        for (int segmentIndex = 0;
            segmentIndex < signature.SubjectName.Segments.Length;
            segmentIndex++)
        {
            if (segmentIndex > 0)
                WriteByte(output, (byte)'.');

            int introduced = 0;
            while (parameterIndex + introduced
                    < signature.TypeParameters.Length
                && signature.TypeParameters[
                        parameterIndex + introduced]
                    .DefinitionSegmentIndex
                    == segmentIndex)
            {
                introduced++;
            }
            WriteIdentifier(
                output,
                signature.SubjectName.Segments[segmentIndex],
                introduced);
            if (introduced == 0)
                continue;

            WriteByte(output, (byte)'<');
            for (int offset = 0; offset < introduced; offset++)
            {
                if (offset > 0)
                    WriteBytes(output, ", "u8);
                WriteIdentifier(
                    output,
                    signature.TypeParameters[
                        parameterIndex + offset].Name);
            }
            WriteByte(output, (byte)'>');
            parameterIndex += introduced;
        }

        if (parameterIndex != signature.TypeParameters.Length)
        {
            throw new InvalidOperationException(
                "The complete Type Signature does not assign every parameter to a Type segment.");
        }
    }

    private static void WriteTypeParameters(
        IBufferWriter<byte> output,
        TypeSignature signature)
    {
        for (int index = 0;
            index < signature.TypeParameters.Length;
            index++)
        {
            if (index > 0)
                WriteBytes(output, ", "u8);

            TypeSignatureGenericParameter parameter =
                signature.TypeParameters[index];
            bool leafDeclaration =
                parameter.DefinitionSegmentIndex
                    == signature.SubjectName.Segments.Length - 1;
            if (leafDeclaration
                && parameter.Variance is { } variance)
            {
                WriteString(output, variance);
                WriteByte(output, (byte)' ');
            }
            WriteIdentifier(output, parameter.Name);
            bool wroteConstraint = false;
            if (parameter.PrimaryConstraint is { } primary)
            {
                WriteConstraintSeparator(
                    output,
                    ref wroteConstraint);
                WriteString(output, primary);
            }
            foreach (MetadataTypeSignatureConstraintEvidence constraint
                in parameter.TypeConstraints)
            {
                WriteConstraintSeparator(
                    output,
                    ref wroteConstraint);
                WriteType(
                    output,
                    constraint.Type,
                    signature.TypeParameters,
                    DynamicProjection.Empty);
                if (constraint.NullableFlag == 2)
                    WriteByte(output, (byte)'?');
            }
            if (parameter.ConstructorConstraint
                is { } constructor)
            {
                WriteConstraintSeparator(
                    output,
                    ref wroteConstraint);
                WriteString(output, constructor);
            }
            if (parameter.AllowsRefStructConstraint
                is { } allowsRefStruct)
            {
                WriteConstraintSeparator(
                    output,
                    ref wroteConstraint);
                WriteString(output, allowsRefStruct);
            }
        }
    }

    private static void WriteConstraintSeparator(
        IBufferWriter<byte> output,
        ref bool wroteConstraint)
    {
        WriteBytes(
            output,
            wroteConstraint ? ", "u8 : " : "u8);
        wroteConstraint = true;
    }

    private static void WriteType(
        IBufferWriter<byte> output,
        MetadataTypeSignatureType type,
        ImmutableArray<TypeSignatureGenericParameter> parameters,
        DynamicProjection dynamic)
    {
        switch (type)
        {
            case MetadataTypeSignatureType.Primitive primitive:
                WriteBytes(
                    output,
                    dynamic.IsDynamic(type)
                        && primitive.Code
                            is PrimitiveTypeCode.Object
                            ? "dynamic"u8
                            : PrimitiveName(primitive.Code));
                break;

            case MetadataTypeSignatureType.Named named:
                if (dynamic.IsDynamic(type)
                    && IsSystemObject(named.Name))
                {
                    WriteBytes(output, "dynamic"u8);
                }
                else
                {
                    WriteNamed(
                        output,
                        named.Name,
                        [],
                        parameters,
                        dynamic);
                }
                break;

            case MetadataTypeSignatureType.GenericInstance generic:
                WriteNamed(
                    output,
                    generic.Definition.Name,
                    generic.Arguments,
                    parameters,
                    dynamic);
                break;

            case MetadataTypeSignatureType.TypeParameter parameter:
                if ((uint)parameter.Index
                    >= (uint)parameters.Length)
                {
                    throw new InvalidOperationException(
                        "The complete Type Signature referenced an unavailable Type parameter.");
                }
                WriteIdentifier(
                    output,
                    parameters[parameter.Index].Name);
                break;

            case MetadataTypeSignatureType.Array array:
                WriteType(
                    output,
                    array.Element,
                    parameters,
                    dynamic);
                WriteByte(output, (byte)'[');
                if (array.Rank == 1)
                {
                    WriteByte(output, (byte)'*');
                }
                else
                {
                    for (int rank = 1; rank < array.Rank; rank++)
                        WriteByte(output, (byte)',');
                }
                WriteByte(output, (byte)']');
                break;

            case MetadataTypeSignatureType.SzArray array:
                WriteType(
                    output,
                    array.Element,
                    parameters,
                    dynamic);
                WriteBytes(output, "[]"u8);
                break;

            case MetadataTypeSignatureType.Pointer pointer:
                WriteType(
                    output,
                    pointer.Element,
                    parameters,
                    dynamic);
                WriteByte(output, (byte)'*');
                break;

            case MetadataTypeSignatureType.ByReference byReference:
                WriteBytes(output, "ref "u8);
                WriteType(
                    output,
                    byReference.Element,
                    parameters,
                    dynamic);
                break;

            case MetadataTypeSignatureType.FunctionPointer function:
                WriteFunctionPointer(
                    output,
                    function,
                    parameters,
                    dynamic);
                break;

            case MetadataTypeSignatureType.Modified modified:
                WriteType(
                    output,
                    modified.UnmodifiedType,
                    parameters,
                    dynamic);
                break;

            case MetadataTypeSignatureType.Pinned pinned:
                WriteType(
                    output,
                    pinned.Element,
                    parameters,
                    dynamic);
                break;

            default:
                throw new InvalidOperationException(
                    "Unknown complete Signature Type shape.");
        }
    }

    private static void WriteNamed(
        IBufferWriter<byte> output,
        MetadataTypeSignatureNamedType named,
        ImmutableArray<MetadataTypeSignatureType> arguments,
        ImmutableArray<TypeSignatureGenericParameter> parameters,
        DynamicProjection dynamic)
    {
        WriteNamespace(output, named.Namespace);
        if (named.Namespace.Length > 0)
            WriteByte(output, (byte)'.');

        int argumentIndex = 0;
        for (int segmentIndex = 0;
            segmentIndex < named.Segments.Length;
            segmentIndex++)
        {
            if (segmentIndex > 0)
                WriteByte(output, (byte)'.');

            int introduced =
                named.IntroducedGenericParameterCounts.IsDefaultOrEmpty
                    ? 0
                    : named.IntroducedGenericParameterCounts[
                        segmentIndex];
            WriteIdentifier(
                output,
                named.Segments[segmentIndex],
                introduced);
            if (introduced == 0)
                continue;

            if (argumentIndex + introduced > arguments.Length)
            {
                throw new InvalidOperationException(
                    "The complete Type Signature has fewer generic arguments than its named definition.");
            }
            WriteByte(output, (byte)'<');
            for (int offset = 0; offset < introduced; offset++)
            {
                if (offset > 0)
                    WriteBytes(output, ", "u8);
                WriteType(
                    output,
                    arguments[argumentIndex++],
                    parameters,
                    dynamic);
            }
            WriteByte(output, (byte)'>');
        }

        if (argumentIndex != arguments.Length)
        {
            throw new InvalidOperationException(
                "The complete Type Signature has more generic arguments than its named definition.");
        }
    }

    private static void WriteFunctionPointer(
        IBufferWriter<byte> output,
        MetadataTypeSignatureType.FunctionPointer function,
        ImmutableArray<TypeSignatureGenericParameter> parameters,
        DynamicProjection dynamic)
    {
        WriteBytes(output, "delegate*"u8);
        ReadOnlySpan<byte> convention =
            function.Header.CallingConvention switch
            {
                SignatureCallingConvention.Default => [],
                SignatureCallingConvention.CDecl =>
                    " unmanaged[Cdecl]"u8,
                SignatureCallingConvention.StdCall =>
                    " unmanaged[Stdcall]"u8,
                SignatureCallingConvention.ThisCall =>
                    " unmanaged[Thiscall]"u8,
                SignatureCallingConvention.FastCall =>
                    " unmanaged[Fastcall]"u8,
                _ => " unmanaged"u8,
            };
        WriteBytes(output, convention);
        WriteByte(output, (byte)'<');
        for (int index = 0;
            index < function.ParameterTypes.Length;
            index++)
        {
            if (index > 0)
                WriteBytes(output, ", "u8);
            WriteType(
                output,
                function.ParameterTypes[index],
                parameters,
                dynamic);
        }
        if (!function.ParameterTypes.IsEmpty)
            WriteBytes(output, ", "u8);
        WriteType(
            output,
            function.ReturnType,
            parameters,
            dynamic);
        WriteByte(output, (byte)'>');
    }

    private static void WriteNamespace(
        IBufferWriter<byte> output,
        MetadataUtf8Name @namespace)
    {
        ImmutableArray<MetadataUtf8Name> segments =
            @namespace.SplitNamespaceComponents();
        for (int index = 0; index < segments.Length; index++)
        {
            if (index > 0)
                WriteByte(output, (byte)'.');
            WriteIdentifier(output, segments[index]);
        }
    }

    private static void WriteIdentifier(
        IBufferWriter<byte> output,
        MetadataUtf8Name name,
        int introducedGenericParameterCount = 0)
    {
        MetadataUtf8Name display =
            introducedGenericParameterCount > 0
                && name.TryRemoveAritySuffix(
                    introducedGenericParameterCount,
                    out MetadataUtf8Name withoutArity)
                    ? withoutArity
                    : name;
        if (RequiresDeclarationEscape(display))
            WriteByte(output, (byte)'@');
        display.WriteCSharpComposedFieldTo(output);
    }

    private static bool RequiresDeclarationEscape(
        MetadataUtf8Name name) =>
        name.ValueTextEquals("abstract"u8)
        || name.ValueTextEquals("as"u8)
        || name.ValueTextEquals("base"u8)
        || name.ValueTextEquals("bool"u8)
        || name.ValueTextEquals("break"u8)
        || name.ValueTextEquals("byte"u8)
        || name.ValueTextEquals("case"u8)
        || name.ValueTextEquals("catch"u8)
        || name.ValueTextEquals("char"u8)
        || name.ValueTextEquals("checked"u8)
        || name.ValueTextEquals("class"u8)
        || name.ValueTextEquals("const"u8)
        || name.ValueTextEquals("continue"u8)
        || name.ValueTextEquals("decimal"u8)
        || name.ValueTextEquals("default"u8)
        || name.ValueTextEquals("delegate"u8)
        || name.ValueTextEquals("do"u8)
        || name.ValueTextEquals("double"u8)
        || name.ValueTextEquals("else"u8)
        || name.ValueTextEquals("enum"u8)
        || name.ValueTextEquals("event"u8)
        || name.ValueTextEquals("explicit"u8)
        || name.ValueTextEquals("extern"u8)
        || name.ValueTextEquals("false"u8)
        || name.ValueTextEquals("finally"u8)
        || name.ValueTextEquals("fixed"u8)
        || name.ValueTextEquals("float"u8)
        || name.ValueTextEquals("for"u8)
        || name.ValueTextEquals("foreach"u8)
        || name.ValueTextEquals("goto"u8)
        || name.ValueTextEquals("if"u8)
        || name.ValueTextEquals("implicit"u8)
        || name.ValueTextEquals("in"u8)
        || name.ValueTextEquals("int"u8)
        || name.ValueTextEquals("interface"u8)
        || name.ValueTextEquals("internal"u8)
        || name.ValueTextEquals("is"u8)
        || name.ValueTextEquals("lock"u8)
        || name.ValueTextEquals("long"u8)
        || name.ValueTextEquals("namespace"u8)
        || name.ValueTextEquals("new"u8)
        || name.ValueTextEquals("null"u8)
        || name.ValueTextEquals("object"u8)
        || name.ValueTextEquals("operator"u8)
        || name.ValueTextEquals("out"u8)
        || name.ValueTextEquals("override"u8)
        || name.ValueTextEquals("params"u8)
        || name.ValueTextEquals("private"u8)
        || name.ValueTextEquals("protected"u8)
        || name.ValueTextEquals("public"u8)
        || name.ValueTextEquals("readonly"u8)
        || name.ValueTextEquals("ref"u8)
        || name.ValueTextEquals("return"u8)
        || name.ValueTextEquals("sbyte"u8)
        || name.ValueTextEquals("sealed"u8)
        || name.ValueTextEquals("short"u8)
        || name.ValueTextEquals("sizeof"u8)
        || name.ValueTextEquals("stackalloc"u8)
        || name.ValueTextEquals("static"u8)
        || name.ValueTextEquals("string"u8)
        || name.ValueTextEquals("struct"u8)
        || name.ValueTextEquals("switch"u8)
        || name.ValueTextEquals("this"u8)
        || name.ValueTextEquals("throw"u8)
        || name.ValueTextEquals("true"u8)
        || name.ValueTextEquals("try"u8)
        || name.ValueTextEquals("typeof"u8)
        || name.ValueTextEquals("uint"u8)
        || name.ValueTextEquals("ulong"u8)
        || name.ValueTextEquals("unchecked"u8)
        || name.ValueTextEquals("unsafe"u8)
        || name.ValueTextEquals("ushort"u8)
        || name.ValueTextEquals("using"u8)
        || name.ValueTextEquals("virtual"u8)
        || name.ValueTextEquals("void"u8)
        || name.ValueTextEquals("volatile"u8)
        || name.ValueTextEquals("while"u8)
        || name.ValueTextEquals("__arglist"u8)
        || name.ValueTextEquals("__makeref"u8)
        || name.ValueTextEquals("__reftype"u8)
        || name.ValueTextEquals("__refvalue"u8)
        || name.ValueTextEquals("await"u8)
        || name.ValueTextEquals("closed"u8)
        || name.ValueTextEquals("file"u8)
        || name.ValueTextEquals("init"u8)
        || name.ValueTextEquals("record"u8)
        || name.ValueTextEquals("required"u8)
        || name.ValueTextEquals("scoped"u8);

    private static bool IsSystemObject(
        MetadataTypeSignatureNamedType name) =>
        name.Namespace.ValueTextEquals("System"u8)
        && name.Segments is [var segment]
        && segment.ValueTextEquals("Object"u8);

    private static ReadOnlySpan<byte> PrimitiveName(
        PrimitiveTypeCode code) =>
        code switch
        {
            PrimitiveTypeCode.Void => "void"u8,
            PrimitiveTypeCode.Boolean => "bool"u8,
            PrimitiveTypeCode.Char => "char"u8,
            PrimitiveTypeCode.SByte => "sbyte"u8,
            PrimitiveTypeCode.Byte => "byte"u8,
            PrimitiveTypeCode.Int16 => "short"u8,
            PrimitiveTypeCode.UInt16 => "ushort"u8,
            PrimitiveTypeCode.Int32 => "int"u8,
            PrimitiveTypeCode.UInt32 => "uint"u8,
            PrimitiveTypeCode.Int64 => "long"u8,
            PrimitiveTypeCode.UInt64 => "ulong"u8,
            PrimitiveTypeCode.Single => "float"u8,
            PrimitiveTypeCode.Double => "double"u8,
            PrimitiveTypeCode.String => "string"u8,
            PrimitiveTypeCode.Object => "object"u8,
            PrimitiveTypeCode.IntPtr => "nint"u8,
            PrimitiveTypeCode.UIntPtr => "nuint"u8,
            PrimitiveTypeCode.TypedReference =>
                "TypedReference"u8,
            _ => throw new InvalidOperationException(
                "Unknown primitive Type code."),
        };

    private static void WriteCountRow(
        IBufferWriter<byte> output,
        ReadOnlySpan<byte> label,
        int value)
    {
        WriteBytes(output, label);
        WriteByte(output, (byte)'\t');
        Span<byte> text = stackalloc byte[11];
        if (!value.TryFormat(
                text,
                out int written,
                provider:
                    System.Globalization.CultureInfo
                        .InvariantCulture))
        {
            throw new InvalidOperationException(
                "A nonnegative Count did not fit its invariant decimal representation.");
        }
        WriteBytes(output, text[..written]);
        WriteByte(output, (byte)'\n');
    }

    private static void WriteRow(
        IBufferWriter<byte> output,
        ReadOnlySpan<byte> label,
        string value)
    {
        WriteBytes(output, label);
        WriteByte(output, (byte)'\t');
        WriteString(
            output,
            new InertString(
                TextPolicy.Field,
                value).ToString());
        WriteByte(output, (byte)'\n');
    }

    private static void WriteString(
        IBufferWriter<byte> output,
        string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> destination = output.GetSpan(byteCount);
        int written = Encoding.UTF8.GetBytes(
            value,
            destination);
        output.Advance(written);
    }

    private static void WriteByte(
        IBufferWriter<byte> output,
        byte value)
    {
        Span<byte> destination = output.GetSpan(1);
        destination[0] = value;
        output.Advance(1);
    }

    private static void WriteBytes(
        IBufferWriter<byte> output,
        ReadOnlySpan<byte> value)
    {
        value.CopyTo(output.GetSpan(value.Length));
        output.Advance(value.Length);
    }

    private sealed class DynamicProjection
    {
        private readonly HashSet<MetadataTypeSignatureType> _dynamic =
            new(ReferenceEqualityComparer.Instance);
        private ImmutableArray<byte> _flags;
        private int _position;

        internal static DynamicProjection Empty { get; } =
            new();

        internal static DynamicProjection Create(
            MetadataTypeSignatureType type,
            ImmutableArray<byte> flags)
        {
            var projection = new DynamicProjection
            {
                _flags = flags,
            };
            if (!flags.IsDefaultOrEmpty)
                projection.Visit(type);
            return projection;
        }

        internal bool IsDynamic(
            MetadataTypeSignatureType type) =>
            _dynamic.Contains(type);

        private void Visit(MetadataTypeSignatureType type)
        {
            switch (type)
            {
                case MetadataTypeSignatureType.Primitive primitive:
                    Consume(
                        type,
                        primitive.Code
                            is PrimitiveTypeCode.Object);
                    break;
                case MetadataTypeSignatureType.Named named:
                    Consume(type, IsSystemObject(named.Name));
                    break;
                case MetadataTypeSignatureType.GenericInstance generic:
                    Consume(type, canBeDynamic: false);
                    foreach (MetadataTypeSignatureType argument
                        in generic.Arguments)
                    {
                        Visit(argument);
                    }
                    break;
                case MetadataTypeSignatureType.TypeParameter:
                    Consume(type, canBeDynamic: false);
                    break;
                case MetadataTypeSignatureType.Array array:
                    Consume(type, canBeDynamic: false);
                    Visit(array.Element);
                    break;
                case MetadataTypeSignatureType.SzArray array:
                    Consume(type, canBeDynamic: false);
                    Visit(array.Element);
                    break;
                case MetadataTypeSignatureType.Pointer pointer:
                    Consume(type, canBeDynamic: false);
                    Visit(pointer.Element);
                    break;
                case MetadataTypeSignatureType.ByReference byReference:
                    Consume(type, canBeDynamic: false);
                    Visit(byReference.Element);
                    break;
                case MetadataTypeSignatureType.FunctionPointer function:
                    Consume(type, canBeDynamic: false);
                    Visit(function.ReturnType);
                    foreach (MetadataTypeSignatureType parameter
                        in function.ParameterTypes)
                    {
                        Visit(parameter);
                    }
                    break;
                case MetadataTypeSignatureType.Modified modified:
                    Consume(type, canBeDynamic: false);
                    Visit(modified.UnmodifiedType);
                    break;
                case MetadataTypeSignatureType.Pinned pinned:
                    Visit(pinned.Element);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown complete Signature Type shape.");
            }
        }

        private void Consume(
            MetadataTypeSignatureType type,
            bool canBeDynamic)
        {
            byte flag = _position < _flags.Length
                ? _flags[_position]
                : (byte)0;
            _position++;
            if (canBeDynamic && flag != 0)
                _dynamic.Add(type);
        }
    }
}
