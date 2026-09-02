import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import PrivacyDataPage from "./PrivacyDataPage";
import type { AccessView } from "../api";

type Visibility = { expertId: string; hidden: boolean; hiddenSince: string | null };

const setVisibility = vi.fn();
const download = vi.fn();
const erase = vi.fn();
const contest = vi.fn();

let visibility: Visibility | undefined;
let access: AccessView | undefined;
let ownsNothing = false;

function accessView(over: Partial<AccessView> = {}): AccessView {
  return {
    expertId: "e1",
    origin: "SelfRegistered",
    basis: "ContractNecessity",
    source: null,
    noticeVersionAcknowledged: "2026-09-01",
    pausedSince: null,
    export: "Right",
    retentionClock: "Claimed",
    expiresAt: "2028-01-01T00:00:00Z",
    expiringSoon: false,
    purposes: ["Maintaining a bench record."],
    dataCategories: ["Your name and contact details.", "Your career history.", "Your skills."],
    recipients: [
      { recipient: "Service Managers of this organisation", why: "They maintain the bench." },
      { recipient: "Google (Gemini), as our AI model provider", why: "Scoring happens there." },
    ],
    retention: "We keep your record while it is in use.",
    art22Logic: "### How the scoring works\n\nA model reads your CV.",
    rights: ["See everything held about you."],
    complaintRight: "You can complain to a data protection supervisory authority.",
    record: {},
    derived: {
      assessments: [
        {
          source: "Roster scan",
          sourceId: "cand-1",
          at: null,
          score: 41,
          band: "weak",
          rationale: "Reads as a user of payment platforms rather than a builder of them.",
          digest: null,
          matchAnswer: null,
        },
      ],
      searchIndexNote: "Your summary is also held as numeric representations.",
    },
    history: [],
    ...over,
  };
}

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = { isPending: false, isError: false, error: null, isSuccess: false };
  return {
    ...actual,
    useMyVisibility: () => ({
      data: ownsNothing ? undefined : visibility,
      isLoading: false,
      isError: ownsNothing,
      error: null,
    }),
    useMyAccessView: () => ({
      data: ownsNothing ? undefined : access,
      isLoading: false,
      isError: ownsNothing,
      error: null,
    }),
    useSetMyVisibility: () => ({ mutate: setVisibility, ...idle }),
    useDownloadMyExport: () => ({ mutate: download, ...idle }),
    useEraseMyAccount: () => ({ mutate: erase, ...idle }),
    useContestScore: () => ({ mutate: contest, ...idle }),
    useNoticeStatus: () => ({ data: { pendingVersion: null } }),
    useAcknowledgeNotice: () => ({ mutate: vi.fn(), ...idle }),
    useRedeemClaimCode: () => ({ mutate: vi.fn(), ...idle }),
  };
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <PrivacyDataPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

/** A row by its label — the labels are the page's structure, so the tests read it that way. */
function row(label: string): HTMLElement {
  return screen.getByTestId(`row-${label}`);
}

beforeEach(() => {
  visibility = { expertId: "e1", hidden: false, hiddenSince: null };
  access = accessView();
  ownsNothing = false;
  vi.clearAllMocks();
});

describe("what we hold on you (P1T-191)", () => {
  it("renders every section from real data rather than a stub", () => {
    renderPage();

    expect(screen.getByText("Your career history.")).toBeInTheDocument();
    expect(screen.getByText("Maintaining a bench record.")).toBeInTheDocument();
    expect(screen.getByText(/numeric representations/)).toBeInTheDocument();
    expect(screen.getByText(/We keep your record while it is in use/)).toBeInTheDocument();
    expect(screen.getByText(/supervisory authority/)).toBeInTheDocument();
    expect(screen.getByText(/A model reads your CV/)).toBeInTheDocument();
  });

  /** The disclosure that is new information rather than a restatement (P1T-187). */
  it("names the model provider among the recipients", () => {
    renderPage();

    expect(screen.getByText(/Google \(Gemini\)/)).toBeInTheDocument();
  });

  /**
   * Derived data is owed under access and excluded from portability, and this page is the access
   * side — the person reads what software wrote about them. The consequence is deliberate: a
   * rationale has to be defensible, because its subject sees it.
   */
  it("shows the scores and rationales written about them", () => {
    renderPage();

    expect(screen.getByText(/41\/100/)).toBeInTheDocument();
    expect(screen.getByText(/a user of payment platforms/)).toBeInTheDocument();
  });

  it("says the export leaves those conclusions out", () => {
    renderPage();

    expect(screen.getByText(/scores and rationales above are not in it/)).toBeInTheDocument();
  });
});

