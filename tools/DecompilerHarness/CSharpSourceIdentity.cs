using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

internal sealed record CSharpSourceMemberIdentity(
    string MetadataName,
    string? Body,
    IReadOnlyList<string> Evidence);

internal sealed class CSharpSourceIdentityContext
{
    readonly IReadOnlyDictionary<string, string> _partialIndexerNames;

    CSharpSourceIdentityContext(IReadOnlyDictionary<string, string> partialIndexerNames)
    {
        _partialIndexerNames = partialIndexerNames;
    }

    public static CSharpSourceIdentityContext Create(IEnumerable<CompilationUnitSyntax> roots)
    {
        var partialIndexerNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in roots)
            AddPartialIndexerNames(root.Members, namespaceName: "", containingTypes: [], partialIndexerNames);
        return new CSharpSourceIdentityContext(partialIndexerNames);
    }

    public IReadOnlyList<CSharpSourceMemberIdentity> MethodMembers(MethodDeclarationSyntax method)
    {
        if (method.ExplicitInterfaceSpecifier is not null || IsBodylessPartial(method))
            return [];

        return
        [
            new CSharpSourceMemberIdentity(
                method.Identifier.ValueText,
                BodyText(method),
                MethodEvidence(method).ToArray()),
        ];
    }

    public IReadOnlyList<CSharpSourceMemberIdentity> PropertyMembers(PropertyDeclarationSyntax property)
    {
        if (IsBodylessPartial(property))
            return [];

        var evidence = PropertyEvidence(property).ToArray();
        var members = new List<CSharpSourceMemberIdentity>(2);
        if (HasGetter(property))
            members.Add(new CSharpSourceMemberIdentity($"get_{property.Identifier.ValueText}", GetterBodyText(property), evidence));
        if (HasSetter(property))
            members.Add(new CSharpSourceMemberIdentity($"set_{property.Identifier.ValueText}", SetterBodyText(property), evidence));
        return members;
    }

    public IReadOnlyList<CSharpSourceMemberIdentity> IndexerMembers(IndexerDeclarationSyntax indexer, string fullType)
    {
        if (IsBodylessPartial(indexer))
            return [];

        string metadataName = IndexerMetadataName(indexer) ?? PartialIndexerMetadataName(fullType, indexer) ?? "Item";
        var evidence = IndexerEvidence(indexer, metadataName).ToArray();
        var members = new List<CSharpSourceMemberIdentity>(2);
        if (HasGetter(indexer))
            members.Add(new CSharpSourceMemberIdentity($"get_{metadataName}", GetterBodyText(indexer), evidence));
        if (HasSetter(indexer))
            members.Add(new CSharpSourceMemberIdentity($"set_{metadataName}", SetterBodyText(indexer), evidence));
        return members;
    }

    string? PartialIndexerMetadataName(string fullType, IndexerDeclarationSyntax indexer)
        => _partialIndexerNames.TryGetValue(PartialIndexerKey(fullType, indexer), out var metadataName)
            ? metadataName
            : null;

    static IEnumerable<string> MethodEvidence(MethodDeclarationSyntax method)
    {
        if (method.Modifiers.Any(SyntaxKind.PartialKeyword))
            yield return "partial-implementation";
    }

    static IEnumerable<string> IndexerEvidence(IndexerDeclarationSyntax indexer, string metadataName)
    {
        if (metadataName != "Item")
            yield return "indexer-name-attribute";
        if (indexer.Modifiers.Any(SyntaxKind.PartialKeyword))
            yield return "partial-implementation";
    }

    static void AddPartialIndexerNames(
        SyntaxList<MemberDeclarationSyntax> declarations,
        string namespaceName,
        IReadOnlyList<string> containingTypes,
        Dictionary<string, string> names)
    {
        foreach (var declaration in declarations)
        {
            switch (declaration)
            {
                case BaseNamespaceDeclarationSyntax ns:
                {
                    string nextNamespace = namespaceName.Length == 0
                        ? ns.Name.ToString()
                        : $"{namespaceName}.{ns.Name}";
                    AddPartialIndexerNames(ns.Members, nextNamespace, containingTypes, names);
                    break;
                }
                case TypeDeclarationSyntax type:
                {
                    string typeName = type.Identifier.ValueText;
                    var typeStack = containingTypes.Concat([typeName]).ToArray();
                    string fullType = namespaceName.Length == 0
                        ? string.Join(".", typeStack)
                        : $"{namespaceName}.{string.Join(".", typeStack)}";
                    foreach (var indexer in type.Members.OfType<IndexerDeclarationSyntax>().Where(IsBodylessPartial))
                    {
                        if (IndexerMetadataName(indexer) is { } metadataName)
                            names.TryAdd(PartialIndexerKey(fullType, indexer), metadataName);
                    }

                    AddPartialIndexerNames(type.Members, namespaceName, typeStack, names);
                    break;
                }
            }
        }
    }

    static string PartialIndexerKey(string fullType, IndexerDeclarationSyntax indexer)
        => $"{fullType}({string.Join(",", indexer.ParameterList.Parameters.Select(parameter => parameter.Type?.ToString() ?? ""))})";

    static string? IndexerMetadataName(IndexerDeclarationSyntax indexer)
    {
        foreach (var attribute in indexer.AttributeLists.SelectMany(list => list.Attributes))
        {
            string name = attribute.Name.ToString();
            if (!name.EndsWith("IndexerName", StringComparison.Ordinal)
                && !name.EndsWith("IndexerNameAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression)
                && literal.Token.ValueText is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    static bool IsBodylessPartial(IndexerDeclarationSyntax indexer)
        => indexer.Modifiers.Any(SyntaxKind.PartialKeyword)
            && indexer.ExpressionBody is null
            && indexer.AccessorList?.Accessors.All(accessor => accessor.Body is null && accessor.ExpressionBody is null) == true;

    static bool IsBodylessPartial(PropertyDeclarationSyntax property)
        => property.Modifiers.Any(SyntaxKind.PartialKeyword)
            && property.ExpressionBody is null
            && property.AccessorList?.Accessors.All(accessor => accessor.Body is null && accessor.ExpressionBody is null) == true;

    static bool IsBodylessPartial(MethodDeclarationSyntax method)
        => method.Modifiers.Any(SyntaxKind.PartialKeyword)
            && method.Body is null
            && method.ExpressionBody is null;

    static string? BodyText(MethodDeclarationSyntax method)
    {
        if (method.Body is { } body)
            return StatementsText(body);
        if (method.ExpressionBody is { } expressionBody)
            return ExpressionBodyText(method.ReturnType, expressionBody.Expression);
        return null;
    }

    static bool HasGetter(PropertyDeclarationSyntax property)
        => property.ExpressionBody is not null
            || property.AccessorList?.Accessors.Any(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) == true;

    static bool HasSetter(PropertyDeclarationSyntax property)
        => property.AccessorList?.Accessors.Any(accessor =>
            accessor.IsKind(SyntaxKind.SetAccessorDeclaration) || accessor.IsKind(SyntaxKind.InitAccessorDeclaration)) == true;

    static bool HasGetter(IndexerDeclarationSyntax indexer)
        => indexer.ExpressionBody is not null
            || indexer.AccessorList?.Accessors.Any(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) == true;

    static bool HasSetter(IndexerDeclarationSyntax indexer)
        => indexer.AccessorList?.Accessors.Any(accessor =>
            accessor.IsKind(SyntaxKind.SetAccessorDeclaration) || accessor.IsKind(SyntaxKind.InitAccessorDeclaration)) == true;

    static string? GetterBodyText(IndexerDeclarationSyntax indexer)
    {
        if (indexer.ExpressionBody is { } expressionBody)
            return $"return {expressionBody.Expression};";
        var getter = indexer.AccessorList?.Accessors.FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        if (getter?.Body is { } body)
            return StatementsText(body);
        if (getter?.ExpressionBody is { } getterExpression)
            return $"return {getterExpression.Expression};";
        return null;
    }

    static string? GetterBodyText(PropertyDeclarationSyntax property)
    {
        if (property.ExpressionBody is { } expressionBody)
            return $"return {expressionBody.Expression};";
        var getter = property.AccessorList?.Accessors.FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        if (getter?.Body is { } body)
            return StatementsText(body);
        if (getter?.ExpressionBody is { } getterExpression)
            return $"return {getterExpression.Expression};";
        return null;
    }

    static string? SetterBodyText(IndexerDeclarationSyntax indexer)
    {
        var setter = indexer.AccessorList?.Accessors.FirstOrDefault(accessor =>
            accessor.IsKind(SyntaxKind.SetAccessorDeclaration) || accessor.IsKind(SyntaxKind.InitAccessorDeclaration));
        if (setter?.Body is { } body)
            return StatementsText(body);
        if (setter?.ExpressionBody is { } setterExpression)
            return $"{setterExpression.Expression};";
        return null;
    }

    static string? SetterBodyText(PropertyDeclarationSyntax property)
    {
        var setter = property.AccessorList?.Accessors.FirstOrDefault(accessor =>
            accessor.IsKind(SyntaxKind.SetAccessorDeclaration) || accessor.IsKind(SyntaxKind.InitAccessorDeclaration));
        if (setter?.Body is { } body)
            return StatementsText(body);
        if (setter?.ExpressionBody is { } setterExpression)
            return $"{setterExpression.Expression};";
        return null;
    }

    static IEnumerable<string> PropertyEvidence(PropertyDeclarationSyntax property)
    {
        if (property.Modifiers.Any(SyntaxKind.PartialKeyword))
            yield return "partial-implementation";
    }

    static string StatementsText(BlockSyntax body)
        => string.Join(Environment.NewLine, body.Statements.Select(statement => statement.ToString()));

    static string ExpressionBodyText(TypeSyntax returnType, ExpressionSyntax expression)
        => returnType is PredefinedTypeSyntax predefined
            && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword)
            ? $"{expression};"
            : $"return {expression};";
}
