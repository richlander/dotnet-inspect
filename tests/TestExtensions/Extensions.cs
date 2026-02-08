namespace TestExtensions;

// Classic extension methods (C# 3+)
public static class ClassicStringExtensions
{
    public static int WordCount(this string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    public static string Reverse(this string s) => new(s.ToCharArray().Reverse().ToArray());
}

// C# 14 extension members for string
public static class StringExtensions
{
    extension(string s)
    {
        // Extension methods (will have [Extension] attribute on the method)
        public string Truncate(int maxLength) => s.Length <= maxLength ? s : s[..maxLength];

        // Extension properties (compile as get_ accessors without [Extension] on the method)
        public bool IsBlank => string.IsNullOrWhiteSpace(s);
        public int CharCount => s.Length;
    }
}

// C# 14 extension members for IEnumerable<T>
public static class EnumerableExtensions
{
    extension<T>(IEnumerable<T> source)
    {
        // Extension methods
        public IEnumerable<T> Page(int pageNumber, int pageSize) => source.Skip((pageNumber - 1) * pageSize).Take(pageSize);
        public IEnumerable<TResult> Map<TResult>(Func<T, TResult> selector) => source.Select(selector);

        // Extension properties
        public bool IsEmpty => !source.Any();
    }
}

// C# 14 extension members for Task<T>
public static class TaskExtensions
{
    extension<T>(Task<T> task)
    {
        // Extension property
        public bool IsSuccessful => task.IsCompletedSuccessfully;
    }
}
