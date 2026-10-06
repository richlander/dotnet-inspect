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
/// Pattern text: property, list, and positional patterns.
/// </summary>
public sealed partial class CSharpPrinter
{
    string? TryPropertyPatternText(LogicalBinary logical)
        => TryPropertyPatternText(logical, negated: false);

    string? TryPropertyPatternText(LogicalBinary logical, bool negated)
    {
        if (logical.Kind != LogicalKind.And)
        {
            return null;
        }

        var conjuncts = new List<IrExpression>();
        CollectConjuncts(logical, conjuncts);
        if (conjuncts is not [IsPattern pattern, .. var rest])
            return null;

        var subpatterns = new List<(string PropertyName, string Subpattern)>();
        foreach (var conjunct in rest)
        {
            if (!TryPropertySubpattern(conjunct, pattern.LocalIndex, allowStringEquality: !pattern.PreserveLocalInPropertyPattern, out var propertyName, out var subpattern))
                return null;
            subpatterns.Add((propertyName, subpattern));
        }
        if (subpatterns.Count == 0
            || subpatterns.Select(p => p.PropertyName).Distinct(StringComparer.Ordinal).Count() != subpatterns.Count)
        {
            return null;
        }

        if (negated && pattern.PreserveLocalInPropertyPattern)
            return null;

        string designation = pattern.PreserveLocalInPropertyPattern ? $" {LocalName(pattern.LocalIndex)}" : "";
        string not = negated ? " not" : "";
        return $"{TypeTestValueText(pattern.Value)} is{not} {TypeText(pattern.Type)} {{ {string.Join(", ", subpatterns.Select(p => $"{CSharpNaming.ContainedIdentifier(p.PropertyName)}: {p.Subpattern}"))} }}{designation}";
    }

    static void CollectConjuncts(IrExpression expression, List<IrExpression> conjuncts)
    {
        if (expression is LogicalBinary { Kind: LogicalKind.And } logical)
        {
            CollectConjuncts(logical.Left, conjuncts);
            CollectConjuncts(logical.Right, conjuncts);
            return;
        }

        conjuncts.Add(expression);
    }

    /// <summary>
    /// Folds a single comparison of the pattern local's property against a
    /// constant into a property sub-pattern. Equality becomes the bare constant
    /// (<c>{ P: 5 }</c>); the four relational kinds become a relational pattern
    /// (<c>{ P: &gt; 5 }</c>). Floats are excluded: their unordered (NaN)
    /// comparison semantics are not reproduced by a relational pattern.
    /// </summary>
    static bool TryPropertySubpattern(IrExpression expression, int patternLocal, bool allowStringEquality, out string propertyName, out string subpattern)
    {
        propertyName = "";
        subpattern = "";

        if (expression is not Comparison comparison)
        {
            if (expression is LogicalNot { Operand: Call negatedCall }
                && allowStringEquality
                && MemberIdentity.IsStringEquality(negatedCall)
                && negatedCall.Arguments is [var negatedLeft, var negatedRight]
                && TryPropertyConstant(negatedLeft, negatedRight, patternLocal, out propertyName, out var negatedConstant))
            {
                subpattern = $"not {ConstantText(negatedConstant)}";
                return true;
            }
            if (expression is Call call
                && allowStringEquality
                && MemberIdentity.IsStringEquality(call)
                && call.Arguments is [var left, var right]
                && TryPropertyConstant(left, right, patternLocal, out propertyName, out var stringConstant))
            {
                subpattern = ConstantText(stringConstant);
                return true;
            }
            if (expression is Call inequalityCall
                && allowStringEquality
                && MemberIdentity.IsStringInequality(inequalityCall)
                && inequalityCall.Arguments is [var inequalityLeft, var inequalityRight]
                && TryPropertyConstant(inequalityLeft, inequalityRight, patternLocal, out propertyName, out var notConstant))
            {
                subpattern = $"not {ConstantText(notConstant)}";
                return true;
            }
            return false;
        }

        // Orient the comparison so the property is the left operand; mirror the
        // kind when the constant leads (`5 < t.P` reads as `t.P > 5`).
        LoadProperty property;
        Constant constant;
        ComparisonKind kind;
        if (comparison.Left is LoadProperty leftProperty && IsPatternLocalProperty(leftProperty, patternLocal) && comparison.Right is Constant rightConstant)
        {
            property = leftProperty;
            constant = rightConstant;
            kind = comparison.Kind;
        }
        else if (comparison.Right is LoadProperty rightProperty && IsPatternLocalProperty(rightProperty, patternLocal) && comparison.Left is Constant leftConstant)
        {
            property = rightProperty;
            constant = leftConstant;
            kind = Conditions.Mirror(comparison.Kind);
        }
        else
        {
            return false;
        }

        propertyName = property.PropertyName;

        if (kind == ComparisonKind.Equal)
        {
            subpattern = ConstantText(constant);
            return true;
        }

        // Relational sub-patterns require an ordered comparison; floats carry
        // unordered/NaN semantics, and != has no relational pattern form.
        string? relationalOperator = kind switch
        {
            ComparisonKind.LessThan => "<",
            ComparisonKind.LessThanOrEqual => "<=",
            ComparisonKind.GreaterThan => ">",
            ComparisonKind.GreaterThanOrEqual => ">=",
            _ => null,
        };
        if (relationalOperator is null || comparison.IsUnsigned || IsFloatComparison(comparison.Left, comparison.Right))
            return false;

        subpattern = $"{relationalOperator} {ConstantText(constant)}";
        return true;
    }

