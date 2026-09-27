import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import ExpertsPage from "./ExpertsPage";
import type { ExpertSummary } from "../types";
import type { RosterPageResult, RosterQuery } from "../api";

/**
 * The sidebar's filters and the counts beside them (EXP-47).
 *
 * Same seam as `ExpertsPage.server.test.tsx`, and for the same reason: every control on this page
 * turns into one query object the server answers, so the assertions are about the object the hook
 * was handed and about the URL that round-trips it. A test that only read the table would pass
 * against a page that filtered rows it already had, which is exactly the bug this slice removes.
 *
 * The counts go the other way — they are the server's answer, rendered — so those assertions read
 * the labels. The rule they encode (each group counted without its own filter) is the server's and
 * is held in `Application.Tests/RosterFacetTests`; what matters here is that the page prints the
 * numbers it was given rather than numbers it worked out from the page of rows on screen.
 */

const rows: ExpertSummary[] = Array.from({ length: 3 }, (_, i) => ({
  id: `e${i}`,
  firstName: "Ada",
  lastName: `Person${i}`,
  title: "Engineer",
  location: "London",
  email: `e${i}@example.com`,
  currentCapacityPercent: 100,
  status: "Active",
}));

const facets: RosterPageResult["facets"] = {
  status: [
    { value: "Active", count: 4 },
    { value: "Draft", count: 2 },
  ],
  band: [
    { value: "full", count: 2 },
    { value: "partial", count: 3 },
    { value: "none", count: 1 },
  ],
  location: [
    { value: "London", count: 3 },
    { value: "Arlington", count: 2 },
    { value: "Atlantis", count: 0 },
  ],
};

const useRosterPage = vi.fn((_query: RosterQuery) => ({
  data: { items: rows, total: 42, facets },
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

function Where() {
  const location = useLocation();
  return <div data-url={location.pathname + location.search} />;
}

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

const checkbox = (name: string) => screen.getByRole("checkbox", { name });
const radio = (name: string) => screen.getByRole("radio", { name });

beforeEach(() => {
  useRosterPage.mockClear();
});

describe("the sidebar shows the server's counts", () => {
  it("labels every status and band with its count, including a zero", () => {
    renderPage();

    expect(checkbox("Active (4)")).toBeTruthy();
    expect(checkbox("Draft (2)")).toBeTruthy();
    expect(radio("Full (100%) (2)")).toBeTruthy();
    expect(radio("Partial (1–99%) (3)")).toBeTruthy();
    expect(radio("Unavailable (0%) (1)")).toBeTruthy();
  });

  it("lists the locations in the order the server sent them", () => {
    renderPage();

    const group = screen.getByRole("group", { name: "Location" });
    const labels = within(group)
      .getAllByRole("checkbox")
      .map((box) => box.closest("label")!.textContent);

    expect(labels).toEqual(["London (3)", "Arlington (2)", "Atlantis (0)"]);
  });

  it("disables a location nothing is left in, but not one that is ticked", () => {
    renderPage();

    expect(checkbox("Atlantis (0)")).toHaveProperty("disabled", true);
    expect(checkbox("London (3)")).toHaveProperty("disabled", false);
  });

  it("keeps a ticked location usable even at zero, so it can be unticked", () => {
    renderPage("/experts?location=Atlantis");

    const ticked = checkbox("Atlantis (0)");
    expect(ticked).toHaveProperty("checked", true);
    expect(ticked).toHaveProperty("disabled", false);
  });
});

describe("a filter is a server request and a URL", () => {
  it("sends a status the server's way and writes it into the URL", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(checkbox("Draft (2)"));

    expect(asked().statuses).toEqual(["Draft"]);
    expect(url()).toBe("/experts?status=Draft");
  });

  it("unions two statuses rather than replacing one with the other", async () => {
    const user = userEvent.setup();
    renderPage("/experts?status=Draft");

    await user.click(checkbox("Active (4)"));

    expect(asked().statuses).toEqual(["Draft", "Active"]);
    expect(url()).toBe("/experts?status=Draft&status=Active");
  });

  it("unticks a status it was given", async () => {
    const user = userEvent.setup();
    renderPage("/experts?status=Draft&status=Active");

    await user.click(checkbox("Draft (2)"));

    expect(asked().statuses).toEqual(["Active"]);
    expect(url()).toBe("/experts?status=Active");
  });

  it("sends a location the server's way, comma and all", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(checkbox("London (3)"));

    expect(asked().locations).toEqual(["London"]);
    expect(url()).toBe("/experts?location=London");
  });

  it("sends the availability band, and Any clears it", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(radio("Full (100%) (2)"));
    expect(asked().band).toBe("full");
    expect(url()).toBe("/experts?band=full");

    await user.click(radio("Any"));
    expect(asked().band).toBe(null);
    expect(url()).toBe("/experts");
  });

  it("drops the page number, because a filter reshapes the whole match", async () => {
    const user = userEvent.setup();
    renderPage("/experts?page=4");

    await user.click(checkbox("Draft (2)"));

    expect(asked().page).toBe(1);
    expect(url()).toBe("/experts?status=Draft");
  });

  it("keeps the search and the sort while a filter changes", async () => {
    const user = userEvent.setup();
    renderPage("/experts?q=ada&sort=capacity&dir=desc");

    await user.click(checkbox("Draft (2)"));

    expect(asked()).toMatchObject({ q: "ada", sort: "capacity", dir: "desc", statuses: ["Draft"] });
  });
});

