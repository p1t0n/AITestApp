import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AgentJobForm } from "./AgentJobTab";
import { http } from "../../api";
import type { ExpertSummary } from "../../types";

/**
 * The expert pickers offer the **bench**, never the whole Roster (EXP-49).
 *
 * A Draft is agent-staged and unvetted: it is not on the bench, so it is not somebody to tailor
 * a CV for, assess against a job description, or build an interview kit about. The roster page
 * gained Drafts; these three pickers deliberately did not, and that is checked here at the
 * request the picker actually makes rather than through a mocked hook that could drift.
 */

const person = (over: Partial<ExpertSummary> & Pick<ExpertSummary, "id" | "lastName">): ExpertSummary => ({
  firstName: "Ada",
  title: "Engineer",
  location: null,
  email: `${over.id}@example.com`,
  currentCapacityPercent: 100,
  status: "Active",
  ...over,
});

const ACTIVE = person({ id: "a", firstName: "Published", lastName: "Activeson" });
const DRAFT = person({ id: "d", firstName: "Staged", lastName: "Draftly", status: "Draft" });

/** The server's own rule, in miniature: Drafts only when the caller asks for them. */
function serveExperts(url: string, config?: { params?: { includeDrafts?: boolean } }) {
  if (url !== "/experts") throw new Error(`unexpected GET ${url}`);
  return Promise.resolve({
    data: config?.params?.includeDrafts ? [ACTIVE, DRAFT] : [ACTIVE],
  } as never);
}

function renderForm(mode: "cv-tailoring" | "match" | "interview-kit") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <AgentJobForm mode={mode} />
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe.each(["cv-tailoring", "match", "interview-kit"] as const)("the %s expert picker", (mode) => {
  it("offers published experts and not the Draft the ingest agent staged", async () => {
    const get = vi.spyOn(http, "get").mockImplementation(serveExperts as never);

    renderForm(mode);
    await waitFor(() => expect(get).toHaveBeenCalled());

    await userEvent.click(screen.getByRole("combobox", { name: /expert/i }));

    expect(await screen.findByText(/Published Activeson/)).toBeTruthy();
    expect(screen.queryByText(/Staged Draftly/)).toBeNull();

    // And it asked for the bench, so the Draft was never even sent over the wire.
    expect(get).toHaveBeenCalledWith("/experts");
    expect(get).not.toHaveBeenCalledWith("/experts", { params: { includeDrafts: true } });
  });
});
