using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

internal sealed record ClosureDiagnosticReference(
    string Name,
    string? ContainingType = null,
    string? ContainingNamespace = null);

internal static class ClosureDiagnosticEvidence
{
    public static string NormalizeTypeName(string name)
    {
        int angle = name.IndexOf('<');
        if (angle >= 0)
            name = name[..angle];
        int dot = name.LastIndexOf('.');
        if (dot >= 0)
            name = name[(dot + 1)..];
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick];
        return name.StartsWith('@') ? name[1..] : name;
    }

    public static ClosureDiagnosticReference? Extract(Diagnostic diagnostic, SemanticModel semanticModel)
    {
        if (!diagnostic.Location.IsInSource
            || diagnostic.Location.SourceTree != semanticModel.SyntaxTree)
        {
            return null;
        }

        var span = diagnostic.Location.SourceSpan;
        var root = semanticModel.SyntaxTree.GetRoot();
        var node = root.FindNode(span, getInnermostNodeForTie: true);
        var simpleName = node as SimpleNameSyntax
            ?? (node as MemberBindingExpressionSyntax)?.Name
            ?? node.DescendantNodesAndSelf()
                .OfType<SimpleNameSyntax>()
                .FirstOrDefault(candidate => candidate.Span.IntersectsWith(span))
            ?? node.AncestorsAndSelf()
                .OfType<SimpleNameSyntax>()
                .FirstOrDefault(candidate => candidate.Span.Contains(span));
        if (simpleName is null)
            return null;

        string name = simpleName.Identifier.ValueText;
        return diagnostic.Id switch
        {
            "CS0234" => new ClosureDiagnosticReference(
                name,
                ContainingNamespace: ContainingNamespace(simpleName)),
            "CS1061" or "CS0117" => new ClosureDiagnosticReference(
                name,
                ContainingType: ReceiverType(simpleName, semanticModel)),
            "CS0122" => InaccessibleReference(simpleName, semanticModel)
                ?? new ClosureDiagnosticReference(name),
            "CS0246" or "CS0103" => new ClosureDiagnosticReference(name),
            _ => null,
        };
    }

    static string? ContainingNamespace(SimpleNameSyntax simpleName)
    {
        var qualified = simpleName.Ancestors()
            .OfType<QualifiedNameSyntax>()
            .FirstOrDefault(candidate => candidate.Right.Span.Contains(simpleName.Span));
        return qualified is null ? null : IdentifierPath(qualified.Left);
    }

    static string? ReceiverType(SimpleNameSyntax simpleName, SemanticModel semanticModel)
    {
        var memberAccess = simpleName.AncestorsAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault(candidate => candidate.Name.Span.Contains(simpleName.Span));
        ExpressionSyntax? receiver = memberAccess?.Expression;
        if (receiver is null)
        {
            receiver = simpleName.Ancestors()
                .OfType<ConditionalAccessExpressionSyntax>()
                .FirstOrDefault(candidate => candidate.WhenNotNull.Span.Contains(simpleName.Span))
                ?.Expression;
        }

        if (receiver is null)
            return null;

        var type = semanticModel.GetTypeInfo(receiver).Type
            ?? semanticModel.GetSymbolInfo(receiver).Symbol as ITypeSymbol;
        return type is null ? null : TypeName(type);
    }

    static ClosureDiagnosticReference? InaccessibleReference(
        SimpleNameSyntax simpleName,
        SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(simpleName);
        var candidate = symbolInfo.CandidateSymbols.FirstOrDefault();
        return candidate switch
        {
            INamedTypeSymbol type => new ClosureDiagnosticReference(TypeName(type)),
            IMethodSymbol method => new ClosureDiagnosticReference(
                method.Name,
                TypeName(method.ContainingType)),
            IPropertySymbol property => new ClosureDiagnosticReference(
                property.Name,
                TypeName(property.ContainingType)),
            IFieldSymbol field => new ClosureDiagnosticReference(
                field.Name,
                TypeName(field.ContainingType)),
            IEventSymbol @event => new ClosureDiagnosticReference(
                @event.Name,
                TypeName(@event.ContainingType)),
            _ => null,
        };
    }

    static string TypeName(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            return type.Name;

        var names = new Stack<string>();
        for (INamedTypeSymbol? current = named.OriginalDefinition; current is not null; current = current.ContainingType)
            names.Push(CompileBackCSharpNames.Identifier(current.Name));

        string typeName = string.Join(".", names);
        string ns = named.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
            ? containingNamespace.ToDisplayString()
            : "";
        return ns.Length == 0 ? typeName : $"{ns}.{typeName}";
    }

    static string IdentifierPath(NameSyntax name)
        => string.Join(
            ".",
            name.DescendantTokens()
                .Where(token => token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken))
                .Select(token => token.ValueText));
}
