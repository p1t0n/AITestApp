import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import {
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  FormControl,
  FormControlLabel,
  FormGroup,
  IconButton,
  InputAdornment,
  InputLabel,
  LinearProgress,
  Menu,
  MenuItem,
  Paper,
  Radio,
  RadioGroup,
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
import DeleteIcon from "@mui/icons-material/Delete";
import DescriptionIcon from "@mui/icons-material/Description";
import FilterListIcon from "@mui/icons-material/FilterList";
import SearchIcon from "@mui/icons-material/Search";
import {
  ROSTER_BANDS,
  ROSTER_DEFAULTS,
  ROSTER_SORTS,
  ROSTER_STATUSES,
  useCreateExpert,
  useDeleteExpert,
  useExpert,
  useRosterPage,
  useUpdateExpert,
  type RosterBand,
  type RosterDir,
  type RosterFacetCount,
  type RosterQuery,
  type RosterSort,
  type RosterStatusFilter,
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

function isBand(value: string | null): value is RosterBand {
  return ROSTER_BANDS.includes(value as RosterBand);
}

/** The availability facet's labels, spelling out the boundaries the server splits on. */
const BAND_LABELS: Record<RosterBand, string> = {
  full: "Full (100%)",
  partial: "Partial (1–99%)",
  none: "Unavailable (0%)",
};

/**
 * The view, read out of the URL — which is where it lives, so a reload or a pasted link reproduces
 * the same page (EXP-45, EXP-47). Anything unreadable falls back to the default rather than being
 * sent to the server to be refused: a stale bookmark should show the roster, not an error.
 */
function queryFromUrl(params: URLSearchParams): RosterQuery {
  const page = Number(params.get("page"));
  const sort = params.get("sort");
  const dir = params.get("dir");
  const band = params.get("band");
  return {
    q: params.get("q") ?? ROSTER_DEFAULTS.q,
    // A status the server would refuse is dropped here rather than forwarded, for the same reason
    // an unreadable sort key is: a link somebody edited by hand should show a roster.
    statuses: params
      .getAll("status")
      .filter((s): s is RosterStatusFilter => ROSTER_STATUSES.includes(s as RosterStatusFilter)),
    // Locations are free text off the roster itself, so there is nothing to check them against —
    // one nobody is in shows an empty roster with the checkbox still there to untick.
    locations: params.getAll("location").filter(Boolean),
    band: isBand(band) ? band : ROSTER_DEFAULTS.band,
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
  // Repeated keys rather than one comma-joined value: a location may legitimately contain a comma,
  // and splitting it back apart would invent two places nobody is in.
  for (const status of query.statuses) params.append("status", status);
  for (const location of query.locations) params.append("location", location);
  if (query.band) params.set("band", query.band);
  if (query.sort !== ROSTER_DEFAULTS.sort) params.set("sort", query.sort);
  if (query.dir !== ROSTER_DEFAULTS.dir) params.set("dir", query.dir);
  if (query.page !== ROSTER_DEFAULTS.page) params.set("page", String(query.page));
  return params;
}

/** Checked → unchecked and back, without caring which it was. */
function toggle<T>(list: readonly T[], value: T): T[] {
  return list.includes(value) ? list.filter((v) => v !== value) : [...list, value];
}

function facetCount(facet: RosterFacetCount[] | undefined, value: string): number {
  return facet?.find((f) => f.value === value)?.count ?? 0;
}

/**
 * One labelled group in the sidebar. A real `group` with a name, not a heading over a div: a
 * screen reader reaching the eighth checkbox in the Location list has to be able to say which
 * question it answers, and "Active" and "Full (100%)" are only unambiguous inside their own group.
 */
function Facet({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Box role="group" aria-label={title} sx={{ mt: 2 }}>
      <Typography variant="overline" color="text.secondary" component="h3">
        {title}
      </Typography>
      {children}
    </Box>
  );
}

/**
 * Debounced text → server param: typing stays instant, the request waits for a pause.
 *
 * The second element skips the wait. **Reset filters** needs it: without a flush the box clears
 * and the roster keeps showing the old search for another 300ms, because the pending debounce is
 * still the value the request is built from and it is about to fire anyway.
 */
function useDebounced<T>(value: T, ms = 300): [T, (value: T) => void] {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return [settled, setSettled];
}

export default function ExpertsPage() {
  const [params, setParams] = useSearchParams();
  const query = useMemo(() => queryFromUrl(params), [params]);

  // The box is local and the URL is not: a keystroke must not push a history entry, and the
  // request must not fire on every letter. The debounced value is what reaches both.
  const [search, setSearch] = useState(query.q);
  const [debounced, flushSearch] = useDebounced(search);
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
  // Any filter reshapes the whole match, so the page number from the old one points at rows
  // nobody was looking at. Every one of them goes back to the first page.
  const filter = (patch: Partial<RosterQuery>) => go({ ...patch, page: 1 });
  const facets = data?.facets;
  const filtered =
    !!view.q || view.statuses.length > 0 || view.locations.length > 0 || view.band !== null;

  const reset = () => {
    setSearch("");
    flushSearch("");
    // Claim the write before the effect below can: it reacts to the debounced value changing and
    // would answer this one by editing the *old* URL — deleting `q` from it and leaving every
    // filter the reset had just cleared back in place.
    settled.current = "";
    setParams(urlFromQuery(ROSTER_DEFAULTS));
  };

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

          {/* Every count below is the server's, and every one of them is computed against the
              *other* groups rather than its own (EXP-47) — so "Draft (2)" beside a roster already
              narrowed to Active says what ticking Draft as well would add. Counted its own way it
              would read 0 for every unchecked box and tell nobody anything. */}
          <Facet title="Status">
            <FormGroup>
              {ROSTER_STATUSES.map((status) => (
                <FormControlLabel
                  key={status}
                  label={`${status} (${facetCount(facets?.status, status)})`}
                  control={
                    <Checkbox
                      size="small"
                      checked={view.statuses.includes(status)}
                      onChange={() => filter({ statuses: toggle(view.statuses, status) })}
                    />
                  }
                />
              ))}
            </FormGroup>
          </Facet>

          <Facet title="Availability today">
            <RadioGroup
              value={view.band ?? ""}
              onChange={(e) => filter({ band: (e.target.value || null) as RosterBand | null })}
            >
              <FormControlLabel value="" control={<Radio size="small" />} label="Any" />
              {ROSTER_BANDS.map((band) => (
                <FormControlLabel
                  key={band}
                  value={band}
                  control={<Radio size="small" />}
                  label={`${BAND_LABELS[band]} (${facetCount(facets?.band, band)})`}
                />
              ))}
            </RadioGroup>
          </Facet>

          <Facet title="Location">
            {facets?.location.length === 0 && (
              <Typography variant="body2" color="text.secondary">
                No locations on record.
              </Typography>
            )}
            {/* The server orders these, busiest first. A zero is greyed out rather than dropped:
                a checkbox that vanishes at zero cannot be unchecked back into view — which matters
                most for one that is still ticked, and is therefore still narrowing the roster. */}
            <FormGroup sx={{ maxHeight: 260, overflow: "auto", flexWrap: "nowrap" }}>
              {facets?.location.map(({ value, count }) => (
                <FormControlLabel
                  key={value}
                  label={`${value} (${count})`}
                  disabled={count === 0 && !view.locations.includes(value)}
                  control={
                    <Checkbox
                      size="small"
                      checked={view.locations.includes(value)}
                      onChange={() => filter({ locations: toggle(view.locations, value) })}
                    />
                  }
                />
              ))}
            </FormGroup>
          </Facet>

          <Button
            size="small"
            startIcon={<FilterListIcon />}
            disabled={!filtered}
            onClick={reset}
            sx={{ mt: 2 }}
          >
            Reset filters
          </Button>
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
