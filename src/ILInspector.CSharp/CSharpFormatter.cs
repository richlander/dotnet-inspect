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
    readonly CSharpFormatOptions _options;

    public CSharpFormatter(CSharpFormatOptions? options = null)
    {
        options ??= new CSharpFormatOptions();
        if (!Enum.IsDefined(options.TypeNamePolicy))
            throw new ArgumentOutOfRangeException(nameof(options), options.TypeNamePolicy, "C# type-name policy must be defined.");
        if (!Enum.IsDefined(options.NamespacePolicy))
            throw new ArgumentOutOfRangeException(nameof(options), options.NamespacePolicy, "C# namespace policy must be defined.");
        _options = options with
        {
            Usings = options.Usings?.ToArray()
                ?? throw new ArgumentException("C# formatter usings cannot be null.", nameof(options))
        };
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
            ToMetadataOptions(),
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
            ToMetadataOptions(),
            methodParameters));
    }

    public string FormatTypeDeclaration(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return CSharpDeclarationWriter.RenderTypeDeclaration(type, ToMetadataOptions());
    }

    public CSharpFormattedDeclaration FormatTypeUnit(
        ApiType type,
        IEnumerable<ApiMember>? members = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        var memberArray = members?.ToArray();
        return ToFormattedDeclaration(CSharpDeclarationWriter.RenderTypeUnit(
            type,
            memberArray,
            ToMetadataOptions()));
    }

    CSharpDeclarationOptions ToMetadataOptions()
        => new()
        {
            TypeNameMode = _options.TypeNamePolicy switch
            {
                CSharpTypeNamePolicy.Qualified => CSharpTypeNameMode.Qualified,
                CSharpTypeNamePolicy.ShortWithUsings => CSharpTypeNameMode.ShortWithUsings,
                CSharpTypeNamePolicy.ContextualShort => CSharpTypeNameMode.ContextualShort,
                _ => throw new InvalidOperationException()
            },
            ContainingNamespace = _options.ContainingNamespace,
            Usings = _options.Usings,
            NamespaceMode = _options.NamespacePolicy switch
            {
                CSharpNamespacePolicy.Omit => CSharpNamespaceMode.Omit,
                CSharpNamespacePolicy.FileScoped => CSharpNamespaceMode.FileScoped,
                _ => throw new InvalidOperationException()
            },
            AbbreviateSignature = _options.AbbreviateSignature,
            TerminateMemberDeclaration = _options.TerminateMemberDeclaration,
            ForceAsync = _options.ForceAsync,
            ForceUnsafe = _options.ForceUnsafe,
            IncludeCustomAttributes = _options.IncludeCustomAttributes,
            IncludeObsoleteAttribute = _options.IncludeObsoleteAttribute,
            OmitInterfaceMemberModifiers = _options.OmitInterfaceMemberModifiers
        };

    static CSharpFormattedDeclaration ToFormattedDeclaration(CSharpRenderedDeclaration declaration)
        => new(declaration.Source, declaration.Usings.ToArray(), declaration.Diagnostics.ToArray());
}
