using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.DecompilerHarness;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class CapturingLambdaCoupledBodyTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Callback_ProductArtifactPreservesFactoryAndActualDelegateTarget()
    {
        Type samples = typeof(CapturingLambdaStorageFinalizationSamples);
        string methodName = nameof(CapturingLambdaStorageFinalizationSamples.Callback);
        string assemblyPath = samples.Assembly.Location;
        var result = Assert.Single(await ReturnToSender.CompileBackTargets(
            assemblyPath,
            [new ReturnToSender.RequestedTarget(samples.FullName!, methodName, 0)],
            sourceIndex: null,
            applyCompileBackFloor: false));

        Assert.False(result.UsedCompileBackFloor);
        Assert.True(
            result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Status}: {result.Detail}\n{result.Source}");
        Assert.NotNull(result.DonorPe);
        Assert.NotNull(result.MemberAnchor);

        using var original = new PEReader(File.OpenRead(assemblyPath));
        using var compiled = new PEReader(new MemoryStream(result.DonorPe, writable: false));
        var originalReader = original.GetMetadataReader();
        var compiledReader = compiled.GetMetadataReader();
        var originalFactory = (MethodDefinitionHandle)MetadataTokens.EntityHandle(
            samples.GetMethod(methodName)!.MetadataToken);
        var compiledFactory = Assert.Single(compiledReader.MethodDefinitions, handle =>
        {
            var method = compiledReader.GetMethodDefinition(handle);
            return ApiMemberIdentity.CreateMethodAnchor(
                compiledReader, method.GetDeclaringType(), method) == result.MemberAnchor;
        });

        AssertExact(originalFactory, compiledFactory);

        // Exact factory operands bind the single delegate construction on each side.
        // Follow its real MethodDef instead of guessing a generated member by name.
        var originalTarget = DelegateTarget(original, originalReader, originalFactory);
        var compiledTarget = DelegateTarget(compiled, compiledReader, compiledFactory);
        AssertExact(originalTarget, compiledTarget);

        void AssertExact(MethodDefinitionHandle oldMethod, MethodDefinitionHandle newMethod)
        {
            var comparison = IlAssemblyDiff.CompareMembers(
                original,
                originalReader,
                oldMethod,
                compiled,
                compiledReader,
                newMethod,
                normalization: FidelityCheck.ContractBodyDiffNormalization);
            Assert.True(comparison.Diff.Outcome == IlBodyDiffOutcome.Exact,
                IlDiffPrinter.RenderUnified(comparison.Diff));
        }
    }

    static MethodDefinitionHandle DelegateTarget(
        PEReader pe,
        MetadataReader reader,
        MethodDefinitionHandle factory)
    {
        var body = pe.GetMethodBody(reader.GetMethodDefinition(factory).RelativeVirtualAddress);
        var decoded = MethodInstructions.Decode(body);
        Assert.True(decoded.IsComplete, decoded.Blocks.IncompleteReason);
        var target = Assert.Single(decoded.Instructions, instruction => instruction.OpCode == ILOpCode.Ldftn);
        Assert.Equal(ILOpCode.Newobj,
            Assert.Single(decoded.Instructions, instruction => instruction.Offset == target.NextOffset).OpCode);
        var handle = MetadataTokens.EntityHandle(checked((int)target.OperandValue));
        Assert.Equal(HandleKind.MethodDefinition, handle.Kind);
        return (MethodDefinitionHandle)handle;
    }
}
