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
        => $"{CSharpNaming.EscapeIdentifier(entry.Member!)} = {Expression(entry.Arguments[0])}";

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
            return $"{CSharpNaming.EscapeIdentifier(member)} = {valueText}";

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
            string escapedName = CSharpNaming.EscapeIdentifier(name);
            string text = Expression(value);
            bool shorthand = text == escapedName
                || (value is LoadField field && field.Field.Name == name && text.EndsWith("." + escapedName, StringComparison.Ordinal))
                || (value is LoadProperty property && property.PropertyName == name && text.EndsWith("." + escapedName, StringComparison.Ordinal));
            parts.Add(shorthand ? text : $"{escapedName} = {text}");
        }
        return $"new {{ {string.Join(", ", parts)} }}";
    }

    /// <summary>
    /// Renders a recovered lambda: parameter names (the delegate type supplies
    /// their types, so they stay unannotated), then <c>=&gt; expr</c> for an
    /// expression body or <c>=&gt; { ... }</c> otherwise. A single parameter
    /// drops the parentheses. Zero-local bodies reuse the current printer so
    /// capture substitutions still bind to the outer scope; local-bearing bodies
    /// are non-capturing and print through an isolated lambda scope. A block body
    /// with more than one statement expands across lines like any other
    /// statement block (see <see cref="LambdaBlockText"/>); a single-statement
    /// block stays inline, matching a developer's own shorthand for a trivial body.
    /// </summary>
    string LambdaText(Lambda lambda)
    {
        string parameters = lambda.Parameters is [var single]
            ? CSharpNaming.EscapeIdentifier(single.Name)
            : $"({string.Join(", ", lambda.Parameters.Select(p => CSharpNaming.EscapeIdentifier(p.Name)))})";

        if (lambda.ExpressionBody is { } expr)
            return $"{parameters} => {ExpressionTreeBodyText(lambda, expr)}";

        int statementCount = lambda.Body.Blocks.SelectMany(b => b.Children).Count();
        if (LambdaReturnType(lambda) is { } fallbackReturnType
            && NeedsUnsupportedFallbackReturn(fallbackReturnType, requiresAsyncBodyModifier: false, lambda.Body))
        {
            statementCount++;
        }

        if (NeedsNestedLambdaScope(lambda))
        {
            string bodyText = LambdaBodyTextWithLocalScope(lambda);
            return statementCount > 1
                ? LambdaBlockText(parameters, bodyText)
                : $"{parameters} => {{ {FlattenLambdaBodyText(bodyText)} }}";
        }

        // Building the child statement texts one indent level deeper keeps any
        // nested lambda block expansion (a lambda-in-lambda) aligned to where
        // that statement will actually land once LambdaBlockText re-applies
        // _statementIndent for the outer braces below.
        int enclosingIndent = _statementIndent;
        _statementIndent = enclosingIndent + 1;
        List<string> statements;
        try
        {
            statements = lambda.Body.Blocks
                .SelectMany(b => b.Children)
                .Select(LambdaStatement)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
            if (LambdaReturnType(lambda) is { } returnType
                && NeedsUnsupportedFallbackReturn(returnType, requiresAsyncBodyModifier: false, lambda.Body))
            {
                statements.Add("return default;");
            }
        }
        finally
        {
            _statementIndent = enclosingIndent;
        }

        return statements.Count > 1
            ? LambdaBlockText(parameters, string.Join(Environment.NewLine, statements))
            : $"{parameters} => {{ {string.Join(" ", statements)} }}";
    }

    /// <summary>
    /// Renders a multi-statement lambda block body expanded across lines, the
    /// way every other statement block prints, instead of collapsed onto the
    /// lambda's own line. <paramref name="bodyText"/> is one statement per line,
    /// already indented relative to column 0 (as <see cref="Statement"/> output or
    /// a nested <see cref="PrintBody"/> render is); braces align to <see
    /// cref="_statementIndent"/> — the enclosing statement's own indentation —
    /// rather than wherever the lambda happens to start inside that statement's
    /// expression tree, matching how a developer would have written the block.
    /// </summary>
    string LambdaBlockText(string parameters, string bodyText)
    {
        string pad = new(' ', _statementIndent * 4);
        string innerPad = new(' ', (_statementIndent + 1) * 4);
        var sb = new StringBuilder();
        sb.Append(parameters).Append(" =>").Append(Environment.NewLine);
        sb.Append(pad).Append('{').Append(Environment.NewLine);
        foreach (var line in bodyText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            sb.Append(innerPad).Append(line).Append(Environment.NewLine);
        sb.Append(pad).Append('}');
        return sb.ToString();
    }

    // An expression-tree lambda body compiles (at the consuming site) into an
    // Expression tree under that project's overflow-checking default. The matched
    // graph used the unchecked arithmetic factories, so render the body as if a
    // checked context enclosed it: BinaryText then wraps an overflow-prone
    // add/sub/mul in an explicit unchecked(...), pinning the recompiled node to the
    // unchecked Expression.Add (not AddChecked) regardless of CheckForOverflowUnderflow.
    // Divide/modulo, parameter loads, and constants carry no overflow form, so they
    // stay bare. Ordinary delegate lambdas keep the default context untouched.
    string ExpressionTreeBodyText(Lambda lambda, IrExpression expr)
    {
        if (!lambda.IsExpressionTree)
            return Expression(expr);

        bool enclosingChecked = _checkedContext;
        _checkedContext = true;
        try
        {
            return Expression(expr);
        }
        finally
        {
            _checkedContext = enclosingChecked;
        }
    }

    static TypeRef? LambdaReturnType(Lambda lambda)
        => lambda.DelegateType switch
        {
            { Kind: TypeRefKind.GenericInstance, ElementType: { Namespace: "System", Name: var name }, TypeArguments: { Length: > 0 } args }
                when name.StartsWith("Func`", StringComparison.Ordinal) => args[^1],
            { Kind: TypeRefKind.GenericInstance, ElementType: { Namespace: "System", Name: var name } }
                when name.StartsWith("Action`", StringComparison.Ordinal) => TypeRef.CoreLib("System", "Void"),
            { Kind: TypeRefKind.Definition, Namespace: "System", Name: "Action" } => TypeRef.CoreLib("System", "Void"),
            _ => null,
        };

    static bool NeedsNestedLambdaScope(Lambda lambda)
        => !lambda.Locals.IsEmpty
            || lambda.Body.Descendants.Any(node => node is LoadStackSlot or StoreStackSlot);

    /// <summary>Renders a locals-bearing lambda body through an isolated nested printer, trimmed but not yet flattened.</summary>
    string LambdaBodyTextWithLocalScope(Lambda lambda)
    {
        var body = lambda.Body;
        body.Detach();
        try
        {
            var function = new IrFunction(
                "<lambda>",
                _function.DeclaringType,
                new MethodSignature(LambdaReturnType(lambda) ?? TypeRef.CoreLib("System", "Void"), lambda.Parameters, HasThis: false, GenericParameterCount: 0),
                lambda.Locals,
                body)
            {
                LocalNames = lambda.LocalNames,
                UsesUpdatedMemorySafetyRules = lambda.UsesUpdatedMemorySafetyRules,
                SkipLocalsInit = lambda.SkipLocalsInit,
            };
            return new CSharpPrinter(function, _options, CurrentScopeNames()).PrintBody(function).Trim();
        }
        finally
        {
            body.Detach();
            lambda.ResetBody(body);
        }
    }

    static string FlattenLambdaBodyText(string bodyText)
        => string.Join(" ", bodyText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));

    string? LambdaStatement(IrNode node) => node switch
    {
        Return { Value: { } value } => $"return {Expression(value)};",
        Return => "return;",
        _ => Statement(node),
    };

    /// <summary>The text of one switch-expression arm: its labels (or <c>_</c>) and the value it yields.</summary>
    string SwitchArmText(SwitchExpressionArm arm, TypeRef? target = null, TypeRef? labelEnum = null, TypeRef? primitiveCoercionSourceType = null, bool joinHasExactTypedArm = true)
        => $"{SwitchExpressionLabelText(arm, labelEnum)} => {SwitchArmValueText(arm.Value, target, primitiveCoercionSourceType, joinHasExactTypedArm)}";

    string SwitchExpressionLabelText(SwitchExpressionArm arm, TypeRef? labelEnum)
        => arm.IsDefault
            ? "_"
            : string.Join(" or ", arm.Labels.Select(label => SwitchLabelText(
                new Constant(label, TypeRef.CoreLib("System", "Int32")),
                labelEnum)));

    string SwitchArmValueText(IrExpression value, TypeRef? target, TypeRef? primitiveCoercionSourceType = null, bool joinHasExactTypedArm = true)
        => TryCoerceJoinArm(value, target, primitiveCoercionSourceType, joinHasExactTypedArm) is { } coerced
            ? coerced
            // The bool-arm composition, mirroring ConditionalArm (#2345
            // review, GPT-5.5: the primitive gate admits bool arms via
            // CanSpellBoolToInteger, so the render path must compose them —
            // a bare bool arm at an integer-typed switch join is CS0029).
            : target is { } intTarget && TypeFamilies.IsIntegerLike(intTarget)
                && EffectiveType(value) is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary }
                ? BoolToIntegerText(value, intTarget)
                : Expression(value);

    /// <summary>The single-line form of a switch expression, used when it is nested inside another expression.</summary>
    string SwitchExpressionInline(SwitchExpression node, TypeRef? target = null)
    {
        var labelEnum = SwitchLabelEnumType(node.Value);
        var armValues = node.Arms.Select(arm => arm.Value).ToList();
        var armTarget = EffectiveJoinTarget(target, armValues);
        // Thread the node's merged source type and the constant anchor flag
        // exactly like ConditionalText (#2345 round-2, GPT-5.5: without the
        // source type, a narrower-than-target arm the node-width gate
        // admitted failed the arm-width spell check and rendered bare).
        TypeRef? primitiveCoercionSourceType =
            armTarget is not null
            && EffectiveType(node) is { } nodeType
            && !nodeType.Equals(armTarget)
            && CanRenderPrimitiveJoinForTarget(armTarget, nodeType, armValues)
                ? nodeType
                : null;
        bool joinHasExactTypedArm = armTarget is { } anchorTarget && armValues.Any(value => JoinArmAnchorsTarget(value, anchorTarget));
        return $"{Operand(node.Value)} switch {{ {string.Join(", ", node.Arms.Select(arm => SwitchArmText(arm, armTarget, labelEnum, primitiveCoercionSourceType, joinHasExactTypedArm)))} }}";
    }

    string UnionSwitchExpressionInline(UnionSwitchExpression node, TypeRef? target = null)
    {
        var arms = node.Arms.Select(arm => UnionSwitchArmText(arm, target));
        if (node.NullValue is { } nullValue)
            arms = arms.Prepend($"null => {SwitchArmValueText(nullValue, target)}");
        if (node.DefaultValue is { } defaultValue)
            arms = arms.Append($"_ => {SwitchArmValueText(defaultValue, target)}");
        return $"{UnionSwitchReceiverText(node.Value)} switch {{ {string.Join(", ", arms)} }}";
    }

    string UnionSwitchArmText(UnionSwitchExpressionArm arm, TypeRef? target = null)
        => $"{TypeText(arm.PatternType)}{(arm.LocalIndex is { } index ? $" {LocalName(index)}" : "")}{(arm.Guard is { } guard ? $" when {RenderedCondition(guard).At(Precedence.NullCoalescing)}" : "")} => {SwitchArmValueText(arm.Value, target)}";

    string UnionSwitchReceiverText(IrExpression value)
        => UnionValueReceiverText(value) ?? Operand(value);

    /// <summary>The single-line form of a type / property-pattern switch expression, used when it is nested inside another expression.</summary>
    string PatternSwitchExpressionInline(PatternSwitchExpression node, TypeRef? target = null)
    {
        var arms = node.Arms.Select(arm => PatternSwitchArmText(arm, target));
        if (node.DefaultValue is { } defaultValue)
            arms = arms.Append($"_ => {SwitchArmValueText(defaultValue, target)}");
        return $"{Operand(node.Value)} switch {{ {string.Join(", ", arms)} }}";
    }

    /// <summary>One arm of a <see cref="PatternSwitchExpression"/>: <c>Type[ local]</c> or
    /// <c>Type { Property: Inner inner }</c>, an optional <c>when</c> guard, and the yielded value.</summary>
    string PatternSwitchArmText(PatternSwitchExpressionArm arm, TypeRef? target = null)
    {
        string pattern = arm.Subpattern is { } sub
            ? $"{TypeText(arm.PatternType)}{(arm.LocalIndex is { } outer ? $" {LocalName(outer)}" : "")} {{ {CSharpNaming.EscapeIdentifier(sub.PropertyName)}: {TypeText(sub.PatternType)} {LocalName(sub.LocalIndex)} }}"
            : $"{TypeText(arm.PatternType)}{(arm.LocalIndex is { } index ? $" {LocalName(index)}" : "")}";
        string guard = arm.Guard is { } g ? $" when {RenderedCondition(g).At(Precedence.NullCoalescing)}" : "";
        return $"{pattern}{guard} => {SwitchArmValueText(arm.Value, target)}";
    }

    /// <summary>The single-line form of a tuple relational-pattern switch expression, used when it is nested inside another expression.</summary>
    string TupleSwitchExpressionInline(TupleSwitchExpression node, TypeRef? target = null)
    {
        var componentTypes = TupleSwitchComponentTypes(node);
        return $"{TupleSwitchGoverningValueText(node)} switch {{ {string.Join(", ", node.Arms.Select(arm => TupleSwitchArmText(arm, componentTypes, target)))} }}";
    }

    string TupleSwitchGoverningValueText(TupleSwitchExpression node)
        => $"({string.Join(", ", node.Components.Select(Operand))})";

    /// <summary>The declared type of each governing component, so a subpattern anchor can be spelled in the component's type (e.g. a <c>char</c> literal, not a bare <c>int</c>).</summary>
    static IReadOnlyList<TypeRef?> TupleSwitchComponentTypes(TupleSwitchExpression node)
        => node.Components.Select(component => component.ResultType).ToList();

    /// <summary>The text of one tuple switch arm: its positional pattern (or <c>_</c> for the default) and the value it yields.</summary>
    string TupleSwitchArmText(TupleSwitchExpressionArm arm, IReadOnlyList<TypeRef?> componentTypes, TypeRef? target = null)
        => $"{TupleSwitchArmLabelText(arm, componentTypes)} => {SwitchArmValueText(arm.Value, target)}";

    static string TupleSwitchArmLabelText(TupleSwitchExpressionArm arm, IReadOnlyList<TypeRef?> componentTypes)
    {
        if (arm.IsDefault)
            return "_";
        var constants = arm.Constants;
        return $"({string.Join(", ", arm.Subpatterns.Select((subpattern, i) => PositionalSubpatternText(subpattern, constants[i], componentTypes[i])))})";
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
                sb.Append('{').Append(InterpolatedExpression(node.FormattedValues[part.ExpressionIndex], part.Format?.FormatString is not null));
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

    string InterpolatedExpression(IrExpression value, bool hasFormat)
    {
        // A format clause's ':' competes with the conditional operator's ':',
        // so conditional-precedence fragments wrap when a format is present.
        // Without a format clause, preserve the historical conservative
        // parenthesization until broad interpolation-hole churn has its own A/B.
        var demand = hasFormat ? Precedence.NullCoalescing : Precedence.Primary;
        return RenderedExpression(value).At(demand);
    }

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
        LoadField field => NullConditionalFieldSuffix(field.Field),
        LoadProperty property when property.IndexArguments.Count > 0 => $"[{Arguments(property.IndexArguments)}]",
        LoadProperty property => $".{CSharpNaming.EscapeIdentifier(property.PropertyName)}",
        Call call => NullConditionalCallSuffix(call),
        _ => $".{member.Describe()}",
    };

    static string NullConditionalFieldSuffix(FieldRef field)
    {
        if (field.BackingPropertyName is { } property)
            return $".{CSharpNaming.EscapeIdentifier(property)}";
        if (CSharpNaming.PrimaryConstructorCaptureName(field.Name) is { } capture)
            return $".{CSharpNaming.EscapeIdentifier(capture)}";
        return $".{CSharpNaming.SafeIdentifier(field.Name)}";
    }

    string NullConditionalCallSuffix(Call call)
    {
        string typeArguments = call.Callee.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", call.Callee.TypeArguments.Select(TypeText))}>";
        return $".{CSharpNaming.SourceMethodName(call.Callee.Name)}{typeArguments}({Arguments(call.Arguments.Skip(1), call.Callee.ParameterTypes, call.Callee.ParameterRefKinds)})";
    }
}
