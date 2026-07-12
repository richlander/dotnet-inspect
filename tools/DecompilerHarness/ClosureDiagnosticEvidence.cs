using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

internal sealed record ClosureDiagnosticReference(
    string Name,
    string? ContainingType = null,
    string? ContainingNamespace = null,
    IReadOnlyList<string>? CompatibleReceiverTypes = null,
    bool CompatibleReceiverTypesComplete = false);

internal static class ClosureDiagnosticEvidence
{
    public static bool Supports(string diagnosticId)
        => diagnosticId is "CS0246" or "CS0234" or "CS0103" or "CS0122" or "CS1061" or "CS0117";

    public static string FailureReason(string reason, IEnumerable<string> unextractedDiagnosticIds)
    {
        var ids = unextractedDiagnosticIds
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return ids.Length == 0
            ? reason
            : $"{reason}-unextracted[{string.Join(",", ids)}]";
    }

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
        bool preferRightmost = diagnostic.Id is "CS0234" or "CS0122" or "CS1061" or "CS0117";
        var simpleName = FindSimpleName(node, span, preferRightmost);
        if (diagnostic.Id == "CS1061"
            && !IsMissingExplicitMemberReference(simpleName, semanticModel)
            && ImplicitMemberReference(node, span, semanticModel) is { } implicitMember)
        {
            return implicitMember;
        }

        if (simpleName is null)
            return null;

