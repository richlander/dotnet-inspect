using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// SynthesizeInlineCatchVariables binds a catch variable for a handler that uses the
// caught exception inline (no entry store), but only when it is used exactly once —
// the single shape Release's store-elision produces. A handler with two or more
// inline uses can only come from hand-written or obfuscated IL (a duped exception
// on the stack); binding a variable there would re-introduce a store the original
// lacked, so EhStructuring must leave the method unconsumed instead of mis-raising
// it. The compiler never emits that shape, so it is constructed directly as IR.
public class MultiUseCatchExceptionTests
{
    static readonly TypeRef ExceptionType = TypeRef.CoreLib("System", "Exception");
    static readonly TypeRef VoidType = TypeRef.CoreLib("System", "Void");
    static readonly MethodRef Sink = new(TypeRef.CoreLib("System", "Sample"), "Sink", VoidType, [ExceptionType], HasThis: false);
    static readonly MethodRef Work = new(TypeRef.CoreLib("System", "Sample"), "Work", VoidType, [], HasThis: false);

    // try { Work(); } catch (Exception) { Sink(<caught>); … }  — `inlineUses` copies
    // of the caught-exception value use, with no entry store.
    static IrFunction BuildInlineCatch(int inlineUses)
    {
        var tryBlock = new Block(0x00);
        tryBlock.Add(new ExpressionStatement(new Call(Work, isVirtual: false, [])));
        tryBlock.Add(new Leave(0x20));

        var handlerBlock = new Block(0x10);
        for (int i = 0; i < inlineUses; i++)
            handlerBlock.Add(new ExpressionStatement(new Call(Sink, isVirtual: false, [new CaughtException(ExceptionType)])));
        handlerBlock.Add(new Leave(0x20));

        var exit = new Block(0x20);
        exit.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(tryBlock);
        body.Add(handlerBlock);
        body.Add(exit);

        var signature = new MethodSignature(VoidType, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("System", "Sample"), signature, [], body)
        {
            Regions = [new HandlerRegion(HandlerKind.Catch, 0x00, 0x10, 0x10, 0x10, 0, ExceptionType)],
        };
    }

    [Fact]
    public void SingleInlineUse_StructuresWithSynthesizedVariable()
    {
        var function = BuildInlineCatch(inlineUses: 1);

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.Empty(function.Regions);                                  // region consumed
        var clause = Assert.Single(function.Descendants.OfType<TryCatch>()).Clauses.Single();
        Assert.NotNull(clause.VariableIndex);                            // a variable was bound
        Assert.Empty(function.Descendants.OfType<CaughtException>());    // no bare exception leaks
        function.CheckInvariant();
    }

    [Fact]
    public void TwoInlineUses_StayFlatRatherThanReintroduceAStore()
    {
        var function = BuildInlineCatch(inlineUses: 2);

        new EhStructuringPass().Run(function, PassContext.None);

        Assert.NotEmpty(function.Regions);                              // not consumed
        Assert.Empty(function.Descendants.OfType<TryCatch>());
        function.CheckInvariant();
    }
}
