using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

static class PrinterSyntaxInventory
{
    public const int Version = 1;

    public static bool TryCollect(
        string printerBody,
        out IReadOnlyList<string> features,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(printerBody);

        StatementSyntax statement = SyntaxFactory.ParseStatement(
            "{\n" + printerBody + "\n}",
            options: new CSharpParseOptions(LanguageVersion.Preview));
        Diagnostic? diagnostic = statement.GetDiagnostics()
            .FirstOrDefault(item => item.Severity == DiagnosticSeverity.Error);
        if (diagnostic is not null)
        {
            features = [];
            error = diagnostic.ToString();
            return false;
        }

        var collector = new Collector();
        collector.Visit(statement);
        features = [.. collector.Features];
        error = null;
        return true;
    }

    sealed class Collector : CSharpSyntaxWalker
    {
        public SortedSet<string> Features { get; } =
            new(StringComparer.Ordinal);

        public override void Visit(SyntaxNode? node)
        {
            if (node is null)
                return;

            switch (node)
            {
                case StatementSyntax and not BlockSyntax:
                    Add("statement", node.Kind(), "Statement");
                    break;
                case PatternSyntax:
                    Add("pattern", node.Kind(), "Pattern");
                    break;
                case PositionalPatternClauseSyntax:
                    Features.Add("clause.positional-pattern");
                    break;
                case PropertyPatternClauseSyntax:
                    Features.Add("clause.property-pattern");
                    break;
                case ArrayTypeSyntax:
                    Features.Add("type.array");
                    break;
                case FunctionPointerTypeSyntax:
                    Features.Add("type.function-pointer");
                    break;
                case NullableTypeSyntax:
                    Features.Add("type.nullable");
                    break;
                case PointerTypeSyntax:
                    Features.Add("type.pointer");
                    break;
                case RefTypeSyntax:
                    Features.Add("type.ref");
                    break;
                case ScopedTypeSyntax:
                    Features.Add("type.scoped");
                    break;
                case TupleTypeSyntax:
                    Features.Add("type.tuple");
                    break;
                case GenericNameSyntax:
                    Features.Add("syntax.generic-name");
                    break;
                case SpreadElementSyntax:
                    Features.Add("expression.collection-spread");
                    break;
                case ExpressionSyntax and not TypeSyntax
                    and not OmittedArraySizeExpressionSyntax:
                    Add("expression", node.Kind(), "Expression");
                    break;
                case AnonymousObjectMemberDeclaratorSyntax
                {
                    NameEquals: not null,
                }:
                    Features.Add("expression.anonymous-member-explicit-name");
                    break;
                case CatchClauseSyntax @catch:
                    Features.Add("clause.catch");
                    if (@catch.Declaration is not null)
                    {
                        Features.Add("clause.catch-declaration");
                        if (!@catch.Declaration.Identifier.IsKind(SyntaxKind.None))
                            Features.Add("clause.catch-variable");
                    }
                    break;
                case CatchFilterClauseSyntax:
                    Features.Add("clause.catch-filter");
                    break;
                case ElseClauseSyntax:
                    Features.Add("clause.else");
                    break;
                case FinallyClauseSyntax:
                    Features.Add("clause.finally");
                    break;
                case CaseSwitchLabelSyntax:
                    Features.Add("clause.switch-case");
                    break;
                case CasePatternSwitchLabelSyntax:
                    Features.Add("clause.switch-case-pattern");
                    break;
                case DefaultSwitchLabelSyntax:
                    Features.Add("clause.switch-default");
                    break;
                case SwitchExpressionArmSyntax:
                    Features.Add("clause.switch-expression-arm");
                    break;
                case WhenClauseSyntax:
                    Features.Add("clause.when");
                    break;
                case InterpolationSyntax interpolation:
                    Features.Add("interpolation.hole");
                    if (interpolation.AlignmentClause is not null)
                        Features.Add("interpolation.alignment");
                    if (interpolation.FormatClause is not null)
                        Features.Add("interpolation.format");
                    break;
                case ArgumentSyntax argument when argument.RefKindKeyword.Kind() is
                    SyntaxKind.RefKeyword or SyntaxKind.OutKeyword or SyntaxKind.InKeyword:
                    Features.Add($"argument.{argument.RefKindKeyword.ValueText}");
                    break;
            }

            if (node is LocalDeclarationStatementSyntax local)
            {
                Features.Add(
                    local.Declaration.Type is IdentifierNameSyntax
                    {
                        Identifier.ValueText: "var",
                    }
                        ? "declaration.local.var"
                        : "declaration.local.explicit-type");
                if (local.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
                {
                    Features.Add(
                        local.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)
                            ? "statement.await-using-declaration"
                            : "statement.using-declaration");
                }
            }
            if (node is CommonForEachStatementSyntax @foreach
                && @foreach.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword))
            {
                Features.Add("statement.await-foreach");
            }
            if (node is LocalFunctionStatementSyntax localFunction
                && localFunction.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                Features.Add("statement.static-local-function");
            }
            if (node is UsingStatementSyntax @using
                && @using.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword))
            {
                Features.Add("statement.await-using");
            }
            if (node is UsingStatementSyntax { Declaration: not null })
                Features.Add("clause.using-variable-declaration");
            if (node is ArgumentSyntax { NameColon: not null })
                Features.Add("argument.named");
            if (node is ParameterSyntax parameter)
                AddParameterModifiers(parameter.Modifiers);
            if (node is FunctionPointerParameterSyntax functionPointerParameter)
                AddParameterModifiers(functionPointerParameter.Modifiers);
            if (node is FunctionPointerCallingConventionSyntax convention)
            {
                Features.Add(
                    $"type.function-pointer-"
                    + convention.ManagedOrUnmanagedKeyword.ValueText);
                if (convention.UnmanagedCallingConventionList is not null)
                {
                    Features.Add(
                        "type.function-pointer-named-calling-convention");
                }
            }

            base.Visit(node);
        }

        void AddParameterModifiers(SyntaxTokenList modifiers)
        {
            foreach (SyntaxToken modifier in modifiers)
            {
                if (modifier.Kind() is
                    SyntaxKind.RefKeyword or
                    SyntaxKind.OutKeyword or
                    SyntaxKind.InKeyword)
                {
                    Features.Add($"parameter.{modifier.ValueText}");
                }
            }
        }

        void Add(string prefix, SyntaxKind kind, string suffix)
        {
            string name = kind.ToString();
            if (name.EndsWith(suffix, StringComparison.Ordinal))
                name = name[..^suffix.Length];
            Features.Add($"{prefix}.{KebabCase(name)}");
        }
    }

    static string KebabCase(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (char.IsUpper(current)
                && i > 0
                && (char.IsLower(value[i - 1])
                    || i + 1 < value.Length && char.IsLower(value[i + 1])))
            {
                result.Append('-');
            }
            result.Append(char.ToLowerInvariant(current));
        }
        return result.ToString();
    }
}
