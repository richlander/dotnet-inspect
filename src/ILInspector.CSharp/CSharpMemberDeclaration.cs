using ILInspector.Metadata;

namespace ILInspector.CSharp;

/// <summary>Renders one Metadata-issued declaration using the shared C# spelling owner.</summary>
public static class CSharpMemberDeclaration
{
    public static string Render(ApiType type, ApiMember member)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        return CSharpDeclarationWriter.RenderMemberDeclaration(type, member);
    }

    /// <summary>
    /// Renders a member's baseline and current declarations for comparison:
    /// custom attributes included, Type names shortened against both sides
    /// together, and each side's identified parts. A missing side (an added
    /// or removed member) is null.
    /// </summary>
    public static (CSharpDeclarationParts? Baseline, CSharpDeclarationParts? Current) RenderComparablePair(
        ApiType? baselineType,
        ApiMember? baselineMember,
        ApiType? currentType,
        ApiMember? currentMember)
        => CSharpDeclarationWriter.RenderMemberDeclarationPair(
            baselineType,
            baselineMember,
            currentType,
            currentMember,
            ComparableOptions);

    static readonly CSharpDeclarationOptions ComparableOptions = new()
    {
        IncludeCustomAttributes = true,
        TypeNameMode = CSharpTypeNameMode.ShortWithUsings,
    };
}
