using System.Collections.Generic;

namespace DiffFixtureSample
{
    public static class DiffSample
    {
        // V2: the allocation moved into a loop and gained a second site (2 allocations,
        // in-loop) -> an allocation regression that is hot.
        public static void RegressesAllocInLoop(int n, List<object> sink)
        {
            sink.Add(new object());
            for (int i = 0; i < n; i++)
                sink.Add(new object());
        }

        // V2: a single allocation -> an improvement vs V1's three.
        public static void ImprovesAlloc(List<object> sink)
        {
            sink.Add(new object());
        }

        // V2: the same single allocation moved into a loop (count still 1, but
        // allocInLoop=true) -> a hotness-only allocation regression the count delta misses.
        public static void SameAllocationCountBecomesHot(int n, List<object> sink)
        {
            for (int i = 0; i < n; i++)
                sink.Add(new object());
        }

        // Identical in both versions -> no diff row.
        public static int Stable() => 42;

        // V1/V2 differ only in the loaded constant value.
        public static int ConstantValue() => 2;

        // V1/V2 have two separated value changes with stable work between them.
        public static int MultipleHunks(int value)
        {
            int first = value + 2;
            Sink(first);
            return value + 4;
        }

        // V1/V2 differ only in a user-string token operand.
        public static string StringToken() => "beta";

        // V1/V2 differ only in a member-reference token operand.
        public static int CallToken(int value) => System.Math.Sign(value);

        // V1/V2 differ in instance and static field token operands.
        public static int FieldToken(FieldTokenHolder holder, int value)
        {
            holder.InstanceB = value;
            FieldTokenHolder.StaticB = value + 1;
            return holder.InstanceB + FieldTokenHolder.StaticB;
        }

        // V1/V2 differ in type token operands across common type-bearing opcodes.
        public static int TypeTokenShapes(object input, int length)
        {
            System.Type type = typeof(TypeTokenB);
            int matches = input is TypeTokenB ? 1 : 0;
            object typedInput = input ?? new TypeTokenB();
            TypeTokenB cast = (TypeTokenB)typedInput;
            TypeTokenB[] values = new TypeTokenB[length];
            return type.Name.Length + matches + cast.Value + values.Length;
        }

        // V1/V2 differ in primitive type token operands for box/unbox.any.
        public static int BoxToken(int value)
        {
            object boxed = (long)value;
            return (int)(long)boxed;
        }

        // C# emits ldtoken for the type handle.
        public static int LdTokenType()
        {
            System.Type type = typeof(TypeTokenB);
            return type.Name.Length;
        }

        // C# emits ldtoken for the backing data field used by InitializeArray.
        public static int LdTokenField()
        {
            int[] values = new int[]
            {
                16, 15, 14, 13, 12, 11, 10, 9,
                8, 7, 6, 5, 4, 3, 2, 1,
            };
            return values[0];
        }

        // C# emits a method token operand for the function pointer target.
        public static unsafe int MethodToken(int value)
        {
            delegate*<int, int> target = &TokenTargetB;
            return target(value);
        }

        // V2 inserts an operation before the label target. The branch target's
        // raw IL offset shifts, but it still targets the same logical return.
        public static int BranchTargetOffsetShift(bool skip)
        {
            if (skip)
                goto Target;

            Sink(1);
            Sink(2);

        Target:
            return 3;
        }

        // V2 retargets the branch to a different return.
        public static int BranchRetarget(bool skip)
        {
            if (skip)
                goto Second;

            goto First;

        First:
            return 1;

        Second:
            return 2;
        }

        // V1/V2 keep the same switch target count but retarget case arms.
        public static int SwitchRetarget(int value)
        {
            switch (value)
            {
                case 0:
                    goto Second;
                case 1:
                    goto First;
                case 2:
                    goto Third;
                default:
                    return 4;
            }

        First:
            return 1;

        Second:
            return 2;

        Third:
            return 3;
        }

        // V2 wraps equivalent work in a catch region.
        public static int TryCatchAvailability(int value)
        {
            try
            {
                MaybeThrow(value);
                return value + 1;
            }
            catch (System.InvalidOperationException)
            {
                return -1;
            }
        }

        // V2 wraps equivalent work in a finally region.
        public static int FinallyAvailability(int value)
        {
            try
            {
                Sink(value);
                return value;
            }
            finally
            {
                Sink(-value);
            }
        }

        // V1/V2 both have a catch region, but V2 extends the protected range.
        public static int TryCatchRegionShape(int value)
        {
            try
            {
                MaybeThrow(value);
                Sink(value + 1);
            }
            catch (System.InvalidOperationException)
            {
                return -1;
            }

            return value;
        }

        // V1/V2 have repeated calls where only the second occurrence changes.
        public static int RepeatedCallOneOccurrence(int first, int second)
        {
            return System.Math.Abs(first) + System.Math.Sign(second);
        }

        // V1/V2 are a slot/local near-miss: raw slot identity is still surfaced.
        public static int SlotLocalShapeNearMiss(int value)
        {
            int second;
            int first;
            Assign(out second, value + 2);
            Assign(out first, value + 1);
            return first + second;
        }

