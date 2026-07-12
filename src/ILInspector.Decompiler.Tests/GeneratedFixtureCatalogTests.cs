using System.Text.Json;

using ILInspector.DecompilerHarness;
using ILInspector.Instructions;
using ILInspector.MetadataPrimitives;

using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The seed generated-fixture catalogue for #1742: compile small source-shape
/// entries into a temporary library, run the existing compile-back oracle, and
/// report by stable fixture ID instead of only by metadata method name.
/// </summary>
[Trait("Speed", "Slow")]
public class GeneratedFixtureCatalogTests
{
    [Fact]
    public void MinimalCompileBackRungs_ReportExpectedOutcomesByFixtureId()
    {
        var run = GeneratedFixtureRunner.Run(GeneratedFixtureCatalog.MinimalCompileBackRungs);
        string report = GeneratedFixtureRunner.FormatReport(run);

        Assert.True(run.Passed, report);
        Assert.Contains("GENERATED FIXTURE LADDER", report);

        AssertTarget(
            run,
            "minimal.property.literal",
            "GeneratedFixtures.MinimalPropertyLiteral.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.property.literal",
            "GeneratedFixtures.MinimalPropertyLiteral.Class1",
            "get_Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);

        AssertTarget(
            run,
            "minimal.primary-ctor.field-init",
            "GeneratedFixtures.MinimalPrimaryCtorFieldInit.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.primary-ctor.field-init",
            "GeneratedFixtures.MinimalPrimaryCtorFieldInit.Class1",
            "get_Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.ctor-field.getter",
            "GeneratedFixtures.MinimalCtorFieldGetter.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.ctor-field.getter",
            "GeneratedFixtures.MinimalCtorFieldGetter.Class1",
            "get_Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.auto-property.getter",
            "GeneratedFixtures.MinimalAutoPropertyGetter.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.auto-property.getter",
            "GeneratedFixtures.MinimalAutoPropertyGetter.Class1",
            "get_Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.method-call.same-type",
            "GeneratedFixtures.MinimalMethodCallSameType.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.method-call.same-type",
            "GeneratedFixtures.MinimalMethodCallSameType.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.method-call.same-type",
            "GeneratedFixtures.MinimalMethodCallSameType.Class1",
            "Method2",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-method-call",
            "GeneratedFixtures.MinimalStaticMethodCall.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-method-call",
            "GeneratedFixtures.MinimalStaticMethodCall.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-method-call",
            "GeneratedFixtures.MinimalStaticMethodCall.Class1",
            "Helper",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-constructor",
            "GeneratedFixtures.MinimalStaticConstructor.Class1",
            ".cctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-constructor",
            "GeneratedFixtures.MinimalStaticConstructor.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-constructor",
            "GeneratedFixtures.MinimalStaticConstructor.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-class",
            "GeneratedFixtures.MinimalStaticClass.Class1",
            ".cctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-class",
            "GeneratedFixtures.MinimalStaticClass.Class1",
            "get_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.static-class",
            "GeneratedFixtures.MinimalStaticClass.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.interface-implementation",
            "GeneratedFixtures.MinimalInterfaceImplementation.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.interface-implementation",
            "GeneratedFixtures.MinimalInterfaceImplementation.Class1",
            "GetValue",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.object-initializer",
            "GeneratedFixtures.MinimalObjectInitializer.Helper",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.object-initializer",
            "GeneratedFixtures.MinimalObjectInitializer.Helper",
            "get_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.object-initializer",
            "GeneratedFixtures.MinimalObjectInitializer.Helper",
            "set_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.object-initializer",
            "GeneratedFixtures.MinimalObjectInitializer.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.object-initializer",
            "GeneratedFixtures.MinimalObjectInitializer.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.collection-initializer",
            "GeneratedFixtures.MinimalCollectionInitializer.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.collection-initializer",
            "GeneratedFixtures.MinimalCollectionInitializer.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-methods",
            "GeneratedFixtures.MinimalGenericMethods.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-methods",
            "GeneratedFixtures.MinimalGenericMethods.Class1",
            "Echo",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-methods",
            "GeneratedFixtures.MinimalGenericMethods.Class1",
            "Choose",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-methods",
            "GeneratedFixtures.MinimalGenericMethods.Class1",
            "Create",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-methods",
            "GeneratedFixtures.MinimalGenericMethods.Class1",
            "DefaultValue",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-methods",
            "GeneratedFixtures.MinimalGenericMethods.Class1",
            "Comparable",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-type",
            "GeneratedFixtures.MinimalGenericType.Box`1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-type",
            "GeneratedFixtures.MinimalGenericType.Box`1",
            "get_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-type",
            "GeneratedFixtures.MinimalGenericType.Box`1",
            "set_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-type",
            "GeneratedFixtures.MinimalGenericType.Box`1",
            "Echo",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-type-constraints",
            "GeneratedFixtures.MinimalGenericTypeConstraints.Factory`1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-type-constraints",
            "GeneratedFixtures.MinimalGenericTypeConstraints.Factory`1",
            "Create",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.generic-type-constraints",
            "GeneratedFixtures.MinimalGenericTypeConstraints.Factory`1",
            "Echo",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-modifiers",
            "GeneratedFixtures.MinimalParameterModifiers.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-modifiers",
            "GeneratedFixtures.MinimalParameterModifiers.Class1",
            "Increment",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-modifiers",
            "GeneratedFixtures.MinimalParameterModifiers.Class1",
            "Set",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-modifiers",
            "GeneratedFixtures.MinimalParameterModifiers.Class1",
            "Read",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-modifiers",
            "GeneratedFixtures.MinimalParameterModifiers.Class1",
            "Sum",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.default-parameters",
            "GeneratedFixtures.MinimalDefaultParameters.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.default-parameters",
            "GeneratedFixtures.MinimalDefaultParameters.Class1",
            "Add",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.default-parameters",
            "GeneratedFixtures.MinimalDefaultParameters.Class1",
            "Format",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.default-parameters",
            "GeneratedFixtures.MinimalDefaultParameters.Class1",
            "DecimalDefault",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.default-parameters",
            "GeneratedFixtures.MinimalDefaultParameters.Class1",
            "EnumDefault",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.default-parameters",
            "GeneratedFixtures.MinimalDefaultParameters.Class1",
            "DateTimeDefault",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-attributes",
            "GeneratedFixtures.MinimalParameterAttributes.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-attributes",
            "GeneratedFixtures.MinimalParameterAttributes.Class1",
            "Length",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-attributes",
            "GeneratedFixtures.MinimalParameterAttributes.Class1",
            "Copy",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-attributes",
            "GeneratedFixtures.MinimalParameterAttributes.Class1",
            "Update",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-marshalling",
            "GeneratedFixtures.MinimalParameterMarshalling.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-marshalling",
            "GeneratedFixtures.MinimalParameterMarshalling.Class1",
            "I4",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-marshalling",
            "GeneratedFixtures.MinimalParameterMarshalling.Class1",
            "LpStr",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-marshalling",
            "GeneratedFixtures.MinimalParameterMarshalling.Class1",
            "Bool",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-marshalling",
            "GeneratedFixtures.MinimalParameterMarshalling.Class1",
            "Sum",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-marshalling",
            "GeneratedFixtures.MinimalParameterMarshalling.Class1",
            "FirstFour",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-marshalling",
            "GeneratedFixtures.MinimalParameterMarshalling.Class1",
            "PlainArray",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-marshalling",
            "GeneratedFixtures.MinimalParameterMarshalling.Class1",
            "FixedArray",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.parameter-marshalling",
            "GeneratedFixtures.MinimalParameterMarshalling.Class1",
            "ZeroSizedArray",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.return-parameter-metadata",
            "GeneratedFixtures.MinimalReturnParameterMetadata.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.return-parameter-metadata",
            "GeneratedFixtures.MinimalReturnParameterMetadata.Class1",
            "I4",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.return-parameter-metadata",
            "GeneratedFixtures.MinimalReturnParameterMetadata.Class1",
            "Text",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.return-parameter-metadata",
            "GeneratedFixtures.MinimalReturnParameterMetadata.Class1",
            "NotNullText",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.return-parameter-metadata",
            "GeneratedFixtures.MinimalReturnParameterMetadata.Class1",
            "FallbackSignature",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.property-return-metadata",
            "GeneratedFixtures.MinimalPropertyReturnMetadata.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.property-return-metadata",
            "GeneratedFixtures.MinimalPropertyReturnMetadata.Class1",
            "get_Text",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.property-return-metadata",
            "GeneratedFixtures.MinimalPropertyReturnMetadata.Class1",
            "get_Number",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.property-return-metadata",
            "GeneratedFixtures.MinimalPropertyReturnMetadata.Class1",
            "get_Item",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.member-attributes",
            "GeneratedFixtures.MinimalMemberAttributes.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.member-attributes",
            "GeneratedFixtures.MinimalMemberAttributes.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.member-attributes",
            "GeneratedFixtures.MinimalMemberAttributes.Class1",
            "ObsoleteMethod",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.member-attributes",
            "GeneratedFixtures.MinimalMemberAttributes.Class1",
            "get_Text",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.type-attributes",
            "GeneratedFixtures.MinimalTypeAttributes.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.type-attributes",
            "GeneratedFixtures.MinimalTypeAttributes.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.if-else",
            "GeneratedFixtures.MinimalIfElse.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.if-else",
            "GeneratedFixtures.MinimalIfElse.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.integer-addition",
            "GeneratedFixtures.MinimalIntegerAddition.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.integer-addition",
            "GeneratedFixtures.MinimalIntegerAddition.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.struct-members",
            "GeneratedFixtures.MinimalStructMembers.Counter",
            "get_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.struct-members",
            "GeneratedFixtures.MinimalStructMembers.Counter",
            "set_Value",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.struct-members",
            "GeneratedFixtures.MinimalStructMembers.Counter",
            "Add",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.struct-constructor.field-init",
            "GeneratedFixtures.MinimalStructConstructorFieldInit.Counter",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.array-index",
            "GeneratedFixtures.MinimalArrayIndex.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.array-index",
            "GeneratedFixtures.MinimalArrayIndex.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.array-length",
            "GeneratedFixtures.MinimalArrayLength.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.array-length",
            "GeneratedFixtures.MinimalArrayLength.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.indexer.getter",
            "GeneratedFixtures.MinimalIndexerGetter.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.indexer.getter",
            "GeneratedFixtures.MinimalIndexerGetter.Class1",
            "get_Item",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.indexer.setter",
            "GeneratedFixtures.MinimalIndexerSetter.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.indexer.setter",
            "GeneratedFixtures.MinimalIndexerSetter.Class1",
            "get_Item",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.indexer.setter",
            "GeneratedFixtures.MinimalIndexerSetter.Class1",
            "set_Item",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.string-length",
            "GeneratedFixtures.MinimalStringLength.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.string-length",
            "GeneratedFixtures.MinimalStringLength.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.null-coalesce",
            "GeneratedFixtures.MinimalNullCoalesce.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.null-coalesce",
            "GeneratedFixtures.MinimalNullCoalesce.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.try-finally",
            "GeneratedFixtures.MinimalTryFinally.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.try-finally",
            "GeneratedFixtures.MinimalTryFinally.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.using-dispose",
            "GeneratedFixtures.MinimalUsingDispose.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.using-dispose",
            "GeneratedFixtures.MinimalUsingDispose.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.foreach-array",
            "GeneratedFixtures.MinimalForeachArray.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.foreach-array",
            "GeneratedFixtures.MinimalForeachArray.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.for-loop",
            "GeneratedFixtures.MinimalForLoop.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.for-loop",
            "GeneratedFixtures.MinimalForLoop.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.while-loop",
            "GeneratedFixtures.MinimalWhileLoop.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.while-loop",
            "GeneratedFixtures.MinimalWhileLoop.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.do-while",
            "GeneratedFixtures.MinimalDoWhileLoop.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.do-while",
            "GeneratedFixtures.MinimalDoWhileLoop.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.switch-int",
            "GeneratedFixtures.MinimalSwitchInt.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            run,
            "minimal.switch-int",
            "GeneratedFixtures.MinimalSwitchInt.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        Assert.Contains("minimal.property.literal", report);
        Assert.Contains("minimal.primary-ctor.field-init", report);
        Assert.Contains("minimal.ctor-field.getter", report);
        Assert.Contains("minimal.auto-property.getter", report);
        Assert.Contains("minimal.method-call.same-type", report);
        Assert.Contains("minimal.member-attributes", report);
        Assert.Contains("minimal.type-attributes", report);
        Assert.Contains("minimal.static-method-call", report);
        Assert.Contains("minimal.static-class", report);
        Assert.Contains("minimal.static-constructor", report);
        Assert.Contains("minimal.interface-implementation", report);
        Assert.Contains("minimal.object-initializer", report);
        Assert.Contains("minimal.collection-initializer", report);
        Assert.Contains("minimal.generic-methods", report);
        Assert.Contains("minimal.generic-type", report);
        Assert.Contains("minimal.generic-type-constraints", report);
        Assert.Contains("minimal.parameter-modifiers", report);
        Assert.Contains("minimal.default-parameters", report);
        Assert.Contains("minimal.parameter-attributes", report);
        Assert.Contains("minimal.parameter-marshalling", report);
        Assert.Contains("minimal.return-parameter-metadata", report);
        Assert.Contains("minimal.property-return-metadata", report);
        Assert.Contains("minimal.if-else", report);
        Assert.Contains("minimal.integer-addition", report);
        Assert.Contains("minimal.struct-members", report);
        Assert.Contains("minimal.struct-constructor.field-init", report);
        Assert.Contains("minimal.array-index", report);
        Assert.Contains("minimal.array-length", report);
        Assert.Contains("minimal.indexer.getter", report);
        Assert.Contains("minimal.indexer.setter", report);
        Assert.Contains("minimal.string-length", report);
        Assert.Contains("minimal.null-coalesce", report);
        Assert.Contains("minimal.try-finally", report);
        Assert.Contains("minimal.using-dispose", report);
        Assert.Contains("minimal.foreach-array", report);
        Assert.Contains("minimal.for-loop", report);
        Assert.Contains("minimal.while-loop", report);
        Assert.Contains("minimal.do-while", report);
        Assert.Contains("minimal.switch-int", report);
        Assert.Contains("minimal.conditional-expression-shape-frontier", report);
        Assert.DoesNotContain("minimal.switch-two-case-lowers-if", report);
        Assert.Contains("decompiler=Full", report);
        Assert.Contains("compile-back=Exact", report);
        Assert.Contains("shape=ForStatement", report);
        Assert.Contains("shape=ElementAccessExpression", report);

        AssertShape(run, "minimal.integer-addition", "Method1", SyntaxKind.AddExpression);
        AssertShape(run, "minimal.array-index", "Method1", SyntaxKind.ElementAccessExpression);
        AssertShape(run, "minimal.array-length", "Method1", SyntaxKind.SimpleMemberAccessExpression);
        AssertShape(run, "minimal.string-length", "Method1", SyntaxKind.SimpleMemberAccessExpression);
        AssertShape(run, "minimal.null-coalesce", "Method1", SyntaxKind.CoalesceExpression);
        AssertShape(run, "minimal.try-finally", "Method1", SyntaxKind.TryStatement);
        AssertShape(run, "minimal.foreach-array", "Method1", SyntaxKind.ForEachStatement);
        AssertShape(run, "minimal.for-loop", "Method1", SyntaxKind.ForStatement);
        AssertShape(run, "minimal.while-loop", "Method1", SyntaxKind.WhileStatement);
        AssertShape(run, "minimal.do-while", "Method1", SyntaxKind.DoStatement);
        AssertShape(run, "minimal.switch-int", "Method1", SyntaxKind.SwitchStatement);
        AssertShape(run, "minimal.conditional-expression-shape-frontier", "Method1", SyntaxKind.ReturnStatement);
    }

    [Fact]
    public void CatalogueSelection_MatchesExactIdOrPrefix()
    {
        Assert.Equal(
            ["minimal.property.literal"],
            GeneratedFixtureCatalog.Select("minimal.property.literal").Select(fixture => fixture.Id).ToArray());

        Assert.Equal(
            ["rts.attribute-shell"],
            GeneratedFixtureCatalog.Select("rts").Select(fixture => fixture.Id).Order(StringComparer.Ordinal).ToArray());

        Assert.Equal(
            [
                "record.equality-operators",
                "record.field-read-helpers",
                "record.generic-typed-equals",
                "record.nested-generic-typed-equals",
                "record.property-getter",
                "record.struct-field-read-helpers",
                "record.virtual-helpers",
            ],
            GeneratedFixtureCatalog.Select("record").Select(fixture => fixture.Id).Order(StringComparer.Ordinal).ToArray());

        Assert.Equal(
            [
                "minimal.array-index",
                "minimal.array-length",
                "minimal.auto-property.getter",
                "minimal.collection-initializer",
                "minimal.conditional-expression-shape-frontier",
                "minimal.ctor-field.getter",
                "minimal.default-parameters",
                "minimal.do-while",
                "minimal.for-loop",
                "minimal.foreach-array",
                "minimal.generic-methods",
                "minimal.generic-type",
                "minimal.generic-type-constraints",
                "minimal.if-else",
                "minimal.indexer.getter",
                "minimal.indexer.setter",
                "minimal.integer-addition",
                "minimal.interface-implementation",
                "minimal.member-attributes",
                "minimal.method-call.same-type",
                "minimal.null-coalesce",
                "minimal.object-initializer",
                "minimal.parameter-attributes",
                "minimal.parameter-marshalling",
                "minimal.parameter-modifiers",
                "minimal.primary-ctor.field-init",
                "minimal.property-return-metadata",
                "minimal.property.literal",
                "minimal.return-parameter-metadata",
                "minimal.static-class",
                "minimal.static-constructor",
                "minimal.static-method-call",
                "minimal.string-length",
                "minimal.struct-constructor.field-init",
                "minimal.struct-members",
                "minimal.switch-int",
                "minimal.switch-two-case-lowers-if",
                "minimal.try-finally",
                "minimal.type-attributes",
                "minimal.using-dispose",
                "minimal.while-loop",
            ],
            GeneratedFixtureCatalog.Select("minimal").Select(fixture => fixture.Id).Order(StringComparer.Ordinal).ToArray());

        Assert.Empty(GeneratedFixtureCatalog.Select("missing"));
    }

    [Fact]
    public void CatalogueListJson_ContainsFixtureIdsAndExpectedStatuses()
    {
        string json = GeneratedFixtureRunner.FormatListJson(GeneratedFixtureCatalog.Catalog);
        string list = GeneratedFixtureRunner.FormatList(GeneratedFixtureCatalog.Catalog);

        Assert.Contains("compile-back=Exact", list);
        Assert.Contains("shape=ElementAccessExpression", list);
        Assert.Contains("shape=none", list);

        using var document = JsonDocument.Parse(json);
        var fixtures = document.RootElement.EnumerateArray().ToArray();
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.property.literal");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.ctor-field.getter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.auto-property.getter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.conditional-expression-shape-frontier");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.method-call.same-type");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.member-attributes");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.type-attributes");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.static-method-call");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.static-class");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.static-constructor");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.interface-implementation");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.object-initializer");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.collection-initializer");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.generic-methods");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.generic-type");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.generic-type-constraints");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.parameter-modifiers");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.default-parameters");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.parameter-attributes");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.parameter-marshalling");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.return-parameter-metadata");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.property-return-metadata");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.if-else");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.integer-addition");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.struct-members");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.struct-constructor.field-init");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.array-index");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.array-length");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.indexer.getter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.indexer.setter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.string-length");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.null-coalesce");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.try-finally");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.using-dispose");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.foreach-array");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.for-loop");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.while-loop");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.do-while");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.switch-int");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "minimal.switch-two-case-lowers-if");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "record.property-getter");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "record.equality-operators");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "record.virtual-helpers");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "record.field-read-helpers");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "record.struct-field-read-helpers");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "record.generic-typed-equals");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "record.nested-generic-typed-equals");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("Id").GetString() == "rts.attribute-shell");

        var primaryCtor = Assert.Single(fixtures,
            fixture => fixture.GetProperty("Id").GetString() == "minimal.primary-ctor.field-init");
        var ctor = Assert.Single(primaryCtor.GetProperty("Targets").EnumerateArray(),
            target => target.GetProperty("Method").GetString() == ".ctor");
        Assert.Equal("Exact", ctor.GetProperty("ExpectedStatus").GetString());
        Assert.Equal(JsonValueKind.Null, ctor.GetProperty("ExpectedShape").ValueKind);
        Assert.False(ctor.GetProperty("IsFrontier").GetBoolean());

        var arrayIndex = Assert.Single(fixtures,
            fixture => fixture.GetProperty("Id").GetString() == "minimal.array-index");
        var arrayIndexMethod = Assert.Single(arrayIndex.GetProperty("Targets").EnumerateArray(),
            target => target.GetProperty("Method").GetString() == "Method1");
        Assert.Equal("ElementAccessExpression", arrayIndexMethod.GetProperty("ExpectedShape").GetString());

        var conditionalFrontier = Assert.Single(fixtures,
            fixture => fixture.GetProperty("Id").GetString() == "minimal.conditional-expression-shape-frontier");
        var conditionalTarget = Assert.Single(conditionalFrontier.GetProperty("Targets").EnumerateArray(),
            target => target.GetProperty("Method").GetString() == "Method1");
        Assert.Equal("ReturnStatement", conditionalTarget.GetProperty("ExpectedShape").GetString());
        Assert.Equal("ConditionalExpression", conditionalTarget.GetProperty("FrontierShape").GetString());
        Assert.True(conditionalTarget.GetProperty("IsFrontier").GetBoolean());
    }

    [Fact]
    public void ReturnToSenderCatalogReport_ClassifiesSupportedAndSkippedTargets()
    {
        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog(
            [
                GeneratedFixtureCatalog.MinimalPropertyLiteral,
                GeneratedFixtureCatalog.MinimalMethodCallSameType,
            ]);
        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);

        Assert.True(run.Passed, report);
        Assert.Contains("RETURNTOSENDER GENERATED FIXTURE FRONTIER", report);
        Assert.Contains("Passed : 2", report);
        Assert.Contains("Skipped: 0", report);
        Assert.Contains("Research evidence:", report);
        Assert.Contains("rts.status.pass:", report);
        Assert.DoesNotContain("constructor-target", report);

        var propertyGetter = Assert.Single(run.Results, result =>
            result.FixtureId == "minimal.property.literal"
            && result.Method == "get_Method1");
        Assert.Equal(GeneratedFixtureReturnToSenderStatus.Pass, propertyGetter.Status);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, propertyGetter.ActualStatus);
        Assert.Null(propertyGetter.IlDiffDiagnostic);
        Assert.NotNull(propertyGetter.MemberAnchor);
        Assert.StartsWith("Method1~", propertyGetter.MemberAnchor.StableSelector, StringComparison.Ordinal);
        Assert.Equal("P:GeneratedFixtures.MinimalPropertyLiteral.Class1.Method1", propertyGetter.MemberAnchor.CanonicalSignature);

        Assert.Contains(run.Results, result =>
            result.FixtureId == "minimal.method-call.same-type"
            && result.Method == "Method1"
            && result.Status == GeneratedFixtureReturnToSenderStatus.Pass);
        Assert.Contains(run.Results, result =>
            result.FixtureId == "minimal.method-call.same-type"
            && result.Method == "Method2"
            && result.Status == GeneratedFixtureReturnToSenderStatus.Pass);

        string json = GeneratedFixtureRunner.FormatReturnToSenderCatalogJson(run);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("Passed").GetBoolean());
        Assert.Contains(document.RootElement.GetProperty("Results").EnumerateArray(),
            result => result.GetProperty("Status").GetString() == "Pass");
        Assert.Contains(document.RootElement.GetProperty("Results").EnumerateArray(),
            result => result.GetProperty("Method").GetString() == "get_Method1"
                && result.GetProperty("MemberAnchor").GetProperty("StableSelector").GetString()!.StartsWith("Method1~", StringComparison.Ordinal));
        var researchDiff = document.RootElement.GetProperty("ResearchDiff");
        Assert.Contains(researchDiff.GetProperty("Changes").EnumerateArray(),
            change => change.GetProperty("DescriptorId").GetString() == "rts.status.pass");
        Assert.DoesNotContain(document.RootElement.GetProperty("Results").EnumerateArray(),
            result => result.GetProperty("Reason").GetString() == "constructor-target");
    }

    [Fact]
    public void ReturnToSenderCatalogReport_IncludesIlDiffDiagnosticsWhenPresent()
    {
        var run = new GeneratedFixtureReturnToSenderRunResult(
            ProjectDirectory: "",
            AssemblyPath: "",
            Results:
            [
                new GeneratedFixtureReturnToSenderResult(
                    "test.il-diff-diagnostic",
                    "TestType",
                    "Method1",
                    Overload: 0,
                    GeneratedFixtureReturnToSenderStatus.Fail,
                    FidelityCheck.CompileBackStatus.OpcodeDiff,
                    "opcode-diff",
                    Detail: null,
                    IlDiffDiagnostic: new IlDiffDisplayResult(
                        Failure: null,
                        Rows:
                        [
                            new IlDiffDisplayRow(
                                0,
                                IlDiffKind.Remove,
                                "-",
                                0,
                                "IL_0000",
                                "ldc.i4",
                                IlOperandIdentityKind.Immediate,
                                "1",
                                "ldc.i4 1",
                                "Removed IL operation 'ldc.i4 1'"),
                            new IlDiffDisplayRow(
                                0,
                                IlDiffKind.Add,
                                "+",
                                0,
                                "IL_0000",
                                "ldc.i4",
                                IlOperandIdentityKind.Immediate,
                                "2",
                                "ldc.i4 2",
                                "Added IL operation 'ldc.i4 2'"),
                        ]),
                    IlDiff: null,
                    MemberAnchor: new MemberAnchor(
                        "Method1~abcdef1234",
                        "M:TestType.Method1()",
                        "abcdef1234",
                        "TestType",
                        "Method1"),
                    FaultIsolation: null,
                    ClosureEvidence: new ReturnToSenderClosureEvidence(
                        RequiredTypes: 2,
                        RequiredMembers: 1,
                        RoslynRecoveredTypes: 1,
                        RoslynRecoveredMemberSurfaces: 1,
                        RoslynFallbacks:
                        [
                            new ReturnToSenderRoslynFallback("CS0103", "closure-root", 1),
                        ],
                        Requirements:
                        [
                            new ReturnToSenderClosureRequirement(
                                "TestType",
                                RequiredMembers: 1,
                                RoslynRecovered: true,
                                RoslynRecoveredMemberSurface: true,
                                Facts: ["roslyn/closure-root: CS0103: Helper"]),
                        ]),
                    IsFrontier: false,
                    Note: null),
            ]);

        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);
        var view = ReturnToSenderCatalogReport.Build(run, maxExamples: 10);

        Assert.Contains("il-diff:", report);
        Assert.Contains("h0 - IL_0000 ldc.i4 1", report);
        Assert.Contains("h0 + IL_0000 ldc.i4 2", report);
        Assert.Contains("member: Method1~abcdef1234  canonical=M:TestType.Method1()", report);
        Assert.Contains("closure: types=2 members=1 roslyn-types=1 roslyn-member-surfaces=1", report);
        Assert.Contains("roslyn-fallbacks=CS0103/closure-root:1", report);
        Assert.Contains("Roslyn fallback evidence:", report);
        Assert.Contains("CS0103/closure-root: 1", report);
        Assert.Contains("Research evidence:", report);
        Assert.Contains("rts.status.fail: 1", report);
        Assert.Contains("il.operation.added: 1", report);
        Assert.Contains("il.operation.removed: 1", report);
        Assert.Contains("Actionable subjects (first 1", report);
        Assert.Contains("Method1~abcdef1234  rts=OpcodeDiff  detail=opcode-diff", report);
        Assert.Contains("      il.operation.added: 1", report);
        Assert.Contains("      il.operation.removed: 1", report);
        Assert.Equal(1, view.Fixtures.Failed);
        Assert.Equal(1, view.Targets.Failed);
        Assert.NotNull(view.Research);
        Assert.Single(view.Research.Summary.ActionableSubjects);
        var fallback = Assert.Single(view.RoslynFallbacks);
        Assert.Equal("CS0103/closure-root", fallback.Key);
        Assert.Equal(1, fallback.Count);
        Assert.Single(view.FailedTargetBuckets);
        Assert.Single(view.FailedFixtures);

        string markout = GeneratedFixtureRunner.FormatReturnToSenderCatalogMarkout(run, maxExamples: 10);
        Assert.Contains("# ReturnToSender Catalog", markout);
        Assert.Contains("## Summary", markout);
        Assert.Contains("## Research evidence", markout);
        Assert.Contains("## Actionable subjects", markout);
        Assert.Contains("## Roslyn fallback evidence", markout);
        Assert.Contains("CS0103/closure-root", markout);
        Assert.Contains("Method1~abcdef1234", markout);
        Assert.Contains("il.operation.added: 1", markout);
        Assert.Contains("IL display:", markout);
        Assert.Contains("- il.operation.removed: h0 - IL_0000 ldc.i4 1", markout);
        Assert.Contains("IL diff:", markout);
        Assert.Contains("- h0 + IL_0000 ldc.i4 2", markout);
        Assert.Contains("      il-display:", report);
        Assert.Contains("        il.operation.removed: h0 - IL_0000 ldc.i4 1", report);
        Assert.Contains("        il.operation.added: h0 + IL_0000 ldc.i4 2", report);

        string json = GeneratedFixtureRunner.FormatReturnToSenderCatalogJson(run);
        using var document = JsonDocument.Parse(json);
        var fallbackJson = Assert.Single(document.RootElement.GetProperty("RoslynFallbacks").EnumerateArray());
        Assert.Equal("CS0103/closure-root", fallbackJson.GetProperty("Key").GetString());
        Assert.Equal(1, fallbackJson.GetProperty("Count").GetInt32());
        var actionable = Assert.Single(document.RootElement
            .GetProperty("ResearchSummary")
            .GetProperty("ActionableSubjects")
            .EnumerateArray());
        var ilEvidence = actionable.GetProperty("IlEvidence").EnumerateArray().ToArray();
        Assert.Contains(ilEvidence, evidence =>
            evidence.GetProperty("ChangeId").GetString() == "il.operation.removed"
            && evidence.GetProperty("Rows").EnumerateArray().Single().GetProperty("UnifiedLine").GetString() == "h0 - IL_0000 ldc.i4 1");
        Assert.Contains(ilEvidence, evidence =>
            evidence.GetProperty("ChangeId").GetString() == "il.operation.added"
            && evidence.GetProperty("Rows").EnumerateArray().Single().GetProperty("UnifiedLine").GetString() == "h0 + IL_0000 ldc.i4 2");
    }

    [Fact]
    public void ReturnToSenderCatalogReport_IncludesResearchIlFailureRows()
    {
        var failure = new IlDiffDisplayFailureRow(
            IlDiffFailureKind.NewBodyMissing,
            "new body missing",
            Side: "new",
            Detail: "method has no body");
        var run = new GeneratedFixtureReturnToSenderRunResult(
            ProjectDirectory: "",
            AssemblyPath: "",
            Results:
            [
                new GeneratedFixtureReturnToSenderResult(
                    "test.il-diff-failure",
                    "TestType",
                    "Method1",
                    Overload: 0,
                    GeneratedFixtureReturnToSenderStatus.Fail,
                    FidelityCheck.CompileBackStatus.OpcodeDiff,
                    "opcode-diff",
                    Detail: null,
                    IlDiffDiagnostic: new IlDiffDisplayResult(
                        Failure: failure.UnifiedLine,
                        Rows: [],
                        FailureRows: [failure]),
                    IlDiff: null,
                    MemberAnchor: new MemberAnchor(
                        "Method1~abcdef1234",
                        "M:TestType.Method1()",
                        "abcdef1234",
                        "TestType",
                        "Method1"),
                    FaultIsolation: null,
                    ClosureEvidence: null,
                    IsFrontier: false,
                    Note: null),
            ]);

        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);

        Assert.Contains("      il-display:", report);
        Assert.Contains("        il.diff.new-body-missing: IL diff failed: new body missing", report);

        string json = GeneratedFixtureRunner.FormatReturnToSenderCatalogJson(run);
        using var document = JsonDocument.Parse(json);
        var actionable = Assert.Single(document.RootElement
            .GetProperty("ResearchSummary")
            .GetProperty("ActionableSubjects")
            .EnumerateArray());
        var evidence = Assert.Single(actionable.GetProperty("IlEvidence").EnumerateArray());
        Assert.Equal("il.diff.new-body-missing", evidence.GetProperty("ChangeId").GetString());
        var failureJson = evidence.GetProperty("Failure");
        Assert.Equal("NewBodyMissing", failureJson.GetProperty("Kind").GetString());
        Assert.Equal("new body missing", failureJson.GetProperty("Message").GetString());
        Assert.Equal("new", failureJson.GetProperty("Side").GetString());
        Assert.Equal("method has no body", failureJson.GetProperty("Detail").GetString());
        Assert.Equal("IL diff failed: new body missing", failureJson.GetProperty("UnifiedLine").GetString());
    }

    [Fact]
    public void ReturnToSenderRecordCatalog_CoversGeneratedRecordHelpers()
    {
        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog(
            GeneratedFixtureCatalog.Select("record"));
        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);

        Assert.True(run.Passed, report);
        Assert.Equal(12, run.Results.Count);
        Assert.All(run.Results, result =>
        {
            Assert.Equal(GeneratedFixtureReturnToSenderStatus.Pass, result.Status);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.ActualStatus);
        });
        Assert.Contains(run.Results, result =>
            result.FixtureId == "record.equality-operators" &&
            result.Method == "op_Equality");
        Assert.Contains(run.Results, result =>
            result.FixtureId == "record.field-read-helpers" &&
            result.Method == "Equals" &&
            result.Overload == 1);
        Assert.Contains(run.Results, result =>
            result.FixtureId == "record.struct-field-read-helpers" &&
            result.Method == "Equals" &&
            result.Overload == 1);
        Assert.Contains(run.Results, result =>
            result.FixtureId == "record.generic-typed-equals" &&
            result.Type == "RecordGenericTypedEqualsRow`1");
        Assert.Contains(run.Results, result =>
            result.FixtureId == "record.nested-generic-typed-equals" &&
            result.Type == "RecordNestedGenericTypedEqualsContainer`1.Row`1");
        Assert.Contains("record.generic-typed-equals", report);
        Assert.Contains("record.nested-generic-typed-equals", report);
        Assert.DoesNotContain("Skipped target reasons", report);
        Assert.DoesNotContain("Failed target buckets", report);
    }

    [Fact]
    public void ReturnToSenderRtsCatalog_CoversAttributeShellBaseType()
    {
        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog(
            GeneratedFixtureCatalog.Select("rts.attribute-shell"));
        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);

        Assert.True(run.Passed, report);
        Assert.Equal(2, run.Results.Count);
        Assert.All(run.Results, result =>
        {
            Assert.Equal(GeneratedFixtureReturnToSenderStatus.Pass, result.Status);
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.ActualStatus);
        });
        Assert.Contains(run.Results, result =>
            result.FixtureId == "rts.attribute-shell" &&
            result.Method == ".ctor");
        Assert.Contains(run.Results, result =>
            result.FixtureId == "rts.attribute-shell" &&
            result.Method == "get_FeatureType");
        Assert.DoesNotContain("CS0641", report);
    }

    [Fact]
    public void ReturnToSenderRecordCatalog_TargetBodyFragmentsDoNotMatchShellSource()
    {
        var shellOnlyFragment = new GeneratedFixtureDefinition(
            "test.record-shell-only-fragment",
            """
            public record ShellOnlyFragmentRecord(string Name, string Value);
            """,
            [
                new(
                    "ShellOnlyFragmentRecord",
                    "GetHashCode",
                    FidelityCheck.CompileBackStatus.Exact,
                    ExpectedTargetBodyFragments:
                    [
                        "public string Name;",
                    ]),
            ],
            ["test", "record"]);

        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog([shellOnlyFragment]);
        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);
        var result = Assert.Single(run.Results);

        Assert.False(run.Passed, report);
        Assert.Equal(GeneratedFixtureReturnToSenderStatus.Fail, result.Status);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.ActualStatus);
        Assert.Equal("target-body-fragment-missing", result.Reason);
        Assert.Contains("missing expected target body fragment: public string Name;", result.Detail);
    }

    [Fact]
    public void ReturnToSenderCatalog_NonExactRowsKeepFailureReasonBeforeBodyFragments()
    {
        var opcodeDiffWithBodyFragment = new GeneratedFixtureDefinition(
            "test.non-exact-body-fragment",
            """
            public class NonExactBodyFragment
            {
                public string Method1(int value)
                {
                    switch (value)
                    {
                        case 0:
                            return "zero";
                        case 1:
                            return "one";
                        default:
                            return "many";
                    }
                }
            }
            """,
            [
                new(
                    "NonExactBodyFragment",
                    "Method1",
                    FidelityCheck.CompileBackStatus.OpcodeDiff,
                    IsFrontier: true,
                    ExpectedTargetBodyFragments:
                    [
                        "fragment that is absent from the target body",
                    ]),
            ],
            ["test"]);

        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog([opcodeDiffWithBodyFragment]);
        string report = GeneratedFixtureRunner.FormatReturnToSenderCatalogReport(run, maxExamples: 10);
        var result = Assert.Single(run.Results);

        Assert.False(run.Passed, report);
        Assert.Equal(GeneratedFixtureReturnToSenderStatus.Fail, result.Status);
        Assert.Equal(FidelityCheck.CompileBackStatus.OpcodeDiff, result.ActualStatus);
        Assert.Equal("opcode-diff", result.Reason);
        Assert.NotNull(result.IlDiffDiagnostic);
        Assert.NotEmpty(result.IlDiffDiagnostic.Rows);
        Assert.Contains(result.IlDiffDiagnostic.Rows, row => row.Offset.StartsWith("IL_", StringComparison.Ordinal));
        Assert.DoesNotContain("target-body-fragment-missing", report);

        string json = GeneratedFixtureRunner.FormatReturnToSenderCatalogJson(run);
        using var document = JsonDocument.Parse(json);
        var jsonResult = Assert.Single(document.RootElement.GetProperty("Results").EnumerateArray());
        var rows = jsonResult.GetProperty("IlDiffDiagnostic").GetProperty("Rows").EnumerateArray().ToArray();
        Assert.NotEmpty(rows);
        Assert.Contains(rows, row =>
            row.GetProperty("Offset").GetString()?.StartsWith("IL_", StringComparison.Ordinal) == true
            && row.GetProperty("OpcodeFamily").GetString() is { Length: > 0 });
        var summary = document.RootElement.GetProperty("ResearchSummary");
        Assert.Equal(1, summary.GetProperty("FailingMembers").GetInt32());
        Assert.Equal(1, summary.GetProperty("OpcodeDiffMembers").GetInt32());
        var actionable = Assert.Single(summary.GetProperty("ActionableSubjects").EnumerateArray());
        Assert.StartsWith("Method1~", actionable.GetProperty("SubjectId").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CompilerLoweringFrontier_IsSelectableButNotInDefaultRun()
    {
        var switchRun = GeneratedFixtureRunner.Run(GeneratedFixtureCatalog.Select("minimal.switch-two-case-lowers-if"));
        string switchReport = GeneratedFixtureRunner.FormatReport(switchRun);

        Assert.True(switchRun.Passed, switchReport);
        AssertTarget(
            switchRun,
            "minimal.switch-two-case-lowers-if",
            "GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            switchRun,
            "minimal.switch-two-case-lowers-if",
            "GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.OpcodeDiff,
            frontier: true);

        var shapeRun = GeneratedFixtureRunner.Run(GeneratedFixtureCatalog.MinimalCompileBackRungs);
        string shapeReport = GeneratedFixtureRunner.FormatReport(shapeRun);

        Assert.True(shapeRun.Passed, shapeReport);
        AssertTarget(
            shapeRun,
            "minimal.conditional-expression-shape-frontier",
            "GeneratedFixtures.MinimalConditionalExpressionShapeFrontier.Class1",
            ".ctor",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: false);
        AssertTarget(
            shapeRun,
            "minimal.conditional-expression-shape-frontier",
            "GeneratedFixtures.MinimalConditionalExpressionShapeFrontier.Class1",
            "Method1",
            FidelityCheck.CompileBackStatus.Exact,
            frontier: true);
        var shapeResult = Assert.Single(shapeRun.Results, result =>
            result.FixtureId == "minimal.conditional-expression-shape-frontier" &&
            result.Method == "Method1");
        Assert.Equal(SyntaxKind.ReturnStatement, shapeResult.ExpectedShape);
        Assert.Equal(SyntaxKind.ReturnStatement, shapeResult.ActualShape);
        Assert.Equal(SyntaxKind.ConditionalExpression, shapeResult.FrontierShape);
        Assert.Contains("frontier-shape=ConditionalExpression", shapeReport);

        Assert.Contains(GeneratedFixtureCatalog.Frontiers, fixture =>
            fixture.Id == "minimal.switch-two-case-lowers-if" &&
            fixture.Tags.Contains("compiler-lowering"));
        Assert.DoesNotContain(GeneratedFixtureCatalog.Frontiers, fixture =>
            fixture.Id == "minimal.conditional-expression-shape-frontier");
        Assert.Contains(GeneratedFixtureCatalog.MinimalCompileBackRungs, fixture =>
            fixture.Id == "minimal.conditional-expression-shape-frontier" &&
            fixture.Tags.Contains("shape"));
    }

    [Fact]
    public void SelectedFixtureRunJson_ContainsOnlySelectedFixtureResults()
    {
        var selected = GeneratedFixtureCatalog.Select("minimal.array-index");
        var run = GeneratedFixtureRunner.Run(selected);
        string json = GeneratedFixtureRunner.FormatJson(run);

        using var document = JsonDocument.Parse(json);
        var results = document.RootElement.GetProperty("Results").EnumerateArray().ToArray();

        Assert.Equal(2, results.Length);
        Assert.All(results, result => Assert.Equal("minimal.array-index", result.GetProperty("FixtureId").GetString()));
        var method = Assert.Single(results, result => result.GetProperty("Method").GetString() == "Method1");
        Assert.Equal("Exact", method.GetProperty("ActualStatus").GetString());
        Assert.Equal("ElementAccessExpression", method.GetProperty("ActualShape").GetString());
        Assert.Equal("ElementAccessExpression", method.GetProperty("ExpectedShape").GetString());
        Assert.True(method.GetProperty("ShapePassed").GetBoolean());
    }

    [Fact]
    public void ShapeFrontierRunJson_ReportsAcceptedAndFrontierShapes()
    {
        var selected = GeneratedFixtureCatalog.Select("minimal.conditional-expression-shape-frontier");
        var run = GeneratedFixtureRunner.Run(selected);
        string json = GeneratedFixtureRunner.FormatJson(run);

        using var document = JsonDocument.Parse(json);
        var results = document.RootElement.GetProperty("Results").EnumerateArray().ToArray();
        var method = Assert.Single(results, result => result.GetProperty("Method").GetString() == "Method1");

        Assert.Equal("Exact", method.GetProperty("ActualStatus").GetString());
        Assert.Equal("ReturnStatement", method.GetProperty("ActualShape").GetString());
        Assert.Equal("ReturnStatement", method.GetProperty("ExpectedShape").GetString());
        Assert.Equal("ConditionalExpression", method.GetProperty("FrontierShape").GetString());
        Assert.True(method.GetProperty("ShapePassed").GetBoolean());
    }

    [Fact]
    public void ShapeFrontierRun_FailsWhenFrontierShapeIsAchieved()
    {
        var alreadyImproved = new GeneratedFixtureDefinition(
            "test.frontier-shape-achieved",
            """
            namespace GeneratedFixtures.TestFrontierShapeAchieved;

            public class Class1
            {
                public int Method1(int left, int right) => left + right;
            }
            """,
            [
                new(
                    "GeneratedFixtures.TestFrontierShapeAchieved.Class1",
                    "Method1",
                    FidelityCheck.CompileBackStatus.Exact,
                    IsFrontier: true,
                    ExpectedShape: SyntaxKind.ReturnStatement,
                    FrontierShape: SyntaxKind.AddExpression),
            ],
            ["test"]);

        var run = GeneratedFixtureRunner.Run([alreadyImproved]);
        string report = GeneratedFixtureRunner.FormatReport(run);
        var result = Assert.Single(run.Results);

        Assert.False(run.Passed);
        Assert.Equal(SyntaxKind.AddExpression, result.ActualShape);
        Assert.Equal(SyntaxKind.ReturnStatement, result.ExpectedShape);
        Assert.Equal(SyntaxKind.AddExpression, result.FrontierShape);
        Assert.False(result.ShapePassed);
        Assert.Contains("frontier-shape-achieved", report);
    }

    static void AssertTarget(
        GeneratedFixtureRunResult run,
        string fixtureId,
        string type,
        string method,
        FidelityCheck.CompileBackStatus expectedStatus,
        bool frontier)
    {
        var result = Assert.Single(run.Results, r =>
            r.FixtureId == fixtureId &&
            r.Type == type &&
            r.Method == method);

        Assert.Equal(expectedStatus, result.ExpectedStatus);
        Assert.Equal(expectedStatus, result.ActualStatus);
        Assert.True(result.CompileBackPassed);
        Assert.True(result.ShapePassed);
        Assert.Equal(frontier, result.IsFrontier);
        Assert.Equal("Full", result.DecompilerFidelity);
        Assert.True(result.Passed);
    }

    static void AssertShape(GeneratedFixtureRunResult run, string fixtureId, string method, SyntaxKind expected)
    {
        var result = Assert.Single(run.Results, r =>
            r.FixtureId == fixtureId &&
            r.Method == method);

        Assert.Equal(expected, result.ExpectedShape);
        Assert.Equal(expected, result.ActualShape);
        Assert.True(result.ShapePassed);
    }
}
