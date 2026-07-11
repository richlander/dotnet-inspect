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
    readonly CSharpDeclarationOptions _metadataOptions;

    public CSharpFormatter(CSharpFormatOptions? options = null)
    {
        options ??= new CSharpFormatOptions();
        if (!Enum.IsDefined(options.TypeNamePolicy))
            throw new ArgumentOutOfRangeException(nameof(options), options.TypeNamePolicy, "C# type-name policy must be defined.");
        if (!Enum.IsDefined(options.NamespacePolicy))
            throw new ArgumentOutOfRangeException(nameof(options), options.NamespacePolicy, "C# namespace policy must be defined.");
        var usings = options.Usings?.ToArray()
            ?? throw new ArgumentException("C# formatter usings cannot be null.", nameof(options));
        _metadataOptions = ToMetadataOptions(options, usings);
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
            _metadataOptions,
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
            _metadataOptions,
            methodParameters));
    }

    public string FormatTypeDeclaration(
        ApiType type,
        IReadOnlyList<ApiParameter>? primaryConstructorParameters = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        string declaration = CSharpDeclarationWriter.RenderTypeDeclaration(type, _metadataOptions);
        if (primaryConstructorParameters is not { Count: > 0 })
            return declaration;

        string declarationWithoutAttributes = CSharpDeclarationWriter.RenderTypeDeclaration(
            type,
            _metadataOptions with { IncludeCustomAttributes = false });
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

        string attributes = _metadataOptions.IncludeCustomAttributes && type.Attributes.Count > 0
            ? $"[{string.Join(", ", type.Attributes)}] "
            : "";
        string unsafeText = invoke.IsUnsafe ? " unsafe" : "";
        string parameters = string.Join(", ", signature.Parameters.Select(parameter => parameter.Declaration));
        string declaration =
            $"{attributes}public{unsafeText} delegate {signature.ReturnType ?? "void"} {FormatTypeName(type, includeVariance: true)}({parameters})";
        foreach (var typeParameter in type.TypeParameters)
        {
            if (typeParameter.ConstraintsSummary is { } constraints)
                declaration += $" where {EscapeIdentifier(typeParameter.Name)} : {constraints}";
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
            _metadataOptions));
    }

    public static string EscapeIdentifier(string identifier)
        => CSharpDeclarationWriter.EscapeIdentifier(identifier);

    public static string EscapeNamespace(string @namespace)
        => CSharpDeclarationWriter.EscapeNamespace(@namespace);

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

    static CSharpDeclarationOptions ToMetadataOptions(
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
        string parameterList = string.Join(", ", parameters.Select(parameter => parameter.Declaration));
        int constraints = declaration.IndexOf(" where ", StringComparison.Ordinal);
        string head = constraints >= 0 ? declaration[..constraints] : declaration;
        string tail = constraints >= 0 ? declaration[constraints..] : "";
        int inheritance = head.IndexOf(" : ", StringComparison.Ordinal);
        return inheritance >= 0
            ? head[..inheritance] + $"({parameterList})" + head[inheritance..] + tail
            : $"{head}({parameterList}){tail}";
    }
}
