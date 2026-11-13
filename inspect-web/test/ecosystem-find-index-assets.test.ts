import assert from "node:assert/strict";
import test from "node:test";

interface AssetModule {
  loadAspire(): Promise<string>;
  loadMicrosoftExtensions(): Promise<string>;
}

function isAssetModule(value: unknown): value is AssetModule {
  if (typeof value !== "object" || value === null) return false;
  return "loadAspire" in value
    && typeof value.loadAspire === "function"
    && "loadMicrosoftExtensions" in value
    && typeof value.loadMicrosoftExtensions === "function";
}

async function loadModule(testName: string): Promise<AssetModule> {
  const moduleUrl = new URL(
    `../src/ecosystem-find-index-assets.ts?${testName}`,
    import.meta.url,
  ).href;
  const loaded: unknown = await import(moduleUrl);
  if (!isAssetModule(loaded))
    throw new TypeError("The indexed Find asset module is invalid.");
  return loaded;
}

test("indexed Find assets preserve bytes and cache each fetch", async () => {
  const originalFetch = globalThis.fetch;
  const calls: string[] = [];
  const expected = Uint8Array.from(
    { length: 0x8001 },
    (_, index) => index % 251,
  );
  globalThis.fetch = async input => {
    calls.push(
      typeof input === "string"
        ? input
        : input instanceof URL
        ? input.href
        : input.url,
    );
    return new Response(expected);
  };
  try {
    const assets = await loadModule("byte-fidelity");
    const first = await assets.loadAspire();
    const second = await assets.loadAspire();

    assert.equal(first, Buffer.from(expected).toString("base64"));
    assert.equal(second, first);
    assert.deepEqual(calls, ["/assets/ecosystem-find/aspire.json"]);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("indexed Find asset HTTP failures remain visible", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response(null, { status: 503 });
  try {
    const assets = await loadModule("http-failure");
    await assert.rejects(
      assets.loadMicrosoftExtensions(),
      /microsoftExtensions.*HTTP 503/,
    );
  } finally {
    globalThis.fetch = originalFetch;
  }
});
