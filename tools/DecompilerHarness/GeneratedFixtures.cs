using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Research;
using ILInspector.MetadataPrimitives;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Addressable generated C# fixtures for progressive shape and compile-back
/// checks. The catalogue names the source shape, expected target methods, and
/// expected outcomes; the runner materializes those entries as a temporary class
/// library and grades them with a Roslyn shape check plus
/// <see cref="FidelityCheck.Evaluate(string)"/>.
/// </summary>
internal static class GeneratedFixtureCatalog
{
    public static readonly GeneratedFixtureDefinition SharedReturnHasRows = new(
        "shared.return.hasrows",
        """
        using System.Collections.Generic;

        namespace GeneratedFixtures.SharedReturnHasRows;

        public class Class1
        {
            public List<string>? Rows { get; }
            public List<string>? RowsWithDocs { get; }
            public List<string>? SelectRows { get; }
            public List<string>? SelectRowsWithDocs { get; }
            public List<string>? SummaryRows { get; }

            public bool HasRows =>
                Rows is { Count: > 0 }
                || RowsWithDocs is { Count: > 0 }
                || SelectRows is { Count: > 0 }
                || SelectRowsWithDocs is { Count: > 0 }
                || SummaryRows is { Count: > 0 };
        }
        """,
        [
            new("GeneratedFixtures.SharedReturnHasRows.Class1", "get_HasRows",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["shared-return", "hasrows", "or-chain", "property-pattern"]);

    public static readonly GeneratedFixtureDefinition MinimalPropertyLiteral = new(
        "minimal.property.literal",
        """
        namespace GeneratedFixtures.MinimalPropertyLiteral;

        public class Class1
        {
            public string Method1 => "Hello World";
        }
        """,
        [
            new("GeneratedFixtures.MinimalPropertyLiteral.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalPropertyLiteral.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "property", "literal"]);

    public static readonly GeneratedFixtureDefinition MinimalPrimaryCtorFieldInit = new(
        "minimal.primary-ctor.field-init",
        """
        namespace GeneratedFixtures.MinimalPrimaryCtorFieldInit;

        public class Class1(string message)
        {
            private readonly string _message = message;

            public string Method1 => _message;
        }
        """,
        [
            new(
                "GeneratedFixtures.MinimalPrimaryCtorFieldInit.Class1",
                ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalPrimaryCtorFieldInit.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "primary-constructor", "field-initializer"]);

    public static readonly GeneratedFixtureDefinition MinimalCtorFieldGetter = new(
        "minimal.ctor-field.getter",
        """
        namespace GeneratedFixtures.MinimalCtorFieldGetter;

        public class Class1
        {
            private readonly string _message;

            public Class1(string message)
            {
                _message = message;
            }

            public string Method1 => _message;
        }
        """,
        [
            new("GeneratedFixtures.MinimalCtorFieldGetter.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalCtorFieldGetter.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "constructor", "field", "property"]);

    public static readonly GeneratedFixtureDefinition MinimalAutoPropertyGetter = new(
        "minimal.auto-property.getter",
        """
        namespace GeneratedFixtures.MinimalAutoPropertyGetter;

        public class Class1
        {
            public Class1(string message)
            {
                Method1 = message;
            }

            public string Method1 { get; }
        }
        """,
        [
            new("GeneratedFixtures.MinimalAutoPropertyGetter.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalAutoPropertyGetter.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "constructor", "auto-property"]);

    public static readonly GeneratedFixtureDefinition MinimalMethodCallSameType = new(
        "minimal.method-call.same-type",
        """
        namespace GeneratedFixtures.MinimalMethodCallSameType;

        public class Class1
        {
            public string Method1() => Method2();

            private string Method2() => "Hello World";
        }
        """,
        [
            new("GeneratedFixtures.MinimalMethodCallSameType.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalMethodCallSameType.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalMethodCallSameType.Class1", "Method2",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "method-call"]);

    public static readonly GeneratedFixtureDefinition MinimalStaticMethodCall = new(
        "minimal.static-method-call",
        """
        namespace GeneratedFixtures.MinimalStaticMethodCall;

        public class Class1
        {
            public static string Method1() => Helper();

            private static string Helper() => "Hello World";
        }
        """,
        [
            new("GeneratedFixtures.MinimalStaticMethodCall.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticMethodCall.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticMethodCall.Class1", "Helper",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "static", "method-call"]);

    public static readonly GeneratedFixtureDefinition MinimalStaticConstructor = new(
        "minimal.static-constructor",
        """
        namespace GeneratedFixtures.MinimalStaticConstructor;

        public class Class1
        {
            private static int s_value;

            static Class1()
            {
                s_value = 42;
            }

            public static int Method1() => s_value;
        }
        """,
        [
            new("GeneratedFixtures.MinimalStaticConstructor.Class1", ".cctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticConstructor.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticConstructor.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "static", "constructor", "field"]);

    public static readonly GeneratedFixtureDefinition MinimalStaticClass = new(
        "minimal.static-class",
        """
        namespace GeneratedFixtures.MinimalStaticClass;

        public static class Class1
        {
            private static int s_value = 42;

            public static int Value => s_value;

            public static int Method1(int value) => value + s_value;
        }
        """,
        [
            new("GeneratedFixtures.MinimalStaticClass.Class1", ".cctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticClass.Class1", "get_Value",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticClass.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "static", "class", "field"]);

    public static readonly GeneratedFixtureDefinition MinimalInterfaceImplementation = new(
        "minimal.interface-implementation",
        """
        namespace GeneratedFixtures.MinimalInterfaceImplementation;

        public interface IValue
        {
            int GetValue();
        }

        public class Class1 : IValue
        {
            public int GetValue() => 42;
        }
        """,
        [
            new("GeneratedFixtures.MinimalInterfaceImplementation.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalInterfaceImplementation.Class1", "GetValue",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "interface", "implementation"]);

    public static readonly GeneratedFixtureDefinition MinimalObjectInitializer = new(
        "minimal.object-initializer",
        """
        namespace GeneratedFixtures.MinimalObjectInitializer;

        public class Helper
        {
            public int Value { get; set; }
        }

        public class Class1
        {
            public Helper Method1(int value) => new Helper { Value = value };
        }
        """,
        [
            new("GeneratedFixtures.MinimalObjectInitializer.Helper", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalObjectInitializer.Helper", "get_Value",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalObjectInitializer.Helper", "set_Value",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalObjectInitializer.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalObjectInitializer.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "object-initializer", "property", "setter"]);

    public static readonly GeneratedFixtureDefinition MinimalCollectionInitializer = new(
        "minimal.collection-initializer",
        """
        namespace GeneratedFixtures.MinimalCollectionInitializer;

        public class Class1
        {
            public System.Collections.Generic.List<int> Method1(int value)
                => new System.Collections.Generic.List<int> { value };
        }
        """,
        [
            new("GeneratedFixtures.MinimalCollectionInitializer.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalCollectionInitializer.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "collection", "initializer", "generic"]);

    public static readonly GeneratedFixtureDefinition MinimalGenericMethods = new(
        "minimal.generic-methods",
        """
        namespace GeneratedFixtures.MinimalGenericMethods;

        public class Class1
        {
            public T Echo<T>(T value) => value;

            public T Choose<T>(T left, T right) where T : class => left;

            public T Create<T>() where T : new() => new T();

            public T DefaultValue<T>() where T : struct => default;

            public T Comparable<T>(T value) where T : System.IComparable<T> => value;
        }
        """,
        [
            new("GeneratedFixtures.MinimalGenericMethods.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalGenericMethods.Class1", "Echo",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalGenericMethods.Class1", "Choose",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalGenericMethods.Class1", "Create",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalGenericMethods.Class1", "DefaultValue",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalGenericMethods.Class1", "Comparable",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "generic", "method", "constraints"]);

    public static readonly GeneratedFixtureDefinition MinimalGenericType = new(
        "minimal.generic-type",
        """
        namespace GeneratedFixtures.MinimalGenericType;

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
        """,
        [
            new("GeneratedFixtures.MinimalGenericType.Box`1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalGenericType.Box`1", "get_Value",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalGenericType.Box`1", "set_Value",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalGenericType.Box`1", "Echo",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "generic", "type", "property", "setter"]);

    public static readonly GeneratedFixtureDefinition MinimalGenericTypeConstraints = new(
        "minimal.generic-type-constraints",
        """
        namespace GeneratedFixtures.MinimalGenericTypeConstraints;

        public class Factory<T> where T : class, new()
        {
            public T Create() => new T();

            public T Echo(T value) => value;
        }
        """,
        [
            new("GeneratedFixtures.MinimalGenericTypeConstraints.Factory`1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalGenericTypeConstraints.Factory`1", "Create",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalGenericTypeConstraints.Factory`1", "Echo",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "generic", "type", "constraints"]);

    public static readonly GeneratedFixtureDefinition MinimalParameterModifiers = new(
        "minimal.parameter-modifiers",
        """
        namespace GeneratedFixtures.MinimalParameterModifiers;

        public class Class1
        {
            public void Increment(ref int value)
            {
                value++;
            }

            public void Set(out int value)
            {
                value = 42;
            }

            public int Read(in int value) => value;

            public int Sum(params int[] values)
            {
                var sum = 0;
                foreach (var value in values)
                    sum += value;
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalParameterModifiers.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalParameterModifiers.Class1", "Increment",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalParameterModifiers.Class1", "Set",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalParameterModifiers.Class1", "Read",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalParameterModifiers.Class1", "Sum",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "parameters", "ref", "out", "in", "params"]);

    public static readonly GeneratedFixtureDefinition MinimalDefaultParameters = new(
        "minimal.default-parameters",
        """
        namespace GeneratedFixtures.MinimalDefaultParameters;

        public enum Choice
        {
            None = 0,
            One = 1,
        }

        public class Class1
        {
            public int Add(int value = 42, bool enabled = true)
                => enabled ? value + 1 : value;

            public string Format(string text = "hello", object value = null)
                => value is null ? text : text + value.ToString();

            public decimal DecimalDefault(decimal value = 1.25m)
                => value;

            public int EnumDefault(Choice choice = Choice.One)
                => (int)choice;

            public long DateTimeDefault(
                [System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(637000000000000000L)] System.DateTime when)
                => when.Ticks;
        }
        """,
        [
            new("GeneratedFixtures.MinimalDefaultParameters.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalDefaultParameters.Class1", "Add",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalDefaultParameters.Class1", "Format",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalDefaultParameters.Class1", "DecimalDefault",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalDefaultParameters.Class1", "EnumDefault",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalDefaultParameters.Class1", "DateTimeDefault",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "parameters", "defaults"]);

    public static readonly GeneratedFixtureDefinition MinimalParameterAttributes = new(
        "minimal.parameter-attributes",
        """
        namespace GeneratedFixtures.MinimalParameterAttributes;

        public class Class1
        {
            public int Length([System.Diagnostics.CodeAnalysis.NotNull] string value)
                => value.Length;

            public int Copy([System.Runtime.InteropServices.Out] int value)
                => value;

            public int Update([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] ref int value)
            {
                value++;
                return value;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalParameterAttributes.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalParameterAttributes.Class1", "Length",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalParameterAttributes.Class1", "Copy",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalParameterAttributes.Class1", "Update",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "parameters", "attributes"]);

    public static readonly GeneratedFixtureDefinition MinimalParameterMarshalling = new(
        "minimal.parameter-marshalling",
        """
        namespace GeneratedFixtures.MinimalParameterMarshalling;

        public class Class1
        {
            public int I4([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] int value)
                => value;

            public int LpStr([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)] string value)
                => value.Length;

            public int Bool([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool value)
                => value ? 1 : 0;

            public int Sum(
                [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeParamIndex = 1)] int[] values,
                int count)
            {
                var sum = 0;
                for (var i = 0; i < count; i++)
                    sum += values[i];
                return sum;
            }

            public int FirstFour(
                [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeConst = 4)] int[] values)
                => values[0] + values[1] + values[2] + values[3];

            public int PlainArray(
                [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)] int[] values)
                => values.Length;

            public int FixedArray(
                [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 4)] int[] values)
                => values[0] + values[1] + values[2] + values[3];

            public int ZeroSizedArray(
                [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 0)] int[] values)
                => values.Length;
        }
        """,
        [
            new("GeneratedFixtures.MinimalParameterMarshalling.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalParameterMarshalling.Class1", "I4",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)",
                ]),
            new("GeneratedFixtures.MinimalParameterMarshalling.Class1", "LpStr",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)",
                ]),
            new("GeneratedFixtures.MinimalParameterMarshalling.Class1", "Bool",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)",
                ]),
            new("GeneratedFixtures.MinimalParameterMarshalling.Class1", "Sum",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeParamIndex = 1)",
                ]),
            new("GeneratedFixtures.MinimalParameterMarshalling.Class1", "FirstFour",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeConst = 4)",
                ]),
            new("GeneratedFixtures.MinimalParameterMarshalling.Class1", "PlainArray",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)",
                ]),
            new("GeneratedFixtures.MinimalParameterMarshalling.Class1", "FixedArray",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 4)",
                ]),
            new("GeneratedFixtures.MinimalParameterMarshalling.Class1", "ZeroSizedArray",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 0)",
                ]),
        ],
        ["minimal", "parameters", "marshalling"]);

    public static readonly GeneratedFixtureDefinition MinimalReturnParameterMetadata = new(
        "minimal.return-parameter-metadata",
        """
        namespace GeneratedFixtures.MinimalReturnParameterMetadata;

        public class Class1
        {
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]
            public int I4()
                => 42;

            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]
            public string Text()
                => "hello";

            [return: System.Diagnostics.CodeAnalysis.NotNull]
            public string NotNullText()
                => "hello";

            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]
            public int FallbackSignature(
                [System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(637000000000000000L)] System.DateTime when)
                => when.Year;
        }
        """,
        [
            new("GeneratedFixtures.MinimalReturnParameterMetadata.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalReturnParameterMetadata.Class1", "I4",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]",
                ]),
            new("GeneratedFixtures.MinimalReturnParameterMetadata.Class1", "Text",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]",
                ]),
            new("GeneratedFixtures.MinimalReturnParameterMetadata.Class1", "NotNullText",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[return: System.Diagnostics.CodeAnalysis.NotNull]",
                ]),
            new("GeneratedFixtures.MinimalReturnParameterMetadata.Class1", "FallbackSignature",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]",
                    "[System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(637000000000000000L)] System.DateTime when",
                ]),
        ],
        ["minimal", "parameters", "return", "marshalling", "attributes"]);

    public static readonly GeneratedFixtureDefinition MinimalPropertyReturnMetadata = new(
        "minimal.property-return-metadata",
        """
        namespace GeneratedFixtures.MinimalPropertyReturnMetadata;

        public class Class1
        {
            public string Text
            {
                [return: System.Diagnostics.CodeAnalysis.NotNull]
                get => "hello";
            }

            public int Number
            {
                [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]
                get => 42;
            }

            public string this[int index]
            {
                [return: System.Diagnostics.CodeAnalysis.NotNull]
                get => "hello";
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalPropertyReturnMetadata.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalPropertyReturnMetadata.Class1", "get_Text",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[return: System.Diagnostics.CodeAnalysis.NotNull] get",
                ]),
            new("GeneratedFixtures.MinimalPropertyReturnMetadata.Class1", "get_Number",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] get",
                ]),
            new("GeneratedFixtures.MinimalPropertyReturnMetadata.Class1", "get_Item",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[return: System.Diagnostics.CodeAnalysis.NotNull] get",
                ]),
        ],
        ["minimal", "property", "return", "marshalling", "attributes"]);

    public static readonly GeneratedFixtureDefinition MinimalMemberAttributes = new(
        "minimal.member-attributes",
        """
        namespace GeneratedFixtures.MinimalMemberAttributes;

        public class Class1
        {
            [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            public int Method1()
                => 42;

            [System.Obsolete("use Method1")]
            public int ObsoleteMethod()
                => 43;

            [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            public string Text
            {
                get => "hello";
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalMemberAttributes.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalMemberAttributes.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] public int Method1()",
                ]),
            new("GeneratedFixtures.MinimalMemberAttributes.Class1", "ObsoleteMethod",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[System.Obsolete(\"use Method1\")] public int ObsoleteMethod()",
                ]),
            new("GeneratedFixtures.MinimalMemberAttributes.Class1", "get_Text",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] public string Text",
                ]),
        ],
        ["minimal", "member", "attributes"]);

    public static readonly GeneratedFixtureDefinition MinimalTypeAttributes = new(
        "minimal.type-attributes",
        """
        namespace GeneratedFixtures.MinimalTypeAttributes;

        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        public class Class1
        {
            public int Method1()
                => 42;
        }
        """,
        [
            new("GeneratedFixtures.MinimalTypeAttributes.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] public class Class1",
                ]),
            new("GeneratedFixtures.MinimalTypeAttributes.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] public class Class1",
                ]),
        ],
        ["minimal", "type", "attributes"]);

    public static readonly GeneratedFixtureDefinition MinimalIfElse = new(
        "minimal.if-else",
        """
        namespace GeneratedFixtures.MinimalIfElse;

        public class Class1
        {
            public int Method1(int value)
            {
                if (value < 0)
                    return -1;
                return 1;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalIfElse.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalIfElse.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "branch", "if-else"]);

    public static readonly GeneratedFixtureDefinition MinimalIntegerAddition = new(
        "minimal.integer-addition",
        """
        namespace GeneratedFixtures.MinimalIntegerAddition;

        public class Class1
        {
            public int Method1(int left, int right) => left + right;
        }
        """,
        [
            new("GeneratedFixtures.MinimalIntegerAddition.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalIntegerAddition.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.AddExpression),
        ],
        ["minimal", "integer", "arithmetic", "addition"]);

    public static readonly GeneratedFixtureDefinition MinimalStructMembers = new(
        "minimal.struct-members",
        """
        namespace GeneratedFixtures.MinimalStructMembers;

        public struct Counter
        {
            private int _value;

            public Counter(int value)
            {
                _value = value;
            }

            public int Value
            {
                get => _value;
                set => _value = value;
            }

            public int Add(int value) => _value + value;
        }
        """,
        [
            new("GeneratedFixtures.MinimalStructMembers.Counter", "get_Value",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStructMembers.Counter", "set_Value",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStructMembers.Counter", "Add",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "struct", "value-type", "property", "setter"]);

    public static readonly GeneratedFixtureDefinition MinimalStructConstructorFieldInit = new(
        "minimal.struct-constructor.field-init",
        """
        namespace GeneratedFixtures.MinimalStructConstructorFieldInit;

        public struct Counter
        {
            private int _value;

            public Counter(int value)
            {
                _value = value;
            }

            public int Value
            {
                get => _value;
                set => _value = value;
            }
        }
        """,
        [
            new(
                "GeneratedFixtures.MinimalStructConstructorFieldInit.Counter",
                ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "struct", "constructor", "value-type"]);

    public static readonly GeneratedFixtureDefinition MinimalArrayIndex = new(
        "minimal.array-index",
        """
        namespace GeneratedFixtures.MinimalArrayIndex;

        public class Class1
        {
            public int Method1(int[] values, int index) => values[index];
        }
        """,
        [
            new("GeneratedFixtures.MinimalArrayIndex.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalArrayIndex.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.ElementAccessExpression),
        ],
        ["minimal", "array", "index"]);

    public static readonly GeneratedFixtureDefinition MinimalArrayLength = new(
        "minimal.array-length",
        """
        namespace GeneratedFixtures.MinimalArrayLength;

        public class Class1
        {
            public int Method1(int[] values) => values.Length;
        }
        """,
        [
            new("GeneratedFixtures.MinimalArrayLength.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalArrayLength.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.SimpleMemberAccessExpression),
        ],
        ["minimal", "array", "length"]);

    public static readonly GeneratedFixtureDefinition MinimalIndexerGetter = new(
        "minimal.indexer.getter",
        """
        namespace GeneratedFixtures.MinimalIndexerGetter;

        public class Class1
        {
            public int this[int index] => index + 1;
        }
        """,
        [
            new("GeneratedFixtures.MinimalIndexerGetter.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalIndexerGetter.Class1", "get_Item",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "indexer", "property"]);

    public static readonly GeneratedFixtureDefinition MinimalIndexerSetter = new(
        "minimal.indexer.setter",
        """
        namespace GeneratedFixtures.MinimalIndexerSetter;

        public class Class1
        {
            private readonly int[] _values;

            public Class1()
            {
                _values = new int[4];
            }

            public int this[int index]
            {
                get => _values[index];
                set => _values[index] = value;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalIndexerSetter.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalIndexerSetter.Class1", "get_Item",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalIndexerSetter.Class1", "set_Item",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "indexer", "property", "setter"]);

    public static readonly GeneratedFixtureDefinition MinimalStringLength = new(
        "minimal.string-length",
        """
        namespace GeneratedFixtures.MinimalStringLength;

        public class Class1
        {
            public int Method1(string value) => value.Length;
        }
        """,
        [
            new("GeneratedFixtures.MinimalStringLength.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStringLength.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.SimpleMemberAccessExpression),
        ],
        ["minimal", "string", "length"]);

    public static readonly GeneratedFixtureDefinition MinimalNullCoalesce = new(
        "minimal.null-coalesce",
        """
        namespace GeneratedFixtures.MinimalNullCoalesce;

        public class Class1
        {
            public string Method1(string value) => value ?? "fallback";
        }
        """,
        [
            new("GeneratedFixtures.MinimalNullCoalesce.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalNullCoalesce.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.CoalesceExpression),
        ],
        ["minimal", "null-coalesce"]);

    public static readonly GeneratedFixtureDefinition MinimalTryFinally = new(
        "minimal.try-finally",
        """
        namespace GeneratedFixtures.MinimalTryFinally;

        public class Class1
        {
            private int _count;

            public int Method1(int value)
            {
                try
                {
                    return value + 1;
                }
                finally
                {
                    _count++;
                }
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalTryFinally.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalTryFinally.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.TryStatement),
        ],
        ["minimal", "try-finally", "lifetime"]);

    public static readonly GeneratedFixtureDefinition MinimalUsingDispose = new(
        "minimal.using-dispose",
        """
        namespace GeneratedFixtures.MinimalUsingDispose;

        public class Class1
        {
            public long Method1()
            {
                using var stream = new System.IO.MemoryStream();
                return stream.Length;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalUsingDispose.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalUsingDispose.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "using", "dispose", "lifetime"]);

    public static readonly GeneratedFixtureDefinition MinimalForeachArray = new(
        "minimal.foreach-array",
        """
        namespace GeneratedFixtures.MinimalForeachArray;

        public class Class1
        {
            public int Method1(int[] values)
            {
                var sum = 0;
                foreach (var value in values)
                    sum += value;
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalForeachArray.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalForeachArray.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.ForEachStatement),
        ],
        ["minimal", "foreach", "array", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalForLoop = new(
        "minimal.for-loop",
        """
        namespace GeneratedFixtures.MinimalForLoop;

        public class Class1
        {
            public int Method1(int count)
            {
                var sum = 0;
                for (var i = 0; i < count; i++)
                    sum += i;
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalForLoop.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalForLoop.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.ForStatement),
        ],
        ["minimal", "for", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalWhileLoop = new(
        "minimal.while-loop",
        """
        namespace GeneratedFixtures.MinimalWhileLoop;

        public class Class1
        {
            public int Method1(int count)
            {
                var sum = 0;
                while (count > 0)
                {
                    sum += count;
                    count--;
                }
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalWhileLoop.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalWhileLoop.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.WhileStatement),
        ],
        ["minimal", "while", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalDoWhileLoop = new(
        "minimal.do-while",
        """
        namespace GeneratedFixtures.MinimalDoWhileLoop;

        public class Class1
        {
            public int Method1(int count)
            {
                var sum = 0;
                do
                {
                    sum += count;
                    count--;
                }
                while (count > 0);
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalDoWhileLoop.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalDoWhileLoop.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.DoStatement),
        ],
        ["minimal", "do-while", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalSwitchInt = new(
        "minimal.switch-int",
        """
        namespace GeneratedFixtures.MinimalSwitchInt;

        public class Class1
        {
            public string Method1(int value)
            {
                switch (value)
                {
                    case 0:
                        return "zero";
                    case 1:
                        return "one";
                    case 2:
                        return "two";
                    case 3:
                        return "three";
                    default:
                        return "many";
                }
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalSwitchInt.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalSwitchInt.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedShape: SyntaxKind.SwitchStatement),
        ],
        ["minimal", "switch", "int", "branch"]);

    public static readonly GeneratedFixtureDefinition MinimalSwitchTwoCaseLowersIf = new(
        "minimal.switch-two-case-lowers-if",
        """
        namespace GeneratedFixtures.MinimalSwitchTwoCaseLowersIf;

        public class Class1
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
            new("GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new(
                "GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1",
                "Method1",
                FidelityCheck.CompileBackStatus.OpcodeDiff,
                IsFrontier: true,
                Note: "Current SDK lowers this two-case source switch to if/else; dense minimal.switch-int is the stable switch-statement rung."),
        ],
        ["minimal", "switch", "int", "branch", "frontier", "compiler-lowering"]);

    public static readonly GeneratedFixtureDefinition MinimalConditionalExpressionShapeFrontier = new(
        "minimal.conditional-expression-shape-frontier",
        """
        namespace GeneratedFixtures.MinimalConditionalExpressionShapeFrontier;

        public class Class1
        {
            public int Method1(bool flag) => flag ? 1 : 2;
        }
        """,
        [
            new("GeneratedFixtures.MinimalConditionalExpressionShapeFrontier.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new(
                "GeneratedFixtures.MinimalConditionalExpressionShapeFrontier.Class1",
                "Method1",
                FidelityCheck.CompileBackStatus.Exact,
                IsFrontier: true,
                Note: "Current output is compile-back exact but does not preserve the conditional-expression source shape.",
                ExpectedShape: SyntaxKind.ReturnStatement,
                FrontierShape: SyntaxKind.ConditionalExpression),
        ],
        ["minimal", "conditional-expression", "branch", "frontier", "shape"]);

    public static readonly GeneratedFixtureDefinition RecordPropertyGetter = new(
        "record.property-getter",
        """
        public sealed record RecordPropertyGetterSnapshot(string Assembly, int Count);
        """,
        [
            new(
                "RecordPropertyGetterSnapshot",
                "get_Assembly",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedTargetBodyFragments:
                [
                    "return this.Assembly;",
                ]),
        ],
        ["record", "property", "getter", "return-to-sender"]);

    public static readonly GeneratedFixtureDefinition RecordEqualityOperators = new(
        "record.equality-operators",
        """
        public record RecordEqualityOperatorsRow(string Name);
        """,
        [
            new(
                "RecordEqualityOperatorsRow",
                "op_Equality",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedTargetBodyFragments:
                [
                    "(object)left == (object)right",
                    "left.Equals(right)",
                ]),
            new(
                "RecordEqualityOperatorsRow",
                "op_Inequality",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedTargetBodyFragments:
                [
                    "return !(left == right);",
                ]),
        ],
        ["record", "operator", "equality", "return-to-sender"]);

    public static readonly GeneratedFixtureDefinition RecordVirtualHelpers = new(
        "record.virtual-helpers",
        """
        public record RecordVirtualHelpersRow(string Name, string Value);
        """,
        [
            new(
                "RecordVirtualHelpersRow",
                "ToString",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedTargetBodyFragments:
                [
                    "new StringBuilder()",
                    "PrintMembers(V_0)",
                    "return V_0.ToString();",
                ]),
            new(
                "RecordVirtualHelpersRow",
                "Equals",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedTargetBodyFragments:
                [
                    "return Equals(obj as RecordVirtualHelpersRow);",
                ]),
        ],
        ["record", "virtual", "helper", "return-to-sender"]);

    public static readonly GeneratedFixtureDefinition RecordFieldReadHelpers = new(
        "record.field-read-helpers",
        """
        public record RecordFieldReadHelpersRow(string Name, string Value);
        """,
        [
            new(
                "RecordFieldReadHelpersRow",
                "GetHashCode",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedTargetBodyFragments:
                [
                    "EqualityContract",
                    "this.Name",
                    "this.Value",
                ]),
            new(
                "RecordFieldReadHelpersRow",
                "ToString",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedTargetBodyFragments:
                [
                    "RecordFieldReadHelpersRow",
                    "PrintMembers(V_0)",
                    "return V_0.ToString();",
                ]),
            new(
                "RecordFieldReadHelpersRow",
                "Equals",
                FidelityCheck.CompileBackStatus.Exact,
                Overload: 1,
                ExpectedTargetBodyFragments:
                [
                    "other.EqualityContract",
                    "this.Name, other.Name",
                    "this.Value, other.Value",
                ]),
        ],
        ["record", "field", "helper", "return-to-sender"]);

    public static readonly GeneratedFixtureDefinition RecordStructFieldReadHelpers = new(
        "record.struct-field-read-helpers",
        """
        public record struct RecordStructFieldReadHelpersRow(string Name, string Value);
        """,
        [
            new(
                "RecordStructFieldReadHelpersRow",
                "GetHashCode",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedTargetBodyFragments:
                [
                    "this.Name",
                    "this.Value",
                ]),
            new(
                "RecordStructFieldReadHelpersRow",
                "Equals",
                FidelityCheck.CompileBackStatus.Exact,
                Overload: 1,
                ExpectedTargetBodyFragments:
                [
                    "this.Name, other.Name",
                    "this.Value, other.Value",
                ]),
        ],
        ["record", "struct", "field", "helper", "return-to-sender"]);

    public static readonly GeneratedFixtureDefinition RecordGenericTypedEquals = new(
        "record.generic-typed-equals",
        """
        public record RecordGenericTypedEqualsRow<T>(T Value);
        """,
        [
            new(
                "RecordGenericTypedEqualsRow`1",
                "Equals",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedTargetBodyFragments:
                [
                    "return Equals(obj as RecordGenericTypedEqualsRow<T>);",
                ]),
        ],
        ["record", "generic", "equals", "return-to-sender"]);

    public static readonly GeneratedFixtureDefinition RecordNestedGenericTypedEquals = new(
        "record.nested-generic-typed-equals",
        """
        public class RecordNestedGenericTypedEqualsContainer<T>
        {
            public record Row<U>(T Outer, U Value);
        }
        """,
        [
            new(
                "RecordNestedGenericTypedEqualsContainer`1.Row`1",
                "Equals",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedTargetBodyFragments:
                [
                    "return Equals(obj as Row<U>);",
                ]),
        ],
        ["record", "nested", "generic", "equals", "return-to-sender"]);

    public static readonly GeneratedFixtureDefinition RtsAttributeShell = new(
        "rts.attribute-shell",
        """
        namespace System.Diagnostics.CodeAnalysis;

        [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter | System.AttributeTargets.ReturnValue)]
        public sealed class FeatureGuardAttribute : System.Attribute
        {
            public FeatureGuardAttribute(System.Type featureType)
            {
                FeatureType = featureType;
            }

            public System.Type FeatureType { get; }
        }
        """,
        [
            new(
                "System.Diagnostics.CodeAnalysis.FeatureGuardAttribute",
                ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new(
                "System.Diagnostics.CodeAnalysis.FeatureGuardAttribute",
                "get_FeatureType",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedTargetBodyFragments:
                [
                    "return this.FeatureType;",
                ]),
        ],
        ["rts", "attribute", "shell", "return-to-sender"]);

    public static readonly GeneratedFixtureDefinition RtsShiftedSiblingNestedGeneric = new(
        "rts.shifted-sibling-nested-generic",
        """
        public class ShiftedSiblingOuter<T>
        {
            public class Inner<A, B>
            {
                public int Marker => 123;
            }

            public class Inner<A, B, C>
            {
                public int ReadShifted(ShiftedSiblingOuter<A>.Inner<B, C> shifted) => shifted.Marker;
            }
        }
        """,
        [
            new(
                "ShiftedSiblingOuter`1.Inner`3",
                "ReadShifted",
                FidelityCheck.CompileBackStatus.Exact,
                ExpectedSourceFragments:
                [
                    "ShiftedSiblingOuter<A>.Inner<B, C> shifted",
                ],
                ExpectedTargetBodyFragments:
                [
                    "return shifted.Marker;",
                ]),
        ],
        ["rts", "nested", "generic", "shifted", "return-to-sender"]);

    public static readonly GeneratedFixtureDefinition AssertionNodeCoverage = new(
        "assertion.inverse-node-coverage",
        """
        namespace GeneratedFixtures.AssertionNodeCoverage;

        public enum Choice
        {
            None = 0,
            One = 1,
        }

        public struct Mutable
        {
            public int Value;

            public Mutable(int value)
            {
                Value = value;
            }

            public int Increment()
            {
                Value++;
                return Value;
            }
        }

        public sealed class Holder
        {
            public int Number { get; set; }
            public string Text { get; set; }
        }

        [System.Runtime.CompilerServices.InlineArray(4)]
        public struct InlineBuffer
        {
            private int _element0;
        }

        public unsafe class Class1
        {
            private int _field;

            public static int StaticAdd(int left, int right) => left + right;

            public static int Invoke(delegate*<int, int, int> callback, int left, int right)
                => callback(left, right);

            public static delegate*<int, int, int> Pointer() => &StaticAdd;

            public static System.Func<int, int> Lambda() => x => x + 1;

            public int UseLambda(int value)
            {
                System.Func<int, int> add = x => x + _field;
                return add(value);
            }

            public static int LocalFunction(int value)
            {
                int Twice(int input) => input * 2;
                return Twice(value);
            }

            public int InstanceLocalFunction(int value)
            {
                int AddField(int input) => input + _field;
                return AddField(value);
            }

            public static int Logical(bool left, bool right)
                => left && !right ? 1 : 0;

            public static int Compare(int left, int right)
                => left < right ? left : right;

            public static int NullConditional(Holder holder)
                => holder?.Number ?? -1;

            public static string NullConditionalReference(Holder holder)
                => holder?.Text ?? "none";

            public static System.Type TypeToken()
                => typeof(Holder);

            public static int CaughtException()
            {
                try
                {
                    throw new System.InvalidOperationException();
                }
                catch (System.InvalidOperationException ex)
                {
                    return ex.HResult;
                }
            }

            public static int Size()
                => sizeof(Mutable);

            public static int BoxValue(int value)
            {
                object boxed = value;
                return boxed.GetHashCode();
            }

            public static int UnboxAny(object boxed)
                => (int)boxed;

            public static int Unbox(object boxed)
            {
                var mutable = (Mutable)boxed;
                return mutable.Increment();
            }

            public static string Cast(object value)
                => ((string)value).ToString();

            public static string As(object value)
                => value as string;

            public static int[] NewArray(int count)
                => new int[count];

            public static int ArrayAccess(int[] values, int index)
                => values[index];

            public static int ArrayLength(int[] values)
                => values.Length;

            public static ref int ArrayElementAddress(int[] values, int index)
                => ref values[index];

            public static int Indirect(ref int value)
                => value;

            public static int ArgumentAddress(Mutable value)
                => value.Increment();

            public static int LocalAddress()
            {
                var value = 41;
                Increment(ref value);
                return value;
            }

            public static void Increment(ref int value)
                => value++;

            public static void ForwardArgumentAddress(ref int value)
                => Increment(ref value);

            public Choice CoercedEnum(int value)
                => (Choice)value;

            public int Field()
            {
                _field = 1;
                return _field;
            }

            public int FieldAddress()
            {
                Increment(ref _field);
                return _field;
            }

            public int Property()
            {
                Number = 2;
                return Number;
            }

            public int Number { get; set; }

            public static int Unary(int value)
                => -value;

            public static long ConvertNumeric(int value)
                => (long)value;

            public static int FromEndIndex(int[] values, int offset)
                => values[^offset];

            public static int Slice(int[] values, int start, int end)
                => values[start..end][0];

            public static int SpanLiteral()
            {
                System.ReadOnlySpan<int> values = new int[] { 1, 2, 3 };
                return values[0];
            }

            public static int StackAllocSpan()
            {
                System.Span<int> values = stackalloc int[2];
                values[0] = 42;
                return values[0];
            }

            public static int StackAllocPointer(int count)
            {
                int* values = stackalloc int[count];
                values[0] = 1;
                return values[0];
            }

            public static int InlineArraySpan()
            {
                InlineBuffer buffer = default;
                System.Span<int> values = buffer;
                values[0] = 7;
                return values[0];
            }

            public static void PrefixDecrementStore(int[] values, int index, int value)
            {
                values[--index] = value;
            }

            public static int SwitchExpressionDispatch(int value)
                => value switch
                {
                    0 => 1,
                    1 => 2,
                    2 => 4,
                    3 => 8,
                    _ => 0,
                };
        }

        // Field-backed inline-array place — the canonical InlineArrayAsSpan
        // (ldflda) shape. Exercises the LoadFieldAddress branch of the
        // place-conversion raise and keeps InlineArraySpanConversion covered
        // even if the local-buffer lowering above shifts.
        public sealed class InlineArrayFieldHolder
        {
            private InlineBuffer _values;

            public int InlineArraySpanConversion()
            {
                System.Span<int> values = _values;
                values[0] = 7;
                return values[0];
            }
        }

        public class AsyncClass
        {
            public static async System.Threading.Tasks.Task<int> AwaitExpression()
                => await System.Threading.Tasks.Task.FromResult(42);
        }

        public record RecordPair(int Number, string Text);

        public sealed class Outer
        {
            public Holder Inner { get; } = new Holder();
        }

        public sealed class BoxedSource
        {
            public object PropertyValue { get; set; }
        }

        public sealed class NamedCount
        {
            public NamedCount(string name, int count)
            {
                Name = name;
                Count = count;
            }

            public string Name { get; }
            public int Count { get; }

            public void Deconstruct(out string name, out int count)
            {
                name = Name;
                count = Count;
            }
        }

        // Output-shape node producers (B2 annotation batch, #2213).
        public class OutputShapes
        {
            public static (int, string) Tuple(int number, string text)
                => (number, text);

            public static bool TupleEquals((int, int) left, (int, int) right)
                => left == right;

            public static int IsPatternBind(object value)
            {
                if (value is string text)
                {
                    return text.Length;
                }
                return 0;
            }

            public static int PropertyPattern(BoxedSource value)
            {
                if (value is { PropertyValue: string text })
                {
                    return text.Length;
                }
                return 0;
            }

            public static int ListPattern(string[] values)
            {
                if (values is ["a" or "b"])
                {
                    return 1;
                }
                return 0;
            }

            public static bool Positional(NamedCount value)
                => value is ("ok", > 0);

            // TupleSwitchExpression (issue #2867): csc's tuple relational-pattern
            // switch lowers to the same nested if/return comparison tree as
            // LadderRung5.Quadrant, so this small two-component case keeps the
            // fold covered even if the Quadrant fixture's shape shifts.
            public static int TupleRelationalSwitch(int x, int y)
                => (x, y) switch
                {
                    (> 0, > 0) => 1,
                    (< 0, > 0) => 2,
                    (< 0, < 0) => 3,
                    (> 0, < 0) => 4,
                    _ => 0,
                };

            public static byte[] ArrayLiteralBytes()
                => new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            public static int CollectionExpr(int a, int b)
            {
                System.ReadOnlySpan<int> values = [a, b];
                return values[0];
            }

            public static int[] SpreadConcat(int[] head, int tail)
                => [.. head, tail];

            public static Holder ObjectInit(int number, string text)
                => new Holder { Number = number, Text = text };

            public static Outer NestedInit(int number)
                => new Outer { Inner = { Number = number } };

            public static object Anonymous(int number, string text)
                => new { Number = number, Text = text };

            public static RecordPair WithMutation(RecordPair pair, int number)
                => pair with { Number = number };

            public static string Interpolate(int value, string name)
                => $"{name}: {value:X4}";
        }
        """,
        [],
        ["assertion", "inverse-ledger", "coverage", "unsafe"]);

    // UnionSwitchExpression needs the union marker attribute in
    // System.Runtime.CompilerServices, which the file-scoped-namespace coverage
    // fixture above cannot declare — so union dispatch gets its own fixture
    // (the assertion.il-unbox precedent: one node, one dedicated source).
    public static readonly GeneratedFixtureDefinition AssertionUnionSwitch = new(
        "assertion.union-switch",
        """
        #nullable enable
        namespace System.Runtime.CompilerServices
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class UnionAttribute : System.Attribute;

            public interface IUnion
            {
                object? Value { get; }
            }
        }

        namespace GeneratedFixtures.UnionSwitch
        {
            public sealed class Cat
            {
                public string Name => "cat";
            }

            public sealed class Dog
            {
                public string Name => "dog";
            }

            [System.Runtime.CompilerServices.Union]
            public readonly struct Pet : System.Runtime.CompilerServices.IUnion
            {
                public Pet(Cat value) => Value = value;
                public Pet(Dog value) => Value = value;
                public object? Value { get; }
            }

            public static class Dispatch
            {
                public static string Describe(Pet pet)
                    => pet.Value switch
                    {
                        Cat cat => cat.Name,
                        Dog dog => dog.Name,
                        _ => "unknown",
                    };
            }
        }
        """,
        [],
        ["assertion", "inverse-ledger", "coverage", "union"]);

    public static IReadOnlyList<GeneratedFixtureDefinition> All { get; } =
    [
        SharedReturnHasRows,
        MinimalPropertyLiteral,
        MinimalPrimaryCtorFieldInit,
        MinimalCtorFieldGetter,
        MinimalAutoPropertyGetter,
        MinimalMethodCallSameType,
        MinimalStaticMethodCall,
        MinimalStaticConstructor,
        MinimalStaticClass,
        MinimalInterfaceImplementation,
        MinimalObjectInitializer,
        MinimalCollectionInitializer,
        MinimalGenericMethods,
        MinimalGenericType,
        MinimalGenericTypeConstraints,
        MinimalParameterModifiers,
        MinimalDefaultParameters,
        MinimalParameterAttributes,
        MinimalParameterMarshalling,
        MinimalReturnParameterMetadata,
        MinimalPropertyReturnMetadata,
        MinimalMemberAttributes,
        MinimalTypeAttributes,
        MinimalIfElse,
        MinimalIntegerAddition,
        MinimalStructMembers,
        MinimalStructConstructorFieldInit,
        MinimalArrayIndex,
        MinimalArrayLength,
        MinimalIndexerGetter,
        MinimalIndexerSetter,
        MinimalStringLength,
        MinimalNullCoalesce,
        MinimalTryFinally,
        MinimalUsingDispose,
        MinimalForeachArray,
        MinimalForLoop,
        MinimalWhileLoop,
        MinimalDoWhileLoop,
        MinimalSwitchInt,
        MinimalConditionalExpressionShapeFrontier,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> Frontiers { get; } =
    [
        MinimalSwitchTwoCaseLowersIf,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> ReturnToSenderRecords { get; } =
    [
        RecordPropertyGetter,
        RecordEqualityOperators,
        RecordVirtualHelpers,
        RecordFieldReadHelpers,
        RecordStructFieldReadHelpers,
        RecordGenericTypedEquals,
        RecordNestedGenericTypedEquals,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> ReturnToSenderParity { get; } =
    [
        RtsAttributeShell,
        RtsShiftedSiblingNestedGeneric,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> AssertionCoverage { get; } =
    [
        AssertionNodeCoverage,
        AssertionUnionSwitch,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> Catalog { get; } =
    [
        .. All,
        .. Frontiers,
        .. ReturnToSenderRecords,
        .. ReturnToSenderParity,
        .. AssertionCoverage,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> MinimalCompileBackRungs => All;

    public static IReadOnlyList<GeneratedFixtureDefinition> Select(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return All;

        return Catalog
            .Where(fixture => MatchesSelector(fixture.Id, selector))
            .ToArray();
    }

    // Exact-or-prefix match: a selector selects an id it equals or that starts
    // with it. Kept as a pure predicate so the contract can be tested against
    // synthetic ids rather than the live catalog inventory.
    internal static bool MatchesSelector(string id, string selector)
        => id.Equals(selector, StringComparison.Ordinal)
            || id.StartsWith(selector, StringComparison.Ordinal);
}

internal sealed record GeneratedFixtureDefinition(
    string Id,
    string Source,
    IReadOnlyList<GeneratedFixtureTarget> Targets,
    IReadOnlyList<string> Tags);

internal sealed record GeneratedFixtureTarget(
    string Type,
    string Method,
    FidelityCheck.CompileBackStatus ExpectedStatus,
    int Overload = 0,
    bool IsFrontier = false,
    string? Note = null,
    SyntaxKind? ExpectedShape = null,
    SyntaxKind? FrontierShape = null,
    IReadOnlyList<string>? ExpectedSourceFragments = null,
    IReadOnlyList<string>? ExpectedTargetBodyFragments = null)
{
    public string DisplayMember => $"{Type}::{Method}#{Overload}";
}

internal sealed record GeneratedFixtureRunOptions(
    string? TargetFramework = null,
    bool KeepArtifacts = false)
{
    public static GeneratedFixtureRunOptions Default { get; } = new();
}

internal sealed record GeneratedFixtureRunResult(
    string ProjectDirectory,
    string AssemblyPath,
    IReadOnlyList<GeneratedFixtureResult> Results)
{
    public bool Passed => Results.All(result => result.Passed);
}

internal sealed record GeneratedFixtureResult(
    string FixtureId,
    string Type,
    string Method,
    int Overload,
    string DecompilerFidelity,
    FidelityCheck.CompileBackStatus? ActualStatus,
    FidelityCheck.CompileBackStatus ExpectedStatus,
    SyntaxKind? ActualShape,
    SyntaxKind? ExpectedShape,
    SyntaxKind? FrontierShape,
    string? ShapeDetail,
    bool IsFrontier,
    string? Detail,
    string? Note)
{
    public bool CompileBackPassed => ActualStatus == ExpectedStatus;
    public bool ShapePassed => ExpectedShape is null || ActualShape == ExpectedShape;
    public bool Passed => CompileBackPassed && ShapePassed;
    public string DisplayMember => $"{Type}::{Method}#{Overload}";
}

internal enum GeneratedFixtureReturnToSenderStatus
{
    Pass,
    Skip,
    Fail,
}

internal sealed record GeneratedFixtureReturnToSenderRunResult(
    string ProjectDirectory,
    string AssemblyPath,
    IReadOnlyList<GeneratedFixtureReturnToSenderResult> Results)
{
    public bool Passed => Results.All(result => result.Status != GeneratedFixtureReturnToSenderStatus.Fail);
}

internal sealed record GeneratedFixtureReturnToSenderResult(
    string FixtureId,
    string Type,
    string Method,
    int Overload,
    GeneratedFixtureReturnToSenderStatus Status,
    FidelityCheck.CompileBackStatus? ActualStatus,
    string Reason,
    string? Detail,
    IlDiffDisplayResult? IlDiffDiagnostic,
    IlMemberDiffResult? IlDiff,
    MemberAnchor? MemberAnchor,
    ReturnToSender.FaultIsolationResult? FaultIsolation,
    ReturnToSenderClosureEvidence? ClosureEvidence,
    bool IsFrontier,
    string? Note)
{
    public string DisplayMember => $"{Type}::{Method}#{Overload}";
}

internal sealed record GeneratedFixtureRender(string DecompilerFidelity, string? Body);

internal static class GeneratedFixtureRunner
{
    static readonly SyntaxKind[] s_interestingShapes =
    [
        SyntaxKind.IfStatement,
        SyntaxKind.ForStatement,
        SyntaxKind.ForEachStatement,
        SyntaxKind.WhileStatement,
        SyntaxKind.DoStatement,
        SyntaxKind.SwitchStatement,
        SyntaxKind.TryStatement,
        SyntaxKind.UsingStatement,
        SyntaxKind.ConditionalExpression,
        SyntaxKind.CoalesceExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.AddExpression,
        SyntaxKind.InvocationExpression,
        SyntaxKind.ReturnStatement,
    ];

    static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static GeneratedFixtureRunResult Run(
        IReadOnlyList<GeneratedFixtureDefinition> fixtures,
        GeneratedFixtureRunOptions? options = null)
    {
        return RunWithMaterializedFixtures(fixtures, options, (root, assemblyPath) =>
        {
            var compileBack = FidelityCheck.Evaluate(assemblyPath)
                .ToDictionary(result => Key(result.Type, result.Method, result.Overload), StringComparer.Ordinal);
            var renders = DecompilerRenders(assemblyPath, fixtures);
            var results = new List<GeneratedFixtureResult>();
            foreach (var fixture in fixtures)
            {
                foreach (var target in fixture.Targets)
                {
                    compileBack.TryGetValue(Key(target.Type, target.Method, target.Overload), out var actual);
                    renders.TryGetValue(Key(target.Type, target.Method, target.Overload), out var render);
                    var shape = ShapeVerdict(render?.Body, target.ExpectedShape, target.FrontierShape);
                    results.Add(new GeneratedFixtureResult(
                        fixture.Id,
                        target.Type,
                        target.Method,
                        target.Overload,
                        render?.DecompilerFidelity ?? "Unknown",
                        actual?.Status,
                        target.ExpectedStatus,
                        shape.ActualShape,
                        target.ExpectedShape,
                        target.FrontierShape,
                        shape.Detail,
                        target.IsFrontier,
                        actual?.Detail ?? (actual is null ? "target-method-not-found" : null),
                        target.Note));
                }
            }

            return new GeneratedFixtureRunResult(root, assemblyPath, results);
        });
    }

    public static GeneratedFixtureReturnToSenderRunResult RunReturnToSenderCatalog(
        IReadOnlyList<GeneratedFixtureDefinition> fixtures,
        GeneratedFixtureRunOptions? options = null)
    {
        return RunWithMaterializedFixtures(fixtures, options, (root, assemblyPath) =>
        {
            var requestedTargets = fixtures
                .SelectMany(fixture => fixture.Targets)
                .Select(target => new ReturnToSender.RequestedTarget(target.Type, target.Method, target.Overload))
                .Distinct()
                .ToArray();
            var sourcePaths = Directory.GetFiles(root, "*.cs", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .ToArray();
            IReadOnlyDictionary<string, ReturnToSender.Result> rtsResults = ReturnToSender.CompileBackTargets(assemblyPath, requestedTargets, sourcePaths)
                .ToDictionary(result => Key(
                    result.Plan.TargetMethod.Type,
                    result.Plan.TargetMethod.Method,
                    result.Plan.TargetMethod.Overload), StringComparer.Ordinal);
            var results = new List<GeneratedFixtureReturnToSenderResult>();
            foreach (var fixture in fixtures)
            {
                foreach (var target in fixture.Targets)
                {
                    if (!rtsResults.TryGetValue(Key(target.Type, target.Method, target.Overload), out var actual))
                    {
                        results.Add(Skipped(fixture, target, "unsupported-rts-target"));
                        continue;
                    }

                    bool exact = actual.Status == FidelityCheck.CompileBackStatus.Exact;
                    var missingSourceFragment = exact
                        ? MissingSourceFragment(actual.Source, target.ExpectedSourceFragments)
                        : null;
                    var missingTargetBodyFragment = exact
                        ? MissingSourceFragment(actual.TargetBody, target.ExpectedTargetBodyFragments)
                        : null;
                    var status = exact
                        && missingSourceFragment is null
                        && missingTargetBodyFragment is null
                            ? GeneratedFixtureReturnToSenderStatus.Pass
                            : GeneratedFixtureReturnToSenderStatus.Fail;
                    results.Add(new GeneratedFixtureReturnToSenderResult(
                        fixture.Id,
                        target.Type,
                        target.Method,
                        target.Overload,
                        status,
                        actual.Status,
                        status == GeneratedFixtureReturnToSenderStatus.Pass
                            ? "exact"
                            : missingSourceFragment is not null
                                ? "source-fragment-missing"
                                : missingTargetBodyFragment is not null
                                    ? "target-body-fragment-missing"
                                : FailureReason(actual),
                        missingSourceFragment is not null
                            ? $"missing expected source fragment: {missingSourceFragment}"
                            : missingTargetBodyFragment is not null
                                ? $"missing expected target body fragment: {missingTargetBodyFragment}"
                                : actual.Detail,
                        actual.IlDiffDiagnostic,
                        actual.IlDiff,
                        actual.MemberAnchor,
                        actual.FaultIsolation,
                        ReturnToSenderClosureEvidenceBuilder.FromPlan(actual.Plan),
                        target.IsFrontier,
                        target.Note));
                }
            }

            return new GeneratedFixtureReturnToSenderRunResult(root, assemblyPath, results);
        });
    }

    static string? MissingSourceFragment(string source, IReadOnlyList<string>? expectedSourceFragments)
    {
        if (expectedSourceFragments is not { Count: > 0 })
            return null;

        foreach (var expected in expectedSourceFragments)
        {
            if (!source.Contains(expected, StringComparison.Ordinal))
                return expected;
        }

        return null;
    }

    static GeneratedFixtureReturnToSenderResult Skipped(GeneratedFixtureDefinition fixture, GeneratedFixtureTarget target, string reason)
        => new(
            fixture.Id,
            target.Type,
            target.Method,
            target.Overload,
            GeneratedFixtureReturnToSenderStatus.Skip,
            ActualStatus: null,
            reason,
            Detail: null,
            IlDiffDiagnostic: null,
            IlDiff: null,
            MemberAnchor: null,
            FaultIsolation: null,
            ClosureEvidence: null,
            target.IsFrontier,
            target.Note);

    internal static string FailureReason(ReturnToSender.Result result)
    {
        var baseReason = result.Status switch
        {
            FidelityCheck.CompileBackStatus.RecompileFail => DiagnosticCode(result.Detail),
            FidelityCheck.CompileBackStatus.ContextFail => string.IsNullOrWhiteSpace(result.Detail) ? "context-fail" : result.Detail,
            FidelityCheck.CompileBackStatus.OpcodeDiff => "opcode-diff",
            _ => result.Status.ToString(),
        };

        return result.FaultIsolation?.Kind switch
        {
            ReturnToSender.FaultIsolationKind.BodyDefect => ComposeFaultIsolationReason("body-defect", baseReason),
            ReturnToSender.FaultIsolationKind.ShellOrClosureDefect => ComposeFaultIsolationReason("shell-or-closure-defect", baseReason),
            _ => baseReason,
        };
    }

    static string ComposeFaultIsolationReason(string isolationReason, string baseReason)
        => string.IsNullOrWhiteSpace(baseReason) || string.Equals(isolationReason, baseReason, StringComparison.Ordinal)
            ? isolationReason
            : $"{isolationReason} ({baseReason})";

    internal static T RunWithMaterializedFixtures<T>(
        IReadOnlyList<GeneratedFixtureDefinition> fixtures,
        GeneratedFixtureRunOptions? options,
        Func<string, string, T> run)
    {
        if (fixtures.Count == 0)
            throw new ArgumentException("At least one generated fixture is required.", nameof(fixtures));

        options ??= GeneratedFixtureRunOptions.Default;
        var root = Path.Combine(Path.GetTempPath(), "dotnet-inspect-generated-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string projectPath = Path.Combine(root, "GeneratedDecompilerFixtures.csproj");
            File.WriteAllText(projectPath, ProjectFile(options.TargetFramework ?? CurrentTargetFramework()));

            for (int i = 0; i < fixtures.Count; i++)
            {
                string sourcePath = Path.Combine(root, $"{i:000}_{SafeFileName(fixtures[i].Id)}.cs");
                File.WriteAllText(sourcePath, fixtures[i].Source);
            }

            Build(root);
            string assemblyPath = Path.Combine(root, "bin", "Release",
                options.TargetFramework ?? CurrentTargetFramework(), "GeneratedDecompilerFixtures.dll");
            if (!File.Exists(assemblyPath))
                throw new InvalidOperationException($"Generated fixture assembly was not produced: {assemblyPath}");

            return run(root, assemblyPath);
        }
        finally
        {
            if (!options.KeepArtifacts)
                TryDelete(root);
        }
    }

    public static string FormatReport(GeneratedFixtureRunResult run)
    {
        var sb = new StringBuilder();
        int fixtureCount = run.Results.Select(r => r.FixtureId).Distinct(StringComparer.Ordinal).Count();
        sb.AppendLine(
            $"GENERATED FIXTURE LADDER over {fixtureCount} fixture(s), {run.Results.Count} target method(s)");
        foreach (var result in run.Results.OrderBy(r => r.FixtureId, StringComparer.Ordinal).ThenBy(r => r.DisplayMember, StringComparer.Ordinal))
        {
            string actual = result.ActualStatus?.ToString() ?? "Missing";
            string shape = result.ExpectedShape is null
                ? ""
                : $"  shape={result.ActualShape?.ToString() ?? "Missing"}  expected-shape={result.ExpectedShape}";
            string frontierShape = result.FrontierShape is null
                ? ""
                : $"  frontier-shape={result.FrontierShape}";
            string frontier = result.IsFrontier ? " frontier" : "";
            string status = result.Passed ? "PASS" : "FAIL";
            sb.AppendLine(
                $"  {status}{frontier}  {result.FixtureId}  {result.DisplayMember}  " +
                $"decompiler={result.DecompilerFidelity}  compile-back={actual}  expected-compile-back={result.ExpectedStatus}{shape}{frontierShape}");
            if (!string.IsNullOrWhiteSpace(result.Detail))
                sb.AppendLine($"      detail: {result.Detail}");
            if (!string.IsNullOrWhiteSpace(result.ShapeDetail))
                sb.AppendLine($"      shape-detail: {result.ShapeDetail}");
            if (!string.IsNullOrWhiteSpace(result.Note))
                sb.AppendLine($"      note: {result.Note}");
        }
        return sb.ToString();
    }

    public static string FormatReturnToSenderCatalogReport(GeneratedFixtureReturnToSenderRunResult run, int maxExamples)
        => ReturnToSenderCatalogReport.RenderPlain(ReturnToSenderCatalogReport.Build(run, maxExamples));

    public static string FormatReturnToSenderCatalogMarkout(GeneratedFixtureReturnToSenderRunResult run, int maxExamples)
        => ReturnToSenderCatalogReport.RenderMarkout(ReturnToSenderCatalogReport.Build(run, maxExamples));

    public static string FormatJson(GeneratedFixtureRunResult run)
    {
        var payload = new
        {
            ProjectDirectory = Directory.Exists(run.ProjectDirectory) ? run.ProjectDirectory : null,
            AssemblyPath = File.Exists(run.AssemblyPath) ? run.AssemblyPath : null,
            run.Results,
            run.Passed,
        };
        return JsonSerializer.Serialize(payload, s_jsonOptions);
    }

    public static string FormatReturnToSenderCatalogJson(GeneratedFixtureReturnToSenderRunResult run)
    {
        var research = ReturnToSenderEvidence.ToResearchComparison(ReturnToSenderEvidence.FromCatalog(run));
        var payload = new
        {
            ProjectDirectory = Directory.Exists(run.ProjectDirectory) ? run.ProjectDirectory : null,
            AssemblyPath = File.Exists(run.AssemblyPath) ? run.AssemblyPath : null,
            Results = run.Results.Select(SerializableReturnToSenderResult).ToArray(),
            ResearchDiff = SerializableResearchComparison(research),
            ResearchSummary = ReturnToSenderEvidence.Summarize(research, int.MaxValue),
            RoslynFallbacks = ReturnToSenderCatalogReport.RoslynFallbackBuckets(run.Results),
            run.Passed,
        };
        return JsonSerializer.Serialize(payload, s_jsonOptions);
    }

    static object SerializableReturnToSenderResult(GeneratedFixtureReturnToSenderResult result)
        => new
        {
            result.FixtureId,
            result.Type,
            result.Method,
            result.Overload,
            result.Status,
            result.ActualStatus,
            result.Reason,
            result.Detail,
            IlDiffDiagnostic = SerializableIlDiffDisplayResult(result.IlDiffDiagnostic),
            IlDiff = SerializableIlMemberDiff(result.IlDiff),
            result.MemberAnchor,
            FaultIsolation = SerializableFaultIsolation(result.FaultIsolation),
            result.ClosureEvidence,
            result.IsFrontier,
            result.Note,
            result.DisplayMember,
        };

    static object? SerializableFaultIsolation(ReturnToSender.FaultIsolationResult? result)
        => result is null
            ? null
            : new
            {
                result.Kind,
                SourcePath = ReturnToSenderSourceProbe.SourceFileName(result.SourcePath),
                result.Detail,
            };

    static object? SerializableIlDiffDisplayResult(IlDiffDisplayResult? result)
        => result is null
            ? null
            : new
            {
                result.Failure,
                Rows = result.Rows.IsDefault ? Array.Empty<IlDiffDisplayRow>() : result.Rows.ToArray(),
                FailureRows = result.FailureRows.IsDefault ? Array.Empty<IlDiffDisplayFailureRow>() : result.FailureRows.ToArray(),
            };

    static object? SerializableIlMemberDiff(IlMemberDiffResult? result)
        => result is null
            ? null
            : new
            {
                result.Old,
                result.New,
                result.Diff.IsExact,
                result.Diff.Failure,
                Rows = result.Diff.Rows.IsDefault ? Array.Empty<IlDiffRow>() : result.Diff.Rows.ToArray(),
                FailureRows = result.Diff.FailureRows.IsDefault ? Array.Empty<IlDiffFailureRow>() : result.Diff.FailureRows.ToArray(),
            };

    static object SerializableResearchComparison(ResearchComparison research)
        => new
        {
            research.ApiDiff,
            Changes = research.Changes.Select(SerializableChange).ToArray(),
        };

    static object SerializableChange(ResearchChange change)
        => new
        {
            change.Subject,
            change.Mechanism,
            DescriptorId = change.Descriptor.Id,
            change.Kind,
            change.OldValue,
            change.NewValue,
            change.Delta,
            change.OldIlOffset,
            change.NewIlOffset,
            change.Detail,
            change.Category,
            change.Signal,
            change.Shape,
            change.Magnitude,
            change.DirectionScore,
            change.SubjectInBoth,
            change.InLoop,
            IlDisplayRows = change.IlDisplayRows.IsDefault ? Array.Empty<IlDiffDisplayRow>() : change.IlDisplayRows.ToArray(),
            change.IlDisplayFailureRow,
            IlMemberDiff = SerializableIlMemberDiff(change.IlMemberDiff),
        };

    public static string FormatList(IReadOnlyList<GeneratedFixtureDefinition> fixtures)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GENERATED FIXTURE CATALOG ({fixtures.Count} fixture(s))");
        foreach (var fixture in fixtures.OrderBy(fixture => fixture.Id, StringComparer.Ordinal))
        {
            sb.AppendLine($"  {fixture.Id}  [{string.Join(", ", fixture.Tags)}]");
            foreach (var target in fixture.Targets)
            {
                string frontier = target.IsFrontier ? " frontier" : "";
                string shape = target.ExpectedShape?.ToString() ?? "none";
                string frontierShape = target.FrontierShape is null ? "" : $"  frontier-shape={target.FrontierShape}";
                sb.AppendLine(
                    $"      {target.DisplayMember}  compile-back={target.ExpectedStatus}  shape={shape}{frontierShape}{frontier}");
            }
        }
        return sb.ToString();
    }

    public static string FormatListJson(IReadOnlyList<GeneratedFixtureDefinition> fixtures)
    {
        var items = fixtures
            .OrderBy(fixture => fixture.Id, StringComparer.Ordinal)
            .Select(fixture => new
            {
                fixture.Id,
                fixture.Tags,
                Targets = fixture.Targets.Select(target => new
                {
                    target.Type,
                    target.Method,
                    target.Overload,
                    ExpectedStatus = target.ExpectedStatus.ToString(),
                    ExpectedShape = target.ExpectedShape?.ToString(),
                    FrontierShape = target.FrontierShape?.ToString(),
                    target.IsFrontier,
                    target.Note,
                }),
            });
        return JsonSerializer.Serialize(items, s_jsonOptions);
    }

    static string Key(string type, string method, int overload) => $"{type}::{method}#{overload}";

    static string DiagnosticCode(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "recompile-fail";

        for (int i = 0; i <= detail.Length - 6; i++)
        {
            if (detail[i] == 'C'
                && detail[i + 1] == 'S'
                && char.IsDigit(detail[i + 2])
                && char.IsDigit(detail[i + 3])
                && char.IsDigit(detail[i + 4])
                && char.IsDigit(detail[i + 5]))
            {
                return detail.Substring(i, 6);
            }
        }

        return "recompile-fail";
    }

    static string ProjectFile(string targetFramework) =>
        $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{{targetFramework}}</TargetFramework>
            <ImplicitUsings>disable</ImplicitUsings>
            <Nullable>disable</Nullable>
            <LangVersion>preview</LangVersion>
            <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
            <IsPackable>false</IsPackable>
            <IsAotCompatible>false</IsAotCompatible>
            <AssemblyName>GeneratedDecompilerFixtures</AssemblyName>
          </PropertyGroup>
        </Project>
        """;

    static string CurrentTargetFramework()
    {
        var frameworkName = typeof(GeneratedFixtureRunner).Assembly
            .GetCustomAttributes(typeof(TargetFrameworkAttribute), inherit: false)
            .OfType<TargetFrameworkAttribute>()
            .FirstOrDefault()
            ?.FrameworkName;
        if (frameworkName is null)
            return "net11.0";

        const string prefix = ".NETCoreApp,Version=v";
        return frameworkName.StartsWith(prefix, StringComparison.Ordinal)
            ? "net" + frameworkName[prefix.Length..]
            : "net11.0";
    }

    static string SafeFileName(string id)
    {
        var chars = id.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        return new string(chars);
    }

    static Dictionary<string, GeneratedFixtureRender> DecompilerRenders(
        string assemblyPath,
        IReadOnlyList<GeneratedFixtureDefinition> fixtures)
    {
        var renders = new Dictionary<string, GeneratedFixtureRender>(StringComparer.Ordinal);
        using var source = MetadataSource.Open(assemblyPath);
        foreach (var target in fixtures.SelectMany(fixture => fixture.Targets))
        {
            var function = IrImporter.Import(source, target.Type, target.Method, target.Overload);
            if (function is null)
                continue;

            var rendered = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
            renders[Key(target.Type, target.Method, target.Overload)] = new(function.Fidelity.ToString(), rendered.Output);
        }

        return renders;
    }

    static (SyntaxKind? ActualShape, string? Detail) ShapeVerdict(string? body, SyntaxKind? expected, SyntaxKind? frontier)
    {
        if (expected is null)
            return (null, null);
        if (string.IsNullOrWhiteSpace(body))
            return (null, "shape-body-missing");

        var tree = CSharpSyntaxTree.ParseText(
            $$"""
            class __GeneratedFixtureShapeShell
            {
                void __M()
                {
            {{Indent(body)}}
                }
            }
            """,
            new CSharpParseOptions(LanguageVersion.Preview));
        var root = tree.GetRoot();
        if (frontier is { } frontierKind && root.DescendantNodes().Any(node => node.IsKind(frontierKind)))
            return (frontierKind, "frontier-shape-achieved");

        if (root.DescendantNodes().Any(node => node.IsKind(expected.Value)))
            return (expected.Value, null);

        var observed = root
            .DescendantNodes()
            .Select(node => (SyntaxKind)node.RawKind)
            .FirstOrDefault(kind => s_interestingShapes.Contains(kind));
        return observed == SyntaxKind.None
            ? (null, "shape-not-found")
            : (observed, "shape-not-found");
    }

    static string Indent(string body)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return string.Join(Environment.NewLine, lines.Select(line => "        " + line));
    }

    static void Build(string workingDirectory)
    {
        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.ArgumentList.Add("build");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("Release");
        process.StartInfo.ArgumentList.Add("--nologo");
        process.StartInfo.ArgumentList.Add("--verbosity");
        process.StartInfo.ArgumentList.Add("quiet");
        if (!process.Start())
            throw new InvalidOperationException("Could not start dotnet build for generated fixtures.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 120_000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw new TimeoutException("Generated fixture build timed out.");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Generated fixture build failed with exit code {process.ExitCode}.\n{stdout}\n{stderr}");
    }

    static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
