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

    public string FormatTypeDeclaration(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return CSharpDeclarationWriter.RenderTypeDeclaration(type, _metadataOptions);
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
            OmitInterfaceMemberModifiers = options.OmitInterfaceMemberModifiers
        };

    static CSharpFormattedDeclaration ToFormattedDeclaration(CSharpRenderedDeclaration declaration)
        => new(declaration.Source, declaration.Usings.ToArray(), declaration.Diagnostics.ToArray());
}
