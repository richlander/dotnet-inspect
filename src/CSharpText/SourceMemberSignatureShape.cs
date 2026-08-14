using System.Globalization;

namespace CSharpText;

/// <summary>
/// Produces a conservative signature shape from one source declaration without loading
/// assemblies or consulting using directives.
/// </summary>
public static class SourceMemberSignatureShape
{
    const int MaxDeclarationLength = 256 * 1024;
    const int MaxParameterCount = 1024;

    public static MemberSignatureShapeResult Create(
        string declarationText,
        SourceMemberSignatureKind kind,
        IReadOnlyList<string>? containingTypeParameterNames = null,
        IReadOnlySet<string>? containingValueTypeParameterNames = null)
    {
        if (string.IsNullOrWhiteSpace(declarationText))
            return MemberSignatureShapeResult.Unavailable("The source declaration is empty.");
        if (declarationText.Length > MaxDeclarationLength)
            return MemberSignatureShapeResult.Unavailable("The source declaration exceeds the safety limit.");

        try
        {
            string[] lines = declarationText.ReplaceLineEndings("\n").Split('\n');
            List<ScanToken> scanned = CSharpLexer.ScanTokens(lines);
            var tokens = scanned
                .Where(token => token.Kind is ScanTokenKind.Word
                    or ScanTokenKind.Punctuator
                    or ScanTokenKind.Directive)
                .Select(token => new SourceToken(
                    token.Kind,
                    token.TextIn(lines[token.Line]).ToString(),
                    token.Depth,
                    token.BracketDepth))
                .ToArray();

            return Parse(
                tokens,
                kind,
                containingTypeParameterNames ?? Array.Empty<string>(),
                containingValueTypeParameterNames ?? new HashSet<string>(StringComparer.Ordinal));
        }
        catch (InvalidOperationException ex)
        {
            return MemberSignatureShapeResult.Unavailable(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return MemberSignatureShapeResult.Unavailable(ex.Message);
        }
    }

    static MemberSignatureShapeResult Parse(
        SourceToken[] tokens,
        SourceMemberSignatureKind kind,
        IReadOnlyList<string> containingTypeParameterNames,
        IReadOnlySet<string> containingValueTypeParameterNames)
    {
        if (tokens.Length == 0)
            return MemberSignatureShapeResult.Unavailable("The declaration has no signature tokens.");

        int open;
        int close;
        bool indexer = kind == SourceMemberSignatureKind.Indexer;
        if (indexer)
        {
            if (!TryFindIndexerParameters(tokens, out open, out close))
                return MemberSignatureShapeResult.Unavailable("The indexer parameter list is malformed.");
        }
        else if (kind == SourceMemberSignatureKind.Property)
        {
            int boundary = FindHeaderBoundary(tokens, 0);
            if (HasDirective(tokens, 0, boundary))
                return MemberSignatureShapeResult.Unavailable("A directive occurs in the declaration header.");
            return MemberSignatureShapeResult.Available(
                new MemberSignatureShape(
                    0,
                    SignatureShapeList<MemberParameterSignatureShape>.Empty));
        }
        else if (!TryFindParenthesizedParameters(tokens, out open, out close))
        {
            return MemberSignatureShapeResult.Unavailable("The member parameter list is malformed.");
        }

        int headerEnd = FindHeaderBoundary(tokens, close + 1);
        if (HasDirective(tokens, 0, headerEnd))
            return MemberSignatureShapeResult.Unavailable("A directive occurs in the declaration header.");

        (int genericArity, IReadOnlyList<string> methodTypeParameters) =
            kind == SourceMemberSignatureKind.Method
                ? ReadMethodTypeParameters(tokens, open)
                : (0, Array.Empty<string>());
        IReadOnlySet<string> methodValueTypeParameters =
            ReadMethodValueTypeParameters(tokens, close + 1, headerEnd, methodTypeParameters);

        if (!TryParseParameters(
                tokens,
                open + 1,
                close,
                indexer ? "[" : "(",
                containingTypeParameterNames,
                methodTypeParameters,
                containingValueTypeParameterNames,
                methodValueTypeParameters,
                out var parameters,
                out string? reason))
        {
            return MemberSignatureShapeResult.Unavailable(reason!);
        }

        TypeSignatureShape? conversionReturnType = null;
        if (kind == SourceMemberSignatureKind.ConversionOperator)
        {
            int operatorIndex = FindWord(tokens, "operator", 0, open);
            if (operatorIndex < 0 || operatorIndex + 1 >= open)
                return MemberSignatureShapeResult.Unavailable("The conversion return type is malformed.");

            if (!SourceTypeShapeParser.TryParse(
                    tokens[(operatorIndex + 1)..open],
                    containingTypeParameterNames,
                    methodTypeParameters,
                    containingValueTypeParameterNames,
                    methodValueTypeParameters,
                    out conversionReturnType,
                    out reason))
            {
                return MemberSignatureShapeResult.Unavailable(reason!);
            }
        }

        var shape = new MemberSignatureShape(
            genericArity,
            new(parameters),
            conversionReturnType);
        _ = MemberSignatureShapeCodec.Encode(shape);
        return MemberSignatureShapeResult.Available(shape);
    }

    static bool TryFindParenthesizedParameters(
        SourceToken[] tokens,
        out int open,
        out int close)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Text != "(" || tokens[i].Depth != 0 || tokens[i].BracketDepth != 0)
                continue;
            if (!TryFindMatching(tokens, i, "(", ")", out int candidateClose))
                break;

