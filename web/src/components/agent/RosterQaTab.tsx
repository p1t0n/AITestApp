import { useEffect, useMemo, useRef, useState } from "react";
import { Box, Chip, IconButton, Paper, Stack, TextField, Tooltip, Typography } from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import HistoryIcon from "@mui/icons-material/History";
import RemoveCircleOutlineIcon from "@mui/icons-material/RemoveCircleOutlined";
import SendIcon from "@mui/icons-material/Send";
import SmartToyOutlinedIcon from "@mui/icons-material/SmartToyOutlined";
import VisibilityOffOutlinedIcon from "@mui/icons-material/VisibilityOffOutlined";
import {
  apiErrorMessage,
  useRosterQa,
  useRosterQaConversation,
  useRosterQaConversations,
  type ConversationTurn,
} from "../../api";
import { AgentMarkdown } from "./AgentMarkdown";
import { ConversationHistoryDrawer } from "./ConversationHistoryDrawer";
import { HIDDEN_TEXT, REMOVED_TEXT, conversationToResume, relativeTime } from "./conversationHistory";

type Role = "user" | "assistant" | "error" | "masked";
interface Message {
  role: Role;
  text: string;
  /** The model the provider said wrote this turn (EXP-31). Only ever set on an assistant turn:
   * a question has no author but the person asking, and an error has none at all. */
  modelId?: string | null;
  /** When the turn was written, ISO-8601. Absent on a live question — "just now" is what the
   * person is looking at, not something worth captioning. */
  at?: string;
  /** Off the run's capture scope, never parsed out of the answer (ADR §3). Absent on a live turn:
   * `POST /agents/roster-qa` does not report it, and absent has to read as *unknown* rather than
   * as grounded — which is why the dashed edge keys on `false` and not on falsiness. */
  grounded?: boolean;
  /** Why this turn has no text: erasure (`removed`) or a paused profile (`hidden`). */
  masked?: "removed" | "hidden";
}

/** One stored turn as the transcript reads it. A masked turn is **one** entry, not an empty
 * question and an empty answer — the gap is the fact, and two blank bubbles would be a worse way
 * of saying it than one sentence. */
function messagesOf(turn: ConversationTurn): Message[] {
  if (turn.state !== "ok") {
    return [{ role: "masked", text: "", masked: turn.state, at: turn.createdAt }];
  }
  return [
    { role: "user", text: turn.question },
    {
      role: "assistant",
      text: turn.answer,
      modelId: turn.modelId || null,
      at: turn.createdAt,
      grounded: turn.grounded,
    },
  ];
}

/**
 * A turn that lost its text. Muted, italic, and with an icon that says which of the two kinds of
 * gap this is — erasure is permanent and pause is not, and a reader who owns this conversation is
 * entitled to know which without being told *whose* profile it was.
 */
function MaskedTurn({ kind }: { kind: "removed" | "hidden" }) {
  const Icon = kind === "removed" ? RemoveCircleOutlineIcon : VisibilityOffOutlinedIcon;
  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: "center", color: "text.secondary", py: 0.5 }}>
      <Icon fontSize="small" />
      <Typography variant="body2" sx={{ fontStyle: "italic" }}>
        {kind === "removed" ? REMOVED_TEXT : HIDDEN_TEXT}
      </Typography>
    </Stack>
  );
}

/**
 * One turn in the conversation.
 *
 * The three fills are the three things a reader has to tell apart at a glance, and P1T-163 tuned
 * them for dark rather than leaving them as whatever the palette happened to make of the old
 * hardcoded values:
 *
 * * **the person** — the accent as a *wash* (`action.selected`) with the accent as its edge, not
 *   as a solid `primary.main` slab. A filled accent bubble is the loudest thing on a dark panel and
 *   the design record reserves the accent for the primary action and the focus ring
 *   (`manuals/spa-design-system.md` §3); the wash still says "this one is yours" without competing
 *   with the Send button two inches below it.
 * * **the agent** — the raised step of the surface ramp, which is exactly what a `well` is.
 * * **an error** — left alone on purpose. It fills with `error.light` and labels itself with
 *   `error.contrastText`, the app's only such pairing, which is why `tokens.contrast.test.ts`
 *   asserts that one extra pair; and P1T-153 already decided this bubble keeps a bubble's look
 *   rather than becoming an `ErrorNotice`, because it is a turn in a conversation, not a banner.
 *
 * The square corner marks the speaker's side — the one piece of shape in the panel that carries
 * meaning, so alignment is not the only thing distinguishing two washes of similar weight.
 */
