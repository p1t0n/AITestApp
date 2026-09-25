import { useRef, useState } from "react";
import { Box, Button, IconButton, Paper, Stack, TextField, Typography } from "@mui/material";
import SendIcon from "@mui/icons-material/Send";
import SmartToyOutlinedIcon from "@mui/icons-material/SmartToyOutlined";
import { apiErrorMessage, useRosterQa } from "../../api";
import { AgentMarkdown } from "./AgentMarkdown";

type Role = "user" | "assistant" | "error";
interface Message {
  role: Role;
  text: string;
  /** The model the provider said wrote this turn (EXP-31). Only ever set on an assistant turn:
   * a question has no author but the person asking, and an error has none at all. */
  modelId?: string | null;
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
          borderRadius: 1.5,
          ...(isUser ? { borderBottomRightRadius: 4 } : { borderBottomLeftRadius: 4 }),
        }}
      >
        {isUser ? (
          <Box sx={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>{message.text}</Box>
        ) : (
          <AgentMarkdown text={message.text} />
        )}
        {/* Who wrote it (EXP-31) — inside the bubble, because it is a property of this turn and
            not of the conversation. The model can change mid-thread: an override lands, an alias
            resolves elsewhere, a provider moves a point release under a name. Absent when the
            provider named none, which is the honest answer rather than repeating the configured
            model as if it had been confirmed. */}
        {message.modelId && (
          <Typography
            variant="caption"
            component="div"
            sx={{ color: "text.secondary", mt: 0.5, overflowWrap: "anywhere" }}
          >
            {message.modelId}
          </Typography>
        )}
      </Paper>
    </Box>
  );
}

export function RosterChat() {
  const [draft, setDraft] = useState("");
  const [messages, setMessages] = useState<Message[]>([]);
  // The server-side conversation id. Sent with every follow-up; a response carrying a DIFFERENT
  // id means the thread expired and the server started fresh — we surface that inline.
  const [threadId, setThreadId] = useState<string | undefined>(undefined);
  const ask = useRosterQa();
  const scrollRef = useRef<HTMLDivElement>(null);

  function scrollToEnd() {
    requestAnimationFrame(() => {
      // Optional call: jsdom (vitest) has no scrollTo.
      scrollRef.current?.scrollTo?.({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
    });
  }

  function newConversation() {
    setMessages([]);
    setThreadId(undefined);
  }

  async function send() {
    const question = draft.trim();
    if (!question || ask.isPending) return;
    setDraft("");
    setMessages((m) => [...m, { role: "user", text: question }]);
    scrollToEnd();
    try {
      const sent = threadId;
      const { answer, threadId: returned, modelId } = await ask.mutateAsync({ question, threadId: sent });
      setThreadId(returned);
      setMessages((m) => [
        ...(sent && returned !== sent
          ? [...m, { role: "error" as const, text: "That conversation expired — starting a new one." }]
          : m),
        { role: "assistant", text: answer, modelId },
      ]);
    } catch (err) {
      setMessages((m) => [...m, { role: "error", text: apiErrorMessage(err) }]);
    }
    scrollToEnd();
  }

  return (
    <>
      <Box ref={scrollRef} sx={{ flex: 1, overflowY: "auto", p: 1.5 }}>
        <Stack spacing={1}>
          {messages.length === 0 && (
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
          {messages.map((m, i) => (
            <Bubble key={i} message={m} />
          ))}
          {ask.isPending && <Bubble message={{ role: "assistant", text: "Thinking…" }} />}
        </Stack>
      </Box>

      {messages.length > 0 && (
        <Box sx={{ px: 1.5, pb: 0.5 }}>
          <Button onClick={newConversation} disabled={ask.isPending}>
            New conversation
          </Button>
        </Box>
      )}

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
    </>
  );
}
