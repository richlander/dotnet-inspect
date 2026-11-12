using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class UnionBooleanReturnTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void FoldedReturn_RaisesOnlyAnOwnedUnionTest(bool extraUse, bool unrecognizedArm, bool nonUnion)
    {
        var boolean = TypeRef.CoreLib("System", "Boolean");
        var @object = TypeRef.CoreLib("System", "Object");
        var union = TypeRef.Definition("fixture", "UnionFixtures", "Pet");
        var cat = TypeRef.Definition("fixture", "UnionFixtures", "Cat");
        var dog = TypeRef.Definition("fixture", "UnionFixtures", "Dog");
        var getter = new MethodRef(union, "get_Value", @object, [], HasThis: true);
        var store = new StoreLocal(0, @object,
            new LoadProperty(getter, new LoadArgument(0, "pet", union), []));
        var condition = new IsInstance(cat, new LoadLocal(0, @object));
        IrExpression otherwise = unrecognizedArm
            ? new LoadArgument(1, "other", boolean)
            : new LogicalNot(new Comparison(ComparisonKind.GreaterThan, isUnsigned: true,
                new IsInstance(dog, new LoadLocal(0, @object)), new Constant(null, @object)));
        var ret = new Return(new LogicalNot(new Conditional(condition, new Constant(false, boolean), otherwise)));
        var block = new Block(0);
        block.Add(store);
        block.Add(ret);
        var container = new BlockContainer();
        container.Add(block);
        if (extraUse)
        {
            var sibling = new Block(1);
            sibling.Add(new Return(new LogicalNot(new LoadLocal(0, @object))));
            container.Add(sibling);
        }
        var function = new IrFunction("M", union,
            new MethodSignature(boolean, [new Parameter("pet", union), new Parameter("other", boolean)],
                HasThis: false, GenericParameterCount: 0), [@object], container)
        {
            UnionTypes = nonUnion ? ImmutableHashSet<TypeRef>.Empty : ImmutableHashSet.Create(union),
        };

        new UnionSwitchExpressionPass().Run(function, PassContext.None);
        function.CheckInvariant();

        if (extraUse || unrecognizedArm || nonUnion)
        {
            Assert.Same(store, block.Children[0]);
            Assert.Same(ret, block.Children[1]);
        }
        else
        {
            var raised = Assert.IsType<Return>(Assert.Single(block.Children));
            var disjunction = Assert.IsType<LogicalBinary>(raised.Value);
            Assert.Equal(LogicalKind.Or, disjunction.Kind);
            Assert.All(disjunction.Children, child =>
                Assert.IsType<LoadProperty>(Assert.IsType<IsInstance>(child).Operand));
        }
    }
}
