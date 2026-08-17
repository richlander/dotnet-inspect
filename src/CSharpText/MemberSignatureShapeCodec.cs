using System.Globalization;
using System.Text;

namespace CSharpText;

/// <summary>
/// Canonical, versioned text transport for <see cref="MemberSignatureShape"/>.
/// </summary>
public static class MemberSignatureShapeCodec
{
    const string Prefix = "mss1:";
    internal const int MaxTextLength = 64 * 1024;
    internal const int MaxNodes = 4096;
    internal const int MaxDepth = 128;
    internal const int MaxCollectionElements = 4096;

    public static string Encode(MemberSignatureShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape.GenericArity < 0
            || shape.Parameters.Count > MaxNodes)
        {
            throw new ArgumentException(
                "The member signature shape is invalid or exceeds the safety limit.",
                nameof(shape));
        }

        var writer = new Writer();
        writer.Collection(shape.Parameters.Count, shape);
        writer.Integer(shape.GenericArity);
        writer.Character('(');
        writer.Integer(shape.Parameters.Count);
        writer.Character(':');
        foreach (MemberParameterSignatureShape parameter in shape.Parameters)
        {
            writer.Character(parameter.Passing switch
            {
                ParameterPassingKind.Value => 'v',
                ParameterPassingKind.ByReference => 'r',
                _ => throw new ArgumentException(
                    "The parameter passing kind is invalid.",
                    nameof(shape)),
            });
            writer.Type(parameter.Type);
        }
        writer.Character(')');
        if (shape.ConversionReturnType is null)
        {
            writer.Character('n');
        }
        else
        {
            writer.Character('y');
            writer.Type(shape.ConversionReturnType);
        }