        public static int SemanticSwitchCase(int value)
        {
            switch (value)
            {
                case 0:
                    goto Zero;
                case 1:
                    goto One;
                case 2:
                    goto Two;
                case 3:
                    goto Three;
                case 4:
                    goto Four;
                case 5:
                    goto Five;
                case 7:
                    goto Seven;
                case 8:
                    goto Eight;
                default:
                    return -1;
            }

        Zero:
            return 10;

        One:
            return 11;

        Two:
            return 12;

        Three:
            return 13;

        Four:
            return 14;

        Five:
            return 15;

        Seven:
            return 17;

        Eight:
            return 18;
        }

        public static int SemanticReturnExpression(int value)
        {
            return value + 2;
        }

        public static int SemanticCallChange(int value)
        {
            MaybeThrow(value);
            return value;
        }

        public static int SemanticMultiCallChange(int value)
        {
            MaybeThrow(value);
            MaybeThrow(value + 1);
            return value;
        }

        public static string SemanticCallStringLiteralNearMiss()
        {
            string value = "Jane (Doe)";
            return value;
        }

        public static int SemanticCallArithmeticGroupingNearMiss(int value)
        {
            int adjusted = value * (value + 3);
            return adjusted;
        }

        public static int SemanticCallTrailingExpression(int value)
        {
            return System.Math.Sign(value) + 1;
        }

        // V2: visible unsafe operation added relative to V1.
        public static unsafe int AddsUnsafe(int value)
        {
            int* values = stackalloc int[1];
            values[0] = value;
            return values[0];
        }

        // Generic parameter rename from V1 must not break method identity.
        public static TElement GenericIdentity<TElement>(TElement value) => value;

        public static int GenericParamBody<T>(T value) => 2;

        static int TokenTargetB(int value) => value + 2;

        static void Assign(out int target, int value) => target = value;

        static void MaybeThrow(int value)
        {
            if (value == int.MinValue)
                throw new System.InvalidOperationException();
        }

        static void Sink(int value)
        {
            if (value == int.MinValue)
                throw new System.InvalidOperationException();
        }

        public sealed class FieldTokenHolder
        {
            public int InstanceA;
            public int InstanceB;
            public static int StaticA;
            public static int StaticB;
        }

        sealed class TypeTokenA
        {
            public int Value = 1;
        }

        sealed class TypeTokenB
        {
            public int Value = 1;
        }
    }

    public static class ExtensionSample
    {
        public static int Twice(this int value) => value * 3;
    }

    public class ProtectedSample
    {
        public int PublicConstant() => 0;
        protected int ProtectedConstant() => 2;
    }

    public class GenericOverloadSample
    {
        public int M() => 2;
        public int M<T>() => 10;
    }

    public class GenericTypeAritySample<T>
    {
        public int M() => 2;
    }

    public class GenericTypeAritySample<T1, T2>
    {
        public int M() => 10;
    }

    internal class InternalSurfaceSample
    {
        public int PublicBody() => 2;

        public class NestedPublic
        {
            public int PublicBody() => 2;
        }
    }

    public class NestedGenericOuter<T>
    {
        public class Inner<TInner>
        {
            public int M() => 2;
        }
    }

    public class NestedGenericOuter<T1, T2>
    {
        public class Inner<TInner>
        {
            public int M() => 10;
        }
    }

    public class ConstructorSample
    {
        readonly int _value;

        public ConstructorSample()
        {
            _value = 2;
        }

        public int Value => _value;
    }

    public class ConstructorRemovalSample
    {
        public int Value => 0;
    }

    public readonly struct ConversionSample
    {
        readonly int _value;

        public ConversionSample(int value)
        {
            _value = value;
        }

        public static implicit operator int(ConversionSample value) => value._value + 2;

        public static implicit operator string(ConversionSample value) => "stable";
    }

    public readonly struct OperatorSample
    {
        readonly int _value;

        public OperatorSample(int value)
        {
            _value = value;
        }

        public static OperatorSample operator +(OperatorSample left, OperatorSample right)
            => new(left._value + right._value + 2);
    }

    public readonly struct CheckedConversionSample
    {
        readonly int _value;

        public CheckedConversionSample(int value)
        {
            _value = value;
        }

        public static explicit operator int(CheckedConversionSample value) => value._value;

        public static explicit operator checked int(CheckedConversionSample value) => checked(value._value + 2);
    }

    public class GenericParameterCollisionSample<T>
    {
        public int M<U>(T value) => 1;

        public int M<U>(U value) => 20;
    }

    public class MethodRemovalSample
    {
    }

    public class BodyStateSample
    {
        public int BodyState() => 2;
    }

    public interface IExplicitSurface
    {
        int Get();
    }

    public class ExplicitSurface : IExplicitSurface
    {
        int IExplicitSurface.Get() => 2;
    }

    internal interface IInternalExplicitSurface
    {
        int Get();
    }

    public class InternalExplicitSurface : IInternalExplicitSurface
    {
        int IInternalExplicitSurface.Get() => 2;
    }
}
