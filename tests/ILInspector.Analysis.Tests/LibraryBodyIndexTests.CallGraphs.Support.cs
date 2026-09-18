using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    static List<string> FlattenCallTree(
        CallTreeNode root,
        bool includePerf = false)
    {
        var lines = new List<string>();
        void Walk(CallTreeNode node, int depth)
        {
            lines.Add(includePerf
                ? $"{depth}|{MemberIdentity(node.Member)}"
                    + $"|kind={node.Kind}"
                    + $"|status={node.Status}"
                    + $"|fanin={node.Perf?.Fanin}"
                    + $"|max-depth={node.Perf?.MaxDepth}"
                    + $"|in-loop={node.Perf?.InLoop}"
                    + $"|root-kind={node.Perf?.RootKind}"
                    + $"|source={node.Perf?.Source}"
                : $"{depth}|{node.Member.Name}|{node.Status}|"
                    + $"{node.Perf?.Source ?? ""}");
            foreach (var child in node.Children)
                Walk(child, depth + 1);
        }
        Walk(root, 0);
        return lines;

        static string MemberIdentity(MemberRef member) =>
            $"{GenericMemberIdentity.KeyFragment(member.DeclaringType)}"
            + $"::{member.Name}"
            + $"|arity={member.GenericArity}"
            + $"|parameters={TypeKeys(member.ParameterTypes)}"
            + $"|return={GenericMemberIdentity.KeyFragment(member.ReturnType)}"
            + $"|type-arguments={TypeKeys(member.TypeArguments)}"
            + $"|has-this={member.HasThis}"
            + $"|signature-header={member.SignatureHeader}"
            + $"|required-parameters={member.RequiredParameterCount}"
            + $"|open-parameters={TypeKeys(member.OpenParameterTypes)}"
            + $"|open-return={GenericMemberIdentity.KeyFragment(member.OpenSignatureReturn)}";

        static string TypeKeys(IEnumerable<TypeRef> types) =>
            string.Join(",", types.Select(GenericMemberIdentity.KeyFragment));
    }

    static bool InLeverageFixtures(MethodIdentity method)
        => method.DeclaringType.Name == nameof(LeverageFixtures);

    static MethodIdentity LeverageMethod(string name, int token)
        => new("Asm", Guid.Empty, TypeRef.Definition("Asm", "Ns", "Type"), name, [], TypeRef.CoreLib("System", "Void"), token, IsStatic: true);

    static DirectCall LeverageCall(MethodIdentity caller, MethodIdentity callee)
    {
        var calleeRef = new MemberRef(callee.DeclaringType, callee.Name, callee.ParameterTypes, callee.ReturnType, MemberKind.Method);
        return new DirectCall(caller, calleeRef, ILOffset: 0, OperandToken: callee.MetadataToken, CalleeDefinitionToken: callee.MetadataToken, CallKind.Call);
    }

    static (string Path, string Directory) BuildRepeatedCallSiteFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnet-inspect-repeated-call-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "RepeatedCallSiteFixture.dll");

        var assemblyName = new AssemblyName("RepeatedCallSiteFixture");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("RepeatedCallSiteFixture", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);

        var target = type.DefineMethod("Target", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        target.GetILGenerator().Emit(OpCodes.Ret);

        // Two call sites from one caller.
        var twice = type.DefineMethod("CallsTargetTwice", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var twiceIl = twice.GetILGenerator();
        twiceIl.Emit(OpCodes.Call, target);
        twiceIl.Emit(OpCodes.Call, target);
        twiceIl.Emit(OpCodes.Ret);

        // One call site from a second caller.
        var once = type.DefineMethod("CallsTargetOnce", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var onceIl = once.GetILGenerator();
        onceIl.Emit(OpCodes.Call, target);
        onceIl.Emit(OpCodes.Ret);

        type.CreateType();
        assembly.Save(path);
        return (path, directory);
    }

}