describe("the state is one sentence, not a second surface (P1T-175 Variant A)", () => {
  it("reads as active when nothing is wrong", () => {
    renderPage();

    expect(screen.getByText(/Your record is active and can be offered for work/)).toBeInTheDocument();
  });

  it("reads as paused when paused", () => {
    visibility = { expertId: "e1", hidden: true, hiddenSince: "2026-09-01T00:00:00Z" };
    renderPage();

    expect(screen.getByText(/Your record is paused/)).toBeInTheDocument();
  });

  /**
   * The combination the prototype run worried about. Variant B stacked two chips for it; A merges
   * them into one statement, and that is the whole reason A won — two warnings competing for the
   * same slot is where B started contradicting itself.
   */
  it("merges paused and expiring into a single statement", () => {
    visibility = { expertId: "e1", hidden: true, hiddenSince: "2026-09-01T00:00:00Z" };
    access = accessView({ expiringSoon: true, expiresAt: "2026-10-01T00:00:00Z" });
    renderPage();

    const sentence = screen.getByText(/Your record is paused/);
    expect(sentence).toHaveTextContent(/due to be deleted on/);
    expect(sentence).toHaveTextContent(/that date has already moved/);
  });

  /** No status card, sidebar or sticky summary — the property that killed Variant B. */
  it("states the pause in exactly one place", () => {
    visibility = { expertId: "e1", hidden: true, hiddenSince: "2026-09-01T00:00:00Z" };
    renderPage();

    expect(screen.getAllByText(/Your record is paused/)).toHaveLength(1);
    expect(screen.queryAllByRole("alert")).toHaveLength(0);
  });
});