        return Prefix + writer.ToString();
    }

    public static MemberSignatureShapeResult Decode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return MemberSignatureShapeResult.Unavailable("The signature text is empty.");
        if (text.Length > MaxTextLength)
            return MemberSignatureShapeResult.Unavailable("The signature text exceeds the safety limit.");

        try
        {
            if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            {
                MemberSignatureShapeResult legacy = LegacyMemberSignatureShape.Decode(text);
                if (legacy.Shape is null)
                    return legacy;

                _ = Encode(legacy.Shape);
                return legacy;
            }

            var reader = new Reader(text.AsSpan(Prefix.Length));
            int genericArity = reader.Integer();
            reader.Character('(');
            int parameterCount = reader.Integer();
            reader.Character(':');
            if (genericArity < 0 || parameterCount < 0 || parameterCount > MaxNodes)
                throw new FormatException();

            reader.Collection(parameterCount);
            var parameters = new MemberParameterSignatureShape[parameterCount];
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterPassingKind passing = reader.Take() switch
                {
                    'v' => ParameterPassingKind.Value,
                    'r' => ParameterPassingKind.ByReference,
                    _ => throw new FormatException(),
                };
                parameters[i] = new(passing, reader.Type());
            }

            reader.Character(')');
            TypeSignatureShape? returnType = reader.Take() switch
            {
                'n' => null,
                'y' => reader.Type(),
                _ => throw new FormatException(),
            };
            reader.End();
            var shape = new MemberSignatureShape(genericArity, new(parameters), returnType);
            return string.Equals(Encode(shape), text, StringComparison.Ordinal)
                ? MemberSignatureShapeResult.Available(shape)
                : MemberSignatureShapeResult.Unavailable("The signature text is not canonical.");
        }
        catch (FormatException)
        {
            return MemberSignatureShapeResult.Unavailable("The signature text is malformed.");
        }
        catch (ArgumentException)
        {
            return MemberSignatureShapeResult.Unavailable(
                "The signature text is invalid or exceeds the safety limit.");
        }
    }

    public static MemberSignatureShapeResult Normalize(string text, out string? canonicalText)
    {
        MemberSignatureShapeResult decoded = Decode(text);
        canonicalText = decoded.Shape is null ? null : Encode(decoded.Shape);
        return decoded;
    }

    sealed class Writer
    {
        readonly StringBuilder _builder = new();
        int _nodes;
        int _collectionElements;

        internal void Type(TypeSignatureShape type, int depth = 1)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (depth > MaxDepth || ++_nodes > MaxNodes)
                throw new ArgumentException("The signature shape exceeds the safety limit.", nameof(type));

            switch (type)
            {
                case PrimitiveTypeSignatureShape primitive:
                    NonEmpty(primitive.ClrName, type);
                    Character('p');
                    Text(primitive.ClrName);
                    break;
                case GenericParameterTypeSignatureShape parameter:
                    NonNegative(parameter.Position, type);
                    Character(parameter.Kind switch
                    {
                        SignatureGenericParameterKind.Type => 't',
                        SignatureGenericParameterKind.Method => 'm',
                        _ => throw Invalid(type),
                    });
                    Integer(parameter.Position);
                    Character(';');
                    break;
                case NamedTypeSignatureShape named:
                    if (named.Segments.Count == 0 || named.Segments.Count > MaxNodes)
                        throw Invalid(type);
                    Collection(named.Segments.Count, type);
                    Character('n');
                    Text(named.Namespace);
                    Integer(named.Segments.Count);
                    Character(':');
                    foreach (NamedTypeSegment segment in named.Segments)
                    {
                        NonEmpty(segment.Name, type);
                        NonNegative(segment.Arity, type);
                        if (segment.TypeArguments.Count > MaxNodes)
                            throw Invalid(type);
                        Collection(segment.TypeArguments.Count, type);
                        Text(segment.Name);
                        Integer(segment.Arity);
                        Character(':');
                        Integer(segment.TypeArguments.Count);
                        Character(':');
                        foreach (TypeSignatureShape argument in segment.TypeArguments)
                            Type(argument, depth + 1);
                    }
                    break;
                case UnresolvedNamedTypeSignatureShape unresolved:
                    NonEmpty(unresolved.Name, type);
                    if (unresolved.TypeArguments.Count > MaxNodes)
                        throw Invalid(type);
                    Collection(unresolved.TypeArguments.Count, type);
                    Character('x');
                    Text(unresolved.Name);
                    Integer(unresolved.TypeArguments.Count);
                    Character(':');
                    foreach (TypeSignatureShape argument in unresolved.TypeArguments)
                        Type(argument, depth + 1);
                    break;
                case ArrayTypeSignatureShape array:
                    if (array.Rank <= 0 || (array.IsSzArray && array.Rank != 1))
                        throw Invalid(type);
                    Character(array.IsSzArray ? 'z' : 'a');
                    Integer(array.Rank);
                    Character(':');
                    Type(array.ElementType, depth + 1);
                    break;
                case PointerTypeSignatureShape pointer:
                    Character('*');
                    Type(pointer.ElementType, depth + 1);
                    break;
                case ByReferenceTypeSignatureShape byReference:
                    Character('&');
                    Type(byReference.ElementType, depth + 1);
                    break;
                case NullableTypeSignatureShape nullable:
                    Character('?');
                    Type(nullable.UnderlyingType, depth + 1);
                    break;
                case TupleTypeSignatureShape tuple:
                    if (tuple.ElementTypes.Count < 2 || tuple.ElementTypes.Count > MaxNodes)
                        throw Invalid(type);
                    Collection(tuple.ElementTypes.Count, type);
                    Character('u');
                    Integer(tuple.ElementTypes.Count);
                    Character(':');
                    foreach (TypeSignatureShape element in tuple.ElementTypes)
                        Type(element, depth + 1);
                    break;
                case FunctionPointerTypeSignatureShape functionPointer:
                    NonEmpty(functionPointer.CallingConvention, type);
                    if (functionPointer.ParameterTypes.Count > MaxNodes)
                        throw Invalid(type);
                    Collection(functionPointer.ParameterTypes.Count, type);
                    Character('f');
                    Text(functionPointer.CallingConvention);
                    Type(functionPointer.ReturnType, depth + 1);
                    Integer(functionPointer.ParameterTypes.Count);
                    Character(':');
                    foreach (TypeSignatureShape parameter in functionPointer.ParameterTypes)
                        Type(parameter, depth + 1);
                    break;
                default:
                    throw new ArgumentException("Unknown signature-shape node.", nameof(type));
            }
        }

        static void NonEmpty(string value, TypeSignatureShape type)
        {
            if (string.IsNullOrEmpty(value))
                throw Invalid(type);
        }

        static void NonNegative(int value, TypeSignatureShape type)
        {
            if (value < 0)
                throw Invalid(type);
        }

        static ArgumentException Invalid(TypeSignatureShape type)
            => new(
                $"The {type.GetType().Name} node is invalid or exceeds the safety limit.",
                nameof(type));

        internal void Collection(int count, object value)
        {
            if (count < 0 || count > MaxCollectionElements - _collectionElements)
            {
                throw new ArgumentException(
                    "The signature shape exceeds the collection safety limit.",
                    value is MemberSignatureShape ? "shape" : "type");
            }
            _collectionElements += count;
        }

        internal void Text(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            Integer(text.Length);
            Character(':');
            EnsureAdditional(text.Length);
            _builder.Append(text);
        }

        internal void Integer(int value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            EnsureAdditional(text.Length);
            _builder.Append(text);
        }

        internal void Character(char value)
        {
            EnsureAdditional(1);
            _builder.Append(value);
        }

        void EnsureAdditional(int count)
        {
            if (count < 0 || count > MaxTextLength - Prefix.Length - _builder.Length)
                throw new ArgumentException("The signature shape exceeds the safety limit.");
        }

        public override string ToString() => _builder.ToString();
    }

    ref struct Reader
    {
        readonly ReadOnlySpan<char> _text;
        int _offset;
        int _nodes;
        int _collectionElements;

        internal Reader(ReadOnlySpan<char> text)
        {
            _text = text;
        }

        internal char Take()
        {
            if (_offset >= _text.Length)
                throw new FormatException();
            return _text[_offset++];
        }

        internal void Character(char expected)
        {
            if (Take() != expected)
                throw new FormatException();
        }

        internal int Integer()
        {
            int start = _offset;
            while (_offset < _text.Length && char.IsAsciiDigit(_text[_offset]))
                _offset++;
            if (start == _offset
                || !int.TryParse(
                    _text[start.._offset],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                throw new FormatException();
            }
            return value;
        }

        internal string Text()
        {
            int length = Integer();
            Character(':');
            if (length < 0 || length > _text.Length - _offset)
                throw new FormatException();
            string value = _text.Slice(_offset, length).ToString();
            _offset += length;
            return value;
        }

        internal TypeSignatureShape Type(int depth = 1)
        {
            if (depth > MaxDepth || ++_nodes > MaxNodes)
                throw new FormatException();

            return Take() switch
            {
                'p' => Primitive(),
                't' => Generic(SignatureGenericParameterKind.Type),
                'm' => Generic(SignatureGenericParameterKind.Method),
                'n' => Named(depth),
                'x' => UnresolvedNamed(depth),
                'z' => Array(isSzArray: true, depth),
                'a' => Array(isSzArray: false, depth),
                '*' => new PointerTypeSignatureShape(Type(depth + 1)),
                '&' => new ByReferenceTypeSignatureShape(Type(depth + 1)),
                '?' => new NullableTypeSignatureShape(Type(depth + 1)),
                'u' => Tuple(depth),
                'f' => FunctionPointer(depth),
                _ => throw new FormatException(),
            };
        }

        TypeSignatureShape Primitive()
        {
            string name = Text();
            if (string.IsNullOrEmpty(name))
                throw new FormatException();
            return new PrimitiveTypeSignatureShape(name);
        }

        TypeSignatureShape Generic(SignatureGenericParameterKind kind)
        {
            int position = Integer();
            Character(';');
            if (position < 0)
                throw new FormatException();
            return new GenericParameterTypeSignatureShape(kind, position);
        }

        TypeSignatureShape Named(int depth)
        {
            string @namespace = Text();
            int count = Integer();
            Character(':');
            if (count <= 0 || count > MaxNodes)
                throw new FormatException();

            Collection(count);
            var segments = new NamedTypeSegment[count];
            for (int i = 0; i < segments.Length; i++)
            {
                string name = Text();
                int arity = Integer();
                Character(':');
                int argumentCount = Integer();
                Character(':');
                if (string.IsNullOrEmpty(name)
                    || arity < 0
                    || argumentCount < 0
                    || argumentCount > MaxNodes)
                {
                    throw new FormatException();
                }

                Collection(argumentCount);
                var arguments = new TypeSignatureShape[argumentCount];
                for (int j = 0; j < arguments.Length; j++)
                    arguments[j] = Type(depth + 1);
                segments[i] = new(name, arity, new(arguments));
            }
            return new NamedTypeSignatureShape(@namespace, new(segments));
        }

        TypeSignatureShape UnresolvedNamed(int depth)
        {
            string name = Text();
            int count = Integer();
            Character(':');
            if (string.IsNullOrEmpty(name) || count < 0 || count > MaxNodes)
                throw new FormatException();
            Collection(count);
            var arguments = new TypeSignatureShape[count];
            for (int i = 0; i < arguments.Length; i++)
                arguments[i] = Type(depth + 1);
            return new UnresolvedNamedTypeSignatureShape(name, new(arguments));
        }

        TypeSignatureShape Array(bool isSzArray, int depth)
        {
            int rank = Integer();
            Character(':');
            if (rank <= 0 || (isSzArray && rank != 1))
                throw new FormatException();
            return new ArrayTypeSignatureShape(Type(depth + 1), rank, isSzArray);
        }

        TypeSignatureShape Tuple(int depth)
        {
            int count = Integer();
            Character(':');
            if (count < 2 || count > MaxNodes)
                throw new FormatException();
            Collection(count);
            var elements = new TypeSignatureShape[count];
            for (int i = 0; i < elements.Length; i++)
                elements[i] = Type(depth + 1);
            return new TupleTypeSignatureShape(new(elements));
        }

        TypeSignatureShape FunctionPointer(int depth)
        {
            string callingConvention = Text();
            if (string.IsNullOrEmpty(callingConvention))
                throw new FormatException();
            TypeSignatureShape returnType = Type(depth + 1);
            int count = Integer();
            Character(':');
            if (count < 0 || count > MaxNodes)
                throw new FormatException();
            Collection(count);
            var parameters = new TypeSignatureShape[count];
            for (int i = 0; i < parameters.Length; i++)
                parameters[i] = Type(depth + 1);
            return new FunctionPointerTypeSignatureShape(
                callingConvention,
                returnType,
                new(parameters));
        }

        internal void Collection(int count)
        {
            if (count < 0 || count > MaxCollectionElements - _collectionElements)
                throw new FormatException();
            _collectionElements += count;
        }

        internal void End()
        {
            if (_offset != _text.Length)
                throw new FormatException();
        }
    }
}
