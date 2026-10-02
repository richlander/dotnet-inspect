import assert from "node:assert/strict";
import test from "node:test";

class MemoryCache {
  readonly #items = new Map<string, Response>();

  async match(request: Request): Promise<Response | undefined> {
    return this.#items.get(request.url)?.clone();
  }

  async put(request: Request, response: Response): Promise<void> {
    this.#items.set(request.url, response.clone());
  }
}

test("package-entry Cache Storage retains immutable binary items", async () => {
  const caches = new Map<string, MemoryCache>();
  Object.defineProperty(globalThis, "location", {
    configurable: true,
    value: new URL("https://inspect.example/"),
  });
  Object.defineProperty(globalThis, "caches", {
    configurable: true,
    value: {
      async open(name: string): Promise<MemoryCache> {
        let cache = caches.get(name);
        if (cache === undefined) {
          cache = new MemoryCache();
          caches.set(name, cache);
        }
        return cache;
      },
    },
  });
  Object.defineProperty(globalThis, "navigator", {
    configurable: true,
    value: {
      storage: {
        async persisted() {
          return true;
        },
      },
    },
  });

  const key = `${"a".repeat(64)}/entries/${"b".repeat(64)}`;
  const entryCache = await import(
    "../src/browser-package-entry-cache.ts"
  );
  await entryCache.configure("package-authority-entries-v1");
  await entryCache.publish(key, Buffer.from([1, 2, 3]).toString("base64"));
  await entryCache.publish(key, Buffer.from([9]).toString("base64"));

  const content = await entryCache.read(key);
  assert.notEqual(content, null);
  assert.equal(await entryCache.isPersistent(), true);
  assert.deepEqual(
    Buffer.from(content!, "base64"),
    Buffer.from([1, 2, 3]),
  );
  await assert.rejects(
    entryCache.read("../package.nupkg"),
    /package-entry cache key is invalid/,
  );
});
