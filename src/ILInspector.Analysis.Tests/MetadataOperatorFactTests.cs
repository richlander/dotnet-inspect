using System.Reflection;
using System.Reflection.Emit;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Body identity needs an exact operator answer where the metadata carries one
/// and an explicit unknown where it does not. A MethodDef has the
/// <c>SpecialName</c> flag, the generic arity, and the name; a MemberRef into
/// another assembly has none of them, so guessing from the <c>op_</c> prefix is
/// how an ordinary method named <c>op_Multiply</c> became an operator.
/// </summary>
public class MetadataOperatorFactTests
{
    [Fact]
    public void MethodDefinitions_CarryTheExactOperatorFact()
    {
        string path = BuildAssembly();
        try
        {
            var index = LibraryBodyIndex.Open(path);

            Assert.Equal(
                MetadataOperatorFact.Yes,
                Assert.Single(index.Methods, m => m.Name == "op_Addition").IsOperator);
            // SpecialName is absent: an ordinary method that happens to be named
            // like an operator.
            Assert.Equal(
                MetadataOperatorFact.No,
                Assert.Single(index.Methods, m => m.Name == "op_Multiply").IsOperator);
            // A CLI operator name C# cannot declare is still a metadata operator.
            Assert.Equal(
                MetadataOperatorFact.Yes,
                Assert.Single(index.Methods, m => m.Name == "op_LogicalAnd").IsOperator);
            Assert.Equal(
                MetadataOperatorFact.No,
                Assert.Single(index.Methods, m => m.Name == "Scale").IsOperator);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CrossAssemblyMemberReferences_StayUnknown()
    {
        string path = BuildAssembly();
        try
        {
            var index = LibraryBodyIndex.Open(path);
            var call = Assert.Single(
                index.DirectCalls,
                candidate => candidate.Callee.Name == "op_Equality");

            Assert.Equal(MetadataOperatorFact.Unknown, call.Callee.IsOperator);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static string BuildAssembly()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"operator-fact-{Guid.NewGuid():N}.dll");
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("OperatorFact"),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("OperatorFact");
        var type = module.DefineType("Widget", TypeAttributes.Public | TypeAttributes.Class);

        Define("op_Addition", MethodAttributes.SpecialName);
        Define("op_LogicalAnd", MethodAttributes.SpecialName);
        Define("op_Multiply", MethodAttributes.PrivateScope);
        Define("Scale", MethodAttributes.PrivateScope);

        var caller = type.DefineMethod(
            "CompareStrings",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(string), typeof(string)]);
        var callerIl = caller.GetILGenerator();
        callerIl.Emit(OpCodes.Ldarg_0);
        callerIl.Emit(OpCodes.Ldarg_1);
        callerIl.Emit(
            OpCodes.Call,
            typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        callerIl.Emit(OpCodes.Ret);

        type.CreateType();
        assembly.Save(path);
        return path;

        void Define(string name, MethodAttributes extra)
        {
            var method = type.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static | extra,
                typeof(int),
                [typeof(int), typeof(int)]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ret);
        }
    }
}
