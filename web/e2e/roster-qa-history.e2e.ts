// Conversation history in the Roster Q&A surface, in a real browser (EXP-34).
//
// **The Agents host is not part of the e2e stack.** `run.mjs` starts the database, the Web API and
// the SPA and nothing else, so every `/agents/*` call here is fulfilled at the network boundary by
// the fixture below. That is a deliberate limit rather than an oversight: the Agents host needs
// Keycloak, the MCP server and a model provider's key, and standing all three up would make this
// spec a test of a provider's availability. What it does test is everything on this side of the
// wire — that the surface asks for the right conversation, survives a reload, and continues the
// one it re-opened — which is exactly the half jsdom cannot answer, because it turns on the SPA
// actually re-mounting against a fresh store rather than on state React was holding all along.
//
// The store is a plain Map in the test process and behaves the way `RosterQaConversationStore`
// does: an unknown `threadId` starts a fresh conversation instead of failing.
import { expect, test } from "@playwright/test";
import type { BrowserContext, Page } from "@playwright/test";
import { addVirtualAuthenticator, signUp } from "./passkey";

interface Turn {
  question: string;
  answer: string;
  modelId: string;
  grounded: boolean;
  createdAt: string;
  state: "ok" | "removed" | "hidden";
}
interface Conversation {
  id: string;
  title: string;
  createdAt: string;
  lastActiveAt: string;
  expiresAt: string;
  turns: Turn[];
}

const MODEL = "gemini-3.5-flash-lite";
const SIX_MONTHS_MS = 183 * 24 * 60 * 60 * 1000;

/** The answers this fixture gives, so an assertion names a sentence rather than a substring. */
const ANSWERS: Record<string, string> = {
  "Who knows React?": "Ada Lovelace does.",
  "Is she free in July?": "Yes — Ada Lovelace is free from 1 July.",
};

/**
 * Stands in for the Agents host for one page. Returns the store, so a spec can assert what the
 * server was actually asked to do rather than only what came back.
 */
async function stubTheAgentsHost(page: Page) {
  const store = new Map<string, Conversation>();
  /** Every `threadId` the SPA sent, in order — the continuation claim, seen from the server. */
  const threadIdsSent: (string | undefined)[] = [];

  // Matched on the path *root*, not with a `**/agents/**` glob: Vite serves this repo's own
  // modules under their source paths, and `src/api/agents/rosterQa.ts` contains `/agents/` too —
  // a glob catches those and answers 404, so the SPA never boots and every spec dies at the
  // sign-in form with no hint of why.
  await page.route(
    (url) => url.pathname === "/agents/models" || url.pathname.startsWith("/agents/roster-qa"),
    async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname.replace(/^\/agents/, "");
    const json = (data: unknown) => route.fulfill({ json: data as object });

    if (path === "/models") {
      return json({ provider: "Gemini", surfaces: { roster: [MODEL] } });
    }

    if (path === "/roster-qa" && route.request().method() === "POST") {
      const { question, threadId } = route.request().postDataJSON() as {
        question: string;
        threadId?: string;
      };
      threadIdsSent.push(threadId);
      const now = new Date();
      // An unknown id starts a fresh conversation rather than failing, exactly as the store does.
      const existing = threadId ? store.get(threadId) : undefined;
      const conversation: Conversation = existing ?? {
        id: `c${store.size + 1}`,
        title: question,
        createdAt: now.toISOString(),
        lastActiveAt: now.toISOString(),
        expiresAt: new Date(now.getTime() + SIX_MONTHS_MS).toISOString(),
        turns: [],
      };
      const answer = ANSWERS[question] ?? `No answer scripted for "${question}".`;
      conversation.turns.push({
        question, answer, modelId: MODEL, grounded: true,
        createdAt: now.toISOString(), state: "ok",
      });
      conversation.lastActiveAt = now.toISOString();
      store.set(conversation.id, conversation);
      return json({ answer, threadId: conversation.id, modelId: MODEL });
    }

    if (path === "/roster-qa/conversations") {
      if (route.request().method() === "DELETE") {
        store.clear();
        return route.fulfill({ status: 204 });
      }
      const index = [...store.values()]
        .sort((a, b) => b.lastActiveAt.localeCompare(a.lastActiveAt))
        .map(({ turns: _turns, ...summary }) => summary);
      return json(index);
    }

    const one = /^\/roster-qa\/conversations\/(.+)$/.exec(path);
    if (one) {
      if (route.request().method() === "DELETE") {
        store.delete(one[1]);
        return route.fulfill({ status: 204 });
      }
      const conversation = store.get(one[1]);
      return conversation ? json(conversation) : route.fulfill({ status: 404 });
    }

    // Anything else under `/agents/roster-qa` — nothing today, and a 404 says so honestly.
    return route.fulfill({ status: 404, json: {} });
    },
  );

  return { store, threadIdsSent };
}

