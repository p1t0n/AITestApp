import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router";
import { AxiosError, AxiosHeaders } from "axios";
import UsersPage, { DEMOTION_CONSEQUENCE } from "./UsersPage";
import { LAST_ADMINISTRATOR_REFUSAL, SELF_ROLE_REFUSAL, type UserSummary } from "../api";
import { clearSession, setSession } from "../auth/session";

/**
 * The role selector in the users dictionary (P1T-239).
 *
 * The two refusals the server enforces are *shown* here, as text, before anybody clicks — which is
 * the whole point of the chosen variant. A disabled control whose reason lives only in a tooltip
 * makes a blocked row look merely inert, and the person never learns why.
 */

const ACTOR_ID = "11111111-1111-1111-1111-111111111111";
const OTHER_ID = "22222222-2222-2222-2222-222222222222";

const row = (
  over: Partial<UserSummary> & Pick<UserSummary, "id" | "email" | "role">,
): UserSummary => ({
  status: "Active",
  dailyTokenCap: null,
  weeklyTokenCap: null,
  monthlyTokenCap: null,
  passkeyCount: 1,
  createdAt: "2026-09-01T10:00:00Z",
  ...over,
});

let users: UserSummary[] = [];
const changeRole = vi.fn();
let changeRoleError: unknown = null;

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = { isPending: false, isError: false, error: null };
  return {
    ...actual,
    useUsers: () => ({ data: users, isLoading: false, isError: false, error: null }),
    useUpdateUser: () => ({ mutate: vi.fn(), ...idle }),
    useDeleteUser: () => ({ mutate: vi.fn(), ...idle }),
    useChangeUserRole: () => ({
      mutate: changeRole,
      isPending: false,
      isError: changeRoleError !== null,
      error: changeRoleError,
    }),
    useClaimQueue: () => ({ data: [], isLoading: false, isError: false, error: null }),
    useApproveClaim: () => ({ mutate: vi.fn(), ...idle }),
    useRejectClaim: () => ({ mutate: vi.fn(), ...idle }),
    useContestQueue: () => ({ data: [], isLoading: false, isError: false, error: null }),
    useReviewContest: () => ({ mutate: vi.fn(), ...idle }),
  };
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <UsersPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

/** The row for an address, so a test never asserts against the wrong person's control. */
const rowFor = (email: string) => screen.getByText(email).closest("tr") as HTMLElement;

/** That row's role control — the thing a person clicks. */
const selectIn = (email: string) =>
  within(within(rowFor(email)).getByTestId("users-role-select")).getByRole("combobox");

async function choose(email: string, role: string) {
  await userEvent.click(selectIn(email));
  await userEvent.click(screen.getByRole("option", { name: role }));
}

/** Two Administrators: the signed-in actor, and somebody they are allowed to act on. */
const TWO_ADMINISTRATORS = () => [
  row({ id: ACTOR_ID, email: "actor@example.com", role: "Administrator" }),
  row({ id: OTHER_ID, email: "ada@example.com", role: "Administrator" }),
];

beforeEach(() => {
  users = [];
  changeRoleError = null;
  vi.clearAllMocks();
  setSession("a-token", "actor@example.com", "Administrator", ACTOR_ID);
});

afterEach(() => {
  clearSession();
});

describe("the role column", () => {
  it("sits between Email and Status", () => {
    users = [row({ id: OTHER_ID, email: "ada@example.com", role: "User" })];
    renderPage();

    // Scoped to the accounts table: the claim and contest queues above it have headers too.
    const accounts = screen.getByText("Email").closest("table") as HTMLElement;
    const headers = within(accounts).getAllByRole("columnheader").map((h) => h.textContent);
    expect(headers.slice(0, 3)).toEqual(["Email", "Role", "Status"]);
  });

  it("shows each account's current role", () => {
    users = [
      row({ id: ACTOR_ID, email: "actor@example.com", role: "Administrator" }),
      row({ id: OTHER_ID, email: "ada@example.com", role: "User" }),
    ];
    renderPage();

    expect(selectIn("ada@example.com")).toHaveTextContent("User");
    expect(selectIn("actor@example.com")).toHaveTextContent("Administrator");
  });

  it("no longer claims roles are flat", () => {
    renderPage();

    expect(screen.queryByText(/flat roles/i)).not.toBeInTheDocument();
  });
});