        string name = simpleName.Identifier.ValueText;
        return diagnostic.Id switch
        {
            "CS0234" => new ClosureDiagnosticReference(
                name,
                ContainingNamespace: ContainingNamespace(simpleName)),
            "CS1061" => ReceiverReference(
                name,
                ReceiverType(simpleName, semanticModel)
                    ?? InitializerType(simpleName, semanticModel)),
            "CS0117" => new ClosureDiagnosticReference(
                name,
                ContainingType: TypeNameOrNull(ReceiverType(simpleName, semanticModel))
                    ?? TypeNameOrNull(InitializerType(simpleName, semanticModel))),
            "CS0122" => InaccessibleReference(simpleName, semanticModel)
                ?? new ClosureDiagnosticReference(name),
            "CS0246" or "CS0103" => new ClosureDiagnosticReference(name),
            _ => null,
        };
    }

    static bool IsMissingExplicitMemberReference(
        SimpleNameSyntax? simpleName,
        SemanticModel semanticModel)
    {
        bool isMemberReference = simpleName?.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name == simpleName,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name == simpleName,
            _ => false,
        };
        return isMemberReference
            && semanticModel.GetSymbolInfo(simpleName!).Symbol is null;
    }

    static SimpleNameSyntax? FindSimpleName(
        SyntaxNode node,
        Microsoft.CodeAnalysis.Text.TextSpan diagnosticSpan,
        bool preferRightmost)
    {
        if (node is SimpleNameSyntax simpleName)
            return simpleName;
        if (node is MemberBindingExpressionSyntax memberBinding)
            return memberBinding.Name;

        var candidates = node.DescendantNodesAndSelf()
            .OfType<SimpleNameSyntax>()
            .Where(candidate => candidate.Span.IntersectsWith(diagnosticSpan));
        var descendant = preferRightmost
            ? candidates.MaxBy(candidate => candidate.SpanStart)
            : candidates.MinBy(candidate => candidate.SpanStart);
        return descendant
            ?? node.AncestorsAndSelf()
                .OfType<SimpleNameSyntax>()
                .FirstOrDefault(candidate => candidate.Span.Contains(diagnosticSpan));
    }

    static string? ContainingNamespace(SimpleNameSyntax simpleName)
    {
        var qualified = simpleName.Ancestors()
            .OfType<QualifiedNameSyntax>()
            .FirstOrDefault(candidate => candidate.Right.Span.Contains(simpleName.Span));
        if (qualified is not null)
            return IdentifierPath(qualified.Left);

        var memberAccess = simpleName.Ancestors()
            .OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault(candidate => candidate.Name.Span.Contains(simpleName.Span));
        return memberAccess is null ? null : IdentifierPath(memberAccess.Expression);
    }

    static ITypeSymbol? ReceiverType(SimpleNameSyntax simpleName, SemanticModel semanticModel)
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

        return semanticModel.GetTypeInfo(receiver).Type
            ?? semanticModel.GetSymbolInfo(receiver).Symbol as ITypeSymbol;
    }

    static ClosureDiagnosticReference? ImplicitMemberReference(
        SyntaxNode node,
        Microsoft.CodeAnalysis.Text.TextSpan diagnosticSpan,
        SemanticModel semanticModel)
    {
        var contexts = node.AncestorsAndSelf().ToArray();
        int awaitIndex = Array.FindIndex(
            contexts,
            candidate => candidate is AwaitExpressionSyntax awaitExpression
                && awaitExpression.Span.Contains(diagnosticSpan));
        int collectionIndex = Array.FindIndex(
            contexts,
            candidate => candidate is InitializerExpressionSyntax initializer
                && initializer.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CollectionInitializerExpression));

        var awaitExpression = awaitIndex >= 0
            ? (AwaitExpressionSyntax)contexts[awaitIndex]
            : null;
        bool missingAwaiter = awaitExpression is not null
            && semanticModel.GetAwaitExpressionInfo(awaitExpression).GetAwaiterMethod is null;

        var collectionInitializer = collectionIndex >= 0
            ? (InitializerExpressionSyntax)contexts[collectionIndex]
            : null;
        var collectionElement = collectionInitializer?.Expressions
            .FirstOrDefault(candidate => candidate.Span.IntersectsWith(diagnosticSpan))
            ?? (collectionInitializer?.Expressions.Count == 1
                ? collectionInitializer.Expressions[0]
                : null);
        bool missingAdd = collectionElement is not null
            && semanticModel.GetCollectionInitializerSymbolInfo(collectionElement).Symbol is null;

        if (missingAwaiter
            && (!missingAdd || collectionIndex < 0 || awaitIndex < collectionIndex))
        {
            return TypeSymbol(awaitExpression!.Expression, semanticModel) is { } awaiterType
                ? ReceiverReference("GetAwaiter", awaiterType)
                : null;
        }

        if (missingAdd)
        {
            return InitializerType(collectionInitializer!, semanticModel) is { } collectionType
                ? ReceiverReference("Add", collectionType)
                : null;
        }

        return null;
    }

    static ITypeSymbol? InitializerType(SyntaxNode node, SemanticModel semanticModel)
    {
        var initializer = node as InitializerExpressionSyntax
            ?? node.Ancestors()
                .OfType<InitializerExpressionSyntax>()
                .FirstOrDefault();
        if (initializer is null)
            return null;

        if (initializer.Parent is AssignmentExpressionSyntax assignment
            && assignment.Right == initializer)
        {
            return semanticModel.GetSymbolInfo(assignment.Left).Symbol switch
            {
                IPropertySymbol property => property.Type,
                IFieldSymbol field => field.Type,
                _ => null,
            };
        }

        return initializer.Parent is ExpressionSyntax expression
            ? TypeSymbol(expression, semanticModel)
            : null;
    }

    static string? TypeNameOrNull(ITypeSymbol? type)
        => type is null ? null : TypeName(type);

    static ITypeSymbol? TypeSymbol(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        return semanticModel.GetTypeInfo(expression).Type
            ?? semanticModel.GetSymbolInfo(expression).Symbol as ITypeSymbol;
    }

    static ClosureDiagnosticReference ReceiverReference(string name, ITypeSymbol? receiver)
    {
        if (receiver is null)
            return new ClosureDiagnosticReference(name);

        var (types, complete) = CompatibleReceiverTypes(receiver);
        return new ClosureDiagnosticReference(
            name,
            TypeName(receiver),
            CompatibleReceiverTypes: types,
            CompatibleReceiverTypesComplete: complete);
    }

    static (IReadOnlyList<string> Types, bool Complete) CompatibleReceiverTypes(ITypeSymbol receiver)
    {
        var types = new List<string>();
        bool complete = true;
        Add(receiver);
        if (receiver is INamedTypeSymbol named)
        {
            for (var current = named.BaseType; current is not null; current = current.BaseType)
                Add(current);
            foreach (var @interface in named.Interfaces)
                complete &= !ContainsErrorType(@interface);
            foreach (var @interface in named.AllInterfaces)
                Add(@interface);
        }
        else if (receiver is ITypeParameterSymbol parameter)
        {
            complete = false;
            foreach (var constraint in parameter.ConstraintTypes)
                Add(constraint);
        }
        else
        {
            complete = false;
        }

        return (types.Distinct(StringComparer.Ordinal).ToArray(), complete);

        void Add(ITypeSymbol type)
        {
            types.Add(TypeName(type));
            complete &= !ContainsErrorType(type);
        }
    }

    static bool ContainsErrorType(ITypeSymbol type)
        => type.TypeKind == TypeKind.Error
            || type switch
            {
                INamedTypeSymbol named => named.TypeArguments.Any(ContainsErrorType),
                IArrayTypeSymbol array => ContainsErrorType(array.ElementType),
                IPointerTypeSymbol pointer => ContainsErrorType(pointer.PointedAtType),
                _ => false,
            };

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
        if (type is IArrayTypeSymbol array)
            return $"{TypeName(array.ElementType)}[{new string(',', array.Rank - 1)}]";
        if (type is not INamedTypeSymbol named)
            return type.Name;

        var names = new Stack<string>();
        for (INamedTypeSymbol? current = named.OriginalDefinition; current is not null; current = current.ContainingType)
            names.Push(CompileBackCSharpNames.Identifier(current.Name));

        string typeName = string.Join(".", names);
        string ns = named.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
            ? CompileBackCSharpNames.EscapeNamespace(NamespaceName(containingNamespace))
            : "";
        return ns.Length == 0 ? typeName : $"{ns}.{typeName}";
    }

    static string NamespaceName(INamespaceSymbol containingNamespace)
    {
        var names = new Stack<string>();
        for (INamespaceSymbol? current = containingNamespace; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
            names.Push(current.Name);
        return string.Join(".", names);
    }

    static string IdentifierPath(SyntaxNode name)
        => string.Join(
            ".",
            name.DescendantTokens()
                .Where(token => token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken))
                .Select(token => token.ValueText));
}
