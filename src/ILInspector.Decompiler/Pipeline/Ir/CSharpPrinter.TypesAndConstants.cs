using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CSharpText;
using ILInspector.CSharp;
using ILInspector.ControlFlow;
using ILInspector.Metadata;
using Inspector.Text;
using static ILInspector.Decompiler.Pipeline.PointerArithmetic;
using static ILInspector.Decompiler.Pipeline.PlaceIdentity;
using DecisionKey = (string RuleId, string Category, string Subject, string Detail, string OldValue, string NewValue, string DedupDiscriminator);

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Conversions and casts, enum switch labels, constant literals, and type text.
/// </summary>
public sealed partial class CSharpPrinter
{
    string ConvertText(Convert convert)
    {
        // A checked conversion already inside a checked context drops its own
        // wrapper (the enclosing checked covers it); only the outermost one wraps.
        bool enclosingChecked = _checkedContext;
        if (convert.IsChecked)
        {
            _checkedContext = true;
            try
            {
                return ConvertBody(convert, wrap: !enclosingChecked, uncheckedOverflow: false);
            }
            finally
            {
                _checkedContext = enclosingChecked;
            }
        }
        // The symmetric insert: a plain (non-overflow) narrowing/sign-changing
        // conversion spelled inside a checked region recompiles to a `conv.ovf.*`
        // it never had — `checked(a + unchecked((short)b))` would range-check the
        // inner cast. Wrap it in `unchecked(...)` and clear the context so its
        // operand recompiles plain (a widening conversion never flips, so it is
        // left bare to avoid pointless wrappers).
        bool uncheckedOverflow = enclosingChecked && IsCheckedSensitiveConversion(convert);
        if (uncheckedOverflow)
            _checkedContext = false;
        try
        {
            return ConvertBody(convert, wrap: false, uncheckedOverflow: uncheckedOverflow);
        }
        finally
        {
            _checkedContext = enclosingChecked;
        }
    }

    /// <summary>
    /// True when recompiling the explicit cast <c>(target)operand</c> inside a
    /// lexical <c>checked</c> region would emit a <c>conv.ovf.*</c> opcode: a
    /// narrowing or sign-changing integer conversion, or any float→integer. A plain
    /// <see cref="Convert"/> matching this must be wrapped in <c>unchecked(...)</c>
    /// when spelled inside a checked context, or it silently acquires overflow
    /// checking it never had. An implicit widening (int→long, byte→int, …) never
    /// flips and is left bare. An unknown or non-numeric source is treated as
    /// sensitive — wrapping is always behavior-preserving, so it is the safe
    /// default.
    /// </summary>
    static bool IsCheckedSensitiveConversion(Convert convert)
    {
        if (!TypeFamilies.IsIntegerLike(convert.Target))
            return false;   // float/non-integer targets have no conv.ovf form
        var source = convert.Operand.ResultType;
        if (source is null || TypeFamilies.IsFloat(source) || !TypeFamilies.IsIntegerLike(source))
            return true;    // float→integer always checks; unknown/pointer source: be safe
        if (source.Equals(convert.Target))
            return false;   // identity: no conv emitted
        return !CSharpConversionRules.IsImplicitIntegerWidening(source, convert.Target);
    }

