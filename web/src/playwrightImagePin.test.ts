// The e2e harness drives a browser inside `mcr.microsoft.com/playwright:v<version>-noble` while the
// test runner is the `@playwright/test` installed here. The two speak a wire protocol that is only
// guaranteed between matching versions, and the image pin is a string literal in `e2e/run.mjs` that
// no package manager moves — so a dependency bump that forgets it fails at e2e time, on CI, looking
// like a browser problem. This makes it fail here instead, at the bump.
import fs from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

// Off the working directory, as frozenHooks.test.ts explains: jsdom rewrites import.meta.url.
const read = (p: string) => fs.readFileSync(path.resolve(p), "utf8");

describe("Playwright browser image pin", () => {
  it("matches the installed @playwright/test version", () => {
    const installed = JSON.parse(read("node_modules/@playwright/test/package.json")).version;
    const pinned = /mcr\.microsoft\.com\/playwright:v([\d.]+)-noble/.exec(read("e2e/run.mjs"))?.[1];

    expect(pinned).toBe(installed);
  });
});
