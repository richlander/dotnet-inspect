using System;

// A lookalike System.Linq.Expressions.Expression factory family declared in an
// assembly literally named "System.Linq.Expressions" but unsigned, so it carries
// no framework public-key-token. The real framework type is identical by
// namespace/name/kind; only the token distinguishes them. ExpressionTreeSpoofer
// below builds the exact canonical inline factory graph the raise recovers, so if
// the raise trusted the simple name it would rewrite these lookalike calls to real
// expression-tree lambda semantics. The token-verified DeclaringTypeIsTrustedPlatform
// gate must decline, keeping the honest factory calls.
namespace System.Linq.Expressions
{
    public class Expression
    {
        public static ParameterExpression Parameter(Type type, string name) => new ParameterExpression();

        public static Expression Add(Expression left, Expression right) => new Expression();

        public static Expression Constant(object value, Type type) => new Expression();

        public static Expression<TDelegate> Lambda<TDelegate>(Expression body, params ParameterExpression[] parameters)
            => new Expression<TDelegate>();
    }

    public class LambdaExpression : Expression
    {
    }

    public class Expression<TDelegate> : LambdaExpression
    {
    }

    public class ParameterExpression : Expression
    {
    }
}

namespace ExpressionTreeSpoof
{
    using System.Linq.Expressions;

    public static class ExpressionTreeSpoofer
    {
        // The canonical inline factory graph (parameter, unchecked Add over a
        // boxed int constant, single-parameter Lambda), returned as the generic
        // Expression<Func<int, int>> so it clears the delegate/arity/return-sink
        // gates and would recover — except the Expression factory here is the
        // unsigned lookalike, not the real framework type.
        public static Expression<Func<int, int>> Spoofed()
        {
            var p = Expression.Parameter(typeof(int), "x");
            return Expression.Lambda<Func<int, int>>(
                Expression.Add(p, Expression.Constant(1, typeof(int))),
                p);
        }
    }
}