describe("the URL round-trips the whole view", () => {
  it("reads every filter back out of a pasted link", () => {
    renderPage("/experts?q=ada&status=Active&location=London&location=Arlington&band=partial&page=2");

    expect(asked()).toEqual({
      q: "ada",
      statuses: ["Active"],
      locations: ["London", "Arlington"],
      band: "partial",
      sort: "name",
      dir: "asc",
      page: 2,
      pageSize: 25,
    });
    expect(checkbox("Active (4)")).toHaveProperty("checked", true);
    expect(checkbox("London (3)")).toHaveProperty("checked", true);
    expect(radio("Partial (1–99%) (3)")).toHaveProperty("checked", true);
  });

  it("ignores a status or a band the server would refuse rather than asking for it", () => {
    renderPage("/experts?status=Paused&band=mostly");

    // A link somebody edited by hand should show a roster, not a 400.
    expect(asked().statuses).toEqual([]);
    expect(asked().band).toBe(null);
  });

  it("still keeps a location nobody is in, because there is no closed set to check it against", () => {
    renderPage("/experts?location=Atlantis");

    expect(asked().locations).toEqual(["Atlantis"]);
  });
});

describe("Reset filters", () => {
  it("is off until there is something to reset", async () => {
    const user = userEvent.setup();
    renderPage();

    expect(screen.getByRole("button", { name: "Reset filters" })).toHaveProperty("disabled", true);

    await user.click(checkbox("Draft (2)"));

    expect(screen.getByRole("button", { name: "Reset filters" })).toHaveProperty("disabled", false);
  });

  it("clears every filter and the search in one go", async () => {
    const user = userEvent.setup();
    renderPage("/experts?q=ada&status=Active&location=London&band=full&sort=capacity&dir=desc&page=3");

    await user.click(screen.getByRole("button", { name: "Reset filters" }));

    // The sort survives: it is how the roster is read, not what it is narrowed to, and the ticket's
    // "Reset filters" is beneath the filters. The page goes back to the first one with them.
    await waitFor(() =>
      expect(asked()).toEqual({
        q: "",
        statuses: [],
        locations: [],
        band: null,
        sort: "name",
        dir: "asc",
        page: 1,
        pageSize: 25,
      }),
    );
    expect(screen.getByLabelText("Search the roster")).toHaveProperty("value", "");
    expect(url()).toBe("/experts");
  });

  it("does not let the pending search creep back in after the reset", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText("Search the roster"), "ada");
    await waitFor(() => expect(asked().q).toBe("ada"));

    await user.click(screen.getByRole("button", { name: "Reset filters" }));

    // The debounce is flushed, not merely cancelled: a pending "ada" that fired 300ms later would
    // put the search back into a URL the person had just cleared.
    expect(asked().q).toBe("");
    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(asked().q).toBe("");
    expect(url()).toBe("/experts");
  });
});
