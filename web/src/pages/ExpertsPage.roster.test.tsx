import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import ExpertsPage from "./ExpertsPage";
import type { ExpertSummary } from "../types";

/**
 * The staff roster lists the whole Roster, not the bench (EXP-49).
 *
 * A Draft staged by the resume ingest agent used to appear nowhere once the ingestion surface
 * closed, which made the publication gate unreachable: nothing listed the row, so nobody could
 * open it to promote it. This page is that list, so all three states belong on it — and a Draft
 * says so on the row, the same way a pause does.
 */

const row = (over: Partial<ExpertSummary> & Pick<ExpertSummary, "id" | "lastName">): ExpertSummary => ({
  firstName: "Ada",
  title: "Engineer",
  location: "Vilnius",
  email: `${over.id}@example.com`,
  currentCapacityPercent: 100,
  status: "Active",
  ...over,
});

const DRAFT = row({ id: "d", firstName: "Staged", lastName: "Draftly", status: "Draft" });
const ACTIVE = row({ id: "a", firstName: "Published", lastName: "Activeson" });
const PAUSED = row({ id: "p", firstName: "Self", lastName: "Pausewell", hiddenAt: "2026-09-01T00:00:00Z" });

const useRoster = vi.fn(() => ({ data: [DRAFT, ACTIVE, PAUSED], isLoading: false }));
const useExperts = vi.fn(() => ({ data: [ACTIVE, PAUSED], isLoading: false }));

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = { isPending: false, isError: false, error: null };
  return {
    ...actual,
    useRoster: () => useRoster(),
    useExperts: () => useExperts(),
    useCreateExpert: () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), ...idle }),
    useDeleteExpert: () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), ...idle }),
  };
});

function Where() {
  return <div data-where={useLocation().pathname} />;
}

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/experts"]}>
        <Where />
        <Routes>
          <Route path="/experts" element={<ExpertsPage />} />
          <Route path="/experts/:id" element={<div>detail page</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function rowFor(name: string) {
  return screen.getByText(name, { exact: false }).closest("tr")!;
}

describe("the staff roster list", () => {
  it("reads the whole Roster, not the bench list the agent pickers share", () => {
    renderPage();

    expect(useRoster).toHaveBeenCalled();
    expect(useExperts).not.toHaveBeenCalled();
  });

  it("lists Draft, Active and Paused experts", () => {
    renderPage();

    for (const name of ["Draftly", "Activeson", "Pausewell"]) {
      expect(screen.getByText(name, { exact: false })).toBeTruthy();
    }
  });

  it("marks a Draft row with a Draft chip and leaves the published rows unmarked", () => {
    renderPage();

    expect(within(rowFor("Draftly")).getByText("Draft")).toBeTruthy();
    expect(within(rowFor("Activeson")).queryByText("Draft")).toBeNull();
    expect(within(rowFor("Pausewell")).queryByText("Draft")).toBeNull();
    // The pause badge is untouched by any of this.
    expect(within(rowFor("Pausewell")).getByText("Paused")).toBeTruthy();
  });

  it("opens a Draft's detail page, where the publication gate already lives", async () => {
    const { container } = renderPage();

    await userEvent.click(rowFor("Draftly"));

    expect(container.querySelector("[data-where]")!.getAttribute("data-where"))
      .toBe(`/experts/${DRAFT.id}`);
    expect(screen.getByText("detail page")).toBeTruthy();
  });
});
