// PROTOTYPE — throwaway (EXP-28). In-memory stub data for the conversation-history variants.
// Nothing here talks to a backend; resuming appends a canned answer.

export type TurnState = "ok" | "ungrounded" | "removed" | "hidden";

export interface ProtoTurn {
  question: string;
  answer: string;
  modelId: string;
  state: TurnState;
  at: Date;
}

export interface ProtoConversation {
  id: string;
  title: string;
  lastActiveAt: Date;
  turns: ProtoTurn[];
}

const DAY = 24 * 60 * 60 * 1000;
const ago = (days: number, hours = 0) => new Date(Date.now() - days * DAY - hours * 3600_000);

export const RETENTION_DAYS = 183; // "6 months since last activity" (EXP-26)

export const REMOVED_TEXT = "Removed: referred to someone whose data was erased.";
export const HIDDEN_TEXT = "Hidden: referred to someone who has paused their profile.";

/** First question trimmed to ~60 chars at a word boundary (EXP-25). */
export function titleOf(question: string): string {
  if (question.length <= 60) return question;
  const cut = question.slice(0, 60);
  return cut.slice(0, cut.lastIndexOf(" ")) + "…";
}

export function daysLeft(c: ProtoConversation): number {
  return Math.max(0, RETENTION_DAYS - Math.floor((Date.now() - c.lastActiveAt.getTime()) / DAY));
}

export function groupOf(c: ProtoConversation): "Today" | "This week" | "Older" {
  const age = Date.now() - c.lastActiveAt.getTime();
  return age < DAY ? "Today" : age < 7 * DAY ? "This week" : "Older";
}

const q1 = "Who knows React and is available this summer for a fintech engagement in Berlin?";
const q3 = "Which experts have Kubernetes certifications?";

export function seedConversations(): ProtoConversation[] {
  return [
    {
      id: "c1",
      title: titleOf(q1),
      lastActiveAt: ago(0, 1),
      turns: [
        {
          question: q1,
          answer:
            "Two experts match:\n\n- **Ada Lovelace (a1b2c3d4-…)**: React 5 years, available from 1 June.\n- **Grace Hopper (e5f6a7b8-…)**: React 3 years, available all summer.",
          modelId: "gemini-3.5-flash-lite",
          state: "ok",
          at: ago(0, 2),
        },
        {
          question: "Does either of them speak German?",
          answer: "Grace Hopper (e5f6a7b8-…) lists German at C1. Ada Lovelace lists no German.",
          modelId: "gemini-3.5-flash",
          state: "ok",
          at: ago(0, 1),
        },
      ],
    },
    {
      id: "c2",
      title: "Is Linus free in May?",
      lastActiveAt: ago(3),
      turns: [
        { question: "Is Linus free in May?", answer: "", modelId: "gemini-3.5-flash-lite", state: "removed", at: ago(3, 1) },
        {
          question: "Then who else has Linux kernel experience?",
          answer: "Nobody else in the roster lists kernel work.\n\n_Note: this answer could not be grounded in roster data._",
          modelId: "gemini-3.5-flash-lite",
          state: "ungrounded",
          at: ago(3),
        },
      ],
    },
    {
      id: "c3",
      title: titleOf(q3),
      lastActiveAt: ago(20),
      turns: [
        { question: q3, answer: "", modelId: "gemini-3.5-flash-lite", state: "hidden", at: ago(20, 1) },
        {
          question: "And AWS?",
          answer: "Three experts hold AWS Solutions Architect: Margaret Hamilton (…), Katherine Johnson (…), Alan Turing (…).",
          modelId: "gemini-3.5-flash-lite",
          state: "ok",
          at: ago(20),
        },
      ],
    },
    {
      id: "c4",
      title: "Summarise the bench for next month",
      lastActiveAt: ago(176),
      turns: [
        {
          question: "Summarise the bench for next month",
          answer: "Four experts roll off engagements in October; two have no follow-on booked.",
          modelId: "gemini-2.5-flash-lite",
          state: "ok",
          at: ago(176),
        },
      ],
    },
  ];
}

export function stubAnswer(question: string): ProtoTurn {
  return {
    question,
    answer: "(prototype) A stub answer. In the real build this is the model's reply, grounded in fresh roster data.",
    modelId: "gemini-3.5-flash-lite",
    state: "ok",
    at: new Date(),
  };
}
