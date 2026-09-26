// The Roster Q&A history drawer (EXP-34, ADR §7).
//
// An overlay **inside the surface**, not a MUI `Drawer` on the page: the dock is 360px at its
// narrowest, and the prototype's split-list variant was dropped precisely because a permanent list
// beside the transcript cut titles to about twelve characters. Covering the transcript for as long
// as someone is choosing costs nothing — they are not reading it while they pick — and gives the
// list the panel's full width.
//
// It owns the three history mutations because it is the only place they can be reached from. What
// it does *not* own is which conversation is open: that is the surface's state, and the drawer
// reports a choice rather than making one.
import { useState } from "react";
import {
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  List,
  ListItem,
  ListItemButton,
  ListItemText,
  Paper,
  Stack,
  Typography,
} from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutlined";
import {
  useDeleteAllRosterQaConversations,
  useDeleteRosterQaConversation,
  useRosterQaConversations,
} from "../../api";
import {
  HISTORY_GROUPS,
  RETENTION_NOTE,
  daysUntil,
  groupOf,
  isExpiringSoon,
  relativeTime,
} from "./conversationHistory";

/** The class the row's delete control is reached by, so the reveal is one rule on the row rather
 * than per-control state. A class, not a `data-testid`: the suite names this button by its
 * accessible name, which is the thing §9 of the design-system manual freezes. */
const DELETE_CLASS = "conversation-delete";

/**
 * "disappears in N d", and only in the last fortnight. A countdown on every row would be noise for
 * five and a half of the six months, and `warning` is the colour precisely because the row is
 * still there — nothing has gone wrong yet, and the person has time to act.
 */
function ExpiryHint({ expiresAt }: { expiresAt: string }) {
  if (!isExpiringSoon(expiresAt)) return null;
  return (
    <Typography component="span" variant="caption" sx={{ color: "warning.main" }}>
      {" · "}disappears in {daysUntil(expiresAt)} d
    </Typography>
  );
}

interface Props {
  /** The conversation the surface is showing, so the drawer can mark it. */
  activeId: string | null;
  onOpen: (id: string) => void;
  onClose: () => void;
  /** Told about a delete so the surface can step off a conversation that no longer exists. */
  onDeleted: (id: string) => void;
}

export function ConversationHistoryDrawer({ activeId, onOpen, onClose, onDeleted }: Props) {
  const { data, isLoading } = useRosterQaConversations();
  const deleteOne = useDeleteRosterQaConversation();
  const deleteAll = useDeleteAllRosterQaConversations();
  const [confirmingDeleteAll, setConfirmingDeleteAll] = useState(false);
  const conversations = data ?? [];

  return (
    <Paper
      component="section"
      aria-label="Conversation history"
      elevation={4}
      sx={{
        position: "absolute",
        inset: 0,
        zIndex: 2,
        display: "flex",
        flexDirection: "column",
        borderRadius: 0,
      }}
    >
      <Stack
        direction="row"
        sx={{ alignItems: "center", px: 1.5, py: 0.5, borderBottom: 1, borderColor: "divider" }}
      >
        <Typography variant="subtitle2" component="h2" sx={{ flex: 1, minWidth: 0 }}>
          Conversation history
        </Typography>
        <IconButton aria-label="Close history" onClick={onClose}>
          <CloseIcon fontSize="small" />
        </IconButton>
      </Stack>

      <Box sx={{ flex: 1, overflowY: "auto" }}>
        {isLoading && (
          <Typography variant="body2" sx={{ color: "text.secondary", p: 2 }}>
            Loading your conversations…
          </Typography>
        )}
        {!isLoading && conversations.length === 0 && (
          <Typography variant="body2" sx={{ color: "text.secondary", p: 2 }}>
            No past conversations yet.
          </Typography>
        )}
        {/* Grouped rather than a flat list of dates: the question a person opens this with is
            "the one from this morning" or "the one from last week", and a heading answers it
            without them reading a single timestamp. A group nothing falls into is not drawn —
            three empty headings are three claims that there is nothing there, made three times. */}
        {HISTORY_GROUPS.map((group) => {
          const rows = conversations.filter((c) => groupOf(c.lastActiveAt) === group);
          if (rows.length === 0) return null;
          return (
            <Box key={group}>
              <Typography
                variant="overline"
                component="h3"
                sx={{ display: "block", px: 2, pt: 1.5, color: "text.secondary" }}
              >
                {group}
              </Typography>
              <List dense disablePadding>
                {rows.map((c) => (
                  <ListItem
                    key={c.id}
                    disablePadding
                    // The delete control is a *sibling* of the row button, not a child of it: a
                    // button inside a button is neither valid HTML nor reachable as two things by
                    // a keyboard. `secondaryAction` is MUI's own shape for exactly this.
                    //
                    // Hidden until hover **or focus**. Focus is the half that is usually forgotten
                    // and the half that matters: an opacity-0 control that only a pointer can
                    // reveal is a control a keyboard user cannot see they have.
                    secondaryAction={
                      <IconButton
                        className={DELETE_CLASS}
                        size="small"
                        aria-label={`Delete conversation "${c.title}"`}
                        onClick={() => {
                          deleteOne.mutate(c.id);
                          onDeleted(c.id);
                        }}
                      >
                        <DeleteOutlineIcon fontSize="small" />
                      </IconButton>
                    }
                    sx={{
                      [`& .${DELETE_CLASS}`]: { opacity: 0, transition: "opacity 150ms" },
                      [`&:hover .${DELETE_CLASS}, & .${DELETE_CLASS}:focus-visible`]: { opacity: 1 },
                    }}
                  >
                    <ListItemButton selected={c.id === activeId} onClick={() => onOpen(c.id)}>
                      <ListItemText
                        primary={c.title}
                        secondary={
                          <>
                            {relativeTime(c.lastActiveAt)}
                            <ExpiryHint expiresAt={c.expiresAt} />
                          </>
                        }
                        slotProps={{
                          primary: { noWrap: true, variant: "body2" },
                          secondary: { variant: "caption" },
                        }}
                      />
                    </ListItemButton>
                  </ListItem>
                ))}
              </List>
            </Box>
          );
        })}
      </Box>

      {/* The retention promise sits next to the only control that can pre-empt it, which is the
          one place a sentence about deletion is worth reading. */}
      <Stack spacing={0.5} sx={{ p: 1.5, borderTop: 1, borderColor: "divider" }}>
        <Typography variant="caption" sx={{ color: "text.secondary" }}>
          {RETENTION_NOTE}
        </Typography>
        <Button
          color="error"
          disabled={conversations.length === 0}
          onClick={() => setConfirmingDeleteAll(true)}
          sx={{ alignSelf: "flex-start" }}
        >
          Delete all conversations
        </Button>
      </Stack>

      {/* One row is recoverable by asking again; everything is not. That asymmetry is the whole
          reason the per-row delete has no confirmation and this one does (ADR §5). */}
      <Dialog
        open={confirmingDeleteAll}
        onClose={() => setConfirmingDeleteAll(false)}
        fullWidth
        maxWidth="xs"
      >
        <DialogTitle>Delete all conversations?</DialogTitle>
        <DialogContent>
          <Typography variant="body2">
            All {conversations.length} of your Roster Q&amp;A conversations will be deleted straight
            away. This cannot be undone.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmingDeleteAll(false)}>Cancel</Button>
          <Button
            color="error"
            onClick={() => {
              deleteAll.mutate();
              setConfirmingDeleteAll(false);
            }}
          >
            Delete all
          </Button>
        </DialogActions>
      </Dialog>
    </Paper>
  );
}
