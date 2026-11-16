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
    /// together, terminated as a listing prints them, and each side's
    /// identified parts. A missing side (an added or removed member) is null.
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
            ComparableOptions(baselineType, currentType));

    /// <summary>
    /// The comparison options for a pair: custom attributes included, Type
    /// names shortened with usings, declarations terminated, and names in the
    /// declaring namespace shortened against it when both sides share it, as
    /// the namespace line that encloses them states. A pair across namespaces
    /// shortens against neither, so one side is never read in the other's.
    /// </summary>
    internal static CSharpDeclarationOptions ComparableOptions(ApiType? baselineType, ApiType? currentType)
    {
        string? baselineNamespace = baselineType?.Namespace;
        string? currentNamespace = currentType?.Namespace;
        bool shared = baselineType is null
            || currentType is null
            || string.Equals(baselineNamespace, currentNamespace, StringComparison.Ordinal);
        return ComparableBase with
        {
            ContainingNamespace = shared ? (currentType ?? baselineType)?.Namespace : null,
        };
    }

    /// <summary>
    /// A Type's declaration line as it encloses a member in a listing: a
    /// nested Type by its own name, as it appears inside its containing Types.
    /// </summary>
    public static string RenderEnclosingTypeDeclaration(ApiType type)
        => CSharpStructuredTypePlanProducer.RenderEnclosingTypeDeclaration(type);

    static readonly CSharpDeclarationOptions ComparableBase = new()
    {
        IncludeCustomAttributes = true,
        TypeNameMode = CSharpTypeNameMode.ShortWithUsings,
        TerminateMemberDeclaration = true,
    };
}
