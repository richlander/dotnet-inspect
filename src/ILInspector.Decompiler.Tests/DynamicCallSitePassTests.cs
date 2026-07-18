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

    static MethodRef MutateRef(MethodRef m, TypeRef? dt = null, string? name = null, TypeRef? rt = null, System.Collections.Immutable.ImmutableArray<TypeRef>? pt = null, bool? ht = null)
    {
        var method = new MethodRef(dt ?? m.DeclaringType, name ?? m.Name, rt ?? m.ReturnType, pt ?? m.ParameterTypes, ht ?? m.HasThis)
        {
            IsSpecialName = m.IsSpecialName,
            IsSpecialNameInferred = m.IsSpecialNameInferred,
            AccessorKind = m.AccessorKind,
            DeclaringTypeIsTrustedPlatform = m.DeclaringTypeIsTrustedPlatform,
            DeclaringTypeIsDelegate = m.DeclaringTypeIsDelegate,
            ParameterRefKinds = m.ParameterRefKinds,
            ParameterRefKindsFacts = m.ParameterRefKindsFacts,
            IsOperator = m.IsOperator,
            CompilerGenerated = m.CompilerGenerated,
            DeclaringTypeCompilerGenerated = m.DeclaringTypeCompilerGenerated,
            IsExtension = m.IsExtension,
            IsPInvoke = m.IsPInvoke,
            IsRuntimeAsync = m.IsRuntimeAsync,
            IsUnmanagedCallersOnly = m.IsUnmanagedCallersOnly
        };
        return method;
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
    public void Mutation_WrongBinderName_Declines()
    {
        var f = LoadCanonicalFunction();
        var binderCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "GetMember");
        var mutatedRef = MutateRef(binderCall.Callee, name: "NotGetMember");
        var args = binderCall.Arguments.ToList();
        foreach (var arg in args) arg.Detach();
        binderCall.ReplaceWith(new Call(mutatedRef, false, args));
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_BinderMissingPlatformTrust_Declines()
    {
        var f = LoadCanonicalFunction();
        var binderCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "GetMember");
        var mutatedRef = MutateRef(binderCall.Callee);
        mutatedRef = mutatedRef with { DeclaringTypeIsTrustedPlatform = MetadataFactState.Unknown };
        var args = binderCall.Arguments.ToList();
        foreach (var arg in args) arg.Detach();
        binderCall.ReplaceWith(new Call(mutatedRef, false, args));
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_BinderWrongReturnType_Declines()
    {
        var f = LoadCanonicalFunction();
        var binderCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "GetMember");
        var mutatedRef = MutateRef(binderCall.Callee, rt: TypeRef.CoreLib("System", "Object"));
        var args = binderCall.Arguments.ToList();
        foreach (var arg in args) arg.Detach();
        binderCall.ReplaceWith(new Call(mutatedRef, false, args));
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_BinderWrongParameterTypes_Declines()
    {
        var f = LoadCanonicalFunction();
        var binderCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "GetMember");
        var pts = binderCall.Callee.ParameterTypes.ToArray();
        pts[0] = TypeRef.CoreLib("System", "Int32");
        var mutatedRef = MutateRef(binderCall.Callee, pt: System.Collections.Immutable.ImmutableArray.Create(pts));
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
    public void Mutation_WrongCreateTrust_Declines()
    {
        var f = LoadCanonicalFunction();
        var createCall = f.Descendants.OfType<Call>().FirstOrDefault(c => c.Callee.Name == "Create" && c.Callee.DeclaringType.ElementType?.Name == "CallSite`1");
        if (createCall == null) createCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "Create" && c.Callee.DeclaringType.Name == "CallSite`1");

        var mutatedRef = MutateRef(createCall.Callee);
        mutatedRef = mutatedRef with { DeclaringTypeIsTrustedPlatform = MetadataFactState.Unknown };
        var args = createCall.Arguments.ToList();
        foreach (var arg in args) arg.Detach();
        createCall.ReplaceWith(new Call(mutatedRef, false, args));
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_ArrayLengthNotOne_Declines()
    {
        var f = LoadCanonicalFunction();
        var na = f.Descendants.OfType<NewArray>().Single();
        na.Length.ReplaceWith(new Constant(2, TypeRef.CoreLib("System", "Int32")));
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_ArrayElementIndexNotZero_Declines()
    {
        var f = LoadCanonicalFunction();
        var se = f.Descendants.OfType<StoreElement>().Single();
        se.Index.ReplaceWith(new Constant(1, TypeRef.CoreLib("System", "Int32")));
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_TargetFieldWrongType_Declines()
    {
        var f = LoadCanonicalFunction();
        var invokeCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "Invoke");
        var targetFieldArg = invokeCall.Arguments[0] as LoadField;
        
        var mutatedField = new FieldRef(targetFieldArg!.Field.DeclaringType, "Target", TypeRef.CoreLib("System", "Object"));
        var instance = targetFieldArg.Instance!;
        instance.Detach();
        var newTargetFieldArg = new LoadField(mutatedField, instance);
        targetFieldArg.ReplaceWith(newTargetFieldArg);
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_InvokeWrongSignature_Declines()
    {
        var f = LoadCanonicalFunction();
        var invokeCall = f.Descendants.OfType<Call>().Single(c => c.Callee.Name == "Invoke");
        var pts = invokeCall.Callee.ParameterTypes.ToArray();
        pts[0] = TypeRef.CoreLib("System", "Object");
        var mutatedRef = MutateRef(invokeCall.Callee, pt: System.Collections.Immutable.ImmutableArray.Create(pts));
        var args = invokeCall.Arguments.ToList();
        foreach (var arg in args) arg.Detach();
        invokeCall.ReplaceWith(new Call(mutatedRef, false, args));
        Assert.False(RunPassAndCheck(f));
    }

    [Fact]
    public void Mutation_CSharpArgumentInfoWrongTrust_Declines()
    {
        var f = LoadCanonicalFunction();
        var infoCreate = f.Descendants.OfType<Call>().Single(c => c.Callee.DeclaringType.Name == "CSharpArgumentInfo" && c.Callee.Name == "Create");
        var mutatedRef = MutateRef(infoCreate.Callee);
        mutatedRef = mutatedRef with { DeclaringTypeIsTrustedPlatform = MetadataFactState.Unknown };
        var args = infoCreate.Arguments.ToList();
        foreach (var arg in args) arg.Detach();
        infoCreate.ReplaceWith(new Call(mutatedRef, false, args));
        Assert.False(RunPassAndCheck(f));
    }
}
