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
 * The role selector in the users dictionary (P1T-239), as it stands after EXP-46 moved it off the
 * row and into the edit popup.
 *
 * The two refusals the server enforces are *shown*, as text, before anybody clicks — which is the
 * whole point of the chosen variant. A disabled control whose reason lives only in a tooltip makes
 * a blocked control look merely inert, and the person never learns why.
 *
 * What EXP-46 changed is where the click happens, not what it means: the row is no longer an input,
 * so every assertion below opens the popup first. Both frozen hooks came with it — `users-role-
 * select` is now one per open dialog rather than one per row, and `users-role-confirm` still sits
 * between a click and somebody else's session ending.
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
const rowFor = (email: string) =>
  within(accountsTable()).getByText(email).closest("tr") as HTMLElement;

/** The accounts table by name: the claim and contest queues on this page are tables too. */
const accountsTable = () => screen.getByRole("table", { name: "Accounts" });

/** The edit popup for one account — the only place a role can be changed. */
async function openEditor(email: string) {
  await userEvent.click(within(rowFor(email)).getByRole("button", { name: "Edit" }));
  return screen.getByRole("dialog");
}

/** The popup's role control — the thing a person clicks. */
const roleSelect = () =>
  within(screen.getByTestId("users-role-select")).getByRole("combobox");

async function choose(email: string, role: string) {
  await openEditor(email);
  await userEvent.click(roleSelect());
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
    const headers = within(accountsTable()).getAllByRole("columnheader").map((h) => h.textContent);
    expect(headers.slice(0, 3)).toEqual(["Email", "Role", "Status"]);
  });

  it("shows each account's current role, in the column and in its popup", async () => {
    users = [
      row({ id: ACTOR_ID, email: "actor@example.com", role: "Administrator" }),
      row({ id: OTHER_ID, email: "ada@example.com", role: "User" }),
    ];
    renderPage();

    expect(within(rowFor("ada@example.com")).getByText("User")).toBeVisible();
    expect(within(rowFor("actor@example.com")).getByText("Administrator")).toBeVisible();

    await openEditor("ada@example.com");
    expect(roleSelect()).toHaveTextContent("User");
  });

  // The rule EXP-46 is here to hold: a stray click on a row cannot change anybody's role, because
  // there is nothing in the row to click.
  it("puts no control in the row itself", () => {
    users = TWO_ADMINISTRATORS();
    renderPage();

    expect(within(rowFor("ada@example.com")).queryByTestId("users-role-select"))
      .not.toBeInTheDocument();
    expect(within(rowFor("ada@example.com")).queryByRole("combobox")).not.toBeInTheDocument();
  });

  it("no longer claims roles are flat", () => {
    renderPage();

    expect(screen.queryByText(/flat roles/i)).not.toBeInTheDocument();
  });
});

describe("refusals, visible before the click", () => {
  it("blocks the signed-in person's own account and says why, as text", async () => {
    users = TWO_ADMINISTRATORS();
    renderPage();

    const dialog = await openEditor("actor@example.com");

    expect(roleSelect()).toHaveAttribute("aria-disabled", "true");
    // Beside the control, not in a tooltip: it has to be readable without hovering a dead input.
    expect(within(dialog).getByText(SELF_ROLE_REFUSAL)).toBeVisible();
  });

  it("blocks the last Administrator and says why, as text", async () => {
    users = [
      row({ id: ACTOR_ID, email: "actor@example.com", role: "User" }),
      row({ id: OTHER_ID, email: "solo@example.com", role: "Administrator" }),
    ];
    renderPage();

    const dialog = await openEditor("solo@example.com");

    expect(roleSelect()).toHaveAttribute("aria-disabled", "true");
    expect(within(dialog).getByText(LAST_ADMINISTRATOR_REFUSAL)).toBeVisible();
  });

  it("leaves an Administrator selectable while a second one exists", async () => {
    users = TWO_ADMINISTRATORS();
    renderPage();

    const dialog = await openEditor("ada@example.com");

    expect(roleSelect()).not.toHaveAttribute("aria-disabled");
    expect(within(dialog).queryByText(LAST_ADMINISTRATOR_REFUSAL)).not.toBeInTheDocument();
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
