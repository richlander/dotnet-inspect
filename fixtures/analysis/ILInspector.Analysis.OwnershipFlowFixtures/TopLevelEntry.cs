Console.WriteLine(TopLevelEqual(1, 2));

static bool TopLevelEqual<T>(T left, T right) => left!.Equals(right);
