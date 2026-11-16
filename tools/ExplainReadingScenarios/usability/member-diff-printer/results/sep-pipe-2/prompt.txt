You are a fresh participant in a reading trial. Everything you need is in this message. Do not use tools, read files, or search. Answer only from the outputs shown below, and say "not stated" when the outputs do not establish something. Reply with one JSON object and nothing else.

Each output below is the beginning of a unified diff that compares one Member of the Example library between version 1.0.0 and 2.0.0. Only the file headers and the first hunk header are shown; `...` stands for the hunk's lines.

Output 1:
```text
--- Example 1.0.0
+++ Example 2.0.0
@@ -3,4 +3,4 @@ public void Run(long count) | moved (1) to +40
 ...
```

Output 2:
```text
--- Example 1.0.0
+++ Example 2.0.0
@@ -7,3 +7,3 @@ public int this[int index]
 ...
```

Output 3:
```text
--- Example 1.0.0
+++ Example 2.0.0
@@ -2,5 +2,5 @@ public void Open(FileAccess access = FileAccess.Read \| FileAccess.Write) | whitespace-only: indentation
 ...
```

Output 4:
```text
--- Example 1.0.0
+++ Example 2.0.0
@@ -4,2 +4,2 @@ public string Format(string pattern = "a // b")
 ...
```

Output 5:
```text
--- Example 1.0.0
+++ Example 2.0.0
@@ -12,4 +12,0 @@ | moved (1) from -12; whitespace-only: indentation
 ...
```

Output 6:
```text
--- Example 1.0.0
+++ Example 2.0.0
@@ -1,3 +1,3 @@ public class Widget
 ...
```

In a unified diff hunk header, git may print a heading after the closing "@@" that names the declaration enclosing the hunk. These outputs may also carry a change label that describes how the change was characterized.

For each output (1 to 6), report:
- "heading": the enclosing declaration exactly as it is written in C# source, or null if the header has none.
- "label": the change label exactly, or null if the header has none.

Also report "format_notes": anything in the header format you found ambiguous or had to guess.

Reply with exactly this JSON shape:
{"1": {"heading": null, "label": null}, "2": {...}, "3": {...}, "4": {...}, "5": {...}, "6": {...}, "format_notes": ""}
