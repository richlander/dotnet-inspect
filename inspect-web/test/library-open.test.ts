import assert from "node:assert/strict";
import test from "node:test";

import {
  admitLibraryUpload,
  MAX_LIBRARY_UPLOAD_BYTES,
  type LibraryUploadCandidate,
} from "../src/library-open.ts";

function file(name: string, size: number): LibraryUploadCandidate {
  return { name, size };
}

test("picker, drop, and paste share one accepted file admission", () => {
  const selected = file("Example.dll", 4096);

  for (const source of ["picker", "drop", "paste"]) {
    const result = admitLibraryUpload([selected]);
    assert.deepEqual(
      result,
      { kind: "accepted", file: selected },
      source);
  }
});

test("admission rejects missing, empty, multiple, and over-bound files visibly", () => {
  assert.deepEqual(admitLibraryUpload([]), {
    kind: "rejected",
    message: "Choose one managed .NET assembly.",
  });
  assert.deepEqual(admitLibraryUpload([
    file("Empty.dll", 0),
  ]), {
    kind: "rejected",
    message: "The selected file is empty.",
  });
  assert.deepEqual(admitLibraryUpload([
    file("One.dll", 1),
    file("Two.dll", 1),
  ]), {
    kind: "rejected",
    message: "Open one managed .NET assembly at a time.",
  });
  assert.deepEqual(admitLibraryUpload([
    file("Large.dll", MAX_LIBRARY_UPLOAD_BYTES + 1),
  ]), {
    kind: "rejected",
    message: "The selected file exceeds the 32 MiB upload limit.",
  });
});
