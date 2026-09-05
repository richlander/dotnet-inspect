using System.Collections.Immutable;
using System.Text;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

public enum CSharpTypeNamePolicy
{
    /// <summary>Keep referenced type names qualified and derive no namespace imports.</summary>
    Qualified,

    /// <summary>
    /// Derive namespace imports for the output unit and shorten references against
    /// that shared set.
    /// </summary>
    ShortWithUsings,

    /// <summary>
    /// Shorten references only against the declaring namespace and caller-supplied
    /// namespace context.
    /// </summary>
    ContextualShort
}

public enum CSharpNamespacePolicy
{
    Omit,
    FileScoped
}

public sealed record CSharpFormatOptions
{
    public CSharpTypeNamePolicy TypeNamePolicy { get; init; } = CSharpTypeNamePolicy.Qualified;
    public string? ContainingNamespace { get; init; }
    public IReadOnlyCollection<string> Usings { get; init; } = [];
    internal IReadOnlyCollection<string> AdditionalShadowingNames { get; init; } = [];
    internal IReadOnlyCollection<string> AdditionalRootShadowingNames { get; init; } = [];
    internal IReadOnlyCollection<string> AdditionalUnresolvableRootNames { get; init; } = [];
    internal IReadOnlyCollection<string> AdditionalDeclaredTypeFullNames { get; init; } = [];
    internal IReadOnlyCollection<string> AdditionalImportedDeclaredTypeFullNames { get; init; } = [];
    internal IReadOnlyCollection<string> AdditionalKnownNamespaces { get; init; } = [];
    internal CSharpDeclaredTypeSelfNameAdmission.Admitted? DeclaredTypeSelfName { get; init; }
    internal string? LegacyDeclaredTypeIdentifier { get; init; }
    public CSharpNamespacePolicy NamespacePolicy { get; init; } = CSharpNamespacePolicy.Omit;
    public bool AbbreviateSignature { get; init; }
    public bool TerminateMemberDeclaration { get; init; }
    public bool ForceAsync { get; init; }
    public bool ForceUnsafe { get; init; }
    public bool IncludeCustomAttributes { get; init; } = false;
    public bool IncludeSignatureAttributes { get; init; } = true;
    public bool IncludeObsoleteAttribute { get; init; } = true;
    public bool OmitInterfaceMemberModifiers { get; init; }
    public bool OmitPropertyAccessors { get; init; }
}