describe("the rights, each in its own row", () => {
  it("downloads the export, labelled a right on a 6(1)(b) record", async () => {
    renderPage();

    expect(screen.getByText(/right to data portability/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Download JSON" }));
    expect(download).toHaveBeenCalled();
  });

  /** Same file either way; only the word for it changes (P1T-187). */
  it("labels it a courtesy on a legitimate-interest record", () => {
    access = accessView({ basis: "LegitimateInterest", export: "Courtesy" });
    renderPage();

    expect(screen.getByText(/as a courtesy/)).toBeInTheDocument();
    expect(screen.queryByText(/right to data portability/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Download JSON" })).toBeInTheDocument();
  });

  it("pauses and resumes through one control", async () => {
    renderPage();
    await userEvent.click(screen.getByRole("button", { name: "Pause" }));
    expect(setVisibility).toHaveBeenCalledWith(true);

    visibility = { expertId: "e1", hidden: true, hiddenSince: "2026-09-01T00:00:00Z" };
    renderPage();
    await userEvent.click(screen.getByRole("button", { name: "Resume" }));
    expect(setVisibility).toHaveBeenCalledWith(false);
  });

  it("offers a review of any score, on the row it is about", async () => {
    renderPage();

    await userEvent.click(screen.getByRole("button", { name: "Ask a person to review this" }));

    expect(contest).toHaveBeenCalledWith({ scoringCandidateId: "cand-1" });
  });

  it("offers no review where software has scored nobody", () => {
    access = accessView({
      derived: { assessments: [], searchIndexNote: "Nothing indexed." },
    });
    renderPage();

    expect(screen.queryByRole("button", { name: "Ask a person to review this" })).not.toBeInTheDocument();
    expect(screen.getByText(/software has not scored you/)).toBeInTheDocument();
  });
});

describe("objecting is for legitimate interest only (P1T-171)", () => {
  it("does not appear for a record its owner registered", () => {
    renderPage();

    expect(screen.queryByRole("button", { name: "Object" })).not.toBeInTheDocument();
  });

  /**
   * Inline, in the same rhythm as every other right. Burying it was Variant B's second defect, and
   * it is the only exit somebody on legitimate interest has.
   */
  it("appears inline for a staff-created record, in the same rhythm as the other rights", () => {
    access = accessView({ basis: "LegitimateInterest", export: "Courtesy" });
    renderPage();

    const objecting = row("Objecting to us holding it");
    expect(within(objecting).getByRole("button", { name: "Object" })).toBeInTheDocument();
    expect(within(objecting).getByText(/will not weigh your objection/)).toBeInTheDocument();
  });

  /**
   * Honoured unconditionally means nobody adjudicates it — not that it needs no proof. It deletes
   * the record, so it asks for the control word, the same gate deleting uses, because it is the
   * same act.
   */
  it("asks for the control word, because it deletes the record", async () => {
    access = accessView({ basis: "LegitimateInterest", export: "Courtesy" });
    renderPage();

    await userEvent.click(screen.getByRole("button", { name: "Object" }));
    const objecting = row("Objecting to us holding it");

    const submit = within(objecting).getByRole("button", { name: "Object and delete my record" });
    expect(submit).toBeDisabled();
    expect(erase).not.toHaveBeenCalled();

    await userEvent.type(within(objecting).getByLabelText("Your control word"), "hunter2");
    await userEvent.click(submit);
    expect(erase).toHaveBeenCalledWith("hunter2", expect.anything());
  });
});

describe("pause and delete are different kinds of thing (P1T-171)", () => {
  /**
   * P1T-171 chose two separate controls precisely so nobody deletes when they meant to pause, and
   * this service has no email to undo it with. The separation is position and typography: delete is
   * below a rule, under its own heading, at the foot of a long page. Asserted, because "tidying"
   * the page shorter is the change that would silently undo it.
   */
  it("puts delete below its own heading, after the pause, at the foot", () => {
    renderPage();

    const pause = screen.getByRole("button", { name: "Pause" });
    const heading = screen.getByRole("heading", { name: "Deleting everything" });
    const remove = screen.getByRole("button", { name: "Delete everything" });

    expect(pause.compareDocumentPosition(heading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(heading.compareDocumentPosition(remove) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("points somebody who meant to pause back up the page", () => {
    renderPage();

    expect(screen.getByText(/If you only want to stop being offered for work/)).toBeInTheDocument();
  });

  it("says what deleting takes, and what survives", () => {
    renderPage();

    const said = screen.getByText(/This removes your CV/);
    expect(said).toHaveTextContent(/cannot be undone/);
    expect(said).toHaveTextContent(/no way to contact you afterwards/);
    expect(screen.getByText(/keep their decision/)).toBeInTheDocument();
  });

  it("will not delete without the control word", async () => {
    renderPage();

    const remove = screen.getByRole("button", { name: "Delete everything" });
    expect(remove).toBeDisabled();

    await userEvent.type(screen.getByLabelText("Your control word"), "hunter2");
    await userEvent.click(remove);

    expect(erase).toHaveBeenCalledWith("hunter2", expect.anything());
  });
});

describe("somebody who owns no record", () => {
  /** Most of the page has nothing to say, so it degrades to one accurate sentence rather than to a
   * column of empty rows — the honest degradation that Variant A was chosen for. */
  it("gets one sentence and the two acts still open to them", () => {
    ownsNothing = true;
    renderPage();

    expect(screen.getByText(/nothing held under your name yet/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Redeem" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Deleting everything" })).toBeInTheDocument();

    // And none of the rows that would be lying.
    expect(screen.queryByText("Assessments")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Download JSON" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Pause" })).not.toBeInTheDocument();
  });

  it("says the delete takes only the sign-in", () => {
    ownsNothing = true;
    renderPage();

    expect(screen.getByText(/removes your sign-in/)).toBeInTheDocument();
    expect(screen.queryByText(/This removes your CV/)).not.toBeInTheDocument();
  });
});