            int next = candidateClose + 1;
            if (next >= tokens.Length
                || tokens[next].Text is "where" or ":" or "{" or ";" or "="
                || (tokens[next].Text == "="
                    && next + 1 < tokens.Length
                    && tokens[next + 1].Text == ">"))
            {
                open = i;
                close = candidateClose;
                return true;
            }
        }

        open = -1;
        close = -1;
        return false;
    }

    static bool TryFindIndexerParameters(
        SourceToken[] tokens,
        out int open,
        out int close)
    {
        int thisIndex = FindWord(tokens, "this", 0, tokens.Length);
        if (thisIndex >= 0
            && thisIndex + 1 < tokens.Length
            && tokens[thisIndex + 1].Text == "["
            && TryFindMatching(tokens, thisIndex + 1, "[", "]", out close))
        {
            open = thisIndex + 1;
            return true;
        }

        open = -1;
        close = -1;
        return false;
    }

    static int FindHeaderBoundary(SourceToken[] tokens, int start)
    {
        for (int i = start; i < tokens.Length; i++)
        {
            if (tokens[i].Depth != 0 || tokens[i].BracketDepth != 0)
                continue;
            if (tokens[i].Text is "{" or ";")
                return i;
            if (tokens[i].Text == "="
                && i + 1 < tokens.Length
                && tokens[i + 1].Text == ">")
            {
                return i;
            }
        }
        return tokens.Length;
    }

    static bool HasDirective(SourceToken[] tokens, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (tokens[i].Kind == ScanTokenKind.Directive)
                return true;
        }
        return false;
    }

    static (int Arity, IReadOnlyList<string> Names) ReadMethodTypeParameters(
        SourceToken[] tokens,
        int parameterOpen)
    {
        int close = parameterOpen - 1;
        if (close < 0 || tokens[close].Text != ">")
            return (0, Array.Empty<string>());

        int depth = 0;
        int open = -1;
        for (int i = close; i >= 0; i--)
        {
            if (tokens[i].Text == ">")
                depth++;
            else if (tokens[i].Text == "<" && --depth == 0)
            {
                open = i;
                break;
            }
        }
        if (open < 0)
            throw new InvalidOperationException("The method type-parameter list is malformed.");

        var names = SplitTopLevel(tokens, open + 1, close, ",")
            .Select(range =>
            {
                int nameIndex = range.End - 1;
                bool valid = nameIndex >= range.Start
                    && IsIdentifier(tokens[nameIndex])
                    && (nameIndex == range.Start
                        || (nameIndex == range.Start + 1
                            && tokens[range.Start].Text == "@"));
                if (!valid)
                    throw new InvalidOperationException("A method type parameter is malformed.");
                return IdentifierText(tokens, nameIndex);
            })
            .ToArray();
        return (names.Length, names);
    }

    static IReadOnlySet<string> ReadMethodValueTypeParameters(
        SourceToken[] tokens,
        int start,
        int end,
        IReadOnlyList<string> methodTypeParameters)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (int i = start; i + 3 < end; i++)
        {
            if (tokens[i].Text != "where"
                || !IsIdentifier(tokens[i + 1])
                || tokens[i + 2].Text != ":")
            {
                continue;
            }

            string name = tokens[i + 1].Text;
            if (!methodTypeParameters.Contains(name, StringComparer.Ordinal))
                continue;
            for (int j = i + 3; j < end && tokens[j].Text != "where"; j++)
            {
                if (tokens[j].Text is "struct" or "unmanaged")
                {
                    result.Add(name);
                    break;
                }
            }
        }
        return result;
    }

    static bool TryParseParameters(
        SourceToken[] tokens,
        int start,
        int end,
        string delimiter,
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<string> methodParameters,
        IReadOnlySet<string> typeValueParameters,
        IReadOnlySet<string> methodValueParameters,
        out MemberParameterSignatureShape[] parameters,
        out string? reason)
    {
        var ranges = SplitTopLevel(tokens, start, end, ",").ToArray();
        if (ranges.Length == 1 && ranges[0].Start == ranges[0].End)
        {
            parameters = [];
            reason = null;
            return true;
        }
        if (ranges.Length > MaxParameterCount)
        {
            parameters = [];
            reason = "The parameter count exceeds the safety limit.";
            return false;
        }

        parameters = new MemberParameterSignatureShape[ranges.Length];
        for (int i = 0; i < ranges.Length; i++)
        {
            int parameterStart = ranges[i].Start;
            int parameterEnd = ranges[i].End;
            SkipAttributeLists(tokens, ref parameterStart, parameterEnd);
            CutDefaultValue(tokens, parameterStart, ref parameterEnd);

            ParameterPassingKind passing = ParameterPassingKind.Value;
            while (parameterStart < parameterEnd)
            {
                string text = tokens[parameterStart].Text;
                if (text is "this" or "scoped" or "params")
                {
                    parameterStart++;
                    continue;
                }
                if (text is "ref" or "out" or "in")
                {
                    passing = ParameterPassingKind.ByReference;
                    parameterStart++;
                    if (parameterStart < parameterEnd
                        && tokens[parameterStart].Text == "readonly")
                    {
                        parameterStart++;
                    }
                    continue;
                }
                break;
            }

            int nameIndex = LastIdentifier(tokens, parameterStart, parameterEnd);
            if (nameIndex < 0 || nameIndex != parameterEnd - 1)
            {
                reason = $"A parameter in the {delimiter} list has no unambiguous name.";
                return false;
            }
            bool escapedName = nameIndex > parameterStart
                && tokens[nameIndex - 1].Text == "@";
            if (!escapedName
                && CSharpKeywords.RequiresDeclarationEscape(tokens[nameIndex].Text))
            {
                reason = $"A parameter in the {delimiter} list uses an unescaped keyword.";
                return false;
            }
            int typeEnd = nameIndex > parameterStart
                && escapedName
                    ? nameIndex - 1
                    : nameIndex;

            if (!SourceTypeShapeParser.TryParse(
                    tokens[parameterStart..typeEnd],
                    typeParameters,
                    methodParameters,
                    typeValueParameters,
                    methodValueParameters,
                    out TypeSignatureShape? type,
                    out reason))
            {
                return false;
            }
            parameters[i] = new(passing, type!);
        }

        reason = null;
        return true;
    }

    static void SkipAttributeLists(SourceToken[] tokens, ref int start, int end)
    {
        while (start < end && tokens[start].Text == "[")
        {
            if (!TryFindMatching(tokens, start, "[", "]", out int close) || close >= end)
                throw new InvalidOperationException("A parameter attribute list is malformed.");
            start = close + 1;
        }
    }

    static void CutDefaultValue(SourceToken[] tokens, int start, ref int end)
    {
        foreach ((int segmentStart, int segmentEnd) in SplitTopLevel(tokens, start, end, "="))
        {
            if (segmentEnd < end)
            {
                end = segmentEnd;
                return;
            }
            break;
        }
    }

    internal static IEnumerable<(int Start, int End)> SplitTopLevel(
        SourceToken[] tokens,
        int start,
        int end,
        string separator)
    {
        int round = 0;
        int square = 0;
        int angle = 0;
        int segmentStart = start;
        for (int i = start; i < end; i++)
        {
            switch (tokens[i].Text)
            {
                case "(": round++; break;
                case ")": round--; break;
                case "[": square++; break;
                case "]": square--; break;
                case "<": angle++; break;
                case ">": angle--; break;
            }

            if (round < 0 || square < 0 || angle < 0)
                throw new InvalidOperationException("The declaration signature has unbalanced delimiters.");

            if (tokens[i].Text == separator && round == 0 && square == 0 && angle == 0)
            {
                yield return (segmentStart, i);
                segmentStart = i + 1;
            }
        }
        if (round != 0 || square != 0 || angle != 0)
            throw new InvalidOperationException("The declaration signature has unbalanced delimiters.");
        yield return (segmentStart, end);
    }

    internal static bool TryFindMatching(
        SourceToken[] tokens,
        int open,
        string opening,
        string closing,
        out int close)
    {
        int depth = 0;
        for (int i = open; i < tokens.Length; i++)
        {
            if (tokens[i].Text == opening)
                depth++;
            else if (tokens[i].Text == closing && --depth == 0)
            {
                close = i;
                return true;
            }
        }
        close = -1;
        return false;
    }

    static int FindWord(SourceToken[] tokens, string text, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (tokens[i].Text == text && tokens[i].Kind == ScanTokenKind.Word)
                return i;
        }
        return -1;
    }

    static int LastIdentifier(SourceToken[] tokens, int start, int end)
    {
        for (int i = end - 1; i >= start; i--)
        {
            if (IsIdentifier(tokens[i]))
                return i;
        }
        return -1;
    }

    internal static bool IsIdentifier(SourceToken token)
        => token.Kind == ScanTokenKind.Word
            && CSharpIdentifier.IsIdentifierLike(token.Text);

    internal static string IdentifierText(SourceToken[] tokens, int index)
        => tokens[index].Text;

    internal readonly record struct SourceToken(
        ScanTokenKind Kind,
        string Text,
        int Depth,
        int BracketDepth);
}