/// <summary>
/// A rendered declaration and the namespace imports its shortened type names
/// require. <paramref name="Usings"/> contains raw namespace identities, not
/// rendered <c>using ...;</c> lines.
/// </summary>
public sealed record CSharpFormattedDeclaration(
    string Text,
    ImmutableSortedSet<string> Usings,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Formats Metadata declaration shapes as C# without selecting or grouping APIs.
/// </summary>
public sealed class CSharpFormatter
{
    public static string FormatDeclarationLeafMetadataName(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.DefinitionName is { } exactName)
        {
            string rawLeaf = exactName.Segments[^1];
            IReadOnlyList<int>? introducedCounts =
                type.IntroducedTypeParameterCounts;
            bool hasDeclaredArity = MetadataNameArity.TryReadSuffix(
                rawLeaf,
                out int arity,
                out _);
            bool arityMatches =
                hasDeclaredArity
                && introducedCounts is { Count: > 0 }
                && introducedCounts.Count == exactName.Segments.Length
                && introducedCounts[^1] == arity;
            if (string.Equals(
                    type.MetadataName,
                    rawLeaf,
                    StringComparison.Ordinal)
                && !string.Equals(
                    type.Name,
                    rawLeaf,
                    StringComparison.Ordinal)
                && (!hasDeclaredArity || arityMatches))
            {
                return type.Name;
            }

            string leaf = rawLeaf
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace(".", "\\.", StringComparison.Ordinal)
                .Replace("+", "\\+", StringComparison.Ordinal);
            if (arityMatches)
            {
                return MetadataNameArity.StripFromSegment(leaf);
            }
            return leaf;
        }

        string name = type.Name;
        int separator = name.LastIndexOfAny(['.', '+']);
        if (separator >= 0)
            name = name[(separator + 1)..];
        int angle = name.IndexOf('<');
        if (angle >= 0)
            name = name[..angle];
        return MetadataNameArity.StripFromSegment(name);
    }

    readonly CSharpDeclarationOptions _declarationOptions;

    public CSharpFormatter(CSharpFormatOptions? options = null)
    {
        options ??= new CSharpFormatOptions();
        if (!Enum.IsDefined(options.TypeNamePolicy))
            throw new ArgumentOutOfRangeException(nameof(options), options.TypeNamePolicy, "C# type-name policy must be defined.");
        if (!Enum.IsDefined(options.NamespacePolicy))
            throw new ArgumentOutOfRangeException(nameof(options), options.NamespacePolicy, "C# namespace policy must be defined.");
        var usings = options.Usings?.ToArray()
            ?? throw new ArgumentException("C# formatter usings cannot be null.", nameof(options));
        _declarationOptions = ToDeclarationOptions(options, usings);
    }

    public string FormatMember(
        ApiType type,
        ApiMember member,
        IReadOnlyList<string>? methodParameters = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        return CSharpDeclarationWriter.RenderMemberDeclaration(
            type,
            member,
            _declarationOptions,
            methodParameters);
    }

    /// <summary>
    /// Formats one accessor declaration head, including its return attributes
    /// and accessor-specific accessibility.
    /// </summary>
    public string FormatAccessorHead(
        ApiType type,
        ApiMember member,
        string kind)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var accessor = member.SignatureModel?.Accessors
            .FirstOrDefault(candidate => candidate.Kind == kind);
        var parts = new List<string>();
        if (accessor?.ReturnAttributes is { Count: > 0 } returnAttributes)
        {
            var attributeProbe = new ApiMember
            {
                Name = "__AccessorAttributeProbe",
                Kind = "method",
                SignatureModel = new ApiSignature
                {
                    ReturnType = "void",
                    MemberName = "__AccessorAttributeProbe",
                    ReturnAttributes = returnAttributes
                }
            };
            string formattedProbe = FormatMember(type, attributeProbe);
            attributeProbe.SignatureModel.ReturnAttributes = [];
            string formattedDeclaration = FormatMember(type, attributeProbe);
            if (!formattedProbe.EndsWith(formattedDeclaration, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"C# accessor '{member.Name}.{kind}' return attributes were not rendered.");
            }
            string attributePrefix = formattedProbe[..^formattedDeclaration.Length].TrimEnd();
            if (attributePrefix.Length > 0)
                parts.Add(attributePrefix);
        }
        if (!string.IsNullOrWhiteSpace(accessor?.Accessibility))
            parts.Add(accessor.Accessibility!);
        parts.Add(kind);
        return string.Join(" ", parts);
    }

    public string FormatMemberWithBody(
        ApiType type,
        ApiMember member,
        CSharpMemberBody body,
        IReadOnlyList<string>? methodParameters = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(body);
        IReadOnlyList<string>? parameterNames =
            (body as CSharpBlockBody)?.ParameterNames;
        if (parameterNames is { Count: 0 })
            parameterNames = null;
        return CSharpDeclarationWriter.RenderMemberDeclaration(
            type,
            member,
            _declarationOptions with
            {
                ForceAsync = _declarationOptions.ForceAsync || body.RequiresAsyncModifier,
                ForceUnsafe = _declarationOptions.ForceUnsafe || body.RequiresUnsafeModifier,
                SuppressFinalizerSpelling = body.SuppressDestructorSyntax
            },
            methodParameters,
            parameterNames);
    }

    public CSharpFormattedDeclaration FormatMemberUnit(
        ApiType type,
        ApiMember member,
        IReadOnlyList<string>? methodParameters = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        return ToFormattedDeclaration(CSharpDeclarationWriter.RenderMemberUnit(
            type,
            member,
            _declarationOptions,
            methodParameters));
    }

    public string FormatTypeDeclaration(
        ApiType type,
        IReadOnlyList<ApiParameter>? primaryConstructorParameters = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        return CSharpDeclarationWriter.RenderTypeDeclaration(
            type,
            _declarationOptions,
            primaryConstructorParameters);
    }

    public string FormatDelegate(ApiType type, ApiMember invoke)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(invoke);
        if (type.Kind != "delegate")
            throw new ArgumentException($"Type '{type.FullName}' is not a delegate.", nameof(type));
        if (invoke.SignatureModel is null)
        {
            throw new NotSupportedException(
                $"Delegate '{type.FullName}' requires a structured Invoke signature.");
        }

        return CSharpDeclarationWriter.RenderTypeDeclaration(
            type,
            _declarationOptions,
            delegateInvoke: invoke);
    }

    public CSharpFormattedDeclaration FormatTypeUnit(
        ApiType type,
        IEnumerable<ApiMember>? members = null)
        => FormatTypeUnit(type, members, primaryConstructorParameters: null);

    internal CSharpFormattedDeclaration FormatTypeUnit(
        ApiType type,
        IEnumerable<ApiMember>? members,
        IReadOnlyList<ApiParameter>? primaryConstructorParameters)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ToFormattedDeclaration(CSharpDeclarationWriter.RenderTypeUnit(
            type,
            members,
            _declarationOptions,
            primaryConstructorParameters));
    }

    public static string EscapeIdentifier(string identifier)
        => CSharpDeclarationWriter.EscapeIdentifier(identifier);

    public static string EscapeNamespace(string @namespace)
        => CSharpDeclarationWriter.EscapeNamespace(@namespace);

    /// <summary>
    /// Computes a collision-safe set of namespaces importable as <c>using</c>
    /// directives for a compilation unit declaring <paramref name="types"/>
    /// (flatten nested types in). See
    /// <see cref="CSharpDeclarationWriter.DeriveContextualUsings"/> for the
    /// safety contract.
    /// </summary>
    public static IReadOnlyList<string> DeriveContextualUsings(IReadOnlyCollection<ApiType> types)
        => CSharpDeclarationWriter.DeriveContextualUsings(types);

    /// <summary>
    /// Escapes C# reserved keywords used as identifiers within a type string while
    /// leaving type-syntax keywords (primitive aliases, parameter/type modifiers,
    /// function-pointer syntax) bare. This is the authoritative type-keyword escaper;
    /// consumers must not reimplement it.
    /// </summary>
    public static string EscapeTypeKeywords(string type)
        => CSharpDeclarationWriter.EscapeTypeKeywords(type);

    /// <summary>
    /// Rewrites CLR primitive full names (e.g. <c>System.Int32</c>, <c>System.IntPtr</c>)
    /// to their C# keyword spelling (<c>int</c>, <c>nint</c>) wherever they appear as a
    /// complete type-name segment inside a type string — including nested in generics,
    /// arrays, pointers, and by-ref forms. This is the authoritative primitive-alias
    /// rewriter for the C# layer; consumers must not reimplement it.
    /// </summary>
    public static string AliasPrimitiveTypeNames(string type)
        => CSharpDeclarationWriter.AliasPrimitiveTypeNames(type);

    /// <summary>
    /// Strips the CLR generic-arity suffix (e.g. <c>List`1</c> becomes <c>List</c>)
    /// from each segment of an <see cref="ApiType.Name"/>-shaped type-name chain,
    /// whose nesting boundary is spelled <c>.</c> (<c>Outer`1.Inner`2</c> becomes
    /// <c>Outer.Inner</c>). Pass the type-name chain only: a namespace is not a
    /// type-name segment and carries no arity, so callers keep it aside (as
    /// <see cref="ApiType.Namespace"/> already does) and re-attach it afterwards.
    /// A segment whose backtick does not introduce a canonical decimal arity
    /// (<c>Widget`Literal</c>) is preserved unchanged rather than truncated, so
    /// distinct metadata names do not collapse into one spelling candidate;
    /// <see cref="MetadataNameArity"/> owns that rule and names its gate.
    /// </summary>
    public static string StripArity(string name)
        => MetadataNameArity.StripFromDottedChain(name);

    /// <summary>
    /// Normalizes a raw metadata/IL type display string into C# source spelling:
    /// collapses signatures containing unspeakable generic-parameter markers
    /// (<c>!</c>) to <c>object</c>, sanitizes compiler-generated <c>&lt;...&gt;</c>
    /// name segments, strips <c>modreq</c>/<c>modopt</c> wrappers, aliases CLR
    /// primitives to C# keywords, and escapes reserved type keywords. This is the
    /// authoritative type-display cleaner for the C# layer; consumers must not
    /// reimplement it.
    /// </summary>
    public static string CleanTypeDisplay(string type)
    {
        if (type.Contains('!'))
            return "object";

        type = SanitizeGeneratedTypeSegments(type);

        type = StripCustomModifiers(type).Trim();

        type = AliasPrimitiveTypeNames(type);

        return EscapeTypeKeywords(type);
    }

    /// <summary>
    /// Removes <c>modreq(…)</c>/<c>modopt(…)</c> custom-modifier wrappers, keeping
    /// their inner type text. Only the parentheses that belong to a wrapper are
    /// dropped: paren depth is tracked so unrelated parentheses (for example
    /// tuple syntax such as <c>(int, string)</c>) are preserved verbatim.
    /// </summary>
    static string StripCustomModifiers(string type)
    {
        if (type.IndexOf("modreq(", StringComparison.Ordinal) < 0
            && type.IndexOf("modopt(", StringComparison.Ordinal) < 0)
            return type;

        const int tokenLength = 7; // "modreq(" and "modopt(" are both 7 chars.
        var sb = new StringBuilder(type.Length);
        var modifierCloseDepths = new Stack<int>();
        int depth = 0;
        int i = 0;
        while (i < type.Length)
        {
            if (MatchesAt(type, i, "modreq(") || MatchesAt(type, i, "modopt("))
            {
                modifierCloseDepths.Push(depth);
                depth++;
                i += tokenLength;
                continue;
            }

            char c = type[i];
            if (c == '(')
            {
                depth++;
                sb.Append(c);
            }
            else if (c == ')')
            {
                depth--;
                if (modifierCloseDepths.Count > 0 && modifierCloseDepths.Peek() == depth)
                    modifierCloseDepths.Pop();
                else
                    sb.Append(c);
            }
            else
            {
                sb.Append(c);
            }

            i++;
        }

        return sb.ToString();
    }

    static bool MatchesAt(string value, int index, string token)
        => index + token.Length <= value.Length
            && string.CompareOrdinal(value, index, token, 0, token.Length) == 0;

    static string SanitizeGeneratedTypeSegments(string type)
    {
        var sb = new StringBuilder(type.Length);
        int i = 0;
        while (i < type.Length)
        {
            if (type[i] == '<' && IsGeneratedSegmentStart(type, i))
            {
                int close = i + 1;
                while (close < type.Length && IsGeneratedTypeSegmentChar(type[close]))
                    close++;
                if (close < type.Length && type[close] == '>')
                {
                    int end = close + 1;
                    while (end < type.Length && IsGeneratedTypeSuffixChar(type[end]))
                        end++;
                    sb.Append(CSharpIdentifier.Sanitize(type[i..end]));
                    i = end;
                    continue;
                }
            }

            sb.Append(type[i]);
            i++;
        }

        return sb.ToString();
    }

    static bool IsGeneratedTypeSegmentChar(char c)
        => c != '>'
            && c != '.'
            && c != ','
            && c != '['
            && c != ']'
            && c != '*'
            && !char.IsWhiteSpace(c);

    static bool IsGeneratedTypeSuffixChar(char c)
        => char.IsLetterOrDigit(c) || c is '_' or '{' or '}';

    static bool IsGeneratedSegmentStart(string text, int index)
        => index == 0 || text[index - 1] is '.' or '+' or '<';

    /// <summary>
    /// True when a C# code fragment (typically a member body) requires the
    /// <c>unsafe</c> modifier — it contains pointer syntax (including function
    /// pointers, which carry a <c>*</c>) or a <c>stackalloc</c> expression. This
    /// over-approximates (a body using <c>*</c> as multiplication also reports
    /// true); an unnecessary <c>unsafe</c> is normally harmless, but callers that
    /// may also emit <c>async</c> must not feed it type names — see
    /// <see cref="TypeRequiresUnsafeModifier"/>. This is the authoritative
    /// unsafe-requirement predicate for C# fragments; consumers must not
    /// reimplement it.
    /// </summary>
    public static bool RequiresUnsafeModifier(string csharp)
    {
        ArgumentNullException.ThrowIfNull(csharp);
        return csharp.Contains('*', StringComparison.Ordinal)
            || csharp.Contains("stackalloc", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when a C# <em>type</em> display name requires the <c>unsafe</c>
    /// modifier — it denotes a pointer or function-pointer type (both carry a
    /// <c>*</c>). A bracket-delimited <c>[*]</c> instead denotes a rank-one
    /// non-SZ array and does not require <c>unsafe</c>. Unlike
    /// <see cref="RequiresUnsafeModifier"/> this deliberately does not look for
    /// <c>stackalloc</c>, which is an expression construct that can never appear
    /// in a type name (matching it against a type name — e.g. an escaped
    /// identifier <c>@stackalloc</c> — would be a false positive, and a spurious
    /// <c>unsafe</c> on an <c>async</c> member fails to compile). This is the
    /// authoritative unsafe-requirement predicate for C# type names; consumers
    /// must not reimplement it.
    /// </summary>
    public static bool TypeRequiresUnsafeModifier(string typeDisplayName)
    {
        ArgumentNullException.ThrowIfNull(typeDisplayName);
        for (int i = 0; i < typeDisplayName.Length; i++)
        {
            if (typeDisplayName[i] == '*'
                && (i == 0
                    || i == typeDisplayName.Length - 1
                    || typeDisplayName[i - 1] != '['
                    || typeDisplayName[i + 1] != ']'))
            {
                return true;
            }
        }

        return false;
    }

    public static string EscapeKnownIdentifiers(string text, IEnumerable<string> rawNames)
        => CSharpDeclarationWriter.EscapeKnownIdentifiers(text, rawNames);

    /// <summary>
    /// Renders the constraint list that follows <c>where X : </c> for one type
    /// parameter, escaping reserved-keyword type names and using the metadata-carried
    /// constraint kind (<see cref="TypeParameter.StructuredConstraints"/>) to tell a
    /// keyword constraint apart from a type literally named like one.
    /// </summary>
    public static string FormatTypeParameterConstraints(TypeParameter typeParameter, IEnumerable<string> parameterNames)
        => CSharpDeclarationWriter.FormatConstraintList(typeParameter, parameterNames);

    public static string FormatParameter(ApiParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return CSharpDeclarationWriter.EscapeQualifiedKeywordSegments(
            CSharpDeclarationWriter.FormatParameter(parameter));
    }

    public static string FormatParameterList(IEnumerable<ApiParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var values = parameters.ToArray();
        if (values.Any(parameter => parameter is null))
            throw new ArgumentException("C# parameter lists cannot contain null entries.", nameof(parameters));
        return $"({string.Join(", ", values.Select(FormatParameter))})";
    }

    public static string FormatTypeName(ApiType type, bool includeVariance = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.DefinitionName is { } exactName)
        {
            bool leafOnly =
                exactName.Segments.Length > 1
                && (string.Equals(
                        type.Name,
                        exactName.Segments[^1],
                        StringComparison.Ordinal)
                    || string.Equals(
                        type.MetadataName,
                        exactName.Segments[^1],
                        StringComparison.Ordinal));
            return FormatExactTypeName(
                exactName,
                type.TypeParameters,
                type.IntroducedTypeParameterCounts,
                includeVariance,
                leafOnly,
                string.Equals(
                    type.MetadataName,
                    exactName.Segments[^1],
                    StringComparison.Ordinal)
                    && !string.Equals(
                        type.Name,
                        exactName.Segments[^1],
                        StringComparison.Ordinal)
                        ? type.Name
                        : null);
        }

        bool ambiguousLegacyName = HasArityBeforeFlatBoundary(type.Name);
        string typeName = ambiguousLegacyName
                ? type.Name
                : MetadataNameArity.StripFromDottedChain(type.Name);
        string name = CSharpIdentifier.ContainIdentifierForDeclaration(
            typeName);
        return type.TypeParameters.Count == 0 || ambiguousLegacyName
            ? name
            : $"{name}<{string.Join(", ", type.TypeParameters.Select(parameter => FormatTypeParameter(parameter, includeVariance)))}>";
    }

    static string FormatExactTypeName(
        MetadataTypeDefinitionName name,
        IReadOnlyList<TypeParameter> typeParameters,
        IReadOnlyList<int>? introducedTypeParameterCounts,
        bool includeVariance,
        bool leafOnly,
        string? leafNameOverride)
    {
        long declaredArity = 0;
        foreach (string segment in name.Segments)
            declaredArity += MetadataNameArity.OfSegment(segment);

        bool hasParameterOwnership =
            introducedTypeParameterCounts is not null
            && introducedTypeParameterCounts.Count
                == name.Segments.Length
            && introducedTypeParameterCounts.All(
                static count => count >= 0);
        bool ownershipMatchesArity =
            hasParameterOwnership
            && name.Segments
                .Select((segment, index) =>
                {
                    int declared =
                        MetadataNameArity.OfSegment(segment);
                    int introduced =
                        introducedTypeParameterCounts![index];
                    return declared == 0
                        || declared == introduced;
                })
                .All(static matches => matches);
        long expectedParameterCount = leafOnly
            ? introducedTypeParameterCounts is { Count: > 0 }
                ? introducedTypeParameterCounts[^1]
                : -1
            : hasParameterOwnership
                ? introducedTypeParameterCounts!.Sum(
                    static count => (long)count)
                : declaredArity;
        bool arityMatches =
            hasParameterOwnership
                ? ownershipMatchesArity
                    && expectedParameterCount == typeParameters.Count
                : !leafOnly
                    && declaredArity == typeParameters.Count;
        bool unboundOuterDisplay =
            typeParameters.Count == 0
            && !hasParameterOwnership;
        int parameterIndex = 0;
        var parts = new List<string>(name.Segments.Length);
        for (int segmentIndex = leafOnly
                ? name.Segments.Length - 1
                : 0;
            segmentIndex < name.Segments.Length;
            segmentIndex++)
        {
            string segment = name.Segments[segmentIndex];
            int arity = hasParameterOwnership
                ? introducedTypeParameterCounts![segmentIndex]
                : MetadataNameArity.OfSegment(segment);
            bool stripArity =
                arityMatches
                || (unboundOuterDisplay
                    && segmentIndex < name.Segments.Length - 1);
            string simpleName = stripArity
                ? MetadataNameArity.StripFromSegment(segment)
                : segment;
            simpleName =
                segmentIndex == name.Segments.Length - 1
                && arityMatches
                && leafNameOverride is not null
                    ? CSharpIdentifier.ContainIdentifierForDeclaration(
                        leafNameOverride)
                    : ContainExactSegment(simpleName);
            if (arityMatches && arity > 0)
            {
                var parameters = typeParameters
                    .Skip(parameterIndex)
                    .Take(arity)
                    .Select(parameter =>
                        FormatTypeParameter(
                            parameter,
                            includeVariance));
                simpleName += $"<{string.Join(", ", parameters)}>";
                parameterIndex += arity;
            }
            parts.Add(simpleName);
        }
        return leafOnly
            ? parts[^1]
            : string.Join(".", parts);
    }

    static string ContainExactSegment(string segment)
        => CSharpIdentifier.ContainIdentifierForDeclaration(
            segment
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace(".", "\\.", StringComparison.Ordinal)
                .Replace("+", "\\+", StringComparison.Ordinal));

    static bool HasArityBeforeFlatBoundary(string name)
    {
        foreach (MetadataNameComponent component in MetadataNameArity.EnumerateComponents(name))
        {
            if (component.Arity > 0 && component.Delimiter is not null)
                return true;
        }

        return false;
    }

    public static string NormalizeGeneratedMetadataTypeName(string metadataName)
    {
        ArgumentNullException.ThrowIfNull(metadataName);
        if (!IsGeneratedMetadataName(metadataName))
            return metadataName;

        string sourceName = MetadataNameArity.StripFromSegment(metadataName);
        var builder = new System.Text.StringBuilder(sourceName.Length + 1);
        if (sourceName.Length == 0 || !(char.IsLetter(sourceName[0]) || sourceName[0] == '_'))
            builder.Append('_');
        foreach (char character in sourceName)
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        return builder.ToString();
    }

    public static bool IsGeneratedMetadataName(string name)
        => name.StartsWith('<') && name.Contains('>', StringComparison.Ordinal);

    public static string FormatConstructorInitializer(CSharpConstructorInitializer initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        string target = initializer.Kind switch
        {
            CSharpConstructorInitializerKind.This => "this",
            CSharpConstructorInitializerKind.Base => "base",
            _ => throw new ArgumentOutOfRangeException(
                nameof(initializer),
                initializer.Kind,
                "Unknown constructor initializer kind.")
        };
        return $": {target}({string.Join(", ", initializer.Arguments)})";
    }

    /// <summary>
    /// Parses a printer-form constructor-chain string — <c>this(args)</c> or
    /// <c>base(args)</c>, with an optional leading <c>: </c> and no trailing
    /// <c>;</c> — back into a <see cref="CSharpConstructorInitializer"/>. The inverse
    /// of <see cref="FormatConstructorInitializer"/>. The whole argument list is
    /// carried as a single initializer argument: the printer already rendered it, so
    /// there is no top-level comma split (splitting would break nested calls). Returns
    /// <see langword="null"/> when there is no <c>this</c>/<c>base</c> chain. This is
    /// the authoritative constructor-initializer parser for the C# layer; consumers
    /// must not reimplement it.
    /// </summary>
    public static CSharpConstructorInitializer? ParseConstructorInitializer(string? chain)
    {
        if (string.IsNullOrEmpty(chain))
            return null;

        string text = chain.StartsWith(": ", StringComparison.Ordinal) ? chain[2..] : chain;
        CSharpConstructorInitializerKind? kind = text.StartsWith("this(", StringComparison.Ordinal)
            ? CSharpConstructorInitializerKind.This
            : text.StartsWith("base(", StringComparison.Ordinal)
                ? CSharpConstructorInitializerKind.Base
                : null;
        if (kind is null)
            return null;

        int open = text.IndexOf('(', StringComparison.Ordinal);
        int close = text.LastIndexOf(')');
        if (open < 0 || close <= open)
            return null;

        string arguments = text[(open + 1)..close];
        return new CSharpConstructorInitializer(
            kind.Value,
            arguments.Length == 0 ? [] : [arguments]);
    }

    static CSharpDeclarationOptions ToDeclarationOptions(
        CSharpFormatOptions options,
        IReadOnlyCollection<string> usings)
        => new()
        {
            TypeNameMode = options.TypeNamePolicy switch
            {
                CSharpTypeNamePolicy.Qualified => CSharpTypeNameMode.Qualified,
                CSharpTypeNamePolicy.ShortWithUsings => CSharpTypeNameMode.ShortWithUsings,
                CSharpTypeNamePolicy.ContextualShort => CSharpTypeNameMode.ContextualShort,
                _ => throw new InvalidOperationException()
            },
            ContainingNamespace = options.ContainingNamespace,
            Usings = usings,
            AdditionalShadowingNames = options.AdditionalShadowingNames,
            AdditionalRootShadowingNames = options.AdditionalRootShadowingNames,
            AdditionalUnresolvableRootNames = options.AdditionalUnresolvableRootNames,
            AdditionalDeclaredTypeFullNames = options.AdditionalDeclaredTypeFullNames,
            AdditionalImportedDeclaredTypeFullNames = options.AdditionalImportedDeclaredTypeFullNames,
            AdditionalKnownNamespaces = options.AdditionalKnownNamespaces,
            DeclaredTypeSelfName = options.DeclaredTypeSelfName,
            LegacyDeclaredTypeIdentifier = options.LegacyDeclaredTypeIdentifier,
            NamespaceMode = options.NamespacePolicy switch
            {
                CSharpNamespacePolicy.Omit => CSharpNamespaceMode.Omit,
                CSharpNamespacePolicy.FileScoped => CSharpNamespaceMode.FileScoped,
                _ => throw new InvalidOperationException()
            },
            AbbreviateSignature = options.AbbreviateSignature,
            TerminateMemberDeclaration = options.TerminateMemberDeclaration,
            ForceAsync = options.ForceAsync,
            ForceUnsafe = options.ForceUnsafe,
            IncludeCustomAttributes = options.IncludeCustomAttributes,
            IncludeSignatureAttributes = options.IncludeSignatureAttributes,
            IncludeObsoleteAttribute = options.IncludeObsoleteAttribute,
            OmitInterfaceMemberModifiers = options.OmitInterfaceMemberModifiers,
            OmitPropertyAccessors = options.OmitPropertyAccessors
        };

    static CSharpFormattedDeclaration ToFormattedDeclaration(CSharpRenderedDeclaration declaration)
        => new(declaration.Source, declaration.Usings, declaration.Diagnostics.ToArray());

    static string FormatTypeParameter(TypeParameter parameter, bool includeVariance)
        => includeVariance && parameter.Variance is { } variance
            ? $"{variance} {CSharpIdentifier.ContainIdentifierForDeclaration(parameter.Name)}"
            : CSharpIdentifier.ContainIdentifierForDeclaration(parameter.Name);

}
