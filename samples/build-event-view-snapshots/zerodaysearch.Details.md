Showing 1 of 51 diagnostic(s). Use --cards 51 to show all, --tail-cards N to show the end, or filter with --code.

/home/rich/git/bad-code/ZeroDaySearch/Program.cs(9,5): error CS1739: The best overload for 'Option' does not have a parameter named 'description'
   7 | var versionsOption = new Option<string[]>(
   8 |     aliases: ["--versions", "-v"],
   9 |     description: "Filter by .NET versions (e.g., 8.0 9.0)")
     |     ^
  10 | {
  11 |     AllowMultipleArgumentsPerToken = true
