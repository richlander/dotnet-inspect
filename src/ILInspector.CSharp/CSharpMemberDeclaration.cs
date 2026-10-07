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
}
