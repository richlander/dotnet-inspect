import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path.cwd()
report = (root / "probe-output.txt").read_text()
xml_file = "package/lib/net10.0/System.Text.Json.xml"
dll_file = "package/lib/net10.0/System.Text.Json.dll"
nuspec_file = "package/System.Text.Json.nuspec"
probe_command = "dotnet run --project probe -f net10.0 > probe-output.txt"
commit = "0427b9abbead3fa64c6655eb4290c537397131fa"
source_base = f"https://github.com/dotnet/dotnet/blob/{commit}/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json"
read_source = source_base + "/Serialization/JsonSerializer.Read.Stream.cs"
document_source = source_base + "/Document/JsonDocument.Parse.cs"


def section(name):
    return report.split("=== " + name + " ===\n", 1)[1].split("\n===", 1)[0].strip().splitlines()


def api_list(name):
    entries = []
    for line in section(name):
        declaring_type, signature = line.split(" :: ", 1)
        if "out System.Text.Json.JsonDocument document" in signature:
            signature = signature.replace("out System.Text.Json.JsonDocument document",
                                          "out System.Text.Json.JsonDocument? document")
        # NullabilityInfoContext reports the open generic argument as nullable,
        # while the original C# declaration spells it JsonTypeInfo<TValue>.
        signature = signature.replace("JsonTypeInfo<TValue?>", "JsonTypeInfo<TValue>")
        entries.append({"declaring_type": declaring_type, "signature": signature})
    return entries


stream_apis = api_list("STREAM PUBLIC/PROTECTED")
document_apis = api_list("JSONDOCUMENT RESULTS PUBLIC/PROTECTED")
marked = {}
current = None
for line in section("RUC ALL DECLARED METHODS (including internal/private)"):
    match = re.match(r"^(.+?) :: (\S+) count=\d+ same-named-unmarked=\d+$", line)
    if match:
        current = match.group(1), match.group(2)
        marked[current] = []
    elif line.startswith("  + public ") or line.startswith("  + protected "):
        marked[current].append(line[4:].replace("JsonTypeInfo<TValue?>", "JsonTypeInfo<TValue>"))

ruc_groups = []
for line in section("RUC PUBLIC/PROTECTED GROUPS"):
    match = re.match(
        r"^(.+?) :: (\S+) count=(\d+) unmarked-public-protected-overload=(True|False)$", line
    )
    declaring_type, member, count, unmarked = match.groups()
    signatures = marked[(declaring_type, member)]
    assert len(signatures) == int(count), (declaring_type, member, count, signatures)
    if member.startswith("get_"):
        member = member[4:] + " (getter)"
    elif member == ".ctor":
        member = declaring_type.rsplit(".", 1)[-1] + " (constructor)"
    ruc_groups.append({
        "declaring_type": declaring_type,
        "member": member,
        "marked_signatures": signatures,
        "same_named_unmarked_public_or_protected_overload": unmarked == "True",
    })

doc_member = ET.parse(root / xml_file).find(
    ".//member[@name='M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)']"
)
assert doc_member is not None
summary_xml = ET.tostring(doc_member.find("summary"), encoding="unicode").strip()
assert "The stream is read to completion." in summary_xml
summary = "".join(doc_member.find("summary").itertext())

source_lines = [
    ("Deserialize<TValue>(Stream utf8Json, JsonSerializerOptions? options = null)", 76),
    ("Deserialize(Stream utf8Json, Type returnType, JsonSerializerOptions? options = null)", 146),
    ("Deserialize<TValue>(Stream utf8Json, JsonTypeInfo<TValue> jsonTypeInfo)", 234),
    ("Deserialize(Stream utf8Json, JsonTypeInfo jsonTypeInfo)", 259),
    ("Deserialize(Stream utf8Json, Type returnType, JsonSerializerContext context)", 335),
]
read_source_text = (root / "JsonSerializer.Read.Stream.cs").read_text().splitlines()
for _, lineno in source_lines:
    assert "public static" in read_source_text[lineno - 1]

