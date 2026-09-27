import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import ExpertsPage from "./ExpertsPage";
import type { ExpertSummary } from "../types";
import type { RosterQuery } from "../api";

/**
 * The roster searches, sorts and pages on the **server** (EXP-45).
 *
 * The thing worth holding is the seam, not the rendering: every control on this page turns into
 * one query object, and that same object is what the URL round-trips. So the hook is spied on and
 * the assertions are about the argument it was handed — a test that only read the table would pass
 * just as happily against a page that filtered the rows it already had, which is the exact bug this
 * slice exists to remove.
 */

const rows: ExpertSummary[] = Array.from({ length: 3 }, (_, i) => ({
  id: `e${i}`,
  firstName: "Ada",
  lastName: `Person${i}`,
  title: "Engineer",
  location: "Vilnius",
  email: `e${i}@example.com`,
  currentCapacityPercent: 100,
  status: "Active",
}));

const useRosterPage = vi.fn((_query: RosterQuery) => ({
  data: { items: rows, total: 42 },
  isLoading: false,
  isFetching: false,
}));

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = { isPending: false, isError: false, error: null };
  return {
    ...actual,
    useRosterPage: (query: RosterQuery) => useRosterPage(query),
    useCreateExpert: () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), ...idle }),
    useDeleteExpert: () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), ...idle }),
  };
});

/** The last query the page asked the server for. */
const asked = (): RosterQuery => useRosterPage.mock.calls.at(-1)![0];

/** The current location, published into the DOM rather than into a module variable — writing one
 *  during render is the side effect the react-hooks rule exists to catch. */
function Where() {
  const location = useLocation();
  return <div data-url={location.pathname + location.search} />;
}

/** Where the router currently is, path and query together. */
const url = () => document.querySelector("[data-url]")!.getAttribute("data-url");

function renderPage(at = "/experts") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[at]}>
        <Where />
        <Routes>
          <Route path="/experts" element={<ExpertsPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  useRosterPage.mockClear();
});

describe("the roster's view is the server's query", () => {
  it("asks for the first page in name order when the URL says nothing", () => {
    renderPage();

    expect(asked()).toEqual({ q: "", sort: "name", dir: "asc", page: 1, pageSize: 25 });
  });

  it("heads the results with the server's total, not the number of rows on screen", () => {
    renderPage();

    expect(screen.getByRole("heading", { name: "42 experts" })).toBeTruthy();
    expect(screen.getByText("Showing 1–25 of 42")).toBeTruthy();
  });

  it("sends the search to the server, debounced, and drops the page number with it", async () => {
    const user = userEvent.setup();
    renderPage("/experts?page=3");

    await user.type(screen.getByLabelText("Search the roster"), "ada");

    // The box is instant and the request is not: three keystrokes are not three requests.
    expect(asked().q).toBe("");

    // Real timers rather than fake ones, deliberately: `vi.useFakeTimers` inside one test of this
    // file left the rest of it hanging on userEvent's own scheduling. 300ms is cheap to wait out.
    await waitFor(() => expect(asked().q).toBe("ada"));
    // A new search is a new result set, so the page it was on goes with it.
    expect(asked().page).toBe(1);
    // The URL follows a beat later, in an effect.
    await waitFor(() => expect(url()).toBe("/experts?q=ada"));
  });

  it("sends the chosen sort to the server and back to the first page", async () => {
    const user = userEvent.setup();
    renderPage("/experts?page=4");

    await user.click(screen.getByLabelText("Sort by"));
    await user.click(screen.getByRole("option", { name: "Most available" }));

    expect(asked().sort).toBe("capacity");
    expect(asked().dir).toBe("desc");
    // A re-sort moves every row, so the page somebody was on points at rows they never saw.
    expect(asked().page).toBe(1);
  });

  it("offers every sort the server accepts, and nothing it does not", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByLabelText("Sort by"));

    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual([
      "Name A→Z",
      "Name Z→A",
      "Most available",
      "Least available",
      "Location",
      "Title",
      "Status",
    ]);
  });

  it("walks the pages with Prev and Next", async () => {
    const user = userEvent.setup();
    renderPage();

    expect(screen.getByRole("button", { name: "‹ Prev" })).toHaveProperty("disabled", true);

    await user.click(screen.getByRole("button", { name: "Next ›" }));
    expect(asked().page).toBe(2);
    expect(screen.getByText("Showing 26–42 of 42")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "‹ Prev" }));
    expect(asked().page).toBe(1);
  });

  it("stops at the last page", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: "Next ›" }));

    expect(screen.getByRole("button", { name: "Next ›" })).toHaveProperty("disabled", true);
    expect(screen.getByRole("button", { name: "‹ Prev" })).toHaveProperty("disabled", false);
  });
});

describe("the view round-trips through the URL", () => {
  it("reads a shared link back into the same query", () => {
    renderPage("/experts?q=lovelace&sort=capacity&dir=desc&page=2");

    expect(asked()).toEqual({
      q: "lovelace",
      sort: "capacity",
      dir: "desc",
      page: 2,
      pageSize: 25,
    });
    expect(screen.getByLabelText("Search the roster")).toHaveProperty("value", "lovelace");
  });

  it("writes the view back out, leaving the defaults off the URL", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: "Next ›" }));
    expect(url()).toBe("/experts?page=2");

    await user.click(screen.getByLabelText("Sort by"));
    await user.click(screen.getByRole("option", { name: "Name Z→A" }));
    // `sort=name` is the default, so only the direction is written — and the page turn is dropped,
    // because a re-sort starts again from the first page.
    expect(url()).toBe("/experts?dir=desc");
  });

  it("ignores a sort key the server would refuse rather than asking for it", () => {
    renderPage("/experts?sort=firstname&dir=sideways&page=0");

    expect(asked()).toEqual({ q: "", sort: "name", dir: "asc", page: 1, pageSize: 25 });
  });

  it("follows the URL when it moves without the page's help", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: "Next ›" }));
    await user.click(screen.getByLabelText("Sort by"));
    await user.click(screen.getByRole("option", { name: "Title" }));

    expect(asked().sort).toBe("title");
    expect(url()).toBe("/experts?sort=title");
  });
});

describe("what the row shows", () => {
  it("puts the title and location under the name rather than in their own columns", () => {
    renderPage();

    const row = screen.getByText("Ada Person0").closest("tr")!;
    expect(row.textContent).toContain("Engineer · Vilnius");
  });

  it("says so when nothing matches", () => {
    useRosterPage.mockReturnValueOnce({
      data: { items: [], total: 0 },
      isLoading: false,
      isFetching: false,
    });
    renderPage();

    expect(screen.getByText("No experts match.")).toBeTruthy();
    expect(screen.getByText("Showing 0–0 of 0")).toBeTruthy();
  });

  it("shows a spinner instead of the header until the first page arrives", () => {
    useRosterPage.mockReturnValueOnce({
      data: undefined as never,
      isLoading: true,
      isFetching: true,
    });
    renderPage();

    // The e2e capture waits for `New expert` to decide the roster has loaded, so the header must
    // not exist before the rows do (`manuals/spa-design-system.md` §10).
    expect(screen.queryByRole("button", { name: "New expert" })).toBeNull();
    expect(screen.getByRole("progressbar")).toBeTruthy();
  });
});
