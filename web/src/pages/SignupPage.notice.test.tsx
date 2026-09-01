import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import SignupPage from "./SignupPage";
import NoticeUpdateBanner from "../components/NoticeUpdateBanner";
import { SPECIAL_CATEGORY_GUIDANCE } from "./cvGuidance";
import ExpertFormDialog from "./ExpertFormDialog";

const NOTICE = { version: "2026-09-01", text: "## Notice\n\nSoftware **scores and ranks** you." };

const signup = vi.fn();
const acknowledge = vi.fn();
let noticeStatus: { pendingVersion: string | null } | undefined = { pendingVersion: null };

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useTransparencyNotice: () => ({ data: NOTICE, isPending: false, isError: false }),
    useSignup: () => ({ mutate: signup, isPending: false, isError: false, error: null }),
    useNoticeStatus: () => ({ data: noticeStatus }),
    useAcknowledgeNotice: () => ({
      mutate: acknowledge,
      isPending: false,
      isError: false,
      error: null,
    }),
  };
});

vi.mock("../auth/webauthn", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../auth/webauthn")>();
  return { ...actual, isPasskeySupported: () => true };
});

function renderIn(node: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>{node}</MemoryRouter>
    </QueryClientProvider>,
  );
}

async function fillTheForm() {
  await userEvent.type(screen.getByLabelText(/Email/), "ada@example.com");
  await userEvent.type(screen.getByLabelText(/Control word/), "hunter2");
}

const submit = () => screen.getByRole("button", { name: "Sign up with a passkey" });

describe("registration shows the transparency notice and will not proceed without it (P1T-183)", () => {
  it("renders the notice itself rather than a link to it", () => {
    renderIn(<SignupPage />);

    // Art. 12(1): the information, in front of the person, not one navigation away from them.
    expect(screen.getByText(/scores and ranks/)).toBeInTheDocument();
  });

  it("keeps signup disabled until the notice is acknowledged", async () => {
    renderIn(<SignupPage />);
    await fillTheForm();

    expect(submit()).toBeDisabled();

    await userEvent.click(screen.getByLabelText("I have read the notice above"));
    expect(submit()).toBeEnabled();
  });

  it("sends the exact version acknowledged, so what was agreed to is recoverable", async () => {
    renderIn(<SignupPage />);
    await fillTheForm();
    await userEvent.click(screen.getByLabelText("I have read the notice above"));
    await userEvent.click(submit());

    expect(signup).toHaveBeenCalledWith(
      expect.objectContaining({ acknowledgedNoticeVersion: NOTICE.version }),
      expect.anything(),
    );
  });

  // Not a consent control, and the label must not read as one: under Art. 6(1)(b) necessity does
  // the legal work, and offering consent where another basis applies is misleading.
  it("asks people to confirm they have read it, never that they agree to it", () => {
    renderIn(<SignupPage />);

    expect(screen.getByLabelText("I have read the notice above")).toBeInTheDocument();
    expect(screen.queryByLabelText(/I agree/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/I consent/i)).not.toBeInTheDocument();
  });
});

describe("a changed notice notifies without gating (P1T-183)", () => {
  it("says nothing when the account is up to date", () => {
    noticeStatus = { pendingVersion: null };
    const { container } = renderIn(<NoticeUpdateBanner />);

    expect(container).toBeEmptyDOMElement();
  });

  it("offers the new notice and records that it was read", async () => {
    noticeStatus = { pendingVersion: "2026-09-01" };
    renderIn(<NoticeUpdateBanner />);

    await userEvent.click(screen.getByRole("button", { name: "Read the notice" }));
    expect(screen.getByText(/scores and ranks/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "I've read it" }));
    expect(acknowledge).toHaveBeenCalledWith("2026-09-01");
  });
});

describe("Art. 9 guidance sits on the CV editor (P1T-183)", () => {
  it("asks people to leave special-category detail out of the summary", () => {
    renderIn(
      <ExpertFormDialog open title="Edit expert" onClose={() => {}} onSave={vi.fn()} />,
    );

    expect(screen.getByText(SPECIAL_CATEGORY_GUIDANCE)).toBeInTheDocument();
  });

  it("names the categories rather than saying 'sensitive information' and leaving it there", () => {
    for (const category of ["health", "trade-union", "ethnicity", "sexual orientation"]) {
      expect(SPECIAL_CATEGORY_GUIDANCE).toContain(category);
    }

    // The prohibition on inference is the half people forget, so it is said to the person too.
    expect(SPECIAL_CATEGORY_GUIDANCE).toContain("infers");
  });
});