    static bool TryPropertyConstant(
        IrExpression left,
        IrExpression right,
        int patternLocal,
        out string propertyName,
        out Constant constant)
    {
        if (left is LoadProperty leftProperty && IsPatternLocalProperty(leftProperty, patternLocal) && right is Constant rightConstant)
        {
            propertyName = leftProperty.PropertyName;
            constant = rightConstant;
            return true;
        }

        if (right is LoadProperty rightProperty && IsPatternLocalProperty(rightProperty, patternLocal) && left is Constant leftConstant)
        {
            propertyName = rightProperty.PropertyName;
            constant = leftConstant;
            return true;
        }

        propertyName = "";
        constant = null!;
        return false;
    }

    static bool IsPatternLocalProperty(LoadProperty property, int patternLocal)
        => property.HasInstance
            && property.Instance is LoadLocal local
            && local.Index == patternLocal
            && property.IndexArguments.Count == 0;

    string ListPatternAlternativesText(SingleElementListPattern pattern)
        => string.Join(" or ", pattern.Alternatives.Select(ConstantText));

    string PositionalPatternText(PositionalPattern pattern)
    {
        var constants = pattern.Constants;
        return $"{Operand(pattern.Value)} is ({string.Join(", ", pattern.Subpatterns.Select((subpattern, i) => PositionalSubpatternText(subpattern, constants[i], targetType: null)))})";
    }

    static string PositionalSubpatternText(PositionalPatternSubpattern subpattern, Constant constant, TypeRef? targetType)
    {
        // A char component's anchor is an in-range Int32 constant in IL
        // (ConstantFits admits it), but a relational/constant pattern against a
        // char input rejects a bare int literal (CS0266 — the implicit
        // constant-expression conversion does not apply in patterns). Spell it
        // as the char literal the component's type demands.
        string constantText = targetType is { } type && IsCoreChar(type) && TryCharConstantText(constant, out var charText)
            ? charText
            : ConstantText(constant);
        return subpattern.Kind switch
        {
            ComparisonKind.Equal => constantText,
            ComparisonKind.NotEqual => $"not {constantText}",
            _ => $"{ComparisonOperator(subpattern.Kind)} {constantText}",
        };
    }
}
