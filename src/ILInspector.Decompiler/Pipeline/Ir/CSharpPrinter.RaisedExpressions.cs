using System.Text;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>Rendering helpers for raised high-level expression nodes.</summary>
public sealed partial class CSharpPrinter
{
    /// <summary>
    /// Renders a raised object/collection initializer: <c>new T(args) { ... }</c>
    /// where the body is <c>Member = value</c> / <c>[k] = value</c> entries (object
    /// form) or bare element / <c>{ k, v }</c> entries (collection form).
    /// Constructor parens are omitted when the creation takes no arguments,
    /// matching idiomatic C#.
    /// </summary>
    string ObjectInitializerText(ObjectInitializerExpression initializer)
    {
        var creation = initializer.Creation;
        var arguments = creation.Arguments.Count == 0
            ? string.Empty
            : $"({Arguments(creation.Arguments, creation.Constructor.ParameterTypes, creation.Constructor.ParameterRefKinds)})";
        return $"new {TypeText(creation.Constructor.DeclaringType)}{arguments} {InitializerBodyText(initializer.IsCollection, initializer.Entries)}";
    }

    string WithExpressionText(WithExpression node)
        => $"{Operand(node.Receiver)} with {{ {string.Join(", ", node.Entries.Select(WithExpressionEntryText))} }}";

    string WithExpressionEntryText(InitializerEntry entry)
        => $"{entry.Member} = {Expression(entry.Arguments[0])}";

    /// <summary>Renders the brace body shared by a top-level initializer and a nested <see cref="InitializerBlock"/>.</summary>
    string InitializerBodyText(bool isCollection, IReadOnlyList<InitializerEntry> entries)
        => $"{{ {string.Join(", ", entries.Select(entry => InitializerEntryText(isCollection, entry)))} }}";

    /// <summary>Renders one initializer entry per the parent's collection/object form (see <see cref="InitializerEntry"/>).</summary>
    string InitializerEntryText(bool isCollection, InitializerEntry entry)
    {
        if (isCollection)
        {
            // A single-argument Add is a bare element; a multi-argument Add (a
            // dictionary `{ k, v }`) keeps its brace-wrapped argument list.
            return entry.Arguments.Count == 1
                ? Expression(entry.Arguments[0])
                : $"{{ {string.Join(", ", entry.Arguments.Select(Expression))} }}";
        }

        // A nested member (Inner = { ... }) carries an InitializerBlock value, which
        // prints as its own brace body rather than a `new`-rooted expression.
        var value = entry.Arguments[^1];
        string valueText = value is InitializerBlock block
            ? InitializerBodyText(block.IsCollection, block.Entries)
            : Expression(value);

        if (entry.Member is { } member)
            return $"{member} = {valueText}";

        // An indexer member: the trailing argument is the value, the rest are keys.
        var keys = entry.Arguments.Take(entry.Arguments.Count - 1).Select(Expression);
        return $"[{string.Join(", ", keys)}] = {valueText}";
    }

    /// <summary>
    /// Renders <c>new { Name = value, ... }</c>. A member uses the projection
    /// shorthand (<c>new { x }</c> / <c>new { obj.Member }</c>) only when the value
    /// expression's own member name exactly matches the property name — an
    /// identifier whose text equals the name, or a field/property access ending in
    /// <c>.Name</c>. Any mismatch keeps the explicit <c>Name = value</c> form, so
    /// the recovered name is never silently changed.
    /// </summary>
    string AnonymousObjectText(AnonymousObject anonymous)
    {
        if (anonymous.Values.Count == 0)
            return "new { }";
        var parts = new List<string>(anonymous.Values.Count);
        for (int i = 0; i < anonymous.Values.Count; i++)
        {
            var value = anonymous.Values[i];
            string name = anonymous.PropertyNames[i];
            string text = Expression(value);
            bool shorthand = text == name
                || (value is LoadField field && field.Field.Name == name && text.EndsWith("." + name, StringComparison.Ordinal))
                || (value is LoadProperty property && property.PropertyName == name && text.EndsWith("." + name, StringComparison.Ordinal));
            parts.Add(shorthand ? text : $"{name} = {text}");
        }
        return $"new {{ {string.Join(", ", parts)} }}";
    }

