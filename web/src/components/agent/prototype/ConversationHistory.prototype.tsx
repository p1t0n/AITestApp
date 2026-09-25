// PROTOTYPE — throwaway (EXP-28). Three structurally different placements for Roster Q&A
// conversation history inside the agent dock, switchable with `?proto=history&variant=A|B|C`.
//   A — Drawer: a history overlay slides over the chat, opened from a header button.
//   B — Split: a permanent conversation list beside the transcript.
//   C — Switcher: a conversation picker in the header, plus a "manage" dialog for bulk actions.
// In-memory stub data only (conversationHistory.stub.ts). Nothing here ships.
import { useEffect, useState } from "react";
import {
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  ListSubheader,
  Menu,
  MenuItem,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import HistoryIcon from "@mui/icons-material/History";
import AddIcon from "@mui/icons-material/Add";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutlined";
import SendIcon from "@mui/icons-material/Send";
import CloseIcon from "@mui/icons-material/Close";
import ArrowDropDownIcon from "@mui/icons-material/ArrowDropDown";
import MoreVertIcon from "@mui/icons-material/MoreVert";
import VisibilityOffOutlinedIcon from "@mui/icons-material/VisibilityOffOutlined";
import RemoveCircleOutlineIcon from "@mui/icons-material/RemoveCircleOutlined";
import { useSearchParams } from "react-router";
import { AgentMarkdown } from "../AgentMarkdown";
import {
  HIDDEN_TEXT,
  REMOVED_TEXT,
  daysLeft,
  groupOf,
  seedConversations,
  stubAnswer,
  titleOf,
  type ProtoConversation,
  type ProtoTurn,
} from "./conversationHistory.stub";

// ---- shared state ---------------------------------------------------------------------------

function useProtoHistory() {
  const [conversations, setConversations] = useState<ProtoConversation[]>(seedConversations);
  const [activeId, setActiveId] = useState<string | null>("c1");
  const active = conversations.find((c) => c.id === activeId) ?? null;

  function ask(question: string) {
    const turn = stubAnswer(question);
    if (!active) {
      const id = `c${Date.now()}`;
      setConversations((cs) => [{ id, title: titleOf(question), lastActiveAt: new Date(), turns: [turn] }, ...cs]);
      setActiveId(id);
    } else {
      setConversations((cs) =>
        [...cs.map((c) => (c.id === active.id ? { ...c, lastActiveAt: new Date(), turns: [...c.turns, turn] } : c))].sort(
          (a, b) => b.lastActiveAt.getTime() - a.lastActiveAt.getTime(),
        ),
      );
    }
  }

  function remove(id: string) {
    setConversations((cs) => cs.filter((c) => c.id !== id));
    if (activeId === id) setActiveId(null);
  }

  function removeAll() {
    setConversations([]);
    setActiveId(null);
  }

  return { conversations, active, activeId, setActiveId, ask, remove, removeAll };
}

type Proto = ReturnType<typeof useProtoHistory>;

function relative(d: Date): string {
  const mins = Math.round((Date.now() - d.getTime()) / 60000);
  if (mins < 60) return `${Math.max(1, mins)} min ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.round(hours / 24);
  return days < 30 ? `${days} d ago` : d.toLocaleDateString();
}

const RETENTION_NOTE = "Conversations are deleted 6 months after their last activity.";

function ExpiryHint({ c }: { c: ProtoConversation }) {
  const left = daysLeft(c);
  return left <= 14 ? (
    <Typography component="span" variant="caption" sx={{ color: "warning.main" }}>
      {" "}· disappears in {left} d
    </Typography>
  ) : null;
}

// ---- the transcript (shared: the variants disagree about navigation, not about turns) --------

function Turn({ turn }: { turn: ProtoTurn }) {
  if (turn.state === "removed" || turn.state === "hidden") {
    const Icon = turn.state === "removed" ? RemoveCircleOutlineIcon : VisibilityOffOutlinedIcon;
    return (
      <Stack direction="row" spacing={1} sx={{ alignItems: "center", color: "text.secondary", py: 0.5 }}>
        <Icon fontSize="small" />
        <Typography variant="body2" sx={{ fontStyle: "italic" }}>
          {turn.state === "removed" ? REMOVED_TEXT : HIDDEN_TEXT}
        </Typography>
      </Stack>
    );
  }
  const ungrounded = turn.state === "ungrounded";
  return (
    <Stack spacing={0.5}>
      <Box sx={{ display: "flex", justifyContent: "flex-end" }}>
        <Paper variant="well" sx={{ px: 1.5, py: 1, maxWidth: "85%", bgcolor: "action.selected", border: 1, borderColor: "primary.main" }}>
          <Box sx={{ whiteSpace: "pre-wrap" }}>{turn.question}</Box>
        </Paper>
      </Box>
      <Box sx={{ display: "flex", flexDirection: "column", alignItems: "flex-start" }}>
        <Paper
          variant="well"
          sx={{
            px: 1.5,
            py: 1,
            maxWidth: "85%",
            bgcolor: "surface.raised",
            ...(ungrounded && { border: 1, borderStyle: "dashed", borderColor: "warning.main" }),
          }}
        >
          <AgentMarkdown text={turn.answer} />
        </Paper>
        <Stack direction="row" spacing={1} sx={{ alignItems: "center", mt: 0.25 }}>
          <Typography variant="caption" sx={{ color: "text.secondary" }}>
            {turn.modelId} · {relative(turn.at)}
          </Typography>
          {ungrounded && <Chip size="small" variant="outlined" color="warning" label="Not grounded" />}
        </Stack>
      </Box>
    </Stack>
  );
}

function Transcript({ p }: { p: Proto }) {
  const [draft, setDraft] = useState("");
  function send() {
    if (!draft.trim()) return;
    p.ask(draft.trim());
    setDraft("");
  }
  return (
    <Box sx={{ display: "flex", flexDirection: "column", flex: 1, minHeight: 0, minWidth: 0 }}>
      <Box sx={{ flex: 1, overflowY: "auto", p: 1.5 }}>
        <Stack spacing={1.5}>
          {!p.active && (
            <Typography variant="body2" sx={{ color: "text.secondary", textAlign: "center", py: 5 }}>
              New conversation. Ask about the roster.
            </Typography>
          )}
          {p.active?.turns.map((t, i) => <Turn key={i} turn={t} />)}
        </Stack>
      </Box>
      <Box sx={{ p: 1, borderTop: 1, borderColor: "divider" }}>
        <Stack direction="row" spacing={1} sx={{ alignItems: "flex-end" }}>
          <TextField
            fullWidth
            multiline
            maxRows={4}
            placeholder={p.active ? "Continue this conversation…" : "Ask about the roster…"}
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                send();
              }
            }}
          />
          <IconButton color="primary" aria-label="Send" disabled={!draft.trim()} onClick={send}>
            <SendIcon />
          </IconButton>
        </Stack>
      </Box>
    </Box>
  );
}

function GroupedList({ p, onPick, dense }: { p: Proto; onPick: (id: string) => void; dense?: boolean }) {
  const groups = (["Today", "This week", "Older"] as const).map((g) => ({
    g,
    items: p.conversations.filter((c) => groupOf(c) === g),
  }));
  if (p.conversations.length === 0) {
    return (
      <Typography variant="body2" sx={{ color: "text.secondary", p: 2 }}>
        No past conversations.
      </Typography>
    );
  }
  return (
    <List dense={dense} disablePadding>
      {groups
        .filter(({ items }) => items.length)
        .map(({ g, items }) => [
          <ListSubheader key={g} sx={{ lineHeight: 2.25, bgcolor: "transparent" }}>
            {g}
          </ListSubheader>,
          ...items.map((c) => (
            <ListItemButton
              key={c.id}
              selected={c.id === p.activeId}
              onClick={() => onPick(c.id)}
              sx={{ pr: 0.5, "& .proto-del": { opacity: 0 }, "&:hover .proto-del, & .proto-del:focus-visible": { opacity: 1 } }}
            >
              <ListItemText
                primary={c.title}
                secondary={
                  <>
                    {relative(c.lastActiveAt)}
                    <ExpiryHint c={c} />
                  </>
                }
                slotProps={{ primary: { noWrap: true, variant: "body2" } }}
              />
              <IconButton
                className="proto-del"
                size="small"
                aria-label={`Delete conversation "${c.title}"`}
                onClick={(e) => {
                  e.stopPropagation();
                  p.remove(c.id);
                }}
              >
                <DeleteOutlineIcon fontSize="small" />
              </IconButton>
            </ListItemButton>
          )),
        ])}
    </List>
  );
}

// ---- A: drawer over the chat ------------------------------------------------------------------

function VariantA() {
  const p = useProtoHistory();
  const [open, setOpen] = useState(false);
  return (
    <Box sx={{ position: "relative", display: "flex", flexDirection: "column", flex: 1, minHeight: 0 }}>
      <Stack direction="row" spacing={0.5} sx={{ alignItems: "center", px: 1, py: 0.5, borderBottom: 1, borderColor: "divider" }}>
        <Tooltip title="Conversation history">
          <IconButton aria-label="Conversation history" onClick={() => setOpen(true)}>
            <HistoryIcon fontSize="small" />
          </IconButton>
        </Tooltip>
        <Typography variant="body2" noWrap sx={{ flex: 1, minWidth: 0 }}>
          {p.active?.title ?? "New conversation"}
        </Typography>
        <Tooltip title="New conversation">
          <IconButton aria-label="New conversation" onClick={() => p.setActiveId(null)}>
            <AddIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      </Stack>
      <Transcript p={p} />
      {open && (
        <Paper
          sx={{ position: "absolute", inset: 0, zIndex: 2, display: "flex", flexDirection: "column", borderRadius: 0 }}
        >
          <Stack direction="row" sx={{ alignItems: "center", px: 1.5, py: 0.5 }}>
            <Typography variant="subtitle2" sx={{ flex: 1 }}>
              Conversation history
            </Typography>
            <IconButton aria-label="Close history" onClick={() => setOpen(false)}>
              <CloseIcon fontSize="small" />
            </IconButton>
          </Stack>
          <Divider />
          <Box sx={{ flex: 1, overflowY: "auto" }}>
            <GroupedList
              p={p}
              onPick={(id) => {
                p.setActiveId(id);
                setOpen(false);
              }}
            />
          </Box>
          <Divider />
          <Stack spacing={0.5} sx={{ p: 1.5 }}>
            <Typography variant="caption" sx={{ color: "text.secondary" }}>
              {RETENTION_NOTE}
            </Typography>
            <Button color="error" disabled={!p.conversations.length} onClick={p.removeAll} sx={{ alignSelf: "flex-start" }}>
              Delete all conversations
            </Button>
          </Stack>
        </Paper>
      )}
    </Box>
  );
}

// ---- B: permanent split list ------------------------------------------------------------------

function VariantB() {
  const p = useProtoHistory();
  const [menu, setMenu] = useState<HTMLElement | null>(null);
  return (
    <Box sx={{ display: "flex", flex: 1, minHeight: 0 }}>
      <Box sx={{ width: "38%", minWidth: 150, borderRight: 1, borderColor: "divider", display: "flex", flexDirection: "column" }}>
        <Stack direction="row" sx={{ alignItems: "center", px: 1, py: 0.5 }}>
          <Button startIcon={<AddIcon />} onClick={() => p.setActiveId(null)} sx={{ flex: 1, justifyContent: "flex-start" }}>
            New
          </Button>
          <IconButton aria-label="History options" onClick={(e) => setMenu(e.currentTarget)}>
            <MoreVertIcon fontSize="small" />
          </IconButton>
          <Menu anchorEl={menu} open={!!menu} onClose={() => setMenu(null)}>
            <MenuItem
              disabled={!p.conversations.length}
              onClick={() => {
                p.removeAll();
                setMenu(null);
              }}
              sx={{ color: "error.main" }}
            >
              Delete all conversations
            </MenuItem>
          </Menu>
        </Stack>
        <Divider />
        <Box sx={{ flex: 1, overflowY: "auto" }}>
          <GroupedList p={p} onPick={p.setActiveId} dense />
        </Box>
        <Typography variant="caption" sx={{ color: "text.secondary", p: 1, borderTop: 1, borderColor: "divider" }}>
          {RETENTION_NOTE}
        </Typography>
      </Box>
      <Transcript p={p} />
    </Box>
  );
}

// ---- C: header switcher + manage dialog ---------------------------------------------------------

function VariantC() {
  const p = useProtoHistory();
  const [menu, setMenu] = useState<HTMLElement | null>(null);
  const [manage, setManage] = useState(false);
  return (
    <Box sx={{ display: "flex", flexDirection: "column", flex: 1, minHeight: 0 }}>
      <Stack direction="row" spacing={0.5} sx={{ alignItems: "center", px: 1, py: 0.5, borderBottom: 1, borderColor: "divider" }}>
        <Button
          color="inherit"
          endIcon={<ArrowDropDownIcon />}
          onClick={(e) => setMenu(e.currentTarget)}
          sx={{ flex: 1, minWidth: 0, justifyContent: "space-between", textTransform: "none" }}
        >
          <Box component="span" sx={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
            {p.active?.title ?? "New conversation"}
          </Box>
        </Button>
        <Tooltip title="New conversation">
          <IconButton aria-label="New conversation" onClick={() => p.setActiveId(null)}>
            <AddIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      </Stack>
      <Menu anchorEl={menu} open={!!menu} onClose={() => setMenu(null)} slotProps={{ paper: { sx: { maxWidth: 360 } } }}>
        {p.conversations.slice(0, 5).map((c) => (
          <MenuItem
            key={c.id}
            selected={c.id === p.activeId}
            onClick={() => {
              p.setActiveId(c.id);
              setMenu(null);
            }}
          >
            <ListItemText primary={c.title} secondary={relative(c.lastActiveAt)} slotProps={{ primary: { noWrap: true } }} />
          </MenuItem>
        ))}
        <Divider />
        <MenuItem
          onClick={() => {
            setManage(true);
            setMenu(null);
          }}
        >
          All conversations…
        </MenuItem>
      </Menu>
      <Transcript p={p} />
      <Dialog open={manage} onClose={() => setManage(false)} fullWidth maxWidth="sm">
        <DialogTitle>Your conversations</DialogTitle>
        <DialogContent>
          <Typography variant="body2" sx={{ color: "text.secondary", mb: 1 }}>
            {RETENTION_NOTE}
          </Typography>
          <Table>
            <TableHead>
              <TableRow>
                <TableCell>Title</TableCell>
                <TableCell>Last active</TableCell>
                <TableCell>Deleted in</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {p.conversations.map((c) => (
                <TableRow key={c.id} hover>
                  <TableCell
                    sx={{ maxWidth: 220, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", cursor: "pointer" }}
                    onClick={() => {
                      p.setActiveId(c.id);
                      setManage(false);
                    }}
                  >
                    {c.title}
                  </TableCell>
                  <TableCell>{relative(c.lastActiveAt)}</TableCell>
                  <TableCell>{daysLeft(c)} d</TableCell>
                  <TableCell>
                    <IconButton size="small" aria-label={`Delete conversation "${c.title}"`} onClick={() => p.remove(c.id)}>
                      <DeleteOutlineIcon fontSize="small" />
                    </IconButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </DialogContent>
        <DialogActions>
          <Button color="error" disabled={!p.conversations.length} onClick={p.removeAll}>
            Delete all
          </Button>
          <Button onClick={() => setManage(false)}>Close</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}

// ---- switcher -----------------------------------------------------------------------------------

const VARIANTS = [
  { key: "A", name: "Drawer over the chat", C: VariantA },
  { key: "B", name: "Split list beside the transcript", C: VariantB },
  { key: "C", name: "Header switcher + manage dialog", C: VariantC },
] as const;

function PrototypeSwitcher({ current, onChange }: { current: number; onChange: (i: number) => void }) {
  if (import.meta.env.PROD) return null;
  const v = VARIANTS[current];
  return (
    <Paper
      elevation={8}
      sx={{
        position: "fixed",
        bottom: 16,
        left: "50%",
        transform: "translateX(-50%)",
        zIndex: 2000,
        px: 1,
        py: 0.5,
        borderRadius: 999,
        bgcolor: "text.primary",
        color: "background.paper",
        display: "flex",
        alignItems: "center",
        gap: 1,
      }}
    >
      <IconButton size="small" sx={{ color: "inherit" }} aria-label="Previous variant" onClick={() => onChange((current + VARIANTS.length - 1) % VARIANTS.length)}>
        ‹
      </IconButton>
      <Typography variant="body2" sx={{ fontWeight: 600 }}>
        PROTOTYPE {v.key} — {v.name}
      </Typography>
      <IconButton size="small" sx={{ color: "inherit" }} aria-label="Next variant" onClick={() => onChange((current + 1) % VARIANTS.length)}>
        ›
      </IconButton>
    </Paper>
  );
}

export function useConversationHistoryPrototype(): boolean {
  const [params] = useSearchParams();
  return params.get("proto") === "history";
}

export function ConversationHistoryPrototype() {
  const [params, setParams] = useSearchParams();
  const index = Math.max(0, VARIANTS.findIndex((v) => v.key === (params.get("variant") ?? "A")));
  const Variant = VARIANTS[index].C;
  const go = (i: number) =>
    setParams((p) => {
      p.set("variant", VARIANTS[i].key);
      return p;
    }, { replace: true });

  // ←/→ cycle, except while typing.
  useArrowKeys(index, go);

  return (
    <>
      <Variant key={VARIANTS[index].key} />
      <PrototypeSwitcher current={index} onChange={go} />
    </>
  );
}


function useArrowKeys(index: number, go: (i: number) => void) {
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      const t = e.target as HTMLElement | null;
      if (t && (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.isContentEditable)) return;
      if (e.key === "ArrowLeft") go((index + VARIANTS.length - 1) % VARIANTS.length);
      if (e.key === "ArrowRight") go((index + 1) % VARIANTS.length);
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [index, go]);
}
