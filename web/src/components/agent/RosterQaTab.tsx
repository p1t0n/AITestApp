import { useRef, useState } from "react";
import { Box, IconButton, Paper, Stack, TextField, Typography } from "@mui/material";
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
        elevation={0}
        sx={{
          px: 1.5,
          py: 1,
          maxWidth: "85%",
          bgcolor: isUser ? "primary.main" : isError ? "error.light" : "grey.100",
          color: isUser ? "primary.contrastText" : isError ? "error.contrastText" : "text.primary",
          borderRadius: 2,
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
  const ask = useRosterQa();
  const scrollRef = useRef<HTMLDivElement>(null);

  function scrollToEnd() {
    requestAnimationFrame(() => {
      scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
    });
  }

  async function send() {
    const question = draft.trim();
    if (!question || ask.isPending) return;
    setDraft("");
    setMessages((m) => [...m, { role: "user", text: question }]);
    scrollToEnd();
    try {
      const { answer } = await ask.mutateAsync(question);
      setMessages((m) => [...m, { role: "assistant", text: answer }]);
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
              e.g. "Who knows React and is available this summer?"
            </Typography>
          )}
          {messages.map((m, i) => (
            <Bubble key={i} message={m} />
          ))}
          {ask.isPending && <Bubble message={{ role: "assistant", text: "Thinking…" }} />}
        </Stack>
      </Box>

      <Box sx={{ p: 1, borderTop: 1, borderColor: "divider" }}>
        <Stack direction="row" spacing={1} alignItems="flex-end">
          <TextField
            fullWidth
            size="small"
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
