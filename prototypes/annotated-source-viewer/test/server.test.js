import assert from "node:assert/strict";
import { once } from "node:events";
import net from "node:net";
import test from "node:test";
import { createViewerServer } from "../server.js";

test("server returns content and contains malformed paths", async () => {
  const server = createViewerServer();
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  const { port } = server.address();

  try {
    const index = await fetch(`http://127.0.0.1:${port}/`);
    assert.equal(index.status, 200);
    assert.match(await index.text(), /<title>Annotated source viewer<\/title>/);

    const malformed = await rawRequest(
      port,
      "GET /%ZZ HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n",
    );
    assert.match(malformed, /^HTTP\/1\.1 400 Bad Request/);

    const traversal = await rawRequest(
      port,
      "GET /..%2Fserver.js HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n",
    );
    assert.match(traversal, /^HTTP\/1\.1 403 Forbidden/);
  } finally {
    server.close();
    await once(server, "close");
  }
});

function rawRequest(port, request) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(port, "127.0.0.1", () => socket.write(request));
    let response = "";
    socket.setEncoding("utf8");
    socket.on("data", chunk => response += chunk);
    socket.on("end", () => resolve(response));
    socket.on("error", reject);
  });
}