function Bubble({ message }: { message: Message }) {
  const isUser = message.role === "user";
  const isError = message.role === "error";
  // `false`, not falsy: a live turn's grounding is unknown, and drawing the warning edge on
  // "we were not told" would make the dock claim something the run never reported (EXP-34).
  const ungrounded = message.role === "assistant" && message.grounded === false;
  const caption = message.at ? relativeTime(message.at) : null;
  return (
    <Box sx={{ display: "flex", justifyContent: isUser ? "flex-end" : "flex-start" }}>
      <Paper
        variant="well"
        sx={{
          px: 1.5,
          py: 1,
          maxWidth: "85%",
          minWidth: 0,
          bgcolor: isUser ? "action.selected" : isError ? "error.light" : "surface.raised",
          color: isError ? "error.contrastText" : "text.primary",
          ...(isUser && { border: 1, borderColor: "primary.main" }),
          // Dashed rather than a fill: the answer is still the answer and still worth reading —
          // what the edge says is that nothing under it came from the roster.
          ...(ungrounded && { border: 1, borderStyle: "dashed", borderColor: "warning.main" }),
          borderRadius: 1.5,
          ...(isUser ? { borderBottomRightRadius: 4 } : { borderBottomLeftRadius: 4 }),
        }}
      >
        {isUser ? (
          <Box sx={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>{message.text}</Box>
        ) : (
          <AgentMarkdown text={message.text} />
        )}
        {/* Who wrote it, and when (EXP-31, extended by EXP-34) — inside the bubble, because both
            are properties of this turn and not of the conversation. The model can change
            mid-thread: an override lands, an alias resolves elsewhere, a provider moves a point
            release under a name. It is absent when the provider named none, which is the honest
            answer rather than repeating the configured model as if it had been confirmed; the
            time still shows, because a resumed conversation's turns can be months apart.

            The model sits in its own span so that it is addressable as itself — the caption is one
            sentence to a reader and two facts to anything asking which model answered. */}
        {(message.modelId || caption) && (
          <Stack direction="row" spacing={0.75} sx={{ alignItems: "center", mt: 0.5, flexWrap: "wrap" }}>
            <Typography
              variant="caption"
              component="div"
              sx={{ color: "text.secondary", overflowWrap: "anywhere" }}
            >
              {message.modelId && <Box component="span">{message.modelId}</Box>}
              {message.modelId && caption ? " · " : null}
              {caption && <Box component="span">{caption}</Box>}
            </Typography>
            {ungrounded && (
              <Chip size="small" variant="outlined" color="warning" label="Not grounded" />
            )}
          </Stack>
        )}
      </Paper>
    </Box>
  );
}

export function RosterChat() {
  const [draft, setDraft] = useState("");
  // Turns added in this session, on top of whatever the history API returned. Kept apart from the
  // stored transcript rather than merged into it: the detail query is the server's answer and
  // re-reading it must not duplicate what is already on screen.
  const [live, setLive] = useState<Message[]>([]);
  // The conversation whose stored transcript is being shown, or `null` for one that has no
  // history to fetch (a brand-new thread, whose only turns are in `live`).
  const [openedId, setOpenedId] = useState<string | null>(null);
  // The server-side conversation id. Sent with every follow-up; a response carrying a DIFFERENT
  // id means the conversation was unknown and the server started fresh — we surface that inline.
  const [threadId, setThreadId] = useState<string | undefined>(undefined);
  const [historyOpen, setHistoryOpen] = useState(false);
  const ask = useRosterQa();
  const conversations = useRosterQaConversations();
  const opened = useRosterQaConversation(openedId);
  const scrollRef = useRef<HTMLDivElement>(null);

  // The surface opens onto the conversation that was still live half an hour ago, and onto a fresh
  // one otherwise (ADR §7). Once, on the first list the query returns: a later refetch — a send
  // invalidates the index — must not yank somebody out of the conversation they are reading.
  //
  // The second guard is the race rather than the repeat: a slow index arriving *after* somebody
  // has already asked something would otherwise replace what they are looking at with a
  // conversation from twenty minutes ago. Nothing that has already started gets re-pointed.
  const resumed = useRef(false);
  const engaged = threadId !== undefined || live.length > 0;
  useEffect(() => {
    if (resumed.current || !conversations.data) return;
    resumed.current = true;
    if (engaged) return;
    const latest = conversationToResume(conversations.data);
    if (latest) {
      setOpenedId(latest.id);
      setThreadId(latest.id);
    }
  }, [conversations.data, engaged]);

  const stored = useMemo(() => (opened.data?.turns ?? []).flatMap(messagesOf), [opened.data]);
  const messages = useMemo(() => [...stored, ...live], [stored, live]);
  const title =
    conversations.data?.find((c) => c.id === threadId)?.title ?? "New conversation";

  function scrollToEnd() {
    requestAnimationFrame(() => {
      // Optional call: jsdom (vitest) has no scrollTo.
      scrollRef.current?.scrollTo?.({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
    });
  }

  function newConversation() {
    setLive([]);
    setOpenedId(null);
    setThreadId(undefined);
    setHistoryOpen(false);
  }

  function openConversation(id: string) {
    setLive([]);
    setOpenedId(id);
    setThreadId(id);
    setHistoryOpen(false);
  }

  async function send() {
    const question = draft.trim();
    if (!question || ask.isPending) return;
    setDraft("");
    setLive((m) => [...m, { role: "user", text: question }]);
    scrollToEnd();
    try {
      const sent = threadId;
      const { answer, threadId: returned, modelId } = await ask.mutateAsync({ question, threadId: sent });
      const restarted = Boolean(sent) && returned !== sent;
      setThreadId(returned);
      // The server did not continue what we asked it to, so the transcript above this point
      // belongs to a conversation this answer is not part of. Dropping it is the honest move:
      // leaving it would read as context the model still has.
      if (restarted) setOpenedId(null);
      setLive((m) => [
        ...(restarted
          ? [...m, { role: "error" as const, text: "That conversation expired — starting a new one." }]
          : m),
        { role: "assistant", text: answer, modelId, at: new Date().toISOString() },
      ]);
    } catch (err) {
      setLive((m) => [...m, { role: "error", text: apiErrorMessage(err) }]);
    }
    scrollToEnd();
  }

  return (
    // `relative` is what makes the history overlay an overlay *of this surface*: it covers the
    // transcript and the header's own row stays reachable underneath it in the dock chrome above.
    <Box sx={{ position: "relative", display: "flex", flexDirection: "column", flex: 1, minHeight: 0 }}>
      <Stack
        direction="row"
        spacing={0.5}
        sx={{ alignItems: "center", px: 1, py: 0.5, borderBottom: 1, borderColor: "divider" }}
      >
        {/* Ellipsized rather than wrapped, with the full text as the native tooltip: a title is
            the first question, which is a sentence, and a dock 360px wide cannot hold one. */}
        <Typography
          variant="body2"
          title={title}
          sx={{
            flex: 1,
            minWidth: 0,
            overflow: "hidden",
            textOverflow: "ellipsis",
            whiteSpace: "nowrap",
          }}
        >
          {title}
        </Typography>
        <Tooltip title="New conversation">
          <IconButton aria-label="New conversation" disabled={ask.isPending} onClick={newConversation}>
            <AddIcon fontSize="small" />
          </IconButton>
        </Tooltip>
        <Tooltip title="Conversation history">
          <IconButton aria-label="Conversation history" onClick={() => setHistoryOpen(true)}>
            <HistoryIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      </Stack>

      <Box ref={scrollRef} sx={{ flex: 1, overflowY: "auto", p: 1.5 }}>
        {/* A `log` rather than a plain stack: turns arrive one at a time and a screen reader that
            is not told this is a live region simply never hears the answer. */}
        <Stack spacing={1} role="log" aria-label="Roster Q&A transcript">
          {opened.isLoading && (
            <Typography variant="body2" sx={{ color: "text.secondary", textAlign: "center", py: 5 }}>
              Loading this conversation…
            </Typography>
          )}
          {!opened.isLoading && messages.length === 0 && (
            // An empty transcript is the first thing anybody sees in this app's signature surface,
            // and it was one grey sentence hugging the top-left corner. Same words — they are the
            // useful part — centred with the surface's own icon above them, so the panel reads as
            // waiting rather than as failed to load.
            <Stack
              spacing={1}
              sx={{
                alignItems: "center",
                px: 2,
                py: 5,
                color: "text.secondary",
                textAlign: "center"
              }}>
              <SmartToyOutlinedIcon sx={{ fontSize: 32, color: "text.disabled" }} />
              <Typography variant="body2">
                e.g. "Who knows React and is available this summer?" Follow-ups keep the context.
              </Typography>
            </Stack>
          )}
          {messages.map((m, i) =>
            m.masked ? <MaskedTurn key={i} kind={m.masked} /> : <Bubble key={i} message={m} />,
          )}
          {ask.isPending && <Bubble message={{ role: "assistant", text: "Thinking…" }} />}
        </Stack>
      </Box>

      <Box sx={{ p: 1, borderTop: 1, borderColor: "divider" }}>
        <Stack direction="row" spacing={1} sx={{ alignItems: "flex-end" }}>
          <TextField
            fullWidth
            multiline
            maxRows={4}
            placeholder="Ask about the roster…"
            value={draft}
            disabled={ask.isPending}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                void send();
              }
            }}
          />
          <IconButton
            color="primary"
            aria-label="Send"
            disabled={ask.isPending || !draft.trim()}
            onClick={() => void send()}
          >
            <SendIcon />
          </IconButton>
        </Stack>
      </Box>

      {historyOpen && (
        <ConversationHistoryDrawer
          activeId={openedId}
          onOpen={openConversation}
          onClose={() => setHistoryOpen(false)}
          onDeleted={(id) => {
            // Deleting the conversation you are reading leaves you reading something that no
            // longer exists. Stepping to a fresh one is the only state that is still true.
            if (id === threadId || id === openedId) newConversation();
          }}
        />
      )}
    </Box>
  );
}