    string ConvertBody(Convert convert, bool wrap, bool uncheckedOverflow)
    {
        if (IsUnboxPointerConversion(convert) && convert.Operand is Unbox unbox)
        {
            string pointer = $"System.Runtime.CompilerServices.Unsafe.AsPointer(ref System.Runtime.CompilerServices.Unsafe.Unbox<{TypeText(unbox.Type)}>({Operand(unbox.Operand)}))";
            string pointerCast = $"({TypeText(convert.Target)}){pointer}";
            if (wrap)
                return $"checked({pointerCast})";
            return uncheckedOverflow ? $"unchecked({pointerCast})" : pointerCast;
        }
        // An address-of node (ldloca/ldarga/ldflda/ldelema) converted to a
        // pointer or native integer (conv.u/conv.i) is C#'s address-of operator,
        // not a `ref` place: `(nuint)(ref x)` is CS1525 — the faithful unsafe
        // spelling is `(nuint)(&x)`. The bare `ref` form is only valid in a
        // ref-return/ref-argument/ref-local position, never as a cast operand.
        if (convert.Operand is LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress or FixedBufferElementAddress or LoadElementAddress)
        {
            string addressCast = $"({TypeText(convert.Target)})(&{Deref(convert.Operand)})";
            if (wrap)
                return $"checked({addressCast})";
            return uncheckedOverflow ? $"unchecked({addressCast})" : addressCast;
        }
        // Converting an out-of-range integer constant (conv.u8 of ldc.i4.m1 for
        // ulong.MaxValue) is CS0221 as a plain cast; reinterpret its bits with
        // unchecked, matching the constant handling at value boundaries. The
        // unchecked already covers any enclosing checked context.
        if (!convert.IsChecked && convert.Operand is Constant { Value: int or long } c
            && TypeFamilies.IsNumericPrimitive(convert.Target))
        {
            long literal = c.Value is int i ? i : (long)c.Value!;
            if (!CSharpConversionRules.ConstantFits(literal, convert.Target))
            {
                // A widening conversion to an unsigned target ZERO-extends the source
                // (`conv.u8` of `ldc.i4.m1` is 0x00000000FFFFFFFF = uint.MaxValue),
                // where a bare `(ulong)(-1)` sign-extends to ulong.MaxValue — a silent
                // wrong value. Reinterpret the source's bits through its unsigned
                // sibling so the value is faithful and the cast round-trips to the same
                // `conv` opcode (#2101). `conv.i8` (signed target) keeps sign-extension.
                if (literal < 0
                    && TypeFamilies.ZeroExtendingSource(convert.Operand.ResultType, convert.Target) is { } zeroExtendSource)
                    return $"unchecked(({TypeText(convert.Target)})({TypeText(zeroExtendSource)})({Expression(convert.Operand)}))";
                return $"unchecked(({TypeText(convert.Target)})({Expression(convert.Operand)}))";
            }
        }
        // conv.r.un and conv.ovf.*.un interpret the SOURCE as unsigned —
        // a signed operand needs its unsigned cast or the value is wrong.
        string operand = convert.IsUnsigned ? UnsignedOperand(convert.Operand, checkedSafe: !convert.IsChecked) : Operand(convert.Operand);
        // A widening conversion to an unsigned target (`conv.u8`) ZERO-extends the
        // 32-bit stack value, but a bare `(ulong)x` sign-extends a SIGNED operand —
        // a silent wrong value for a negative x. C# elides the sibling cast, so
        // `(ulong)(uint)i` compiles to `ldarg; conv.u8` and the operand is a bare
        // signed `int`. Reinterpret through the unsigned sibling when the operand
        // RENDERS signed. The discriminator is EffectiveType, not the ECMA stack
        // ResultType: an already-unsigned expression such as `(uint)a + b` renders
        // unsigned (its bare `(ulong)` already zero-extends) and must not be
        // re-cast — gating on the rendered type keeps this to genuine corrections.
        // Constants take the value-aware branch above; `.un` operands are already
        // unsigned (#2336, the non-constant sibling of #2101).
        // Only the UNCHECKED widening conv.u8/conv.u zero-extends silently. A
        // checked conv.ovf.u8 of a signed operand throws for a negative value, so
        // the bare `checked((ulong)i)` already matches it — inserting a `(uint)`
        // there would recompile to conv.ovf.u4;conv.u8 (wrong opcodes).
        if (!convert.IsUnsigned && !convert.IsChecked && convert.Operand is not Constant
            && TypeFamilies.WideningZeroExtendSibling(EffectiveType(convert.Operand), convert.Target) is { } zeroExtendWiden)
            operand = $"({TypeText(zeroExtendWiden)}){operand}";
        string targetText = TypeText(convert.Target);
        operand = CastOperand(operand, targetText);
        string cast = $"({targetText}){operand}";
        if (wrap)
            return $"checked({cast})";
        return uncheckedOverflow ? $"unchecked({cast})" : cast;
    }

    static string CastOperand(string operand, string targetText)
        => NeedsCastOperandParentheses(operand, targetText) ? $"({operand})" : operand;

    // A cast whose operand begins with a unary `-`/`+` is parsed as binary
    // subtraction/addition (CS0075) unless the target spelling is a predefined
    // keyword type the parser treats as cast-disambiguating. `nint`/`nuint`
    // are contextual keywords and named types are not, so `(nint)-1` misparses
    // — wrap the operand: `(nint)(-1)`. The parens are opcode-identical.
    static bool NeedsCastOperandParentheses(string operand, string targetText)
        => operand.Length > 0
            && operand[0] is '-' or '+'
            && !s_castDisambiguatingKeywords.Contains(targetText);

