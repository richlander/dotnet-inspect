using ILInspector.DecompilerHarness;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Instructions;
using DotnetInspector.RoundTripCompilation;
using DotnetInspector.Services;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ILInspector.Decompiler.Tests;

public partial class ReturnToSenderPrototypeTests
{
    [Fact]
    public async Task CompileBackTargets_DoesNotMarkFinalNewSlotStructMethodVirtual()
    {
        var assemblyPath = CompileFixture("""
            public interface IThing
            {
                int GetValue();
            }

            public struct Thing : IThing
            {
                public int GetValue() => 42;
            }

            public class Class1
            {
                public int UseThing()
                {
                    var thing = new Thing();
                    return thing.GetValue();
                }
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "UseThing", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public struct Thing", result.Source);
            Assert.Contains("public int GetValue()", result.Source);
            Assert.DoesNotContain("virtual int GetValue", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_DoesNotMarkUserBaseOverrideWithoutBaseShell()
    {
        var assemblyPath = CompileFixture("""
            public abstract class BaseNode
            {
                public abstract string Describe();
            }

            public class DerivedNode : BaseNode
            {
                public override string Describe() => "derived";
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("DerivedNode", "Describe", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public string Describe()", result.Source);
            Assert.DoesNotContain("override string Describe", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_EmitsEqualityOperatorPairSibling()
    {
        var assemblyPath = CompileFixture("""
            public record Row(string Name);
            """);
        try
        {
            var results = await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Row", "op_Equality", 0),
                    new ReturnToSender.RequestedTarget("Row", "op_Inequality", 0),
                ]);

            Assert.Collection(
                results,
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("operator ==(", result.Source);
                    Assert.Contains("operator !=(", result.Source);
                },
                result =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
                    Assert.Contains("operator ==(", result.Source);
                    Assert.Contains("operator !=(", result.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_PreservesRawReferenceEqualityBesideUserOperator()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row
            {
                public static bool operator ==(Row left, Row right) => false;
                public static bool operator !=(Row left, Row right) => true;
                public static bool SameReference(Row left, Row right, bool useOperator)
                {
                    if (useOperator && left == right)
                        return false;
                    return (object)left == right;
                }
                public override bool Equals(object obj) => false;
                public override int GetHashCode() => 0;
            }
            """);
        try
        {
            var results = await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Row", "SameReference", 0),
                ]);

            var referenceComparison = Assert.Single(results);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, referenceComparison.Status);
            Assert.Contains("return (object)left == (object)right;", referenceComparison.Source);
            Assert.Contains("left == right", referenceComparison.Source);
            Assert.Contains("operator ==(", referenceComparison.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_PreservesMixedAndUnrelatedReferenceIdentity()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row
            {
                public static bool operator ==(object left, Row right) => false;
                public static bool operator !=(object left, Row right) => true;
                public override bool Equals(object obj) => false;
                public override int GetHashCode() => 0;
            }

            public sealed class A;
            public sealed class B;

            public sealed class DynamicCarrier
            {
                readonly Row _value = new();
                dynamic _reference = new Row();
                object _objectReference = new Row();
                public dynamic Field = new Row();
                public dynamic Property => _value;
                public dynamic Method() => _value;
                public ref dynamic DynamicReference => ref _reference;
                public ref dynamic DynamicReferenceMethod() => ref _reference;
                public ref object ObjectReferenceMethod() => ref _objectReference;
            }

            public sealed class GenericDynamicCarrier<T>
            {
                public T Value { get; init; }
                public T Slot;
                T _reference;
                public ref T Reference => ref _reference;
            }

            public ref struct RefDynamicFieldCarrier
            {
                public ref dynamic Field;
                public RefDynamicFieldCarrier(ref dynamic field) => Field = ref field;
            }

            public ref struct RefObjectFieldCarrier
            {
                public ref object Field;
                public RefObjectFieldCarrier(ref object field) => Field = ref field;
            }

            public static class Cases
            {
                public static bool Mixed<T>(T left, Row right) => (object)left == (object)right;
                public static bool Unrelated(A left, B right) => (object)left == (object)right;
                public static bool DynamicMember(dynamic value, object right) => (object)value.Member == right;
                public static bool DynamicProperty(DynamicCarrier carrier, object right) => (object)carrier.Property == right;
                public static bool DynamicMethod(DynamicCarrier carrier, object right) => (object)carrier.Method() == right;
                public static bool ByRefDynamicProperty(DynamicCarrier carrier, object right) => (object)carrier.DynamicReference == right;
                public static bool ByRefDynamicMethod(DynamicCarrier carrier, object right) => (object)carrier.DynamicReferenceMethod() == right;
                public static bool ByRefObjectMethod(DynamicCarrier carrier, object right) => (object)carrier.ObjectReferenceMethod() == right;
                public static bool DynamicField(DynamicCarrier carrier, object right) => (object)carrier.Field == right;
                public static bool ByRefDynamicField(ref RefDynamicFieldCarrier carrier, object right)
                    => (object)carrier.Field == right;
                public static bool ByRefObjectField(ref RefObjectFieldCarrier carrier, object right)
                    => (object)carrier.Field == right;
                public static bool DynamicConditional(DynamicCarrier carrier, bool choose, object right)
                    => (object)(choose ? carrier.Property : carrier.Method()) == right;
                public static bool GenericDynamicProperty(GenericDynamicCarrier<dynamic> carrier, object right)
                    => (object)carrier.Value == right;
                public static bool GenericDynamicField(GenericDynamicCarrier<dynamic> carrier, object right)
                    => (object)carrier.Slot == right;
                public static bool GenericByRefDynamicProperty(GenericDynamicCarrier<dynamic> carrier, object right)
                    => (object)carrier.Reference == right;
                public static bool DynamicArrayElement(dynamic[] values, int index, object right)
                    => (object)values[index] == right;
                public static bool ObjectArrayElement(object[] values, int index, object right)
                    => (object)values[index] == right;
                public static bool DynamicSwitch(DynamicCarrier carrier, int selector, object right)
                    => (object)(selector switch
                    {
                        0 => carrier.Field,
                        1 => carrier.Property,
                        _ => carrier.Method(),
                    }) == right;
            }
            """);
        try
        {
            var results = await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Cases", "Mixed", 0),
                    new ReturnToSender.RequestedTarget("Cases", "Unrelated", 0),
                    new ReturnToSender.RequestedTarget("Cases", "DynamicMember", 0),
                    new ReturnToSender.RequestedTarget("Cases", "DynamicProperty", 0),
                    new ReturnToSender.RequestedTarget("Cases", "DynamicMethod", 0),
                    new ReturnToSender.RequestedTarget("Cases", "ByRefDynamicProperty", 0),
                    new ReturnToSender.RequestedTarget("Cases", "ByRefDynamicMethod", 0),
                    new ReturnToSender.RequestedTarget("Cases", "ByRefObjectMethod", 0),
                    new ReturnToSender.RequestedTarget("Cases", "DynamicField", 0),
                    new ReturnToSender.RequestedTarget("Cases", "ByRefDynamicField", 0),
                    new ReturnToSender.RequestedTarget("Cases", "ByRefObjectField", 0),
                    new ReturnToSender.RequestedTarget("Cases", "DynamicConditional", 0),
                    new ReturnToSender.RequestedTarget("Cases", "GenericDynamicProperty", 0),
                    new ReturnToSender.RequestedTarget("Cases", "GenericDynamicField", 0),
                    new ReturnToSender.RequestedTarget("Cases", "GenericByRefDynamicProperty", 0),
                    new ReturnToSender.RequestedTarget("Cases", "DynamicArrayElement", 0),
                    new ReturnToSender.RequestedTarget("Cases", "ObjectArrayElement", 0),
                    new ReturnToSender.RequestedTarget("Cases", "DynamicSwitch", 0),
                ]);

            Assert.All(results, result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)left == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)(value.Member) == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)carrier.Property == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)carrier.Method() == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)(carrier.DynamicReference) == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)(carrier.DynamicReferenceMethod()) == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (carrier.ObjectReferenceMethod()) == right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)carrier.Slot == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)(carrier.Reference) == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "(object)(choose ? carrier.Property : carrier.Method()) == (object)right",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)carrier.Value == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)carrier.Field == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)(carrier.Field) == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (carrier.Field) == right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return (object)values[index] == (object)right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "return values[index] == right;",
                StringComparison.Ordinal));
            Assert.Contains(results, result => result.Source.Contains(
                "(object)(selector switch { 0 => carrier.Field, 1 => carrier.Property, _ => carrier.Method() }) == (object)right",
                StringComparison.Ordinal));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_DeepReferenceHierarchyFallsBackConservatively()
    {
        string hierarchy = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 300).Select(index =>
                index == 0
                    ? "public class C0 { }"
                    : $"public class C{index} : C{index - 1} {{ }}"));
        var assemblyPath = CompileFixture($$"""
            {{hierarchy}}

            public static class Cases
            {
                public static bool Same(C299 left, C299 right) => (object)left == right;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Cases", "Same", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("return (object)left == (object)right;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_PreservesRecordGeneratedVirtualHelperShells()
    {
        var assemblyPath = CompileFixture("""
            public record Row(string Name, string Value);
            """);
        try
        {
            var results = await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Row", "ToString", 0),
                    new ReturnToSender.RequestedTarget("Row", "Equals", 0),
                ]);

            Assert.Collection(
                results,
                toString =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, toString.Status);
                    Assert.Contains("protected virtual bool PrintMembers", toString.Source);
                    Assert.Contains("protected virtual Type EqualityContract", toString.Source);
                },
                equalsObject =>
                {
                    Assert.Equal(FidelityCheck.CompileBackStatus.Exact, equalsObject.Status);
                    Assert.Contains("public virtual bool Equals(Row other)", equalsObject.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_RecordSurfaceHelperKeepsGenericDependency()
    {
        // A record with a custom ToString that calls a generic same-type helper: the record
        // surface path reconstructs the faithful member surface, but the metadata surface
        // enumeration skips generic methods — so the IR-gathered generic dependency must still
        // be carried (via AddRequiredMembers) or compile-back regresses to a non-Exact floor.
        var assemblyPath = CompileFixture("""
            public record Row(string Name, string Value)
            {
                public override string ToString() => Render<int>();

                private string Render<T>() => Name + Value;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "ToString", 0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("Render", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_RecordSurfaceHelperKeepsGenericSameNameOverload()
    {
        // A user generic `PrintMembers<T>` overload called by a custom ToString must survive
        // the record-surface stub removal, and the synthesized `PrintMembers(StringBuilder)`
        // it also calls must still be present: the removal must not leave the synthesized shape
        // unre-emitted when a same-name overload shadows it in the name-based surface dedup.
        var assemblyPath = CompileFixture("""
            using System.Text;
            public record Row(string Name)
            {
                public override string ToString()
                {
                    var b = new StringBuilder();
                    PrintMembers<int>(7);
                    PrintMembers(b);
                    return b.ToString();
                }

                public bool PrintMembers<T>(int x) => x == 7;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "ToString", 0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("PrintMembers<T>", result.Source);
            Assert.Contains("PrintMembers(StringBuilder", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_UsesFieldShellForRecordGeneratedFieldReadHelpers()
    {
        var assemblyPath = CompileFixture("""
            public record Row(string Name, string Value);
            """);
        try
        {
            var results = await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Row", "GetHashCode", 0),
                    new ReturnToSender.RequestedTarget("Row", "ToString", 0),
                    new ReturnToSender.RequestedTarget("Row", "Equals", 1),
                ]);

            Assert.Collection(
                results,
                getHashCode =>
                {
                    Assert.True(
                        getHashCode.Status == FidelityCheck.CompileBackStatus.OperandDiff,
                        $"{getHashCode.Status}: {getHashCode.Detail}{Environment.NewLine}{getHashCode.Source}");
                    Assert.Contains("public string Name;", getHashCode.Source);
                    Assert.Contains("public string Value;", getHashCode.Source);
                    Assert.DoesNotContain("public string Name { get; }", getHashCode.Source);
                },
                toString =>
                {
                    Assert.True(
                        toString.Status == FidelityCheck.CompileBackStatus.Exact,
                        $"{toString.Status}: {toString.Detail}{Environment.NewLine}{toString.Source}");
                    Assert.Contains("public string Name { get; init; }", toString.Source);
                    Assert.DoesNotContain("public string Name;", toString.Source);
                },
                typedEquals =>
                {
                    Assert.True(
                        typedEquals.Status == FidelityCheck.CompileBackStatus.OperandDiff,
                        $"{typedEquals.Status}: {typedEquals.Detail}{Environment.NewLine}{typedEquals.Source}");
                    Assert.Contains("public virtual bool Equals(Row other)", typedEquals.Source);
                    Assert.Contains("public string Name;", typedEquals.Source);
                    Assert.Contains("public string Value;", typedEquals.Source);
                    Assert.DoesNotContain("public string Name { get; }", typedEquals.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_UsesRecordEqualityContractShellForFieldReadHelpers()
    {
        var assemblyPath = CompileFixture("""
            public record Row(string Name, string Value);
            """);
        try
        {
            var results = await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Row", "GetHashCode", 0),
                    new ReturnToSender.RequestedTarget("Row", "Equals", 1),
                ]);

            Assert.Collection(
                results,
                getHashCode => AssertRecordEqualityContractRequirement(getHashCode),
                typedEquals => AssertRecordEqualityContractRequirement(typedEquals));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }

        static void AssertRecordEqualityContractRequirement(ReturnToSender.Result result)
        {
            Assert.Equal(FidelityCheck.CompileBackStatus.OperandDiff, result.Status);
            Assert.NotNull(result.FidelityDiff);
            Assert.False(result.FidelityDiff.IsExact);
            var type = Assert.Single(result.Plan.Types);
            Assert.DoesNotContain(type.SourceFacts, fact => fact.Producer == "roslyn" && fact.Id == "closure-member");
            Assert.Contains(type.Members, member =>
                member.Name == "EqualityContract"
                && member.SourceFacts.Any(fact => fact.Id == "record-equality-contract" && fact.Detail == "get_EqualityContract"));
            Assert.Contains("EqualityContract", result.Source);
        }
    }

    [Fact]
    public async Task CompileBackTargets_UsesIncrementConsumedOperatorEvidence()
    {
        var assemblyPath = CompileFixture("""
            public struct Counter
            {
                public int Value;

                public Counter(int value)
                {
                    Value = value;
                }

                public static Counter operator ++(Counter value) => new Counter(value.Value + 1);
            }

            public class Class1
            {
                public Counter Method1(Counter counter)
                {
                    counter++;
                    return counter;
                }
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Method1", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var counter = Assert.Single(result.Plan.Types, type => type.Name == "Counter");
            Assert.Contains(counter.Members, member =>
                member.Name == "op_Increment"
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "op_Increment"));
            Assert.Contains("operator ++", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_PreservesCheckedBinaryOperatorSibling()
    {
        var assemblyPath = CompileFixture("""
            public struct CustomNumber
            {
                public int Value;

                public CustomNumber(int value)
                {
                    Value = value;
                }

                public static CustomNumber operator +(CustomNumber left, CustomNumber right) => new CustomNumber(left.Value + right.Value);
                public static CustomNumber operator checked +(CustomNumber left, CustomNumber right) => new CustomNumber(checked(left.Value + right.Value));
            }

            public class Class1
            {
                public CustomNumber Method1(CustomNumber left, CustomNumber right) => checked(left + right);
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Method1", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var number = Assert.Single(result.Plan.Types, type => type.Name == "CustomNumber");
            Assert.Contains(number.Members, member =>
                member.Name == "op_CheckedAddition"
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "op_CheckedAddition"));
            Assert.Contains(number.Members, member =>
                member.Name == "op_Addition"
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "op_Addition"));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_UsesFieldShellForRecordStructGeneratedFieldReadHelpers()
    {
        var assemblyPath = CompileFixture("""
            public record struct Row(string Name, string Value);
            """);
        try
        {
            var results = await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Row", "GetHashCode", 0),
                    new ReturnToSender.RequestedTarget("Row", "Equals", 1),
                ]);

            Assert.Collection(
                results,
                getHashCode =>
                {
                    Assert.True(
                        getHashCode.Status == FidelityCheck.CompileBackStatus.OperandDiff,
                        $"{getHashCode.Status}: {getHashCode.Detail}{Environment.NewLine}{getHashCode.Source}");
                    Assert.Contains("public string Name;", getHashCode.Source);
                    Assert.Contains("public string Value;", getHashCode.Source);
                    Assert.DoesNotContain("public string Name { get; }", getHashCode.Source);
                },
                typedEquals =>
                {
                    Assert.True(
                        typedEquals.Status == FidelityCheck.CompileBackStatus.OperandDiff,
                        $"{typedEquals.Status}: {typedEquals.Detail}{Environment.NewLine}{typedEquals.Source}");
                    Assert.Contains("public bool Equals(Row other)", typedEquals.Source);
                    Assert.Contains("public string Name;", typedEquals.Source);
                    Assert.Contains("public string Value;", typedEquals.Source);
                    Assert.DoesNotContain("public string Name { get; }", typedEquals.Source);
                });
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackSelfType_DistinguishesNestedTypeFromNamespacePeer()
    {
        var assemblyPath = CompileFixture("""
            namespace N;

            public class Container
            {
                public record Row(string Name);
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            using var source = MetadataSource.Open(assemblyPath);
            var reader = pe.GetMetadataReader();
            var nestedHandle = reader.TypeDefinitions
                .Single(handle => TypeResolver.GetFullName(reader, reader.GetTypeDefinition(handle)) == "N.Container.Row");
            var nestedType = reader.GetTypeDefinition(nestedHandle);
            var nestedIdentity = CompileBackTypeIdentity.FromDefinition(reader, nestedType);
            var namespacePeerIdentity = new CompileBackTypeIdentity(
                "N.Container",
                "Row",
                "Row",
                "N.Container.Row",
                "N.Container.Row");
            var function = IrImporter.Import(source, "N.Container.Row", "GetHashCode", publicOnly: false);
            Assert.NotNull(function);
            IrPasses.Run(function, IrPasses.Default, new PassContext(new Stepper(enabled: false)));
            var load = Assert.Single(function.Descendants.OfType<LoadField>(), field => field.Field.BackingPropertyName == "Name");
            var helper = typeof(CompileBackSourceComposer).GetMethod("IsSelfType", BindingFlags.Static | BindingFlags.NonPublic)!;

            Assert.True((bool)helper.Invoke(null, [load.Field.DeclaringType, nestedIdentity])!);
            Assert.False((bool)helper.Invoke(null, [load.Field.DeclaringType, namespacePeerIdentity])!);

            var globalNestedIdentity = new CompileBackTypeIdentity("", "B", "B", "A.B", "A.B");
            var dottedTopLevelType = TypeRef.Definition("fixture", "", "A.B");
            Assert.False((bool)helper.Invoke(null, [dottedTopLevelType, globalNestedIdentity])!);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_PreservesGenericRecordTypedEqualsShell()
    {
        var assemblyPath = CompileFixture("""
            public record Row<T>(T Value);
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row`1", "Equals", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public virtual bool Equals(Row<T> other)", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_RendersAbstractClosurePropertiesWithoutBodies()
    {
        var assemblyPath = CompileFixture("""
            public abstract class Row
            {
                public abstract string Name { get; set; }
                public void SetName(string value) => Name = value;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "SetName", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("public abstract string Name { get; set; }", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void CompileBackSelfTypeSignature_IncludesDeclaringGenericParameters()
    {
        var assemblyPath = CompileFixture("""
            public class Container<T>
            {
                public record Row<U>(T Outer, U Value);
            }
            """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var typeHandle = reader.TypeDefinitions
                .Single(handle => TypeResolver.GetFullName(reader, reader.GetTypeDefinition(handle)) == "Container`1.Row`1");
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var identity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            var helper = typeof(CompileBackSourceComposer).GetMethod(
                "SelfTypeSignature",
                BindingFlags.Static | BindingFlags.NonPublic);

            var selfType = Assert.IsType<string>(helper!.Invoke(null, [reader, typeDef, identity]));

            Assert.Equal("Container<T>.Row<U>", selfType);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_EmitsSignatureMatchedEqualityOperatorPairSibling()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row
            {
                public static bool operator ==(Row left, Row right) => true;
                public static bool operator !=(Row left, Row right) => false;
                public static bool operator ==(Row left, string right) => right == "x";
                public static bool operator !=(Row left, string right) => !(left == right);
                public override bool Equals(object obj) => obj is Row;
                public override int GetHashCode() => 0;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "op_Equality", 1)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("operator ==(Row left, string right)", result.Source);
            Assert.Contains("operator !=(Row left, string right)", result.Source);
            Assert.DoesNotContain("operator !=(Row left, Row right)", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_EmitsNoBodyEqualityOperatorPairSibling()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row
            {
                public static bool operator ==(Row left, Row right) => true;
                [System.Runtime.InteropServices.DllImport("native")]
                public static extern bool operator !=(Row left, Row right);
                public override bool Equals(object obj) => obj is Row;
                public override int GetHashCode() => 0;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "op_Equality", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Contains("operator ==(Row left, Row right)", result.Source);
            Assert.Contains("operator !=(Row left, Row right)", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_PreservesGenericEqualityOperatorReferenceComparison()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row<T>
            {
                public static bool operator ==(Row<T> left, Row<T> right) => (object)left == (object)right;
                public static bool operator !=(Row<T> left, Row<T> right) => (object)left != (object)right;
                public override bool Equals(object obj) => obj is Row<T>;
                public override int GetHashCode() => 0;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row`1", "op_Equality", 0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("return (object)left == (object)right;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_PreservesInParameterEqualityOperatorReferenceComparison()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row
            {
                public static bool operator ==(in Row left, in Row right) => (object)left == (object)right;
                public static bool operator !=(in Row left, in Row right) => (object)left != (object)right;
                public override bool Equals(object obj) => obj is Row;
                public override int GetHashCode() => 0;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "op_Equality", 0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("return (object)(left) == (object)(right);", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_PreservesSpilledLocalEqualityOperatorReferenceComparison()
    {
        var assemblyPath = CompileFixture("""
            public sealed class Row
            {
                public static bool operator ==(Row left, Row right)
                {
                    Row V_0 = right;
                    Row S_256 = left;
                    System.Console.WriteLine(S_256 is null);
                    return (object)S_256 == (object)V_0;
                }

                public static bool operator !=(Row left, Row right) => (object)left != (object)right;
                public override bool Equals(object obj) => obj is Row;
                public override int GetHashCode() => 0;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Row", "op_Equality", 0)]));

            Assert.True(
                result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.Status}: {result.Detail}{Environment.NewLine}{result.Source}");
            Assert.Contains("return (object)S_256 == (object)V_0;", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_RoundTripsGenericTypeTargets()
    {
        var assemblyPath = CompileFixture("""
            public class Box<T>
            {
                private T _value;

                public Box(T value)
                {
                    _value = value;
                }

                public T Value
                {
                    get => _value;
                    set => _value = value;
                }

                public T Echo(T value) => value;
            }
            """);
        try
        {
            var results = await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget("Box`1", ".ctor", 0),
                    new ReturnToSender.RequestedTarget("Box`1", "get_Value", 0),
                    new ReturnToSender.RequestedTarget("Box`1", "set_Value", 0),
                    new ReturnToSender.RequestedTarget("Box`1", "Echo", 0),
                ]);

            Assert.Collection(
                results,
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status),
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status),
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status),
                result => Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status));
            Assert.All(results, result => Assert.Contains("public class Box<T>", result.Source));
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }
}
