import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import ExpertDetailPage from "./ExpertDetailPage";
import type { ExpertDetail } from "../types";

const exportOnBehalf = vi.fn();

function expert(): ExpertDetail {
  return {
    id: "expert-1",
    firstName: "Quilliam",
    lastName: "Quantrell",
    title: "Engineer",
    email: "q@example.com",
    phone: null,
    location: null,
    summary: null,
    photoUrl: null,
    currentCapacityPercent: 100,
    status: "Active",
    spokenLanguages: [],
    availabilityEntries: [],
    skills: [],
    qualifications: [],
    experiences: [],
  };
}

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = { mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false, isError: false, error: null };
  return {
    ...actual,
    useExpert: () => ({ data: expert(), isLoading: false }),
    useExportExpertOnBehalf: () => ({ ...idle, mutate: exportOnBehalf }),
    useExpertOwnership: () => ({
      data: { expertId: "expert-1", ownerUserId: null, ownerEmail: null },
      isLoading: false,
      isError: false,
      error: null,
    }),
    useIssueClaimCode: () => idle,
    useRevokeOwnership: () => idle,
    useUpdateExpert: () => idle,
    useAddExpertSkill: () => idle,
    useUpdateExpertSkill: () => idle,
    useDeleteExpertSkill: () => idle,
    useAddAvailability: () => idle,
    useUpdateAvailability: () => idle,
    useDeleteAvailability: () => idle,
    useAddLanguage: () => idle,
    useUpdateLanguage: () => idle,
    useDeleteLanguage: () => idle,
    useAddQualification: () => idle,
    useUpdateQualification: () => idle,
    useDeleteQualification: () => idle,
    useAddExperience: () => idle,
    useUpdateExperience: () => idle,
    useDeleteExperience: () => idle,
  };
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/experts/expert-1"]}>
        <Routes>
          <Route path="/experts/:id" element={<ExpertDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => vi.clearAllMocks());

describe("the on-behalf export (P1T-187)", () => {
  /**
   * Somebody phones in and asks for their data, because this service has no email to receive the
   * request by. The Service Manager takes it for them from the record's own page.
   */
  it("offers a Service Manager the export from the expert's page", async () => {
    renderPage();

    await userEvent.click(screen.getByRole("button", { name: "Export their data" }));

    expect(exportOnBehalf).toHaveBeenCalled();
  });

  /** The act is recorded, and the button says so before it is pressed rather than afterwards. */
  it("says the export is recorded", () => {
    renderPage();

    expect(screen.getByRole("button", { name: "Export their data" }))
      .toHaveAttribute("title", expect.stringContaining("recorded"));
  });
});