async function signInAndOpenTheDock(context: BrowserContext, page: Page) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/signin");
  await addVirtualAuthenticator(context, page);
  await signUp(page);

  await page.getByRole("button", { name: "Open the agents assistant" }).click();
  const dockIt = page.getByRole("button", { name: "Dock to side" });
  if (await dockIt.count()) await dockIt.click();
  await expect(page.getByRole("button", { name: "Float" })).toBeVisible();
}

async function ask(page: Page, question: string) {
  await page.getByPlaceholder("Ask about the roster…").fill(question);
  await page.getByRole("button", { name: "Send" }).click();
}

test.describe("Roster Q&A conversation history", () => {
  test("an answer survives a reload, is reachable from the drawer, and can be continued", async ({
    context,
    page,
  }) => {
    const { store, threadIdsSent } = await stubTheAgentsHost(page);
    await signInAndOpenTheDock(context, page);

    // The surface opens onto nothing, and says so.
    await expect(page.getByRole("button", { name: "Conversation history" })).toBeVisible();

    await ask(page, "Who knows React?");
    await expect(page.getByText("Ada Lovelace does.")).toBeVisible();
    expect([...store.values()]).toHaveLength(1);

    // The point of doing this in a browser: after a reload the SPA holds nothing at all, so a
    // transcript that comes back came back from the history API.
    await page.reload();
    await page.getByRole("button", { name: "Open the agents assistant" }).click();
    await expect(page.getByText("Ada Lovelace does.")).toBeVisible();

    // It is in the drawer too, under today's heading and with the model that answered.
    await page.getByRole("button", { name: "Conversation history" }).click();
    const drawer = page.getByRole("region", { name: "Conversation history" });
    await expect(drawer.getByRole("heading", { name: "Today" })).toBeVisible();
    await expect(
      drawer.getByText("Conversations are deleted 6 months after their last activity."),
    ).toBeVisible();

    const row = drawer.getByRole("button", { name: /^Who knows React\?/ });
    await expect(row).toBeVisible();
    await row.click();
    await expect(drawer).toBeHidden();
    await expect(page.getByText("Ada Lovelace does.")).toBeVisible();

    // Continuing it sends the conversation's own id — the claim the whole feature rests on.
    await ask(page, "Is she free in July?");
    await expect(page.getByText("Yes — Ada Lovelace is free from 1 July.")).toBeVisible();

    const opened = [...store.values()][0];
    expect(opened.turns.map((t) => t.question)).toEqual([
      "Who knows React?",
      "Is she free in July?",
    ]);
    // One conversation throughout: the follow-up named it rather than starting a second.
    expect(store.size).toBe(1);
    expect(threadIdsSent).toEqual([undefined, opened.id]);
  });

  test("the per-row delete takes it away with nothing to confirm", async ({ context, page }) => {
    const { store } = await stubTheAgentsHost(page);
    await signInAndOpenTheDock(context, page);

    await ask(page, "Who knows React?");
    await expect(page.getByText("Ada Lovelace does.")).toBeVisible();

    await page.getByRole("button", { name: "Conversation history" }).click();
    const drawer = page.getByRole("region", { name: "Conversation history" });
    const row = drawer.getByRole("button", { name: /^Who knows React\?/ });
    await row.hover();
    await drawer.getByRole("button", { name: 'Delete conversation "Who knows React?"' }).click();

    // No dialog, and the row is gone because the list refetched — this is the invalidation
    // working through a real query client, which is the half `conversations.test.tsx` mocks.
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(row).toHaveCount(0);
    expect(store.size).toBe(0);
  });
});