converter_types = [
    {"type": "System.Text.Json.Serialization.JsonConverter",
     "externally_derivable": False,
     "must_override": [],
     "note": "The constructor is internal; direct external subclassing is unavailable."},
    {"type": "System.Text.Json.Serialization.JsonConverter<T>",
     "externally_derivable": True,
     "must_override": [
         "public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)",
         "public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)",
     ]},
    {"type": "System.Text.Json.Serialization.JsonConverterAttribute",
     "externally_derivable": True,
     "must_override": [],
     "note": "When using its protected parameterless constructor to supply a converter, override the virtual CreateConverter(Type typeToConvert); it is not abstract."},
    {"type": "System.Text.Json.Serialization.JsonConverterFactory",
     "externally_derivable": True,
     "must_override": [
         "public override bool CanConvert(Type typeToConvert)",
         "public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)",
     ]},
    {"type": "System.Text.Json.Serialization.JsonNumberEnumConverter<TEnum>",
     "externally_derivable": False, "must_override": [],
     "note": "Sealed."},
    {"type": "System.Text.Json.Serialization.JsonStringEnumConverter",
     "externally_derivable": True, "must_override": [],
     "note": "Concrete; inherited abstract members are already implemented."},
    {"type": "System.Text.Json.Serialization.JsonStringEnumConverter<TEnum>",
     "externally_derivable": True, "must_override": [],
     "note": "Concrete; inherited abstract members are already implemented."},
]

answers = [
    {
        "question": 1,
        "answer": stream_apis,
        "evidence": [f"{dll_file}; {probe_command}; probe-output.txt section STREAM PUBLIC/PROTECTED",
                     f"{xml_file} (Stream member entries)"],
        "complete": True,
        "why": "Enumerated all declared methods and constructors on exported types, including protected members, and filtered parameters by System.IO.Stream (30 APIs).",
    },
    {
        "question": 2,
        "answer": document_apis,
        "evidence": [f"{dll_file}; {probe_command}; probe-output.txt section JSONDOCUMENT RESULTS PUBLIC/PROTECTED",
                     f"{document_source}#L394"],
        "complete": True,
        "why": "Enumerated declared public/protected APIs with a JsonDocument return, Task<JsonDocument>, ValueTask<JsonDocument>, or out JsonDocument; the source confirms TryParseValue's nullable out annotation (13 APIs).",
    },
    {
        "question": 3,
        "answer": ruc_groups,
        "evidence": [f"{dll_file}; {probe_command}; probe-output.txt sections RUC ALL DECLARED METHODS and RUC PUBLIC/PROTECTED GROUPS"],
        "complete": True,
        "why": "Covers all 20 public/protected member-name groups on exported types using their actual RequiresUnreferencedCode attributes; same-named overload comparisons include only externally accessible members. Private/internal implementation methods are not caller APIs.",
    },
    {
        "question": 4,
        "answer": {
            "signature": "public static Task<JsonDocument> ParseAsync(Stream utf8Json, JsonDocumentOptions options = default, CancellationToken cancellationToken = default)",
            "documentation_summary": summary,
            "documentation_summary_xml": summary_xml,
        },
        "evidence": [f"{xml_file}: member M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)",
                     f"{document_source}#L208-L211"],
        "complete": True,
        "why": "The signature is verbatim from the matching package source, and the summary is verbatim from the lib/net10.0 package XML documentation.",
    },
    {
        "question": 5,
        "answer": converter_types,
        "evidence": [f"{dll_file}; {probe_command}; probe-output.txt section CONVERTER TYPES PUBLIC",
                     f"{xml_file}: converter type and member documentation",
                     f"{source_base}/Serialization/JsonConverter.cs#L16-L57",
                     f"{source_base}/Serialization/JsonConverterFactory.cs#L15-L33"],
        "complete": True,
        "why": "The seven exported type names containing Converter were enumerated; constructor accessibility, sealed/abstract flags, and implemented versus abstract inherited methods determine external derivation obligations.",
    },
    {
        "question": 6,
        "answer": [
            {"overload": name, "file_url": f"{read_source}#L{lineno}", "line": lineno}
            for name, lineno in source_lines
        ],
        "evidence": [f"{nuspec_file}: repository commit {commit}",
                     f"JsonSerializer.Read.Stream.cs: lines 76, 146, 234, 259, 335",
                     read_source],
        "complete": True,
        "why": "All five synchronous Deserialize Stream overloads in the package match public definitions in the source file at the NuGet repository commit.",
    },
]
assert len(stream_apis) == 30
assert len(document_apis) == 13
assert len(ruc_groups) == 20
assert len(converter_types) == 7
(root / "answer.json").write_text(
    json.dumps({"answers": answers,
                "friction": "No .NET inspection tool was available; used a small .NET SDK reflection program against the downloaded net10.0 package plus its XML documentation and commit-pinned source. The SDK template rejected --quiet once; rerunning without it worked."},
               indent=2) + "\n"
)
