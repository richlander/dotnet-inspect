using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace ILInspector.OffsetReport.Tests;

public class OffsetReporterTests
{
    static readonly string AssemblyPath = typeof(OffsetReporterTests).Assembly.Location;

    // Fixtures with known IL shapes.
    static int Helper(int x) => x + 1;
    static int AfterCall(int a) => Helper(a) + 1;          // ldarg.0; call Helper; ldc.i4.1; add; ret
    static void TryCatch()
    {
        try { Console.WriteLine("x"); }
        catch (InvalidOperationException) { }
    }

    static (MethodInstructions Mi, MethodBodyBlock Body, int Token) Decode(string methodName)
    {
        var method = typeof(OffsetReporterTests).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
        int token = method.MetadataToken;
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var reader = pe.GetMetadataReader();
        var def = reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(token));
        var body = pe.GetMethodBody(def.RelativeVirtualAddress);
        return (MethodInstructions.Decode(body), body, token);
    }

    [Fact]
    public void Resolves_instruction_and_callee_at_a_call_site()
    {
        var (mi, _, token) = Decode(nameof(AfterCall));
        var call = mi.Instructions.First(i => i.OpCode is ILOpCode.Call or ILOpCode.Callvirt);

        var report = OffsetReporter.Resolve(AssemblyPath, token, call.Offset);

        Assert.NotNull(report);
        Assert.Equal(call.OpCode, report!.Instruction);
        Assert.Equal(OffsetOperandKind.Method, report.Operand?.Kind);
        Assert.Contains("Helper", report.Operand!.Display);
        Assert.Null(report.ReturnAddressOf); // the offset is the call itself, not a return address
    }

    [Fact]
    public void Reports_return_address_with_literal_instruction_at_the_offset()
    {
        var (mi, _, token) = Decode(nameof(AfterCall));
        var call = mi.Instructions.First(i => i.OpCode == ILOpCode.Call);
        var afterInstruction = mi.InstructionAt(call.NextOffset)!; // ldc.i4.1, not a call

        var report = OffsetReporter.Resolve(AssemblyPath, token, call.NextOffset);

        Assert.NotNull(report);
        // Instruction is the op AT the offset (literal), NOT the call it follows.
        Assert.Equal(afterInstruction.OpCode, report!.Instruction);
        Assert.NotEqual(ILOpCode.Call, report.Instruction);
        // ...and the call is surfaced separately as "return address of".
        Assert.NotNull(report.ReturnAddressOf);
        Assert.Equal(call.Offset, report.ReturnAddressOf!.CallOffset);
        Assert.Contains("Helper", report.ReturnAddressOf.Callee);
    }

    [Fact]
    public void Reports_exception_handling_context()
    {
        var (mi, body, token) = Decode(nameof(TryCatch));
        var region = body.ExceptionRegions.First(r => r.Kind == ExceptionRegionKind.Catch);

        // An offset inside the try body.
        var inTry = mi.Instructions.First(i => i.Offset >= region.TryOffset && i.Offset < region.TryOffset + region.TryLength);
        var tryReport = OffsetReporter.Resolve(AssemblyPath, token, inTry.Offset);
        Assert.Equal("try", tryReport?.ExceptionRegion?.Position);

        // The catch handler entry.
        var catchReport = OffsetReporter.Resolve(AssemblyPath, token, region.HandlerOffset);
        Assert.Equal("catch", catchReport?.ExceptionRegion?.Position);
        Assert.Contains("InvalidOperationException", catchReport!.ExceptionRegion!.CatchType ?? "");
    }

    [Fact]
    public void Error_contract_returns_null()
    {
        var (mi, _, token) = Decode(nameof(AfterCall));
        var call = mi.Instructions.First(i => i.OpCode == ILOpCode.Call);

        // Invalid / out-of-range token.
        Assert.Null(OffsetReporter.Resolve(AssemblyPath, 0x06FFFFFF, 0x0));
        // Non-MethodDef token.
        Assert.Null(OffsetReporter.Resolve(AssemblyPath, 0x02000001, 0x0));
        // Offset not on an instruction boundary (mid-call).
        Assert.Null(OffsetReporter.Resolve(AssemblyPath, token, call.Offset + 1));
        // Offset past the end of the method.
        Assert.Null(OffsetReporter.Resolve(AssemblyPath, token, 0x7FFFFFFF));

        // A method with no body (RVA == 0), if the assembly has one.
        int? noBody = FirstNoBodyToken();
        if (noBody is int t)
            Assert.Null(OffsetReporter.Resolve(AssemblyPath, t, 0x0));
    }

    static int? FirstNoBodyToken()
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                return MetadataTokens.GetToken(handle);
        }
        return null;
    }
}
