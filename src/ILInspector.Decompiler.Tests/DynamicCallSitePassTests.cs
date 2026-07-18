using System;
using System.Linq;
using System.Collections.Generic;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using Xunit;

namespace ILInspector.Decompiler.Tests;

public class DynamicCallSitePassTests
{
    static IrFunction LoadCanonicalFunction()
    {
        var path = typeof(LadderRung9.DynamicAndExpressionTrees).Assembly.Location;
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, "LadderRung9.DynamicAndExpressionTrees", "DynamicGetLength", 0, false);
        
        var context = new PassContext(new Stepper(enabled: false));
        foreach (var pass in IrPasses.Default)
        {
            if (pass.Name == "dynamic-callsite") break;
            pass.Run(function!, context);
        }
        return function!;
    }

    static bool RunPassAndCheck(IrFunction function)
    {
        var pass = new DynamicCallSitePass();
        var context = new PassContext(new Stepper(enabled: false));
        pass.Run(function, context);
        return function.Descendants.OfType<DynamicGetMember>().Any();
    }

    [Fact]
    public void CanonicalPositive_Raises()
    {
        var f = LoadCanonicalFunction();
        Assert.True(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_WrongBinderName_Declines()
    {
        var f = LoadCanonicalFunction();
        var binderCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "GetMember");
        var mutatedRef = new MethodRef(binderCall.Callee.DeclaringType, "NotGetMember", binderCall.Callee.ReturnType, binderCall.Callee.ParameterTypes, binderCall.Callee.HasThis);
        var args = binderCall.Arguments.ToList();
        foreach (var arg in args) arg.Detach();
        binderCall.ReplaceWith(new Call(mutatedRef, false, args));
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_ExtraSideEffect_Declines()
    {
        var f = LoadCanonicalFunction();
        var thenBlock = f.Descendants.OfType<IfStatement>().Single().Then as Block;
        var dummyStore = new StoreLocal(100, TypeRef.CoreLib("System", "Int32"), new Constant(1, TypeRef.CoreLib("System", "Int32"))); 
        
        var temp = thenBlock!.Children.ToList();
        temp.Insert(temp.Count - 1, dummyStore);
        foreach (var c in thenBlock.Children.ToList()) c.Detach();
        foreach (var c in temp) thenBlock.Add(c);
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_DuplicateDefinition_Declines()
    {
        var f = LoadCanonicalFunction();
        var thenBlock = f.Descendants.OfType<IfStatement>().Single().Then as Block;
        var existingStore = thenBlock!.Children.OfType<StoreStackSlot>().First(s => s.Value is TypeOf || s.Value is LoadToken);
        var dummyValue = new TypeOf(TypeRef.CoreLib("System", "Object"));
        var dupStore = new StoreStackSlot(existingStore.Slot, dummyValue);
        
        var temp = thenBlock.Children.ToList();
        temp.Insert(temp.Count - 1, dupStore);
        foreach (var c in thenBlock.Children.ToList()) c.Detach();
        foreach (var c in temp) thenBlock.Add(c);
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_WrongCreateAssembly_Declines()
    {
        var f = LoadCanonicalFunction();
        var createCall = f.Descendants.OfType<Call>().FirstOrDefault(c => c.Callee.Name == "Create" && c.Callee.DeclaringType.ElementType?.Name == "CallSite`1");
        if (createCall == null) createCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "Create" && c.Callee.DeclaringType.Name == "CallSite`1");
        
        var mutatedType = TypeRef.CoreLib("System.Runtime.CompilerServices", "CallSite`1");
        var mutatedRef = new MethodRef(mutatedType, "Create", createCall.Callee.ReturnType, createCall.Callee.ParameterTypes, createCall.Callee.HasThis);
        var args = createCall.Arguments.ToList();
        foreach (var arg in args) arg.Detach();
        createCall.ReplaceWith(new Call(mutatedRef, false, args));
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_WrongFlags_Declines()
    {
        var f = LoadCanonicalFunction();
        var binderCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "GetMember");
        var flagsArg = binderCall.Arguments[0];
        flagsArg.ReplaceWith(new Constant(1, TypeRef.CoreLib("System", "Int32")));
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_UnspellableName_Declines()
    {
        var f = LoadCanonicalFunction();
        var binderCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "GetMember");
        var nameArg = binderCall.Arguments[1];
        nameArg.ReplaceWith(new Constant("123Unspellable", TypeRef.CoreLib("System", "String")));
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_KeywordName_Raises()
    {
        var f = LoadCanonicalFunction();
        var binderCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "GetMember");
        var nameArg = binderCall.Arguments[1];
        nameArg.ReplaceWith(new Constant("class", TypeRef.CoreLib("System", "String")));
        Assert.True(RunPassAndCheck(f));
    }
}
