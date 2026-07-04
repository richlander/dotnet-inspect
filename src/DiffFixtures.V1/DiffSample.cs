using System.Collections.Generic;

namespace DiffFixtureSample
{
    public static class DiffSample
    {
        // V1: a single allocation, not in a loop (1 allocation).
        public static void RegressesAllocInLoop(int n, List<object> sink)
        {
            sink.Add(new object());
        }

        // V1: three allocations.
        public static void ImprovesAlloc(List<object> sink)
        {
            sink.Add(new object());
            sink.Add(new object());
            sink.Add(new object());
        }

        // V1: one allocation, not in a loop (count 1, allocInLoop=false).
        public static void SameAllocationCountBecomesHot(int n, List<object> sink)
        {
            sink.Add(new object());
        }

        // Identical in both versions -> no diff row.
        public static int Stable() => 42;

        // V1/V2 differ only in the loaded constant value.
        public static int ConstantValue() => 1;

        // V1/V2 have two separated value changes with stable work between them.
        public static int MultipleHunks(int value)
        {
            int first = value + 1;
            Sink(first);
            return value + 3;
        }

        // V1/V2 differ only in a user-string token operand.
        public static string StringToken() => "alpha";

        // V1/V2 differ only in a member-reference token operand.
        public static int CallToken(int value) => System.Math.Abs(value);

        // V1/V2 differ in instance and static field token operands.
        public static int FieldToken(FieldTokenHolder holder, int value)
        {
            holder.InstanceA = value;
            FieldTokenHolder.StaticA = value + 1;
            return holder.InstanceA + FieldTokenHolder.StaticA;
        }

        // V1/V2 differ in type token operands across common type-bearing opcodes.
        public static int TypeTokenShapes(object input, int length)
        {
            System.Type type = typeof(TypeTokenA);
            int matches = input is TypeTokenA ? 1 : 0;
            object typedInput = input ?? new TypeTokenA();
            TypeTokenA cast = (TypeTokenA)typedInput;
            TypeTokenA[] values = new TypeTokenA[length];
            return type.Name.Length + matches + cast.Value + values.Length;
        }

        // V1/V2 differ in primitive type token operands for box/unbox.any.
        public static int BoxToken(int value)
        {
            object boxed = (short)value;
            return (short)boxed;
        }

        // C# emits ldtoken for the type handle.
        public static int LdTokenType()
        {
            System.Type type = typeof(TypeTokenA);
            return type.Name.Length;
        }

        // C# emits ldtoken for the backing data field used by InitializeArray.
        public static int LdTokenField()
        {
            int[] values = new int[]
            {
                1, 2, 3, 4, 5, 6, 7, 8,
                9, 10, 11, 12, 13, 14, 15, 16,
            };
            return values[0];
        }

        // C# emits a method token operand for the function pointer target.
        public static unsafe int MethodToken(int value)
        {
            delegate*<int, int> target = &TokenTargetA;
            return target(value);
        }

        // V2 inserts an operation before the label target. The branch target's
        // raw IL offset shifts, but it still targets the same logical return.
        public static int BranchTargetOffsetShift(bool skip)
        {
            if (skip)
                goto Target;

            Sink(1);

        Target:
            return 3;
        }

        // V2 retargets the branch to a different return.
        public static int BranchRetarget(bool skip)
        {
            if (skip)
                goto First;

            goto Second;

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
                    goto First;
                case 1:
                    goto Second;
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
            MaybeThrow(value);
            return value + 1;
        }

        // V2 wraps equivalent work in a finally region.
        public static int FinallyAvailability(int value)
        {
            Sink(value);
            return value;
        }

        // V1/V2 both have a catch region, but V2 extends the protected range.
        public static int TryCatchRegionShape(int value)
        {
            try
            {
                MaybeThrow(value);
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
            return System.Math.Abs(first) + System.Math.Abs(second);
        }

        // V1/V2 are a slot/local near-miss: raw slot identity is still surfaced.
        public static int SlotLocalShapeNearMiss(int value)
        {
            int first;
            int second;
            Assign(out first, value + 1);
            Assign(out second, value + 2);
            return first + second;
        }

        // V1: safe body. V2 adds a visible unsafe operation.
        public static int AddsUnsafe(int value) => value;

        static int TokenTargetA(int value) => value + 1;

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
}