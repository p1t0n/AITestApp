import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import UsersPage from "./UsersPage";
import ExpertOwnership, { REVOKE_CONSEQUENCE } from "../components/ExpertOwnership";
import RedeemClaimCode from "../components/RedeemClaimCode";
import { CLAIM_EVIDENCE_WARNING } from "../components/ClaimQueue";
import type { ClaimQueueItem } from "../api";

const PENDING: ClaimQueueItem = {
  id: "claim-1",
  claimantUserId: "user-1",
  claimantEmail: "ada@example.com",
  expertId: "expert-1",
  expertName: "Ada Lovelace",
  expertEmail: "ada@example.com",
  matchCount: 1,
  state: "Pending",
  createdAt: "2026-09-01T10:00:00Z",
};

const FLAG: ClaimQueueItem = {
  ...PENDING,
  id: "claim-2",
  claimantEmail: "twin@example.com",
  expertId: null,
  expertName: null,
  expertEmail: null,
  matchCount: 2,
  state: "Ambiguous",
};

let queue: ClaimQueueItem[] = [];
const approve = vi.fn();
const reject = vi.fn();
const issueCode = vi.fn();
const revoke = vi.fn();
const redeem = vi.fn();
let ownership: { expertId: string; ownerUserId: string | null; ownerEmail: string | null } = {
  expertId: "expert-1",
  ownerUserId: null,
  ownerEmail: null,
};

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = { isPending: false, isError: false, error: null };
  return {
    ...actual,
    useUsers: () => ({ data: [], isLoading: false, isError: false, error: null }),
    useUpdateUser: () => ({ mutate: vi.fn(), ...idle }),
    useDeleteUser: () => ({ mutate: vi.fn(), ...idle }),
    useClaimQueue: () => ({ data: queue, isLoading: false, isError: false, error: null }),
    useApproveClaim: () => ({ mutate: approve, ...idle }),
    useRejectClaim: () => ({ mutate: reject, ...idle }),
    useIssueClaimCode: () => ({ mutate: issueCode, ...idle }),
    useRevokeOwnership: () => ({ mutate: revoke, ...idle }),
    useRedeemClaimCode: () => ({ mutate: redeem, isSuccess: false, ...idle }),
    useExpertOwnership: () => ({ data: ownership, isLoading: false, isError: false, error: null }),
  };
});

function renderIn(node: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>{node}</MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  queue = [];
  ownership = { expertId: "expert-1", ownerUserId: null, ownerEmail: null };
  vi.clearAllMocks();
});

describe("the claim approval queue (P1T-184)", () => {
  /**
   * A design requirement, not decoration. The approver has no verification signal at all, so a
   * screen that looks authoritative is a screen that gets rubber-stamped — the failure mode the ICO
   * documented in the scoring context. It has to say what it does not know.
   */
  it("states on the screen that a matching email proves nothing", () => {
    queue = [PENDING];
    renderIn(<UsersPage />);

    expect(screen.getByText(CLAIM_EVIDENCE_WARNING)).toBeInTheDocument();
    expect(CLAIM_EVIDENCE_WARNING).toMatch(/proves nothing/);
  });

  it("shows the claimant and the record being claimed side by side", () => {
    queue = [PENDING];
    renderIn(<UsersPage />);

    const row = screen.getByText("Ada Lovelace").closest("tr")!;
    // Both addresses, twice over — and they are the same string, which is exactly the evidence the
    // approver has and exactly why the warning above the table exists.
    expect(within(row).getAllByText("ada@example.com")).toHaveLength(2);
    expect(within(row).getByRole("button", { name: "Approve" })).toBeInTheDocument();
  });

  it("offers no approval at all on a raised flag — there is no record to bind", () => {
    queue = [FLAG];
    renderIn(<UsersPage />);

    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Dismiss" })).toBeInTheDocument();
    expect(screen.getByText(/2 records carry this address/)).toBeInTheDocument();
  });

  it("names the consequence in the approval confirmation before binding anything", async () => {
    queue = [PENDING];
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    renderIn(<UsersPage />);

    await userEvent.click(screen.getByRole("button", { name: "Approve" }));

    expect(confirm.mock.calls[0][0]).toMatch(/never verified/);
    expect(approve).not.toHaveBeenCalled();

    confirm.mockReturnValue(true);
    await userEvent.click(screen.getByRole("button", { name: "Approve" }));
    expect(approve).toHaveBeenCalledWith("claim-1");
  });
});

describe("ownership on the expert record (P1T-184)", () => {
  it("says an unclaimed record is not scanned, and offers a claim code", () => {
    renderIn(<ExpertOwnership expertId="expert-1" />);

    expect(screen.getByText(/not scanned for Jobs/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Issue claim code" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Revoke ownership" })).not.toBeInTheDocument();
  });

  /**
   * The button does not look like it removes somebody from consideration for work, and it does. The
   * chain — unowned, therefore legitimate interest, therefore not scanned — is stated before the
   * click lands, because an approver who is not told will use this for tidying up.
   */
  it("spells out what revocation does before it happens", async () => {
    ownership = { expertId: "expert-1", ownerUserId: "user-1", ownerEmail: "ada@example.com" };
    renderIn(<ExpertOwnership expertId="expert-1" />);

    await userEvent.click(screen.getByRole("button", { name: "Revoke ownership" }));

    expect(screen.getByRole("dialog")).toHaveTextContent(REVOKE_CONSEQUENCE);
    expect(REVOKE_CONSEQUENCE).toMatch(/no longer scanned/);
    expect(revoke).not.toHaveBeenCalled();

    await userEvent.click(
      within(screen.getByRole("dialog")).getByRole("button", { name: "Revoke ownership" }),
    );
    expect(revoke).toHaveBeenCalled();
  });

  it("shows a fresh claim code once and says it will not be shown again", async () => {
    issueCode.mockImplementation((_id: string, opts: { onSuccess: (r: { code: string }) => void }) =>
      opts.onSuccess({ code: "ABCD1234-EFGH5678" }),
    );
    renderIn(<ExpertOwnership expertId="expert-1" />);

    await userEvent.click(screen.getByRole("button", { name: "Issue claim code" }));

    expect(screen.getByText("ABCD1234-EFGH5678")).toBeInTheDocument();
    expect(screen.getByText(/will not be shown again/)).toBeInTheDocument();
    // The whole point of the mechanism: it exists because email cannot be trusted here.
    expect(screen.getByText(/never by email/)).toBeInTheDocument();
  });
});

describe("redeeming a claim code (P1T-184)", () => {
  it("takes a code and spends it, and will not submit an empty one", async () => {
    renderIn(<RedeemClaimCode />);

    const submit = screen.getByRole("button", { name: "Redeem" });
    expect(submit).toBeDisabled();

    await userEvent.type(screen.getByLabelText("Claim code"), "  ABCD1234-EFGH5678  ");
    await userEvent.click(submit);

    // Trimmed on the way out: a code read aloud and pasted back arrives with whitespace.
    expect(redeem).toHaveBeenCalledWith("ABCD1234-EFGH5678", expect.anything());
  });

  it("says out loud that a code is handed over out of band, not emailed", () => {
    renderIn(<RedeemClaimCode />);

    expect(screen.getByText(/in person or by phone/)).toBeInTheDocument();
  });
});
