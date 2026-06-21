import { useRef, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import {
  Box,
  Fab,
  IconButton,
  Link,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import CloseIcon from "@mui/icons-material/Close";
import SendIcon from "@mui/icons-material/Send";
import { apiErrorMessage, useRosterQa } from "../api";

type Role = "user" | "assistant" | "error";
interface Message {
  role: Role;
  text: string;
}

// Matches a GUID anywhere in the text. The agent cites employees by name + id, so we turn
// those ids into links to the employee detail page.
const GUID = /[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}/g;

function LinkifiedText({ text }: { text: string }) {
  const parts: React.ReactNode[] = [];
  let lastIndex = 0;
  for (const match of text.matchAll(GUID)) {
    const id = match[0];
    const start = match.index ?? 0;
    if (start > lastIndex) parts.push(text.slice(lastIndex, start));
    parts.push(
      <Link key={`${id}-${start}`} component={RouterLink} to={`/employees/${id}`}>
        {id}
      </Link>,
    );
    lastIndex = start + id.length;
  }
  if (lastIndex < text.length) parts.push(text.slice(lastIndex));
  return <Box sx={{ whiteSpace: "pre-wrap" }}>{parts}</Box>;
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
        {isUser ? <Box sx={{ whiteSpace: "pre-wrap" }}>{message.text}</Box> : <LinkifiedText text={message.text} />}
      </Paper>
    </Box>
  );
}

export default function RosterChatWidget() {
  const [open, setOpen] = useState(false);
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
      // Stateless today: each call is independent (no memory). #16 will thread a session id.
      const { answer } = await ask.mutateAsync(question);
      setMessages((m) => [...m, { role: "assistant", text: answer }]);
    } catch (err) {
      setMessages((m) => [...m, { role: "error", text: apiErrorMessage(err) }]);
    }
    scrollToEnd();
  }

  return (
    <>
      <Fab
        color="primary"
        aria-label="Ask the roster assistant"
        onClick={() => setOpen((o) => !o)}
        sx={{ position: "fixed", bottom: 24, right: 24, zIndex: 1300 }}
      >
        {open ? <CloseIcon /> : <SmartToyIcon />}
      </Fab>

      {open && (
        <Paper
          elevation={8}
          sx={{
            position: "fixed",
            bottom: 96,
            right: 24,
            width: 380,
            maxWidth: "calc(100vw - 48px)",
            height: 520,
            maxHeight: "calc(100vh - 140px)",
            display: "flex",
            flexDirection: "column",
            zIndex: 1300,
            borderRadius: 3,
            overflow: "hidden",
          }}
        >
          <Box sx={{ px: 2, py: 1.5, bgcolor: "primary.main", color: "primary.contrastText" }}>
            <Stack direction="row" alignItems="center" justifyContent="space-between">
              <Stack direction="row" alignItems="center" spacing={1}>
                <SmartToyIcon fontSize="small" />
                <Typography variant="subtitle1">Roster assistant</Typography>
              </Stack>
              <IconButton size="small" onClick={() => setOpen(false)} sx={{ color: "inherit" }}>
                <CloseIcon fontSize="small" />
              </IconButton>
            </Stack>
            <Typography variant="caption" sx={{ opacity: 0.8 }}>
              Ask about skills, availability, experience. Follow-ups don't keep context yet.
            </Typography>
          </Box>

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
              {ask.isPending && (
                <Bubble message={{ role: "assistant", text: "Thinking…" }} />
              )}
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
        </Paper>
      )}
    </>
  );
}
