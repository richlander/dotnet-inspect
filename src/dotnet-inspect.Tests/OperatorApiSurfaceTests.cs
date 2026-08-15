using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public sealed class OperatorApiSurfaceTests
{
    [Fact]
    public void Extract_ClassifiesOnlyRecognizedNonGenericSpecialNameOperators()
    {
        string path = Path.Combine(Path.GetTempPath(), $"operator-surface-{Guid.NewGuid():N}.dll");
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("OperatorSurface"),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("OperatorSurface");
        var type = module.DefineType(
            "OperatorSurface",
            TypeAttributes.Public | TypeAttributes.Class);

        DefineMethod(type, "op_AdditionAssignment", MethodAttributes.Public, typeof(int));
        DefineMethod(
            type,
            "op_AdditionAssignment",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            typeof(string));
        var generic = DefineMethod(
            type,
            "op_IncrementAssignment",
            MethodAttributes.Public | MethodAttributes.SpecialName);
        generic.DefineGenericParameters("T");
        DefineMethod(
            type,
            "op_Custom",
            MethodAttributes.Public | MethodAttributes.SpecialName);
        DefineMethod(
            type,
            "op_CheckedImplicit",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
            typeof(int));
        type.CreateType();
        assembly.Save(path);

        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            var members = Assert.Single(surface.Types, candidate => candidate.Name == "OperatorSurface").Members;

            var assignments = members
                .Where(member => member.Name == "op_AdditionAssignment")
                .Select(member => member.Kind)
                .Order()
                .ToArray();
            Assert.Equal(["method", "operator"], assignments);
            Assert.Contains(members, member => member.Name == "op_IncrementAssignment" && member.Kind == "method");
            Assert.Contains(members, member => member.Name == "op_Custom" && member.Kind == "method");
            Assert.Contains(members, member => member.Name == "op_CheckedImplicit" && member.Kind == "method");

            var reader = peReader.GetMetadataReader();
            var typeHandle = Assert.Single(reader.TypeDefinitions, handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) == "OperatorSurface");
            var typeDefinition = reader.GetTypeDefinition(typeHandle);
            var assignmentAnchors = typeDefinition.GetMethods()
                .Select(handle => reader.GetMethodDefinition(handle))
                .Where(method => reader.GetString(method.Name) == "op_AdditionAssignment")
                .Select(method => (
                    IsSpecialName: method.Attributes.HasFlag(MethodAttributes.SpecialName),
                    Anchor: ApiMemberIdentity.CreateMethodAnchor(reader, typeHandle, method)))
                .ToArray();

            Assert.Contains(assignmentAnchors, item =>
                !item.IsSpecialName
                && item.Anchor.StableSelector.StartsWith("op_AdditionAssignment~", StringComparison.Ordinal));
            Assert.Contains(assignmentAnchors, item =>
                item.IsSpecialName
                && item.Anchor.StableSelector.StartsWith("operator:op_AdditionAssignment~", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }

        static MethodBuilder DefineMethod(
            TypeBuilder type,
            string name,
            MethodAttributes attributes,
            params Type[] parameterTypes)
        {
            var method = type.DefineMethod(name, attributes, typeof(void), parameterTypes);
            method.GetILGenerator().Emit(OpCodes.Ret);
            return method;
        }
    }
}
