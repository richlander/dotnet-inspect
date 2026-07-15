using ILInspector.Metadata;

namespace ILInspector.CSharp;

public enum CSharpTypeNamePolicy
{
    Qualified,
    ShortWithUsings,
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
    public CSharpNamespacePolicy NamespacePolicy { get; init; } = CSharpNamespacePolicy.Omit;
    public bool AbbreviateSignature { get; init; }
    public bool TerminateMemberDeclaration { get; init; }
    public bool ForceAsync { get; init; }
    public bool ForceUnsafe { get; init; }
    public bool IncludeCustomAttributes { get; init; } = true;
    public bool IncludeObsoleteAttribute { get; init; } = true;
    public bool OmitInterfaceMemberModifiers { get; init; }
    public bool OmitPropertyAccessors { get; init; }
}

public sealed record CSharpFormattedDeclaration(
    string Text,
    IReadOnlyList<string> Usings,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Formats Metadata declaration shapes as C# without selecting or grouping APIs.
/// </summary>
public sealed class CSharpFormatter
{
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

    public string FormatMemberWithBody(
        ApiType type,
        ApiMember member,
        CSharpMemberBody body,
        IReadOnlyList<string>? methodParameters = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(body);
        return CSharpDeclarationWriter.RenderMemberDeclaration(
            type,
            member,
            _declarationOptions with
            {
                ForceAsync = _declarationOptions.ForceAsync || body.RequiresAsyncModifier,
                ForceUnsafe = _declarationOptions.ForceUnsafe || body.RequiresUnsafeModifier
            },
            methodParameters);
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
        string declaration = CSharpDeclarationWriter.RenderTypeDeclaration(type, _declarationOptions);
        if (primaryConstructorParameters is not { Count: > 0 })
            return declaration;

        string declarationWithoutAttributes = CSharpDeclarationWriter.RenderTypeDeclaration(
            type,
            _declarationOptions with { IncludeCustomAttributes = false });
        if (!declaration.EndsWith(declarationWithoutAttributes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"C# type declaration for '{type.FullName}' has an unexpected attribute prefix.");
        }

        string attributePrefix = declaration[..^declarationWithoutAttributes.Length];
        return attributePrefix
            + AddPrimaryConstructorParameters(
                declarationWithoutAttributes,
                primaryConstructorParameters);
    }

    public string FormatDelegate(ApiType type, ApiMember invoke)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(invoke);
        if (type.Kind != "delegate")
            throw new ArgumentException($"Type '{type.FullName}' is not a delegate.", nameof(type));
        if (invoke.SignatureModel is not { } signature)
        {
            throw new NotSupportedException(
                $"Delegate '{type.FullName}' requires a structured Invoke signature.");
        }

        string attributes = _declarationOptions.IncludeCustomAttributes && type.Attributes.Count > 0
            ? $"[{string.Join(", ", type.Attributes)}] "
            : "";
        string unsafeText = invoke.IsUnsafe ? " unsafe" : "";
        string parameters = FormatParameterList(signature.Parameters);
        string declaration =
            $"{attributes}{CSharpDeclarationWriter.TypeAccessibility(type)}{unsafeText} delegate {signature.ReturnType ?? "void"} {FormatTypeName(type, includeVariance: true)}{parameters}";
        foreach (var typeParameter in type.TypeParameters)
        {
            if (typeParameter.ConstraintsSummary is { } constraints)
            {
                declaration +=
                    $" where {EscapeIdentifier(typeParameter.Name)} : "
                    + EscapeKnownIdentifiers(
                        constraints,
                        type.TypeParameters.Select(parameter => parameter.Name));
            }
        }

        return declaration + ";";
    }

    public CSharpFormattedDeclaration FormatTypeUnit(
        ApiType type,
        IEnumerable<ApiMember>? members = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ToFormattedDeclaration(CSharpDeclarationWriter.RenderTypeUnit(
            type,
            members,
            _declarationOptions));
    }

    public static string EscapeIdentifier(string identifier)
        => CSharpDeclarationWriter.EscapeIdentifier(identifier);

    public static string EscapeNamespace(string @namespace)
        => CSharpDeclarationWriter.EscapeNamespace(@namespace);

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
    /// True when a C# type or member-body fragment requires the <c>unsafe</c>
    /// modifier — it contains pointer syntax (including function pointers, which
    /// carry a <c>*</c>) or a <c>stackalloc</c>. This over-approximates (a body
    /// using <c>*</c> as multiplication also reports true), which is safe because
    /// an unnecessary <c>unsafe</c> modifier still compiles. This is the
    /// authoritative unsafe-requirement predicate for the C# layer; consumers must
    /// not reimplement it.
    /// </summary>
    public static bool RequiresUnsafeModifier(string csharp)
    {
        ArgumentNullException.ThrowIfNull(csharp);
        return csharp.Contains('*', StringComparison.Ordinal)
            || csharp.Contains("stackalloc", StringComparison.Ordinal);
    }

    public static string EscapeKnownIdentifiers(string text, IEnumerable<string> rawNames)
        => CSharpDeclarationWriter.EscapeKnownIdentifiers(text, rawNames);

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
        int tick = type.Name.IndexOf('`');
        string name = tick >= 0 ? type.Name[..tick] : type.Name;
        name = EscapeIdentifier(name);
        return type.TypeParameters.Count == 0
            ? name
            : $"{name}<{string.Join(", ", type.TypeParameters.Select(parameter => FormatTypeParameter(parameter, includeVariance)))}>";
    }

    public static string NormalizeGeneratedMetadataTypeName(string metadataName)
    {
        ArgumentNullException.ThrowIfNull(metadataName);
        if (!IsGeneratedMetadataName(metadataName))
            return metadataName;

        int arity = metadataName.IndexOf('`');
        string sourceName = arity < 0 ? metadataName : metadataName[..arity];
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
            IncludeObsoleteAttribute = options.IncludeObsoleteAttribute,
            OmitInterfaceMemberModifiers = options.OmitInterfaceMemberModifiers,
            OmitPropertyAccessors = options.OmitPropertyAccessors
        };

    static CSharpFormattedDeclaration ToFormattedDeclaration(CSharpRenderedDeclaration declaration)
        => new(declaration.Source, declaration.Usings.ToArray(), declaration.Diagnostics.ToArray());

    static string FormatTypeParameter(TypeParameter parameter, bool includeVariance)
        => includeVariance && parameter.Variance is { } variance
            ? $"{variance} {EscapeIdentifier(parameter.Name)}"
            : EscapeIdentifier(parameter.Name);

    static string AddPrimaryConstructorParameters(
        string declaration,
        IReadOnlyList<ApiParameter> parameters)
    {
        string parameterList = FormatParameterList(parameters);
        int constraints = declaration.IndexOf(" where ", StringComparison.Ordinal);
        string head = constraints >= 0 ? declaration[..constraints] : declaration;
        string tail = constraints >= 0 ? declaration[constraints..] : "";
        int inheritance = head.IndexOf(" : ", StringComparison.Ordinal);
        return inheritance >= 0
            ? head[..inheritance] + parameterList + head[inheritance..] + tail
            : $"{head}{parameterList}{tail}";
    }
}
