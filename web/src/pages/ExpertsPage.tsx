import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import {
  Box,
  Button,
  Chip,
  CircularProgress,
  FormControl,
  IconButton,
  InputAdornment,
  InputLabel,
  LinearProgress,
  Menu,
  MenuItem,
  Paper,
  Select,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import FilterAltIcon from "@mui/icons-material/FilterAlt";
import MoreVertIcon from "@mui/icons-material/MoreVert";
import SearchIcon from "@mui/icons-material/Search";
import {
  ROSTER_DEFAULTS,
  ROSTER_SORTS,
  useCreateExpert,
  useDeleteExpert,
  useExpert,
  useRosterPage,
  useUpdateExpert,
  type RosterDir,
  type RosterQuery,
  type RosterSort,
} from "../api";
import type { ExpertSummary } from "../types";
import PageHeader, { PageContainer } from "../components/PageHeader";
import ExpertFormDialog from "./ExpertFormDialog";

function capacityColor(pct: number): "success" | "warning" | "default" {
  if (pct >= 100) return "success";
  if (pct > 0) return "warning";
  return "default";
}

/**
 * The Sort by dropdown, as pairs — a single control rather than a key and a direction, because
 * "Most available" is one thought and `capacity` + `desc` is two. The values are exactly what the
 * server accepts, so the option a person picked is the query string they can then share.
 */
const SORT_OPTIONS: { value: string; label: string }[] = [
  { value: "name:asc", label: "Name A→Z" },
  { value: "name:desc", label: "Name Z→A" },
  { value: "capacity:desc", label: "Most available" },
  { value: "capacity:asc", label: "Least available" },
  { value: "location:asc", label: "Location" },
  { value: "title:asc", label: "Title" },
  { value: "status:asc", label: "Status" },
];

function isSort(value: string | null): value is RosterSort {
  return ROSTER_SORTS.includes(value as RosterSort);
}

/**
 * The view, read out of the URL — which is where it lives, so a reload or a pasted link reproduces
 * the same page (EXP-45). Anything unreadable falls back to the default rather than being sent to
 * the server to be refused: a stale bookmark should show the roster, not an error.
 */
function queryFromUrl(params: URLSearchParams): RosterQuery {
  const page = Number(params.get("page"));
  const sort = params.get("sort");
  const dir = params.get("dir");
  return {
    q: params.get("q") ?? ROSTER_DEFAULTS.q,
    sort: isSort(sort) ? sort : ROSTER_DEFAULTS.sort,
    dir: dir === "desc" ? "desc" : ROSTER_DEFAULTS.dir,
    page: Number.isInteger(page) && page >= 1 ? page : ROSTER_DEFAULTS.page,
    pageSize: ROSTER_DEFAULTS.pageSize,
  };
}

/**
 * And back again, writing only what differs from the default. A URL that spells out every default
 * is one nobody can read and one that changes shape the day a default moves; `/experts` on its own
 * has to keep meaning "the first page, by name".
 */
function urlFromQuery(query: RosterQuery): URLSearchParams {
  const params = new URLSearchParams();
  if (query.q) params.set("q", query.q);
  if (query.sort !== ROSTER_DEFAULTS.sort) params.set("sort", query.sort);
  if (query.dir !== ROSTER_DEFAULTS.dir) params.set("dir", query.dir);
  if (query.page !== ROSTER_DEFAULTS.page) params.set("page", String(query.page));
  return params;
}

/** Debounced text → server param: typing stays instant, the request waits for a pause. */
function useDebounced<T>(value: T, ms = 300): T {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return settled;
}

export default function ExpertsPage() {
  const [params, setParams] = useSearchParams();
  const query = useMemo(() => queryFromUrl(params), [params]);

  // The box is local and the URL is not: a keystroke must not push a history entry, and the
  // request must not fire on every letter. The debounced value is what reaches both.
  const [search, setSearch] = useState(query.q);
  const debounced = useDebounced(search);
  // The last value the two agreed on, so each effect below can tell "I did that" from "something
  // else did". Without it they push each other round in a circle.
  const settled = useRef(query.q);

  useEffect(() => {
    if (debounced === settled.current) return;
    settled.current = debounced;
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (debounced) next.set("q", debounced);
        else next.delete("q");
        // A new search is a new result set, so the page number from the old one means nothing.
        next.delete("page");
        return next;
      },
      // Replace, not push: thirty keystrokes must not cost thirty presses of the back button.
      { replace: true },
    );
  }, [debounced, setParams]);

  // And the other direction — back, forward, or a link somebody pasted moved the URL without us.
  useEffect(() => {
    if (query.q === settled.current) return;
    settled.current = query.q;
    setSearch(query.q);
  }, [query.q]);

  // What the page is actually showing. For the one render between the debounce firing and the URL
  // catching up, the URL still carries the old page number — and asking the server for page 3 of a
  // search that has only just changed is a request for rows nobody wants. The new search is page 1
  // here, immediately, and the effect above then writes exactly that into the URL.
  const view: RosterQuery =
    debounced === query.q ? query : { ...query, q: debounced, page: 1 };

  const { data, isLoading, isFetching } = useRosterPage(view);
  const create = useCreateExpert();
  const navigate = useNavigate();
  const [dialogOpen, setDialogOpen] = useState(false);

  // The open ⋮ menu: which row it belongs to, and the button it hangs off. One menu for the whole
  // table rather than one per row — twenty-five mounted menus to show at most one.
  const [menu, setMenu] = useState<{ row: ExpertSummary; anchor: HTMLElement } | null>(null);
  // The row being edited, by id. The dialog fetches the whole record for itself; the row only
  // carries a summary, and a PUT built from a summary would blank everything it does not show.
  const [editingId, setEditingId] = useState<string | null>(null);

  // The refine box (EXP-50) — deliberately *not* in the URL and deliberately not debounced. It
  // narrows the rows already on screen and nothing else, so there is no request to wait for and
  // nothing worth putting in a shared link: the link would reproduce a page, not a result.
  const [refine, setRefine] = useState("");
  const needle = refine.trim().toLowerCase();
  const rows = data?.items ?? [];
  const shown = needle
    ? rows.filter((e) =>
        `${e.firstName} ${e.lastName} ${e.title} ${e.location ?? ""} ${e.email}`
          .toLowerCase()
          .includes(needle),
      )
    : rows;

  const total = data?.total ?? 0;
  const from = total === 0 ? 0 : (view.page - 1) * view.pageSize + 1;
  const to = Math.min(view.page * view.pageSize, total);
  const go = (patch: Partial<RosterQuery>) => setParams(urlFromQuery({ ...view, ...patch }));

  /** Run a menu item's job and put the menu away — every item wants both. */
  const fromMenu = (run: (row: ExpertSummary) => void) => () => {
    const row = menu!.row;
    setMenu(null);
    run(row);
  };

  // Deliberately still an early return rather than a spinner *under* the header: the e2e capture
  // waits for `New expert` to decide the roster has arrived, and a header that renders while the
  // table is empty would hand it a screenshot of a spinner (`manuals/spa-design-system.md` §10).
  // `keepPreviousData` keeps this to the very first fetch — a sort or a page turn never lands here.
  if (isLoading)
    return (
      <PageContainer width="wide">
        <CircularProgress />
      </PageContainer>
    );

  return (
    <PageHeader
      title="Experts"
      // The roster is nine columns wide at its widest and reads better the more of them fit.
      width="wide"
      actions={
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setDialogOpen(true)}>
          New expert
        </Button>
      }
    >
      <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ alignItems: "flex-start" }}>
        <Paper
          component="search"
          sx={{ width: { xs: "100%", md: 260 }, flexShrink: 0, p: 2 }}
        >
          <TextField
            size="small"
            fullWidth
            label="Search the roster"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            slotProps={{
              input: {
                startAdornment: (
                  <InputAdornment position="start">
                    <SearchIcon fontSize="small" />
                  </InputAdornment>
                ),
              },
            }}
          />
          <Typography variant="caption" color="text.secondary" sx={{ display: "block", mt: 1 }}>
            Matches a name, an email or a title.
          </Typography>
        </Paper>

        <Box sx={{ flex: 1, minWidth: 0, width: "100%" }}>
          <Stack direction="row" spacing={2} sx={{ alignItems: "center", mb: 1.5 }}>
            <Typography variant="h6" component="h2">
              {total} {total === 1 ? "expert" : "experts"}
            </Typography>
            <Box sx={{ flex: 1 }} />
            <TextField
              size="small"
              label="Refine these results"
              value={refine}
              onChange={(e) => setRefine(e.target.value)}
              sx={{ minWidth: 220 }}
              slotProps={{
                input: {
                  startAdornment: (
                    <InputAdornment position="start">
                      <FilterAltIcon fontSize="small" />
                    </InputAdornment>
                  ),
                },
              }}
              // Said at the control, not only in the count below it: somebody who types a name here
              // and sees nothing has to be able to tell "not on the roster" from "not on this page".
              helperText="This page only"
            />
            <FormControl size="small" sx={{ minWidth: 180 }}>
              <InputLabel id="roster-sort-label">Sort by</InputLabel>
              <Select
                labelId="roster-sort-label"
                label="Sort by"
                value={`${view.sort}:${view.dir}`}
                onChange={(e) => {
                  const [sort, dir] = String(e.target.value).split(":");
                  // A re-sort reorders the whole match, so the old page number points at rows the
                  // person was not looking at. Back to the first page.
                  go({ sort: sort as RosterSort, dir: dir as RosterDir, page: 1 });
                }}
              >
                {SORT_OPTIONS.map((option) => (
                  <MenuItem key={option.value} value={option.value}>
                    {option.label}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
          </Stack>

          <Paper>
            {/* A four-pixel band, present either way: a bar that appears and disappears would move
                the whole table down and back on every page turn. */}
            {isFetching ? <LinearProgress /> : <Box sx={{ height: 4 }} />}
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Name</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell>Availability (today)</TableCell>
                  <TableCell align="right">Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {shown.map((e) => (
                  <TableRow
                    key={e.id}
                    hover
                    sx={{ cursor: "pointer" }}
                    onClick={() => navigate(`/experts/${e.id}`)}
                  >
                    <TableCell>
                      <Typography variant="body2" sx={{ fontWeight: 600 }}>
                        {e.firstName} {e.lastName}
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        {e.title} · {e.location ?? "—"}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      {/* Staged by the ingest agent and not yet published (EXP-49). Marked the same
                          way a pause is, because both say the same kind of thing: this person is on
                          the roster and not on the bench. */}
                      {e.status === "Draft" && (
                        <Chip
                          label="Draft"
                          size="small"
                          variant="outlined"
                          title="Staged by the resume ingest agent — open the record to review and publish it."
                        />
                      )}
                      {/* Seen and marked, never dropped (P1T-185): staff must be able to tell
                          somebody who paused themselves from somebody who was never here. */}
                      {e.hiddenAt && (
                        <Chip
                          label="Paused"
                          size="small"
                          variant="outlined"
                          sx={{ ml: e.status === "Draft" ? 1 : 0 }}
                          title="This person paused themselves — they are not being offered for work."
                        />
                      )}
                      {e.status === "Active" && !e.hiddenAt && (
                        <Chip label="Active" size="small" variant="outlined" color="primary" />
                      )}
                    </TableCell>
                    <TableCell>
                      <Chip
                        label={`${e.currentCapacityPercent}%`}
                        size="small"
                        color={capacityColor(e.currentCapacityPercent)}
                      />
                    </TableCell>
                    {/* Nothing on the row writes (EXP-50). Every action is named, in a menu that
                        has to be opened on purpose — including the two that only navigate, so the
                        row offers one target rather than a bank of icons to mis-aim at. */}
                    <TableCell align="right" onClick={(ev) => ev.stopPropagation()}>
                      <IconButton
                        aria-label={`Actions for ${e.firstName} ${e.lastName}`}
                        title="Actions"
                        onClick={(ev) => setMenu({ row: e, anchor: ev.currentTarget })}
                      >
                        <MoreVertIcon />
                      </IconButton>
                    </TableCell>
                  </TableRow>
                ))}
                {shown.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={4}>
                      {rows.length === 0
                        ? "No experts match."
                        : "No rows on this page match the refine."}
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>

            <Stack direction="row" spacing={1} sx={{ alignItems: "center", p: 1.5 }}>
              <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }}>
                Showing {from}–{to} of {total}
                {needle && ` · ${shown.length} after refine`}
              </Typography>
              <Button
                size="small"
                disabled={view.page <= 1}
                onClick={() => go({ page: view.page - 1 })}
              >
                ‹ Prev
              </Button>
              <Button
                size="small"
                disabled={to >= total}
                onClick={() => go({ page: view.page + 1 })}
              >
                Next ›
              </Button>
            </Stack>
          </Paper>
        </Box>
      </Stack>

      <Menu
        anchorEl={menu?.anchor ?? null}
        open={menu !== null}
        onClose={() => setMenu(null)}
        anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
        transformOrigin={{ vertical: "top", horizontal: "right" }}
      >
        <MenuItem onClick={fromMenu((row) => navigate(`/experts/${row.id}`))}>Open</MenuItem>
        <MenuItem onClick={fromMenu((row) => navigate(`/experts/${row.id}/cv`))}>View CV</MenuItem>
        <MenuItem onClick={fromMenu((row) => setEditingId(row.id))}>Edit…</MenuItem>
      </Menu>

      <ExpertFormDialog
        open={dialogOpen}
        title="New expert"
        onClose={() => setDialogOpen(false)}
        onSave={(dto) => create.mutateAsync(dto)}
      />

      {editingId && <RosterEditDialog id={editingId} onClose={() => setEditingId(null)} />}
    </PageHeader>
  );
}

/**
 * Editing one roster row, in the popup (EXP-50).
 *
 * It fetches the whole record before it renders anything. The row carries an `ExpertSummary` —
 * name, title, location, status, capacity — and `PUT /experts/{id}` replaces the record, so a form
 * prefilled from the row alone would save the phone, summary and photo away as blank. Nothing on
 * screen would say it had.
 *
 * Delete lives in here rather than on the row, behind the form's own confirmation.
 */
function RosterEditDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const { data: e } = useExpert(id);
  const update = useUpdateExpert(id);
  const del = useDeleteExpert();

  if (!e) return null;

  return (
    <ExpertFormDialog
      open
      title={`Edit ${e.firstName} ${e.lastName}`}
      initial={{
        firstName: e.firstName,
        lastName: e.lastName,
        title: e.title,
        email: e.email,
        phone: e.phone,
        location: e.location,
        summary: e.summary,
        photoUrl: e.photoUrl,
      }}
      onClose={onClose}
      onSave={(dto) => update.mutateAsync(dto)}
      deleteSubject={`${e.firstName} ${e.lastName}`}
      onDelete={() => del.mutateAsync(id)}
    />
  );
}
