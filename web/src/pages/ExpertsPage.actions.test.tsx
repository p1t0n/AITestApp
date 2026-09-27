import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import ExpertsPage from "./ExpertsPage";
import { DISCARD_QUESTION, UNSAVED_CHANGES } from "../components/EditDialog";
import type { ExpertDetail, ExpertSummary } from "../types";
import type { RosterQuery } from "../api";

/**
 * The roster's row is not a control panel (EXP-50).
 *
 * Two rules meet on this page. The first is the app-wide one EXP-46 wrote for dictionaries: nothing
 * on a row surface is one click away from a change, so every write is reached through a ⋮ menu and
 * happens in a popup. The roster's old row carried a delete button behind `window.confirm`, which
 * is the exact shape that rule exists to remove — one mis-aimed click, one native dialog nobody
 * reads, and a person is gone.
 *
 * The second is that **refine is not search**. Search, filters and sort are the server's (EXP-45,
 * EXP-47); refine only narrows the rows already on screen. The assertions below hold that apart:
 * refining must not move the server's query, and the page must say on its face that it only touched
 * this page.
 */

const people: ExpertSummary[] = [
  {
    id: "e1",
    firstName: "Ada",
    lastName: "Lovelace",
    title: "Analytical Engineer",
    location: "London",
    email: "ada@example.com",
    currentCapacityPercent: 100,
    status: "Active",
  },
  {
    id: "e2",
    firstName: "Grace",
    lastName: "Hopper",
    title: "Rear Admiral",
    location: "New York",
    email: "grace@example.com",
    currentCapacityPercent: 50,
    status: "Active",
  },
];

/** What the server holds for Ada — more than the row ever shows, which is the point of fetching it. */
const detail: ExpertDetail = {
  ...people[0],
  phone: "+370 600 00000",
  summary: "First programmer.",
  photoUrl: null,
  hiddenAt: null,
} as ExpertDetail;

const useRosterPage = vi.fn((_query: RosterQuery) => ({
  data: { items: people, total: 312 },
  isLoading: false,
  isFetching: false,
}));
const deleteExpert = vi.fn(async (_id: string) => ({}));
const updateExpert = vi.fn(async (_dto: unknown) => ({}));
const useExpert = vi.fn((_id: string) => ({ data: detail, isLoading: false }));

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = { isPending: false, isError: false, error: null };
  return {
    ...actual,
    useRosterPage: (query: RosterQuery) => useRosterPage(query),
    useExpert: (id: string) => useExpert(id),
    useCreateExpert: () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), ...idle }),
    useUpdateExpert: () => ({ mutate: vi.fn(), mutateAsync: updateExpert, ...idle }),
    useDeleteExpert: () => ({ mutate: vi.fn(), mutateAsync: deleteExpert, ...idle }),
  };
});

function Where() {
  const location = useLocation();
  return <div data-url={location.pathname} />;
}

const where = () => document.querySelector("[data-url]")!.getAttribute("data-url");

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/experts"]}>
        <Where />
        <Routes>
          <Route path="/experts" element={<ExpertsPage />} />
          <Route path="/experts/:id" element={<div>detail page</div>} />
          <Route path="/experts/:id/cv" element={<div>cv page</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const rowFor = (name: string) => screen.getByText(name).closest("tr")!;

/** Open one row's ⋮ menu. The menu itself is a portal, so it is never `within` the row. */
async function openMenu(user: ReturnType<typeof userEvent.setup>, name: string) {
  await user.click(within(rowFor(name)).getByRole("button", { name: `Actions for ${name}` }));
  return screen.getByRole("menu");
}

beforeEach(() => {
  useRosterPage.mockClear();
  deleteExpert.mockClear();
  updateExpert.mockClear();
});

describe("a roster row", () => {
  it("carries no control that writes — only the ⋮ menu", () => {
    renderPage();

    const row = rowFor("Ada Lovelace");
    expect(within(row).getAllByRole("button").map((b) => b.getAttribute("aria-label"))).toEqual([
      "Actions for Ada Lovelace",
    ]);
  });

  it("has lost the trash icon and the browser confirm behind it", async () => {
    const confirmed = vi.spyOn(window, "confirm").mockReturnValue(true);
    const user = userEvent.setup();
    renderPage();

    expect(screen.queryByRole("button", { name: "Delete" })).toBeNull();
    expect(screen.queryByTitle("Delete")).toBeNull();

    // And the whole table, clicked through, still writes nothing.
    await user.click(rowFor("Ada Lovelace"));

    expect(confirmed).not.toHaveBeenCalled();
    expect(deleteExpert).not.toHaveBeenCalled();
    confirmed.mockRestore();
  });

  it("offers Open, View CV and Edit… and nothing else", async () => {
    const user = userEvent.setup();
    renderPage();

    const menu = await openMenu(user, "Ada Lovelace");

    expect(within(menu).getAllByRole("menuitem").map((i) => i.textContent)).toEqual([
      "Open",
      "View CV",
      "Edit…",
    ]);
  });

  it("opens the detail page from Open", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(within(await openMenu(user, "Grace Hopper")).getByRole("menuitem", { name: "Open" }));

    expect(where()).toBe("/experts/e2");
  });

  it("opens the CV from View CV", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(
      within(await openMenu(user, "Grace Hopper")).getByRole("menuitem", { name: "View CV" }),
    );

    expect(where()).toBe("/experts/e2/cv");
  });
});

