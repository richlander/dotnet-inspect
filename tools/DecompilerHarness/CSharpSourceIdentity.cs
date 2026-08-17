using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

internal sealed record CSharpSourceMemberIdentity(
    string MetadataName,
    MemberSignatureShapeResult SignatureShape,
    string? Body,
    IReadOnlyList<string> Evidence,
    int AttributionStartLine,
    int AttributionEndLine);

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

    public static string TypeMetadataName(TypeDeclarationSyntax type)
    {
        int arity = type.TypeParameterList?.Parameters.Count ?? 0;
        return arity == 0 ? type.Identifier.ValueText : $"{type.Identifier.ValueText}`{arity}";
    }

    public IEnumerable<CSharpSourceMemberIdentity> TypeMembers(TypeDeclarationSyntax type, string fullType)
    {
        foreach (var member in TypeHeaderMembers(type))
            yield return member;
        foreach (var declaration in type.Members)
        {
            IEnumerable<CSharpSourceMemberIdentity> members = declaration switch
            {
                MethodDeclarationSyntax method => MethodMembers(method),
                ConstructorDeclarationSyntax constructor => ConstructorMembers(constructor),
                PropertyDeclarationSyntax property => PropertyMembers(property),
                IndexerDeclarationSyntax indexer => IndexerMembers(indexer, fullType),
                OperatorDeclarationSyntax op => OperatorMembers(op),
                ConversionOperatorDeclarationSyntax conversion => ConversionOperatorMembers(conversion),
                _ => [],
            };
            foreach (var member in members)
                yield return member;
        }
    }

    public IReadOnlyList<CSharpSourceMemberIdentity> MethodMembers(MethodDeclarationSyntax method)
    {
        if (method.ExplicitInterfaceSpecifier is not null || IsBodylessPartial(method))
            return [];

        var span = AttributionSpan(method);
        return
        [
            new CSharpSourceMemberIdentity(
                method.Identifier.ValueText,
                SourceShape(method, SourceMemberSignatureKind.Method),
                BodyText(method),
                MethodEvidence(method).ToArray(),
                span.StartLine,
                span.EndLine),
        ];
    }

    public IReadOnlyList<CSharpSourceMemberIdentity> TypeHeaderMembers(TypeDeclarationSyntax type)
    {
        var members = new List<CSharpSourceMemberIdentity>();
        if (HasPrimaryConstructor(type))
        {
            var span = AttributionSpan(type);
            members.Add(new CSharpSourceMemberIdentity(
                ".ctor",
                SourceShape(type, SourceMemberSignatureKind.Constructor),
                Body: null,
                Evidence: ["primary-constructor"],
                span.StartLine,
                span.EndLine));
        }
        return members;
    }

    public IReadOnlyList<CSharpSourceMemberIdentity> ConstructorMembers(ConstructorDeclarationSyntax constructor)
    {
        string methodName = constructor.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor";
        var span = AttributionSpan(constructor);
        return
        [
            new CSharpSourceMemberIdentity(
                methodName,
                SourceShape(constructor, SourceMemberSignatureKind.Constructor),
                BodyText(constructor),
                ConstructorEvidence(constructor).ToArray(),
                span.StartLine,
                span.EndLine),
        ];
    }

    public IReadOnlyList<CSharpSourceMemberIdentity> OperatorMembers(OperatorDeclarationSyntax op)
    {
        var span = AttributionSpan(op);
        return
        [
            new CSharpSourceMemberIdentity(
                OperatorMetadataName(op),
                SourceShape(op, SourceMemberSignatureKind.Operator),
                BodyText(op),
                ["operator"],
                span.StartLine,
                span.EndLine),
        ];
    }

    public IReadOnlyList<CSharpSourceMemberIdentity> ConversionOperatorMembers(ConversionOperatorDeclarationSyntax conversion)
    {
        bool isImplicit = conversion.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword);
        bool isChecked = conversion.CheckedKeyword.IsKind(SyntaxKind.CheckedKeyword);
        string methodName = (isImplicit, isChecked) switch
        {
            (true, true) => "op_CheckedImplicit",
            (true, false) => "op_Implicit",
            (false, true) => "op_CheckedExplicit",
            (false, false) => "op_Explicit",
        };
        var span = AttributionSpan(conversion);
        return
        [
            new CSharpSourceMemberIdentity(
                methodName,
                SourceShape(conversion, SourceMemberSignatureKind.ConversionOperator),
                BodyText(conversion),
                ["conversion-operator"],
                span.StartLine,
                span.EndLine),
        ];
    }

    public IReadOnlyList<CSharpSourceMemberIdentity> PropertyMembers(PropertyDeclarationSyntax property)
    {
        if (property.ExplicitInterfaceSpecifier is not null || IsBodylessPartial(property))
            return [];

        var evidence = PropertyEvidence(property).ToArray();
        var members = new List<CSharpSourceMemberIdentity>(2);
        if (HasGetter(property))
        {
            var span = AttributionSpan(GetterDeclaration(property));
            members.Add(new CSharpSourceMemberIdentity(
                $"get_{property.Identifier.ValueText}",
                SourceShape(property, SourceMemberSignatureKind.Property),
                GetterBodyText(property),
                evidence,
                span.StartLine,
                span.EndLine));
        }
        if (HasSetter(property))
        {
            var span = AttributionSpan(SetterDeclaration(property));
            members.Add(new CSharpSourceMemberIdentity(
                $"set_{property.Identifier.ValueText}",
                SetterShape(property),
                SetterBodyText(property),
                evidence,
                span.StartLine,
                span.EndLine));
        }
        return members;
    }

    public IReadOnlyList<CSharpSourceMemberIdentity> IndexerMembers(IndexerDeclarationSyntax indexer, string fullType)
    {
        if (indexer.ExplicitInterfaceSpecifier is not null || IsBodylessPartial(indexer))
            return [];

        string metadataName = IndexerMetadataName(indexer) ?? PartialIndexerMetadataName(fullType, indexer) ?? "Item";
        var evidence = IndexerEvidence(indexer, metadataName).ToArray();
        var members = new List<CSharpSourceMemberIdentity>(2);
        if (HasGetter(indexer))
        {
            var span = AttributionSpan(GetterDeclaration(indexer));
            members.Add(new CSharpSourceMemberIdentity(
                $"get_{metadataName}",
                SourceShape(indexer, SourceMemberSignatureKind.Indexer),
                GetterBodyText(indexer),
                evidence,
                span.StartLine,
                span.EndLine));
        }
        if (HasSetter(indexer))
        {
            var span = AttributionSpan(SetterDeclaration(indexer));
            members.Add(new CSharpSourceMemberIdentity(
                $"set_{metadataName}",
                SetterShape(indexer),
                SetterBodyText(indexer),
                evidence,
                span.StartLine,
                span.EndLine));
        }
        return members;
    }

    static (int StartLine, int EndLine) AttributionSpan(SyntaxNode declaration)
    {
        FileLinePositionSpan span = declaration.SyntaxTree.GetMappedLineSpan(declaration.Span);
        return (span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1);
    }

    static SyntaxNode GetterDeclaration(PropertyDeclarationSyntax property)
        => property.ExpressionBody
            ?? (SyntaxNode?)property.AccessorList?.Accessors.FirstOrDefault(
                accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            ?? property;

    static SyntaxNode SetterDeclaration(PropertyDeclarationSyntax property)
        => (SyntaxNode?)property.AccessorList?.Accessors.FirstOrDefault(accessor =>
                accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
                || accessor.IsKind(SyntaxKind.InitAccessorDeclaration))
            ?? property;

    static SyntaxNode GetterDeclaration(IndexerDeclarationSyntax indexer)
        => indexer.ExpressionBody
            ?? (SyntaxNode?)indexer.AccessorList?.Accessors.FirstOrDefault(
                accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            ?? indexer;

    static SyntaxNode SetterDeclaration(IndexerDeclarationSyntax indexer)
        => (SyntaxNode?)indexer.AccessorList?.Accessors.FirstOrDefault(accessor =>
                accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
                || accessor.IsKind(SyntaxKind.InitAccessorDeclaration))
            ?? indexer;

    static MemberSignatureShapeResult SourceShape(
        SyntaxNode declaration,
        SourceMemberSignatureKind kind)
        => SourceMemberSignatureShape.Create(
            declaration.ToFullString(),
            kind,
            ContainingTypeParameters(declaration),
            ContainingValueTypeParameters(declaration));

    static MemberSignatureShapeResult SetterShape(PropertyDeclarationSyntax property)
        => SourceMemberSignatureShape.Create(
            $"void __set({property.Type} value);",
            SourceMemberSignatureKind.Method,
            ContainingTypeParameters(property),
            ContainingValueTypeParameters(property));

    static MemberSignatureShapeResult SetterShape(IndexerDeclarationSyntax indexer)
    {
        string parameters = string.Join(
            ", ",
            indexer.ParameterList.Parameters.Select(parameter => parameter.ToString())
                .Append($"{indexer.Type} value"));
        return SourceMemberSignatureShape.Create(
            $"void __set({parameters});",
            SourceMemberSignatureKind.Method,
            ContainingTypeParameters(indexer),
            ContainingValueTypeParameters(indexer));
    }

    static IReadOnlyList<string> ContainingTypeParameters(SyntaxNode declaration)
        => declaration.AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .Reverse()
            .SelectMany(type => type.TypeParameterList?.Parameters
                .Select(parameter => parameter.Identifier.ValueText)
                ?? [])
            .ToArray();

    static IReadOnlySet<string> ContainingValueTypeParameters(SyntaxNode declaration)
        => declaration.AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .SelectMany(type => type.ConstraintClauses)
            .Where(clause => clause.Constraints.Any(constraint =>
                constraint.IsKind(SyntaxKind.StructConstraint)
                || constraint is TypeConstraintSyntax
                {
                    Type: IdentifierNameSyntax { Identifier.ValueText: "unmanaged" },
                }))
            .Select(clause => clause.Name.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

    string? PartialIndexerMetadataName(string fullType, IndexerDeclarationSyntax indexer)
        => _partialIndexerNames.TryGetValue(PartialIndexerKey(fullType, indexer), out var metadataName)
            ? metadataName
            : null;

    static IEnumerable<string> ConstructorEvidence(ConstructorDeclarationSyntax constructor)
    {
        if (constructor.Modifiers.Any(SyntaxKind.StaticKeyword))
            yield return "static-constructor";
    }

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
                    string typeName = TypeMetadataName(type);
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

    static bool HasPrimaryConstructor(TypeDeclarationSyntax type)
        => type switch
        {
            ClassDeclarationSyntax { ParameterList: not null } => true,
            StructDeclarationSyntax { ParameterList: not null } => true,
            RecordDeclarationSyntax { ParameterList: not null } => true,
            _ => false,
        };

    static string? BodyText(ConstructorDeclarationSyntax constructor)
    {
        if (constructor.Body is { } body)
            return StatementsText(body);
        if (constructor.ExpressionBody is { } expressionBody)
            return $"{expressionBody.Expression};";
        return null;
    }

    static string? BodyText(MethodDeclarationSyntax method)
    {
        if (method.Body is { } body)
            return StatementsText(body);
        if (method.ExpressionBody is { } expressionBody)
            return ExpressionBodyText(method.ReturnType, expressionBody.Expression);
        return null;
    }

    static string? BodyText(OperatorDeclarationSyntax op)
    {
        if (op.Body is { } body)
            return StatementsText(body);
        if (op.ExpressionBody is { } expressionBody)
            return ExpressionBodyText(op.ReturnType, expressionBody.Expression);
        return null;
    }

    static string? BodyText(ConversionOperatorDeclarationSyntax conversion)
    {
        if (conversion.Body is { } body)
            return StatementsText(body);
        if (conversion.ExpressionBody is { } expressionBody)
            return ExpressionBodyText(conversion.Type, expressionBody.Expression);
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

    static string OperatorMetadataName(OperatorDeclarationSyntax op)
    {
        string methodName = op.OperatorToken.Kind() switch
        {
            SyntaxKind.PlusToken => op.ParameterList.Parameters.Count == 1 ? "op_UnaryPlus" : "op_Addition",
            SyntaxKind.MinusToken => op.ParameterList.Parameters.Count == 1 ? "op_UnaryNegation" : "op_Subtraction",
            SyntaxKind.ExclamationToken => "op_LogicalNot",
            SyntaxKind.TildeToken => "op_OnesComplement",
            SyntaxKind.PlusPlusToken => "op_Increment",
            SyntaxKind.MinusMinusToken => "op_Decrement",
            SyntaxKind.TrueKeyword => "op_True",
            SyntaxKind.FalseKeyword => "op_False",
            SyntaxKind.AsteriskToken => "op_Multiply",
            SyntaxKind.SlashToken => "op_Division",
            SyntaxKind.PercentToken => "op_Modulus",
            SyntaxKind.AmpersandToken => "op_BitwiseAnd",
            SyntaxKind.BarToken => "op_BitwiseOr",
            SyntaxKind.CaretToken => "op_ExclusiveOr",
            SyntaxKind.LessThanLessThanToken => "op_LeftShift",
            SyntaxKind.GreaterThanGreaterThanToken => "op_RightShift",
            SyntaxKind.GreaterThanGreaterThanGreaterThanToken => "op_UnsignedRightShift",
            SyntaxKind.EqualsEqualsToken => "op_Equality",
            SyntaxKind.ExclamationEqualsToken => "op_Inequality",
            SyntaxKind.LessThanToken => "op_LessThan",
            SyntaxKind.GreaterThanToken => "op_GreaterThan",
            SyntaxKind.LessThanEqualsToken => "op_LessThanOrEqual",
            SyntaxKind.GreaterThanEqualsToken => "op_GreaterThanOrEqual",
            _ => op.OperatorToken.ValueText,
        };
        return op.CheckedKeyword.IsKind(SyntaxKind.CheckedKeyword)
            ? $"op_Checked{methodName["op_".Length..]}"
            : methodName;
    }
}