static class SourceTypeShapeParser
{
    static readonly HashSet<string> ReferencePrimitives =
        new(StringComparer.Ordinal) { "System.Object", "System.String" };

    internal static bool TryParse(
        SourceMemberSignatureShape.SourceToken[] tokens,
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<string> methodParameters,
        IReadOnlySet<string> typeValueParameters,
        IReadOnlySet<string> methodValueParameters,
        out TypeSignatureShape? shape,
        out string? reason)
    {
        try
        {
            var parser = new Parser(
                tokens,
                typeParameters,
                methodParameters,
                typeValueParameters,
                methodValueParameters);
            shape = parser.ParseType();
            parser.End();
            reason = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            shape = null;
            reason = ex.Message;
            return false;
        }
    }

    ref struct Parser
    {
        readonly SourceMemberSignatureShape.SourceToken[] _tokens;
        readonly IReadOnlyList<string> _typeParameters;
        readonly IReadOnlyList<string> _methodParameters;
        readonly IReadOnlySet<string> _typeValueParameters;
        readonly IReadOnlySet<string> _methodValueParameters;
        int _position;
        int _depth;

        internal Parser(
            SourceMemberSignatureShape.SourceToken[] tokens,
            IReadOnlyList<string> typeParameters,
            IReadOnlyList<string> methodParameters,
            IReadOnlySet<string> typeValueParameters,
            IReadOnlySet<string> methodValueParameters)
        {
            _tokens = tokens;
            _typeParameters = typeParameters;
            _methodParameters = methodParameters;
            _typeValueParameters = typeValueParameters;
            _methodValueParameters = methodValueParameters;
        }

        internal TypeSignatureShape ParseType()
        {
            if (++_depth > MemberSignatureShapeCodec.MaxDepth)
            {
                _depth--;
                throw new InvalidOperationException(
                    "The source type exceeds the signature-shape depth limit.");
            }

            try
            {
                return ParseTypeCore();
            }
            finally
            {
                _depth--;
            }
        }

        TypeSignatureShape ParseTypeCore()
        {
            TypeSignatureShape type = ParsePrimary();
            while (_position < _tokens.Length)
            {
                if (TakeIf("*"))
                {
                    type = new PointerTypeSignatureShape(type);
                    continue;
                }
                if (TakeIf("?"))
                {
                    type = ApplyNullable(type);
                    continue;
                }
                if (Peek("["))
                {
                    var ranks = new List<int>();
                    while (TakeIf("["))
                    {
                        int commas = 0;
                        while (TakeIf(","))
                            commas++;
                        Take("]");
                        ranks.Add(commas + 1);
                    }
                    for (int i = ranks.Count - 1; i >= 0; i--)
                    {
                        type = new ArrayTypeSignatureShape(
                            type,
                            ranks[i],
                            IsSzArray: ranks[i] == 1);
                    }
                    continue;
                }
                break;
            }
            return type;
        }

        TypeSignatureShape ParsePrimary()
        {
            if (TakeIf("("))
                return ParseTuple();
            if (Peek("delegate"))
                return ParseFunctionPointer();

            (string identifier, bool escaped) = TakeIdentifierWithEscape();
            if (identifier == "dynamic")
                return new PrimitiveTypeSignatureShape("System.Object");
            if (PrimitiveTypeNames.TryToClrFullName(identifier, out string primitive))
                return new PrimitiveTypeSignatureShape(primitive);

            int methodPosition = IndexOf(_methodParameters, identifier);
            if (methodPosition >= 0)
            {
                return new GenericParameterTypeSignatureShape(
                    SignatureGenericParameterKind.Method,
                    methodPosition);
            }
            int typePosition = IndexOf(_typeParameters, identifier);
            if (typePosition >= 0)
            {
                return new GenericParameterTypeSignatureShape(
                    SignatureGenericParameterKind.Type,
                    typePosition);
            }

            if (escaped || identifier != "global" || !TakeIf(":") || !TakeIf(":"))
                throw new InvalidOperationException(
                    $"The source type '{identifier}' is not globally qualified and cannot be resolved safely.");

            var parts = new List<(string Name, SignatureShapeList<TypeSignatureShape> Arguments)>();
            do
            {
                string name = TakeIdentifier();
                SignatureShapeList<TypeSignatureShape> arguments =
                    TakeIf("<") ? ParseTypeArguments() : SignatureShapeList<TypeSignatureShape>.Empty;
                parts.Add((name, arguments));
            }
            while (TakeIf("."));

            // Syntax alone cannot distinguish a namespace from a non-generic containing type.
            // This collapse can miss a nested-type match, but cannot invent one when the
            // correspondence operation receives the complete same-name candidate set.
            int typeStart = parts.FindIndex(part => part.Arguments.Count > 0);
            if (typeStart < 0)
                typeStart = parts.Count - 1;
            string @namespace = string.Join(".", parts.Take(typeStart).Select(part => part.Name));
            var segments = parts
                .Skip(typeStart)
                .Select(part => new NamedTypeSegment(
                    part.Name,
                    part.Arguments.Count,
                    part.Arguments))
                .ToArray();

            string fullName = string.IsNullOrEmpty(@namespace)
                ? string.Join(".", segments.Select(segment => segment.Name))
                : @namespace + "." + string.Join(".", segments.Select(segment => segment.Name));
            if (PrimitiveTypeNames.TryToKeyword(fullName, out _))
                return new PrimitiveTypeSignatureShape(fullName);
            if (fullName == "System.Nullable"
                && segments[^1].TypeArguments.Count == 1)
            {
                return new NullableTypeSignatureShape(segments[^1].TypeArguments[0]);
            }
            if (fullName == "System.ValueTuple"
                && segments[^1].TypeArguments.Count >= 2)
            {
                return new TupleTypeSignatureShape(segments[^1].TypeArguments);
            }

            return new NamedTypeSignatureShape(@namespace, new(segments));
        }

        SignatureShapeList<TypeSignatureShape> ParseTypeArguments()
        {
            var values = new List<TypeSignatureShape>();
            do
            {
                values.Add(ParseType());
            }
            while (TakeIf(","));
            Take(">");
            return new(values);
        }

        TypeSignatureShape ParseTuple()
        {
            var elements = new List<TypeSignatureShape>();
            while (true)
            {
                TypeSignatureShape element = ParseType();
                if (Peek("@")
                    && _position + 1 < _tokens.Length
                    && SourceMemberSignatureShape.IsIdentifier(_tokens[_position + 1]))
                {
                    _position += 2;
                }
                else if (_position < _tokens.Length
                    && SourceMemberSignatureShape.IsIdentifier(_tokens[_position]))
                {
                    if (CSharpKeywords.RequiresDeclarationEscape(_tokens[_position].Text))
                    {
                        throw new InvalidOperationException(
                            "A tuple element name uses an unescaped keyword.");
                    }
                    _position++;
                }
                elements.Add(element);
                if (TakeIf(")"))
                    break;
                Take(",");
            }
            if (elements.Count < 2)
                throw new InvalidOperationException("A tuple type must have at least two elements.");
            return new TupleTypeSignatureShape(new(elements));
        }

        TypeSignatureShape ParseFunctionPointer()
        {
            Take("delegate");
            Take("*");
            string convention = "managed";
            if (TakeIf("managed"))
            {
                convention = "managed";
            }
            else if (TakeIf("unmanaged"))
            {
                convention = "unmanaged";
                if (TakeIf("["))
                {
                    var conventions = new List<string>();
                    do conventions.Add(TakeIdentifier());
                    while (TakeIf(","));
                    Take("]");
                    convention = conventions.Count == 1
                        ? NormalizeCallingConvention(conventions[0])
                        : "unmanaged[" + string.Join(",", conventions) + "]";
                }
            }

            Take("<");
            var values = new List<TypeSignatureShape>();
            while (true)
            {
                bool byReference = TakeIf("ref") || TakeIf("out") || TakeIf("in");
                TypeSignatureShape value = ParseType();
                values.Add(byReference
                    ? new ByReferenceTypeSignatureShape(value)
                    : value);
                if (TakeIf(">"))
                    break;
                Take(",");
            }
            if (values.Count == 0)
                throw new InvalidOperationException("A function pointer has no return type.");
            return new FunctionPointerTypeSignatureShape(
                convention,
                values[^1],
                new(values.Take(values.Count - 1)));
        }

        static string NormalizeCallingConvention(string convention)
            => convention switch
            {
                "Cdecl" => "CDecl",
                "Stdcall" => "StdCall",
                "Thiscall" => "ThisCall",
                "Fastcall" => "FastCall",
                _ => convention,
            };

        TypeSignatureShape ApplyNullable(TypeSignatureShape type)
            => type switch
            {
                PrimitiveTypeSignatureShape primitive when ReferencePrimitives.Contains(primitive.ClrName)
                    => primitive,
                PrimitiveTypeSignatureShape primitive when primitive.ClrName != "System.Void"
                    => new NullableTypeSignatureShape(primitive),
                ArrayTypeSignatureShape => type,
                TupleTypeSignatureShape => new NullableTypeSignatureShape(type),
                GenericParameterTypeSignatureShape parameter
                    when parameter.Kind == SignatureGenericParameterKind.Type
                        && parameter.Position < _typeParameters.Count
                        && _typeValueParameters.Contains(_typeParameters[parameter.Position])
                    => new NullableTypeSignatureShape(type),
                GenericParameterTypeSignatureShape parameter
                    when parameter.Kind == SignatureGenericParameterKind.Method
                        && parameter.Position < _methodParameters.Count
                        && _methodValueParameters.Contains(_methodParameters[parameter.Position])
                    => new NullableTypeSignatureShape(type),
                NullableTypeSignatureShape => throw new InvalidOperationException(
                    "A nullable type cannot be made nullable again."),
                _ => throw new InvalidOperationException(
                    "Nullable syntax on an unresolved or unconstrained type is unavailable."),
            };

        string TakeIdentifier()
        {
            (string identifier, bool escaped) = TakeIdentifierWithEscape();
            if (!escaped && CSharpKeywords.RequiresDeclarationEscape(identifier))
                throw new InvalidOperationException("A source type name uses an unescaped keyword.");
            return identifier;
        }

        (string Identifier, bool Escaped) TakeIdentifierWithEscape()
        {
            bool escaped = TakeIf("@");
            if (_position >= _tokens.Length
                || !SourceMemberSignatureShape.IsIdentifier(_tokens[_position]))
            {
                throw new InvalidOperationException("A source type name is malformed.");
            }

            string value = _tokens[_position++].Text;
            return (value, escaped);
        }

        bool Peek(string value)
            => _position < _tokens.Length && _tokens[_position].Text == value;

        bool TakeIf(string value)
        {
            if (!Peek(value))
                return false;
            _position++;
            return true;
        }

        void Take(string value)
        {
            if (!TakeIf(value))
                throw new InvalidOperationException($"Expected '{value}' in a source type.");
        }

        internal void End()
        {
            if (_position != _tokens.Length)
                throw new InvalidOperationException("The source type has unexpected trailing syntax.");
        }

        static int IndexOf(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }
    }
}