    // The predefined-type keyword spellings the C# parser treats as
    // cast-disambiguating: `(int)-1` is a cast, but `(nint)-1` (a contextual
    // keyword) or `(MyEnum)-1` (a named type) parses as subtraction (CS0075).
    static readonly HashSet<string> s_castDisambiguatingKeywords = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "char", "short", "ushort", "int", "uint",
        "long", "ulong", "float", "double", "decimal", "string", "object", "void",
    };

    /// <summary>
    /// A retyped enum constant renders <c>EnumType.Member</c> when its value
    /// names exactly one eligible member of the resolved enum.
    /// </summary>
    string? EnumMemberName(Constant constant)
        => constant.Value is int or long
            && _function.EnumMembers.TryGetValue(NamedDefinition(constant.Type), out var members)
            && members.TryGetValue(constant.Value is int i ? i : (long)constant.Value!, out var name)
            && CSharpNaming.IsEscapableIdentifier(name)
            ? $"{TypeQualifierText(constant.Type)}.{CSharpNaming.ContainedIdentifier(name)}"
            : null;

    /// <summary>
    /// The enum type a <c>switch</c> governing expression carries, or null when it
    /// is not an enum. Mirrors <see cref="TypedConstantsPass"/>'s operand typing
    /// (an <c>ldind</c> through a <c>ref EnumType</c> yields a load typed by the
    /// opcode width, so see through it to the pointee enum) and the cross-assembly
    /// enum reasoning in the numeric cast path: a framework enum like
    /// <c>DateTimeKind</c> resolves to <see cref="TypeShape.Unknown"/> rather than
    /// <see cref="TypeShape.Enum"/> because shape classification only sees the
    /// inspected assembly's own types. Type-safe IL only switches on an integral,
    /// char, or enum, so a non-primitive named governing type is an enum.
    /// </summary>
    TypeRef? SwitchLabelEnumType(IrExpression value)
        => SwitchTypeFacts.EnumType(_function, value);

    IReadOnlyList<Constant> SwitchLabelsForRendering(SwitchSection section, TypeRef? enumType)
    {
        if (_options.EnumCaseLabelOrder != EnumCaseLabelOrder.Alphabetical
            || enumType is null
            || section.Labels.Length < 2
            || !_function.EnumMembers.TryGetValue(NamedDefinition(enumType), out var members))
        {
            return section.Labels;
        }

        var named = new List<(Constant Label, string Name)>(section.Labels.Length);
        foreach (var label in section.Labels)
        {
            if (label.Value is not (int or long))
                return section.Labels;
            long value = label.Value is int i ? i : (long)label.Value;
            if (!members.TryGetValue(value, out var name))
                return section.Labels;
            named.Add((label, name));
        }

        var ordered = named.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        if (named.Select(item => item.Label).SequenceEqual(ordered.Select(item => item.Label)))
            return section.Labels;

        AddDecision(
            "enum-case-label-order",
            DecompilerDecisionCategories.Taste,
            _function.Name,
            $"Sorted {named.Count} named enum labels sharing one switch body by ordinal member name.",
            oldValue: "value",
            newValue: "alphabetical",
            dedupDiscriminator: string.Join("\0", named.Select(item => item.Name)));
        return ordered.Select(item => item.Label).ToArray();
    }

    /// <summary>
    /// Renders a <c>switch</c> case label. When the governing expression is an
    /// enum, a bare integer label is CS0266 (only the literal <c>0</c> converts
    /// implicitly), so the label is spelled by member name when resolved or an
    /// explicit enum cast otherwise — matching how enum constants render
    /// elsewhere. A negative value is parenthesized after the cast (CS0075).
    /// </summary>
    string SwitchLabelText(Constant label, TypeRef? enumType)
    {
        if (enumType is null || label.Value is not (int or long))
            return ConstantText(label);
        return EnumConstantText(label, enumType);
    }

    static string ConstantText(Constant constant) => constant.Value switch
    {
        null => "null",
        string s => StringText(s),
        bool b => b ? "true" : "false",
        char c => CharText(c),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        float f => SingleText(f),
        double d => DoubleText(d),
        _ => constant.Value.ToString() ?? "?",
    };

    static string SingleText(float value)
    {
        if (float.IsNaN(value))
            return "float.NaN";
        if (float.IsPositiveInfinity(value))
            return "float.PositiveInfinity";
        if (float.IsNegativeInfinity(value))
            return "float.NegativeInfinity";
        return $"{value.ToString("R", CultureInfo.InvariantCulture)}f";
    }

    static string DoubleText(double value)
    {
        if (double.IsNaN(value))
            return "double.NaN";
        if (double.IsPositiveInfinity(value))
            return "double.PositiveInfinity";
        if (double.IsNegativeInfinity(value))
            return "double.NegativeInfinity";
        return $"{value.ToString("R", CultureInfo.InvariantCulture)}d";
    }

    static string CharText(char c) => $"'{EscapeChar(c, inString: false)}'";

    /// <summary>A C# string literal with every char that needs escaping escaped — control chars, quotes, backslashes — so the output always compiles.</summary>
    static string StringText(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (char c in value)
            sb.Append(EscapeChar(c, inString: true));
        return sb.Append('"').ToString();
    }

    /// <summary>
    /// The single home for C# character escaping, shared by char and string
    /// literals. The active delimiter is escaped (<c>"</c> in a string,
    /// <c>'</c> in a char); every control character gets a recognized escape
    /// or a <c>\u</c> sequence so a raw newline or tab never reaches the output.
    /// </summary>
    static string EscapeChar(char c, bool inString) => c switch
    {
        '\\' => "\\\\",
        '"' when inString => "\\\"",
        '\'' when !inString => "\\'",
        '\0' => "\\0",
        '\a' => "\\a",
        '\b' => "\\b",
        '\f' => "\\f",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\v' => "\\v",
        // U+2028 LINE SEPARATOR and U+2029 PARAGRAPH SEPARATOR are C# line
        // terminators (ECMA-334 §6.3.1) but are not `char.IsControl`, so a raw
        // emit splits the literal across source lines (CS1010 "Newline in
        // constant"). Escape them explicitly.
        '\u2028' => "\\u2028",
        '\u2029' => "\\u2029",
        // Bidi overrides (U+202A-U+202E, U+2066-U+2069, U+200E/U+200F) are
        // Unicode category Cf, so char.IsControl is false for them. Emitted raw
        // into a literal they survive into the rendered code fence and reorder
        // the displayed source without changing what it compiles to -- Trojan
        // Source (issue #3319). The \u escape is the same C# string.
        _ when CSharpIdentifier.RequiresLiteralEscape(c) => $"\\u{(int)c:x4}",
        // A lone surrogate code unit has no valid UTF-8/UTF-16 text form: emitted
        // raw it cannot survive an encode (writers substitute U+FFFD, corrupting
        // the literal \u2014 char.IsHighSurrogate's own bounds rendered as two
        // replacement characters). Always the \u escape.
        _ when char.IsSurrogate(c) => $"\\u{(int)c:x4}",
        _ => c.ToString(),
    };

    string TypeText(TypeRef type)
    {
        // Scope is the method's declaring type: a nested type of a generic is
        // qualified through its declaring chain (ImmutableArray<string>.Builder)
        // unless the reference is made from inside that enclosing type, where the
        // innermost name is in scope (Enumerator inside List<T>.GetEnumerator).
        string text = TypeTextCore(type);
        RecordFrameworkTypeImportDecision(type, text);
        return text;
    }

    string TypeQualifierText(TypeRef type)
    {
        string rendered = TypeTextCore(type);

        // A type parameter has no global qualification; escape the contextual keyword.
        if (_function.HasAccessorStorageBinding
            && type is { Kind: TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter,
                GenericParameterName: "field" })
            rendered = "@field";
        else if (FirstTypeQualifierSegment(rendered) is { } segment && IsStaticCallNameShadowed(segment))
            rendered = FullyQualifiedTypeText(type);

        RecordFrameworkTypeImportDecision(type, rendered);
        return rendered;
    }

    string TypeTextCore(TypeRef type)
        => _fullyQualifyTypeNames
            ? type.ToFullyQualifiedDisplayString(
                _function.DeclaringType)
            : type.ToDisplayString(_function.DeclaringType);

    static string? FirstTypeQualifierSegment(string rendered)
    {
        if (rendered.Length == 0 || rendered.StartsWith("global::", StringComparison.Ordinal))
            return null;
        int i = rendered[0] == '@' ? 1 : 0;
        if (i >= rendered.Length || !(char.IsLetter(rendered[i]) || rendered[i] == '_'))
            return null;
        while (++i < rendered.Length && (char.IsLetterOrDigit(rendered[i]) || rendered[i] == '_'))
        {
        }
        return rendered[..i];
    }

    static string FullyQualifiedTypeText(TypeRef type)
        => type.ToFullyQualifiedDisplayString(
            TypeRef.Definition(
                "__dotnet_inspect",
                "__",
                "__"));

    static string EscapeNamespace(string ns)
        => string.Join(".", ns.Split('.').Select(CSharpNaming.SafeIdentifier));

    void RecordFrameworkTypeImportDecision(TypeRef type, string rendered)
    {
        foreach (var nested in DescendantTypes(type))
            RecordFrameworkTypeImportDecisionCore(nested, rendered);
    }

    void RecordFrameworkTypeImportDecisionCore(TypeRef type, string rendered)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        if (definition is not { Kind: TypeRefKind.Definition, Namespace.Length: > 0 })
            return;
        if (!IsFrameworkNamespace(definition.Namespace))
            return;
        if (HasGenericEnclosingSegment(definition))
        {
            RecordNestedGenericEnclosingImportDecisions(definition, rendered);
            return;
        }

        string fullName = FrameworkMetadataName(definition);
        string simpleName = TypeNamePath(definition);
        if (rendered.Contains(definition.Namespace + ".", StringComparison.Ordinal))
            return;

        AddDecision(
            "type-name.framework-imported",
            "taste",
            fullName,
            $"Rendered framework type '{fullName}' as imported/simple name '{simpleName}'.",
            oldValue: FrameworkSourceName(definition),
            newValue: simpleName);
    }

    void RecordNestedGenericEnclosingImportDecisions(TypeRef definition, string rendered)
    {
        IReadOnlyList<string> segments = definition.MetadataNameSegments();
        var sourceSegments = new List<string>(segments.Count);
        for (int i = 0; i < segments.Count - 1; i++)
        {
            sourceSegments.Add(CSharpNaming.TypeNameSegment(segments[i]));
            if (GenericArity(segments[i]) == 0)
                continue;

            string oldValue = $"{definition.Namespace}.{string.Join(".", sourceSegments)}";
            string newValue = string.Join(".", sourceSegments);
            if (rendered.Contains(definition.Namespace + ".", StringComparison.Ordinal))
                continue;

            AddDecision(
                "type-name.framework-imported",
                "taste",
                $"{definition.Namespace}.{string.Join("+", segments.Take(i + 1))}",
                $"Rendered framework type '{oldValue}' as imported/simple name '{newValue}'.",
                oldValue: oldValue,
                newValue: newValue);
        }
    }

    static IEnumerable<TypeRef> DescendantTypes(TypeRef type)
    {
        yield return type;
        if (type.ElementType is { } element)
        {
            foreach (var descendant in DescendantTypes(element))
                yield return descendant;
        }
        foreach (var argument in type.TypeArguments)
        {
            foreach (var descendant in DescendantTypes(argument))
                yield return descendant;
        }
    }

    static string FrameworkMetadataName(TypeRef type)
        => type.Namespace.Length == 0 ? CSharpNaming.TypeNameSegment(type.Name) : $"{type.Namespace}.{type.Name}";

    static string FrameworkSourceName(TypeRef type)
        => type.Namespace.Length == 0
            ? TypeNamePath(type)
            : $"{type.Namespace}.{TypeNamePath(type)}";

    static string TypeNamePath(TypeRef type)
        => string.Join(
            ".",
            type.MetadataNameSegments().Select(CSharpNaming.TypeNameSegment));

    static bool HasGenericEnclosingSegment(TypeRef type)
    {
        IReadOnlyList<string> segments = type.MetadataNameSegments();
        return segments.Count > 1
            && segments.Take(segments.Count - 1)
                .Any(segment => GenericArity(segment) > 0);
    }

    static int GenericArity(string metadataName)
        => MetadataNameArity.OfSegment(metadataName);

    static bool IsFrameworkNamespace(string ns)
        => ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);

    /// <summary>
    /// Whether a declared type is a C# built-in (predefined keyword) type — the set
    /// the built-in-types <c>var</c> bucket owns. Drawn from the shared keyword table
    /// (<see cref="PrimitiveTypeNames"/>, the same one <see cref="TypeText"/> renders
    /// keywords from) so both <c>var</c> buckets partition declaration sites
    /// identically. <c>void</c> is excluded — it is never a local's type.
    /// </summary>
    static bool IsBuiltInType(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && PrimitiveTypeNames.TryToKeywordForSystemType(type.Name, out var keyword)
            && keyword != "void";

    string TypeOfTypeText(TypeRef type)
        => type.Kind == TypeRefKind.Definition && OpenGenericArity(type) is { } arity
            ? $"{TypeText(type)}<{new string(',', arity - 1)}>"
            : TypeText(type);

    static int? OpenGenericArity(TypeRef type)
    {
        var name = type.Name;
        var nested = name.LastIndexOf('+');
        var innermost = nested < 0 ? name : name[(nested + 1)..];
        // Only a canonical `N declares arity: int.TryParse would take a signed or
        // padded count, spelling `Widget<>` for a type that is not generic.
        int arity = MetadataNameArity.OfSegment(innermost);
        return arity > 0 ? arity : null;
    }
}
