using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Inspectors;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

[Trait("Speed", "Fast")]
public sealed class AsyncMainCompiledEvidenceTests
{
    [Theory]
    [InlineData(FixtureIds.DecompilerAsyncMainCompiler, false)]
    [InlineData(FixtureIds.DecompilerAsyncMainRuntime, true)]
    public void SelectedMain_DistinguishesPhysicalLoweringFromRaisedBody(
        string fixtureId, bool runtimeAsync)
    {
        string path = FixtureCatalog.AssemblyPath(fixtureId);
        using var pe = new PEReader(File.OpenRead(path));
        var type = Assert.Single(ApiSurfaceExtractor.Extract(
            pe, includeAll: true, includeCompilerGenerated: true).Types,
            type => type.FullName == "Program");
        var method = Assert.Single(type.Members, member => member.Name == "Main");
        var request = new MemberCodeProvider.Request(
            DecompiledSource: true, AnnotatedSource: false,
            CostOverlay: false, SemanticsOverlay: false,
            IL: true, Attributes: true, Calls: false, Callers: false,
            CallGraph: false, UnsafeOperations: false);
        var (_, code) = Assert.Single(MemberCodeProvider.Collect(
            type, [method], path, overloadIndex: 0, request, includeAll: true));
        Assert.Null(code.ILDiagnostic);
        Assert.Contains("await", code.DecompiledResult!.Output);
        Assert.Contains("Delay(1)", code.DecompiledResult.Output);
        Assert.NotNull(code.Attributes);
        var nullable = Assert.Single(code.Attributes, attribute => attribute.Name == "NullableContext");
        Assert.Equal("1", nullable.Value);
        Assert.DoesNotContain(code.Attributes, attribute => attribute.Name == "MethodImpl");
        if (runtimeAsync)
        {
            Assert.Contains("implementation flags: Async", code.ILText);
            Assert.Contains("AsyncHelpers::Await", code.ILText);
            Assert.DoesNotContain("AsyncTaskMethodBuilder", code.ILText);
            Assert.DoesNotContain("Compiler state-machine", code.ILText);
            Assert.DoesNotContain(code.Attributes, attribute => attribute.Name == "AsyncStateMachine");
        }
        else
        {
            Assert.Contains("implementation flags: IL", code.ILText);
            Assert.Contains("AsyncTaskMethodBuilder::Create", code.ILText);
            Assert.Contains("AsyncTaskMethodBuilder::get_Task", code.ILText);
            var stateMachine = Assert.Single(code.Attributes,
                attribute => attribute.Name == "AsyncStateMachine");
            Assert.Equal("type \"Program+<Main>d__0\"", stateMachine.Value);
            Assert.DoesNotContain("AsyncHelpers::Await", code.ILText);
            Assert.Contains("Compiler state-machine MoveNext; physical MethodDef", code.ILText);
            Assert.Contains("Compiler state-machine SetStateMachine; physical MethodDef", code.ILText);
            Assert.Contains("Task::Delay(int)", code.ILText);
            Assert.Contains("TaskAwaiter::GetResult", code.ILText);
        }
    }
    [Fact]
    public void MetadataAttributes_AreOptInAndKeepUnavailableValuesVisible()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(AsyncMainCompiledEvidenceTests).Assembly.Location);
        string type = typeof(AsyncMainCompiledEvidenceTests).FullName!;
        var filtered = session.MethodBodies.ResolveMethod(
            type, nameof(MetadataMarker), 0, publicOnly: false)!;
        var physical = session.MethodBodies.ResolveMethod(
            type, nameof(MetadataMarker), 0, publicOnly: false,
            includeCompilerGeneratedAttributes: true)!;
        Assert.DoesNotContain(filtered.Attributes, attribute => attribute.Name == "CompilerGenerated");
        Assert.Contains(physical.Attributes, attribute => attribute.Name == "CompilerGenerated");
        var unavailable = Assert.Single(physical.Attributes,
            attribute => attribute.Name == "ArrayMarker");
        Assert.Equal("<value unavailable>", unavailable.Value);
        Assert.Equal(filtered.MetadataToken, physical.Implementation!.MethodToken);
    }

    [System.Runtime.CompilerServices.CompilerGenerated]
    [ArrayMarker(new[] { 1, 2 })]
    private static void MetadataMarker() { }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class ArrayMarkerAttribute(int[] values) : Attribute
    {
        public int[] Values { get; } = values;
    }

}