describe("Edit… — the only way to a write", () => {
  it("opens the expert form, filled from the whole record rather than from the row", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(within(await openMenu(user, "Ada Lovelace")).getByRole("menuitem", { name: "Edit…" }));

    const dialog = screen.getByRole("dialog");
    expect(useExpert).toHaveBeenCalledWith("e1");
    // A PUT replaces the record, so a dialog prefilled from the summary alone would blank these.
    expect(within(dialog).getByLabelText("Phone")).toHaveProperty("value", "+370 600 00000");
    expect(within(dialog).getByLabelText("Summary")).toHaveProperty("value", "First programmer.");
  });

  it("refuses Esc and the backdrop once dirty, and asks instead", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(within(await openMenu(user, "Ada Lovelace")).getByRole("menuitem", { name: "Edit…" }));
    await user.type(within(screen.getByRole("dialog")).getByLabelText("Title"), "!");

    expect(screen.getByText(UNSAVED_CHANGES)).toBeVisible();

    await user.keyboard("{Escape}");
    expect(screen.getByRole("dialog")).toBeVisible();
    expect(screen.getByText(DISCARD_QUESTION)).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Keep editing" }));
    await user.click(document.querySelector(".MuiBackdrop-root") as HTMLElement);
    expect(screen.getByRole("dialog")).toBeVisible();
    expect(screen.getByText(DISCARD_QUESTION)).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Discard" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(updateExpert).not.toHaveBeenCalled();
  });

  it("deletes from inside the dialog, behind its own confirmation", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(within(await openMenu(user, "Ada Lovelace")).getByRole("menuitem", { name: "Edit…" }));
    await user.click(screen.getByRole("button", { name: "Delete" }));

    // Asking is not doing.
    expect(deleteExpert).not.toHaveBeenCalled();
    const confirm = screen.getByRole("dialog", { name: "Delete Ada Lovelace?" });

    await user.click(within(confirm).getByRole("button", { name: "Delete expert" }));

    expect(deleteExpert).toHaveBeenCalledWith("e1");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("lets the confirmation be backed out of without deleting", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(within(await openMenu(user, "Ada Lovelace")).getByRole("menuitem", { name: "Edit…" }));
    await user.click(screen.getByRole("button", { name: "Delete" }));
    await user.click(
      within(screen.getByRole("dialog", { name: "Delete Ada Lovelace?" })).getByRole("button", {
        name: "Cancel",
      }),
    );

    expect(deleteExpert).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog", { name: /Edit Ada Lovelace/ })).toBeVisible();
  });
});

describe("Refine these results", () => {
  it("narrows the rows already on the page without asking the server anything", async () => {
    const user = userEvent.setup();
    renderPage();
    const before = useRosterPage.mock.calls.length;

    await user.type(screen.getByLabelText("Refine these results"), "hopper");

    expect(screen.queryByText("Ada Lovelace")).toBeNull();
    expect(screen.getByText("Grace Hopper")).toBeVisible();
    // Every render calls the hook again; what must not change is the query it is handed.
    const first = useRosterPage.mock.calls[0]![0];
    for (const [query] of useRosterPage.mock.calls.slice(before)) expect(query).toEqual(first);
  });

  it("says on its face that it only reached this page", async () => {
    const user = userEvent.setup();
    renderPage();

    // Before anything is typed the box itself says it; afterwards the count does. Between them
    // there is no moment where the page lets a refine be mistaken for a roster search.
    expect(screen.getByLabelText("Refine these results")).toHaveProperty(
      "placeholder",
      "This page only",
    );
    expect(screen.getByText("Showing 1–25 of 312")).toBeVisible();

    await user.type(screen.getByLabelText("Refine these results"), "hopper");

    // The server's own count stays whole, and the refine's count is named as a second, smaller fact.
    expect(screen.getByText("Showing 1–25 of 312 · 1 after refine")).toBeVisible();
  });

  it("distinguishes a page nothing refines to from a roster nothing matches", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText("Refine these results"), "nobody");

    expect(screen.getByText("No rows on this page match the refine.")).toBeVisible();
    expect(screen.queryByText("No experts match.")).toBeNull();
  });

  it("matches a title or a location, not only a name", async () => {
    const user = userEvent.setup();
    renderPage();
    const box = screen.getByLabelText("Refine these results");

    await user.type(box, "analytical");
    expect(screen.getByText("Ada Lovelace")).toBeVisible();
    expect(screen.queryByText("Grace Hopper")).toBeNull();

    await user.clear(box);
    await user.type(box, "new york");
    expect(screen.getByText("Grace Hopper")).toBeVisible();
    expect(screen.queryByText("Ada Lovelace")).toBeNull();
  });
});
