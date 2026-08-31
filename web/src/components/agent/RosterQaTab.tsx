import { useRef, useState } from "react";
import { Box, Button, IconButton, Paper, Stack, TextField, Typography } from "@mui/material";
import SendIcon from "@mui/icons-material/Send";
import { apiErrorMessage, useRosterQa } from "../../api";
import { AgentMarkdown } from "./AgentMarkdown";

type Role = "user" | "assistant" | "error";
interface Message {
  role: Role;
  text: string;
}

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
          bgcolor: isUser ? "primary.main" : isError ? "error.light" : "surface.raised",
          color: isUser ? "primary.contrastText" : isError ? "error.contrastText" : "text.primary",
        }}
      >
        {isUser ? (
          <Box sx={{ whiteSpace: "pre-wrap" }}>{message.text}</Box>
        ) : (
          <AgentMarkdown text={message.text} />
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
      const { answer, threadId: returned } = await ask.mutateAsync({ question, threadId: sent });
      setThreadId(returned);
      setMessages((m) => [
        ...(sent && returned !== sent
          ? [...m, { role: "error" as const, text: "That conversation expired — starting a new one." }]
          : m),
        { role: "assistant", text: answer },
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
            <Typography variant="body2" color="text.secondary" sx={{ p: 1 }}>
              e.g. "Who knows React and is available this summer?" Follow-ups keep the context.
            </Typography>
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
        <Stack direction="row" spacing={1} alignItems="flex-end">
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