    /// <summary>
    /// Renders a recovered lambda: parameter names (the delegate type supplies
    /// their types, so they stay unannotated), then <c>=&gt; expr</c> for an
    /// expression body or <c>=&gt; { ... }</c> otherwise. A single parameter
    /// drops the parentheses. Zero-local bodies reuse the current printer so
    /// capture substitutions still bind to the outer scope; local-bearing bodies
    /// are non-capturing and print through an isolated lambda scope.
    /// </summary>
    string LambdaText(Lambda lambda)
    {
        string parameters = lambda.Parameters is [var single]
            ? CSharpNaming.EscapeIdentifier(single.Name)
            : $"({string.Join(", ", lambda.Parameters.Select(p => CSharpNaming.EscapeIdentifier(p.Name)))})";

        if (lambda.ExpressionBody is { } expr)
            return $"{parameters} => {Expression(expr)}";

        if (NeedsNestedLambdaScope(lambda))
            return $"{parameters} => {{ {LambdaBodyWithLocalScope(lambda)} }}";

        var statements = lambda.Body.Blocks
            .SelectMany(b => b.Children)
            .Select(LambdaStatement)
            .Where(s => s is not null);
        return $"{parameters} => {{ {string.Join(" ", statements)} }}";
    }

    static bool NeedsNestedLambdaScope(Lambda lambda)
        => !lambda.Locals.IsEmpty
            || lambda.Body.Descendants.Any(node => node is LoadStackSlot or StoreStackSlot);