describe("refusals, visible before the click", () => {
  it("blocks the signed-in person's own row and says why, as text", () => {
    users = TWO_ADMINISTRATORS();
    renderPage();

    expect(selectIn("actor@example.com")).toHaveAttribute("aria-disabled", "true");
    // In the row, not in a tooltip: it has to be readable without hovering a dead control.
    expect(within(rowFor("actor@example.com")).getByText(SELF_ROLE_REFUSAL)).toBeVisible();
  });

  it("blocks the last Administrator and says why, as text", () => {
    users = [
      row({ id: ACTOR_ID, email: "actor@example.com", role: "User" }),
      row({ id: OTHER_ID, email: "solo@example.com", role: "Administrator" }),
    ];
    renderPage();

    expect(selectIn("solo@example.com")).toHaveAttribute("aria-disabled", "true");
    expect(within(rowFor("solo@example.com")).getByText(LAST_ADMINISTRATOR_REFUSAL)).toBeVisible();
  });

  it("leaves an Administrator selectable while a second one exists", () => {
    users = TWO_ADMINISTRATORS();
    renderPage();

    expect(selectIn("ada@example.com")).not.toHaveAttribute("aria-disabled");
    expect(
      within(rowFor("ada@example.com")).queryByText(LAST_ADMINISTRATOR_REFUSAL),
    ).not.toBeInTheDocument();
  });

  it("uses the server's sentences verbatim", () => {
    expect(SELF_ROLE_REFUSAL).toBe("You cannot change your own role.");
    expect(LAST_ADMINISTRATOR_REFUSAL).toBe("The last Administrator cannot be demoted.");
  });
});

describe("choosing a role", () => {
  it("promotes immediately, with no confirmation", async () => {
    users = [
      row({ id: ACTOR_ID, email: "actor@example.com", role: "Administrator" }),
      row({ id: OTHER_ID, email: "ada@example.com", role: "User" }),
    ];
    renderPage();

    await choose("ada@example.com", "Administrator");

    expect(screen.queryByTestId("users-role-confirm")).not.toBeInTheDocument();
    expect(changeRole).toHaveBeenCalledWith({ id: OTHER_ID, role: "Administrator" });
  });

  it("asks before a demotion, and writes nothing when the question is declined", async () => {
    users = TWO_ADMINISTRATORS();
    renderPage();

    await choose("ada@example.com", "User");

    const confirm = screen.getByTestId("users-role-confirm");
    expect(changeRole).not.toHaveBeenCalled();
    // What they lose, named: the four surfaces and the session.
    expect(within(confirm).getByText(DEMOTION_CONSEQUENCE)).toBeVisible();

    await userEvent.click(within(confirm).getByRole("button", { name: /cancel/i }));

    expect(screen.queryByTestId("users-role-confirm")).not.toBeInTheDocument();
    expect(changeRole).not.toHaveBeenCalled();
  });

  it("demotes once the question is answered", async () => {
    users = TWO_ADMINISTRATORS();
    renderPage();

    await choose("ada@example.com", "User");
    await userEvent.click(
      within(screen.getByTestId("users-role-confirm")).getByRole("button", { name: /demote/i }),
    );

    // The second argument is the mutation's own `onSuccess`, which closes the dialog.
    expect(changeRole).toHaveBeenCalledWith({ id: OTHER_ID, role: "User" }, expect.anything());
  });

  it("names the account in the question, so the wrong row is visible", async () => {
    users = TWO_ADMINISTRATORS();
    renderPage();

    await choose("ada@example.com", "User");

    expect(within(screen.getByTestId("users-role-confirm")).getByText(/ada@example\.com/))
      .toBeVisible();
  });
});

describe("the two-tab race", () => {
  it("renders the server's sentence from a 409", () => {
    // Two Administrators, so the page itself sees no refusal to render — the sentence on screen
    // can only have come from the response.
    users = TWO_ADMINISTRATORS();
    changeRoleError = new AxiosError(
      "Request failed with status code 409",
      "ERR_BAD_REQUEST",
      undefined,
      undefined,
      {
        status: 409,
        statusText: "Conflict",
        headers: new AxiosHeaders(),
        config: { headers: new AxiosHeaders() },
        data: { title: "Conflict", detail: LAST_ADMINISTRATOR_REFUSAL },
      },
    );
    renderPage();

    const notices = screen.getAllByTestId("error-notice");
    expect(notices.some((n) => n.textContent?.includes(LAST_ADMINISTRATOR_REFUSAL))).toBe(true);
  });
});
