using ILInspector.Decompiler;

namespace ILInspector.Decompiler.Tests;

public class CSharpDeclarationWriterTests
{
    [Fact]
    public void Write_GroupsTypesInNamespaceBlock()
    {
        var unit = new CSharpCompilationUnit(
            Usings: ["System"],
            AssemblyAttributes: [],
            ModuleAttributes: [],
            Types:
            [
                new CSharpTypeDeclaration(
                    Namespace: "Fixtures",
                    Name: "Class1",
                    Kind: CSharpTypeKind.Class,
                    Interfaces: [],
                    Members:
                    [
                        new CSharpMemberDeclaration(
                            Name: "Value",
                            Kind: CSharpMemberKind.PropertyGet,
                            IsStatic: false,
                            ReturnType: "int",
                            Parameters: [],
                            StubBody: CSharpStubBodyKind.TargetBody,
                            TargetBody: "return 42;")
                    ],
                    NestedTypes: [])
            ]);

        var source = CSharpDeclarationWriter.Write(unit);

        Assert.Contains("namespace Fixtures", source);
        Assert.Contains("public unsafe class Class1", source);
        Assert.Contains("public int Value", source);
        Assert.Contains("return 42;", source);
    }

    [Fact]
    public void Write_RendersNestedTypesAndConstructors()
    {
        var unit = new CSharpCompilationUnit(
            Usings: [],
            AssemblyAttributes: [],
            ModuleAttributes: [],
            Types:
            [
                new CSharpTypeDeclaration(
                    Namespace: "",
                    Name: "Outer",
                    Kind: CSharpTypeKind.Class,
                    Interfaces: [],
                    Members: [],
                    NestedTypes:
                    [
                        new CSharpTypeDeclaration(
                            Namespace: "",
                            Name: "Inner",
                            Kind: CSharpTypeKind.Class,
                            Interfaces: [],
                            Members:
                            [
                                new CSharpMemberDeclaration(
                                    Name: ".ctor",
                                    Kind: CSharpMemberKind.Constructor,
                                    IsStatic: false,
                                    ReturnType: null,
                                    Parameters: [new CSharpParameterDeclaration("value", "int")],
                                    StubBody: CSharpStubBodyKind.Throw,
                                    TargetBody: null)
                            ],
                            NestedTypes: [])
                    ])
            ]);

        var source = CSharpDeclarationWriter.Write(unit);

        Assert.Contains("public unsafe class Outer", source);
        Assert.Contains("public unsafe class Inner", source);
        Assert.Contains("public Inner(int value) { throw null; }", source);
    }
}