static class LegacyMemberSignatureShape
{
    internal static MemberSignatureShapeResult Decode(string text)
    {
        int open = text.IndexOf('(');
        int close = text.LastIndexOf(')');
        if (open <= 0 || close < open)
            return MemberSignatureShapeResult.Unavailable("The legacy signature text is malformed.");
        if (close + 1 < text.Length && text[close + 1] != ':')
            return MemberSignatureShapeResult.Unavailable("The legacy signature text is malformed.");

        string arityText = text[..open];
        if (!arityText.StartsWith('`')
            || !int.TryParse(
                arityText.AsSpan(1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int arity)
            || arity < 0)
        {
            return MemberSignatureShapeResult.Unavailable("The legacy generic arity is malformed.");
        }

        var budget = new LegacyParseBudget();
        string parameterText = text[(open + 1)..close];
        var parameters = new List<MemberParameterSignatureShape>();
        if (parameterText.Length > 0)
        {
            foreach (string parameter in Split(parameterText))
            {
                if (!budget.TryReserveCollectionElements(1)
                    || !TryParseLegacyType(
                        parameter,
                        ref budget,
                        depth: 1,
                        out TypeSignatureShape? type))
                {
                    return MemberSignatureShapeResult.Unavailable(
                        $"The legacy type '{parameter}' cannot be normalized safely.");
                }
                ParameterPassingKind passing = ParameterPassingKind.Value;
                if (type is ByReferenceTypeSignatureShape byReference)
                {
                    passing = ParameterPassingKind.ByReference;
                    type = byReference.ElementType;
                }
                parameters.Add(new(passing, type!));
            }
        }

        TypeSignatureShape? returnType = null;
        if (close + 1 < text.Length)
        {
            if (!TryParseLegacyType(
                    text[(close + 2)..],
                    ref budget,
                    depth: 1,
                    out returnType))
            {
                return MemberSignatureShapeResult.Unavailable(
                    "The legacy conversion return type cannot be normalized safely.");
            }
        }
        return MemberSignatureShapeResult.Available(
            new MemberSignatureShape(arity, new(parameters), returnType));
    }

    static IEnumerable<string> Split(string text)
    {
        int angle = 0;
        int square = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<': angle++; break;
                case '>': angle--; break;
                case '[': square++; break;
                case ']': square--; break;
                case ',' when angle == 0 && square == 0:
                    yield return text[start..i];
                    start = i + 1;
                    break;
            }
        }
        yield return text[start..];
    }

    static bool TryParseLegacyType(
        string text,
        ref LegacyParseBudget budget,
        int depth,
        out TypeSignatureShape? type)
    {
        text = text.Trim();
        bool byReference = text.StartsWith("ref ", StringComparison.Ordinal);
        if (byReference)
            text = text[4..];

        var suffixes = new Stack<(char Kind, int Rank, bool Sz)>();
        int coreEnd = text.Length;
        while (coreEnd > 0)
        {
            if (suffixes.Count >= MemberSignatureShapeCodec.MaxDepth)
            {
                type = null;
                return false;
            }
            if (text[coreEnd - 1] == '?')
            {
                suffixes.Push(('?', 0, false));
                coreEnd--;
                continue;
            }
            if (text[coreEnd - 1] == '*')
            {
                suffixes.Push(('*', 0, false));
                coreEnd--;
                continue;
            }
            if (text[coreEnd - 1] == ']')
            {
                int open = text.LastIndexOf('[', coreEnd - 1, coreEnd);
                if (open < 0)
                    break;
                int commaCount = coreEnd - open - 2;
                bool validRank = true;
                for (int i = open + 1; i < coreEnd - 1; i++)
                {
                    if (text[i] != ',')
                    {
                        validRank = false;
                        break;
                    }
                }
                if (!validRank)
                    break;
                suffixes.Push(('a', commaCount + 1, commaCount == 0));
                coreEnd = open;
                continue;
            }
            break;
        }
        text = text[..coreEnd];

        int wrapperCount = suffixes.Count + (byReference ? 1 : 0);
        if (depth > MemberSignatureShapeCodec.MaxDepth - wrapperCount
            || !budget.TryReserveNodes(1 + wrapperCount))
        {
            type = null;
            return false;
        }

        if (PrimitiveTypeNames.TryToClrFullName(text, out string primitive))
        {
            type = new PrimitiveTypeSignatureShape(primitive);
        }
        else if (text.StartsWith("``", StringComparison.Ordinal))
        {
            if (!int.TryParse(
                    text.AsSpan(2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int methodPosition)
                || methodPosition < 0)
            {
                type = null;
                return false;
            }
            type = new GenericParameterTypeSignatureShape(
                SignatureGenericParameterKind.Method,
                methodPosition);
        }
        else if (text.StartsWith('`'))
        {
            if (!int.TryParse(
                    text.AsSpan(1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int typePosition)
                || typePosition < 0)
            {
                type = null;
                return false;
            }
            type = new GenericParameterTypeSignatureShape(
                SignatureGenericParameterKind.Type,
                typePosition);
        }
        else
        {
            int genericOpen = FindGenericOpen(text);
            if (genericOpen >= 0)
            {
                if (!text.EndsWith('>'))
                {
                    type = null;
                    return false;
                }

                string name = text[..genericOpen];
                if (string.IsNullOrEmpty(name))
                {
                    type = null;
                    return false;
                }
                var arguments = new List<TypeSignatureShape>();
                foreach (string argumentText in Split(text[(genericOpen + 1)..^1]))
                {
                    if (!budget.TryReserveCollectionElements(1)
                        || !TryParseLegacyType(
                            argumentText,
                            ref budget,
                            depth + wrapperCount + 1,
                            out TypeSignatureShape? argument))
                    {
                        type = null;
                        return false;
                    }
                    arguments.Add(argument!);
                }

                if (name == "ValueTuple" && arguments.Count >= 2)
                {
                    type = new TupleTypeSignatureShape(new(arguments));
                }
                else if (name == "delegate*" && arguments.Count >= 1)
                {
                    type = new FunctionPointerTypeSignatureShape(
                        "managed",
                        arguments[^1],
                        new(arguments.Take(arguments.Count - 1)));
                }
                else
                {
                    type = new UnresolvedNamedTypeSignatureShape(name, new(arguments));
                }
            }
            else if (text.Length > 0)
            {
                type = new UnresolvedNamedTypeSignatureShape(
                    text,
                    SignatureShapeList<TypeSignatureShape>.Empty);
            }
            else
            {
                type = null;
                return false;
            }
        }

        while (suffixes.TryPop(out var suffix))
        {
            type = suffix.Kind switch
            {
                '*' => new PointerTypeSignatureShape(type),
                '?' => type is PrimitiveTypeSignatureShape primitiveShape
                    && primitiveShape.ClrName is "System.Object" or "System.String"
                        ? type
                        : new NullableTypeSignatureShape(type),
                _ => new ArrayTypeSignatureShape(type, suffix.Rank, suffix.Sz),
            };
        }
        if (byReference)
            type = new ByReferenceTypeSignatureShape(type);
        return true;
    }

    struct LegacyParseBudget
    {
        int _nodes;
        int _collectionElements;

        internal bool TryReserveNodes(int count)
        {
            if (count < 0 || count > MemberSignatureShapeCodec.MaxNodes - _nodes)
                return false;
            _nodes += count;
            return true;
        }

        internal bool TryReserveCollectionElements(int count)
        {
            if (count < 0
                || count > MemberSignatureShapeCodec.MaxCollectionElements - _collectionElements)
            {
                return false;
            }
            _collectionElements += count;
            return true;
        }
    }

    static int FindGenericOpen(string text)
    {
        int square = 0;
        for (int i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[': square++; break;
                case ']': square--; break;
                case '<' when square == 0: return i;
            }
        }
        return -1;
    }
}