    string LambdaBodyWithLocalScope(Lambda lambda)
    {
        var body = lambda.Body;
        body.Detach();
        try
        {
            var function = new IrFunction(
                "<lambda>",
                _function.DeclaringType,
                new MethodSignature(TypeRef.CoreLib("System", "Void"), lambda.Parameters, HasThis: false, GenericParameterCount: 0),
                lambda.Locals,
                body)
            {
                LocalNames = lambda.LocalNames,
                UsesUpdatedMemorySafetyRules = lambda.UsesUpdatedMemorySafetyRules,
                SkipLocalsInit = lambda.SkipLocalsInit,
            };
            var text = new CSharpPrinter(function).PrintBody(function).Trim();
            return string.Join(" ", text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));
        }
        finally
        {
            body.Detach();
            lambda.ResetBody(body);
        }
    }

    string? LambdaStatement(IrNode node) => node switch
    {
        Return { Value: { } value } => $"return {Expression(value)};",
        Return => "return;",
        _ => Statement(node),
    };

    /// <summary>The text of one switch-expression arm: its labels (or <c>_</c>) and the value it yields.</summary>
    string SwitchArmText(SwitchExpressionArm arm, TypeRef? target = null, TypeRef? labelEnum = null)
        => $"{SwitchExpressionLabelText(arm, labelEnum)} => {SwitchArmValueText(arm.Value, target)}";

    string SwitchExpressionLabelText(SwitchExpressionArm arm, TypeRef? labelEnum)
        => arm.IsDefault
            ? "_"
            : string.Join(" or ", arm.Labels.Select(label => SwitchLabelText(
                new Constant(label, TypeRef.CoreLib("System", "Int32")),
                labelEnum)));

    string SwitchArmValueText(IrExpression value, TypeRef? target)
        => target is { } enumTarget
            && IsEnumLikeInteger(enumTarget)
            && value.ResultType is { } valueType
            && TypeFamilies.IsIntegerLike(valueType)
                ? EnumIntegerCast(value, enumTarget)
                : Expression(value);

    /// <summary>The single-line form of a switch expression, used when it is nested inside another expression.</summary>
    string SwitchExpressionInline(SwitchExpression node, TypeRef? target = null)
    {
        var labelEnum = SwitchLabelEnumType(node.Value);
        return $"{Operand(node.Value)} switch {{ {string.Join(", ", node.Arms.Select(arm => SwitchArmText(arm, target, labelEnum)))} }}";
    }

    string InterpolatedStringText(InterpolatedStringExpression node)
    {
        var sb = new StringBuilder().Append("$\"");
        foreach (var part in node.Parts)
        {
            if (part.IsLiteral)
            {
                sb.Append(InterpolatedLiteralText(part.Literal!));
            }
            else if (part.ExpressionIndex >= 0 && part.ExpressionIndex < node.FormattedValues.Count)
            {
                sb.Append('{').Append(InterpolatedExpression(node.FormattedValues[part.ExpressionIndex]));
                if (part.Format is { } format)
                {
                    if (format.HasAlignment)
                        sb.Append(',').Append(format.Alignment.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (format.FormatString is { } formatString)
                        sb.Append(':').Append(InterpolatedFormatText(formatString));
                }
                sb.Append('}');
            }
        }
        return sb.Append('"').ToString();
    }

    string InterpolatedExpression(IrExpression value)
        => value is LoadArgument or LoadLocal or LoadStackSlot or Constant or LoadField or LoadProperty or Call
            ? Expression(value)
            : $"({Expression(value)})";

    static string InterpolatedLiteralText(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c == '{')
                sb.Append("{{");
            else if (c == '}')
                sb.Append("}}");
            else
                sb.Append(EscapeChar(c, inString: true));
        }
        return sb.ToString();
    }

    /// <summary>
    /// The format clause of an interpolation hole (<c>{value:format}</c>) sits
    /// inside the enclosing <c>$"…"</c>, so csc escape-processes it exactly like
    /// literal text: a backslash-escaped custom format such as a TimeSpan
    /// <c>h\:mm\:ss</c> reaches the IR as a single backslash and must be rendered
    /// <c>h\\:mm\\:ss</c> or the bare <c>\:</c> is CS1009 "unrecognized escape
    /// sequence". Braces never appear here — a brace cannot round-trip through a
    /// format spec, so <see cref="MemberIdentity"/> keeps such handlers lowered —
    /// so this only needs the string escaping, not the literal text's brace
    /// doubling.
    /// </summary>
    static string InterpolatedFormatText(string format)
    {
        var sb = new StringBuilder(format.Length);
        foreach (char c in format)
            sb.Append(EscapeChar(c, inString: true));
        return sb.ToString();
    }

    /// <summary>
    /// <c>target?.Member</c>: the member's receiver child is the target, and the
    /// member's name/arguments form the suffix after <c>?</c>. Mirrors the
    /// instance spellings of <see cref="CallText"/>, <see cref="PropertyTarget"/>,
    /// and <see cref="FieldTarget"/>, minus their receiver — the <c>?.</c> owns it.
    /// </summary>
    string NullConditionalText(NullConditional node)
    {
        var member = node.Member;
        var receiver = NullConditionalReceiver(member);
        return $"{ReceiverText(receiver)}?{NullConditionalSuffix(member)}";
    }

    static IrExpression NullConditionalReceiver(IrExpression member) => member switch
    {
        Call call => call.Arguments[0],
        LoadProperty property => property.Instance!,
        LoadField field => field.Instance!,
        _ => member,
    };

    string NullConditionalSuffix(IrExpression member) => member switch
    {
        LoadField field => $".{field.Field.Name}",
        LoadProperty property when property.IndexArguments.Count > 0 => $"[{Arguments(property.IndexArguments)}]",
        LoadProperty property => $".{property.PropertyName}",
        Call call => NullConditionalCallSuffix(call),
        _ => $".{member.Describe()}",
    };

    string NullConditionalCallSuffix(Call call)
    {
        string typeArguments = call.Callee.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", call.Callee.TypeArguments.Select(TypeText))}>";
        return $".{CSharpNaming.SourceMethodName(call.Callee.Name)}{typeArguments}({Arguments(call.Arguments.Skip(1), call.Callee.ParameterTypes, call.Callee.ParameterRefKinds)})";
    }
}
