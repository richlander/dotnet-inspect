I first read the symbol document’s schema to see which fields and lookup indexes could answer each question. I then queried Stream parameters, JsonDocument producers, trimming attributes, converter types, and `ParseAsync` independently. I compared the results with the document’s lookup lists and counts to check for omissions.

For source locations, I fetched the one published source file because the document’s line numbers pointed inside method bodies; reading its numbered declarations gave the lines the question asked for. Once those checks accounted for every matching API, I had enough evidence to write `answer.json` and stopped.

