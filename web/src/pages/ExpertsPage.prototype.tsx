// PROTOTYPE — throwaway (branch `prototype/table-conventions`, never main).
//
// Question: what should the app-wide table convention look like — server + client filtering by key
// fields, paging, sorting, and editing only in popups so a stray click mid-journey never changes
// data? Three structurally different answers on the existing roster route, switchable via
// `?variant=A|B|C` (no param = today's page, as the baseline).
//
// Stubs, on purpose:
// - "Server" is `fakeServer` over the roster the page already fetched, behind 350ms of fake latency.
//   GET /api/experts has no paging/filter/sort params yet; that contract is part of what this
//   prototype is meant to settle.
// - Save never mutates. The edit dialog shows what it *would* send.
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useNavigate } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Drawer,
  FormControl,
  FormControlLabel,
  FormGroup,
  IconButton,
  InputAdornment,
  InputLabel,
  LinearProgress,
  ListItemIcon,
  Menu,
  MenuItem,
  Pagination,
  Paper,
  Radio,
  RadioGroup,
  Select,
  Snackbar,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TablePagination,
  TableRow,
  TableSortLabel,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
} from "@mui/material";
import EditIcon from "@mui/icons-material/Edit";
import SearchIcon from "@mui/icons-material/Search";
import FilterListIcon from "@mui/icons-material/FilterList";
import BoltIcon from "@mui/icons-material/Bolt";
import CloudIcon from "@mui/icons-material/Cloud";
import MoreVertIcon from "@mui/icons-material/MoreVert";
import DescriptionIcon from "@mui/icons-material/Description";
import CloseIcon from "@mui/icons-material/Close";
import type { ExpertStatus, ExpertSummary } from "../types";
import PageHeader from "../components/PageHeader";
import PrototypeSwitcher from "../components/PrototypeSwitcher";

// ───────────────────────── fake server ─────────────────────────

type Band = "none" | "partial" | "full";
type SortKey = "name" | "title" | "location" | "capacity" | "status";
type Dir = "asc" | "desc";

interface ServerQuery {
  q: string; // name / email / title contains
  statuses: ExpertStatus[];
  locations: string[];
  band: Band | null;
  sort: SortKey;
  dir: Dir;
  page: number; // 0-based
  size: number;
}

interface ServerPage {
  rows: ExpertSummary[];
  total: number;
  facets: { status: Record<string, number>; location: Record<string, number>; band: Record<Band, number> };
}

const initialQuery: ServerQuery = {
  q: "",
  statuses: [],
  locations: [],
  band: null,
  sort: "name",
  dir: "asc",
  page: 0,
  size: 25,
};

const bandOf = (pct: number): Band => (pct >= 100 ? "full" : pct > 0 ? "partial" : "none");
const bandLabel: Record<Band, string> = { none: "Unavailable (0%)", partial: "Partial (1–99%)", full: "Full (100%)" };
const fullName = (e: ExpertSummary) => `${e.firstName} ${e.lastName}`;

function sortValue(e: ExpertSummary, key: SortKey): string | number {
  switch (key) {
    case "name":
      return `${e.lastName} ${e.firstName}`.toLowerCase();
    case "title":
      return e.title.toLowerCase();
    case "location":
      return (e.location ?? "~").toLowerCase();
    case "capacity":
      return e.currentCapacityPercent;
    case "status":
      return e.status;
  }
}

function fakeServer(all: ExpertSummary[], q: ServerQuery): ServerPage {
  const needle = q.q.trim().toLowerCase();
  const matchQ = (e: ExpertSummary) =>
    !needle || [fullName(e), e.email, e.title].some((s) => s.toLowerCase().includes(needle));
  const matchS = (e: ExpertSummary) => !q.statuses.length || q.statuses.includes(e.status);
  const matchL = (e: ExpertSummary) => !q.locations.length || q.locations.includes(e.location ?? "—");
  const matchB = (e: ExpertSummary) => !q.band || bandOf(e.currentCapacityPercent) === q.band;

  // Facet counts: each facet counts against every *other* active filter (standard faceted search).
  const count = <K extends string>(rows: ExpertSummary[], key: (e: ExpertSummary) => K) =>
    rows.reduce<Record<string, number>>((acc, e) => ((acc[key(e)] = (acc[key(e)] ?? 0) + 1), acc), {});
  const facets = {
    status: count(all.filter((e) => matchQ(e) && matchL(e) && matchB(e)), (e) => e.status),
    location: count(all.filter((e) => matchQ(e) && matchS(e) && matchB(e)), (e) => e.location ?? "—"),
    band: { none: 0, partial: 0, full: 0, ...count(all.filter((e) => matchQ(e) && matchS(e) && matchL(e)), (e) => bandOf(e.currentCapacityPercent)) } as Record<Band, number>,
  };

  const filtered = all.filter((e) => matchQ(e) && matchS(e) && matchL(e) && matchB(e));
  const sign = q.dir === "asc" ? 1 : -1;
  filtered.sort((a, b) => {
    const x = sortValue(a, q.sort);
    const y = sortValue(b, q.sort);
    return (x < y ? -1 : x > y ? 1 : 0) * sign || fullName(a).localeCompare(fullName(b));
  });
  return { rows: filtered.slice(q.page * q.size, (q.page + 1) * q.size), total: filtered.length, facets };
}

function useServerPage(all: ExpertSummary[], query: ServerQuery) {
  return useQuery({
    queryKey: ["prototype-experts", query, all.length],
    queryFn: () => new Promise<ServerPage>((r) => setTimeout(() => r(fakeServer(all, query)), 350)),
    placeholderData: keepPreviousData,
  });
}

/** Debounced text → server param: typing stays instant, the request waits for a pause. */
function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return v;
}

/** Client-side filter: only over the rows already on screen, no round trip. */
function clientFilter(rows: ExpertSummary[], text: string) {
  const n = text.trim().toLowerCase();
  if (!n) return rows;
  return rows.filter((e) =>
    [fullName(e), e.title, e.email, e.location ?? "", e.status, `${e.currentCapacityPercent}%`].some((s) =>
      s.toLowerCase().includes(n),
    ),
  );
}

// ───────────────────────── shared bits (not layout) ─────────────────────────

function capacityChip(pct: number) {
  return (
    <Chip size="small" label={`${pct}%`} color={pct >= 100 ? "success" : pct > 0 ? "warning" : "default"} />
  );
}

function statusChip(s: ExpertStatus) {
  return <Chip size="small" variant="outlined" label={s} color={s === "Active" ? "primary" : "default"} />;
}

/**
 * The popup-only edit rule, made concrete: the row itself is never an input. Edit opens a modal;
 * a dirty modal refuses backdrop/Esc dismissal and asks before discarding.
 */
function EditExpertDialog({ expert, onClose, onSaved }: {
  expert: ExpertSummary | null;
  onClose: () => void;
  onSaved: (msg: string) => void;
}) {
  const [form, setForm] = useState<ExpertSummary | null>(expert);
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  if (expert && form?.id !== expert.id) setForm(expert);
  if (!expert || !form) return null;

  const dirty = (["firstName", "lastName", "title", "location", "email"] as const).some(
    (k) => (form[k] ?? "") !== (expert[k] ?? ""),
  );
  const tryClose = () => (dirty ? setConfirmDiscard(true) : onClose());
  const field = (k: "firstName" | "lastName" | "title" | "location" | "email", label: string) => (
    <TextField
      label={label}
      value={form[k] ?? ""}
      onChange={(e) => setForm({ ...form, [k]: e.target.value })}
      fullWidth
      size="small"
    />
  );

  return (
    <Dialog open onClose={tryClose} maxWidth="sm" fullWidth>
      <DialogTitle>
        Edit {fullName(expert)}
        {dirty && <Chip size="small" label="Unsaved changes" color="warning" sx={{ ml: 1 }} />}
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Stack direction="row" spacing={2}>
            {field("firstName", "First name")}
            {field("lastName", "Last name")}
          </Stack>
          {field("title", "Title")}
          {field("location", "Location")}
          {field("email", "Email")}
          {confirmDiscard && (
            <Alert
              severity="warning"
              action={
                <>
                  <Button color="inherit" size="small" onClick={() => setConfirmDiscard(false)}>
                    Keep editing
                  </Button>
                  <Button color="inherit" size="small" onClick={onClose}>
                    Discard
                  </Button>
                </>
              }
            >
              Discard your changes?
            </Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={tryClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={!dirty}
          onClick={() => {
            onSaved(`PROTOTYPE — would PUT /api/experts/${expert.id} (nothing was saved)`);
            onClose();
          }}
        >
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** Rule 5: the full query state, always visible. */
function StatePanel({ query, page, clientText, shown, fetching }: {
  query: unknown;
  page?: ServerPage;
  clientText?: string;
  shown: number;
  fetching: boolean;
}) {
  const [open, setOpen] = useState(true);
  return (
    <Paper
      elevation={8}
      sx={{ position: "fixed", right: 16, bottom: 80, width: 300, zIndex: 1900, p: 1.5, fontFamily: "monospace", fontSize: 11 }}
    >
      <Stack sx={{ alignItems: "center", justifyContent: "space-between" }} direction="row">
        <Typography sx={{ fontWeight: 700 }} variant="caption">
          PROTOTYPE state {fetching && "· fetching…"}
        </Typography>
        <Button size="small" onClick={() => setOpen(!open)}>
          {open ? "hide" : "show"}
        </Button>
      </Stack>
      {open && (
        <pre style={{ margin: 0, whiteSpace: "pre-wrap" }}>
          {JSON.stringify(
            { serverRequest: query, serverTotal: page?.total, pageRows: page?.rows.length, clientFilter: clientText ?? null, rowsShown: shown },
            null,
            1,
          )}
        </pre>
      )}
    </Paper>
  );
}

function useEditing() {
  const [editing, setEditing] = useState<ExpertSummary | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const ui = (
    <>
      <EditExpertDialog expert={editing} onClose={() => setEditing(null)} onSaved={setToast} />
      <Snackbar open={!!toast} autoHideDuration={4000} onClose={() => setToast(null)} message={toast} />
    </>
  );
  return { edit: setEditing, ui };
}

function useLocations(all: ExpertSummary[]) {
  return useMemo(() => [...new Set(all.map((e) => e.location ?? "—"))].sort(), [all]);
}

// ───────────────────────── Variant A — toolbar + chips ─────────────────────────
// One filter toolbar above a classic table. Server filters are form controls; the active set is
// echoed as removable chips. A separate, visibly different "filter this page" box is client-side.
// Sort on column headers, MUI TablePagination footer. Row click = read-only detail page; the pencil
// is the only way into editing.

function VariantA({ all }: { all: ExpertSummary[] }) {
  const navigate = useNavigate();
  const locations = useLocations(all);
  const [query, setQuery] = useState(initialQuery);
  const [search, setSearch] = useState("");
  const [clientText, setClientText] = useState("");
  const q = useDebounced(search);
  const serverQuery = useMemo(() => ({ ...query, q, page: q !== query.q ? 0 : query.page }), [query, q]);
  const { data, isFetching } = useServerPage(all, serverQuery);
  const rows = clientFilter(data?.rows ?? [], clientText);
  const { edit, ui } = useEditing();
  const set = (patch: Partial<ServerQuery>) => setQuery({ ...query, q, page: 0, ...patch });

  const sortHeader = (key: SortKey, label: string, align?: "right") => (
    <TableCell align={align} sortDirection={serverQuery.sort === key ? serverQuery.dir : false}>
      <TableSortLabel
        active={serverQuery.sort === key}
        direction={serverQuery.sort === key ? serverQuery.dir : "asc"}
        onClick={() => set({ sort: key, dir: serverQuery.sort === key && serverQuery.dir === "asc" ? "desc" : "asc" })}
      >
        {label}
      </TableSortLabel>
    </TableCell>
  );

  const activeChips: { label: string; clear: () => void }[] = [
    ...(q ? [{ label: `Search: "${q}"`, clear: () => setSearch("") }] : []),
    ...serverQuery.statuses.map((s) => ({ label: `Status: ${s}`, clear: () => set({ statuses: serverQuery.statuses.filter((x) => x !== s) }) })),
    ...serverQuery.locations.map((l) => ({ label: `Location: ${l}`, clear: () => set({ locations: serverQuery.locations.filter((x) => x !== l) }) })),
    ...(serverQuery.band ? [{ label: bandLabel[serverQuery.band], clear: () => set({ band: null }) }] : []),
  ];

  return (
    <>
      <Paper sx={{ p: 2, mb: 2 }}>
        <Stack sx={{ alignItems: { md: "center" } }} direction={{ xs: "column", md: "row" }} spacing={2}>
          <TextField
            size="small"
            label="Search name, email, title"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            sx={{ minWidth: 260 }}
            slotProps={{ input: { startAdornment: <InputAdornment position="start"><CloudIcon fontSize="small" /></InputAdornment> } }}
          />
          <FormControl size="small" sx={{ minWidth: 140 }}>
            <InputLabel>Status</InputLabel>
            <Select
              multiple
              label="Status"
              value={serverQuery.statuses}
              onChange={(e) => set({ statuses: e.target.value as ExpertStatus[] })}
            >
              {(["Active", "Draft"] as const).map((s) => (
                <MenuItem key={s} value={s}>{s}</MenuItem>
              ))}
            </Select>
          </FormControl>
          <FormControl size="small" sx={{ minWidth: 180 }}>
            <InputLabel>Location</InputLabel>
            <Select
              multiple
              label="Location"
              value={serverQuery.locations}
              onChange={(e) => set({ locations: e.target.value as string[] })}
            >
              {locations.map((l) => (
                <MenuItem key={l} value={l}>{l}</MenuItem>
              ))}
            </Select>
          </FormControl>
          <ToggleButtonGroup
            size="small"
            exclusive
            value={serverQuery.band}
            onChange={(_, v: Band | null) => set({ band: v })}
          >
            <ToggleButton value="none">0%</ToggleButton>
            <ToggleButton value="partial">Partial</ToggleButton>
            <ToggleButton value="full">100%</ToggleButton>
          </ToggleButtonGroup>
          <Box sx={{ flex: 1 }} />
          <TextField
            size="small"
            placeholder="Filter this page"
            value={clientText}
            onChange={(e) => setClientText(e.target.value)}
            slotProps={{ input: { startAdornment: <InputAdornment position="start"><BoltIcon fontSize="small" /></InputAdornment> } }}
          />
        </Stack>
        {activeChips.length > 0 && (
          <Stack direction="row" spacing={1} sx={{ mt: 1.5, flexWrap: "wrap" }}>
            {activeChips.map((c) => (
              <Chip key={c.label} size="small" label={c.label} onDelete={c.clear} />
            ))}
            <Button size="small" onClick={() => { setSearch(""); setQuery(initialQuery); }}>Clear all</Button>
          </Stack>
        )}
      </Paper>

      <Paper>
        {isFetching ? <LinearProgress /> : <Box sx={{ height: 4 }} />}
        <Table>
          <TableHead>
            <TableRow>
              {sortHeader("name", "Name")}
              {sortHeader("title", "Title")}
              {sortHeader("location", "Location")}
              {sortHeader("capacity", "Availability (today)")}
              {sortHeader("status", "Status")}
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((e) => (
              <TableRow key={e.id} hover sx={{ cursor: "pointer" }} onClick={() => navigate(`/experts/${e.id}`)}>
                <TableCell>{fullName(e)}</TableCell>
                <TableCell>{e.title}</TableCell>
                <TableCell>{e.location ?? "—"}</TableCell>
                <TableCell>{capacityChip(e.currentCapacityPercent)}</TableCell>
                <TableCell>{statusChip(e.status)}</TableCell>
                <TableCell align="right" onClick={(ev) => ev.stopPropagation()}>
                  <IconButton title="Edit…" onClick={() => edit(e)}><EditIcon /></IconButton>
                </TableCell>
              </TableRow>
            ))}
            {rows.length === 0 && (
              <TableRow><TableCell colSpan={6}>No experts match.</TableCell></TableRow>
            )}
          </TableBody>
        </Table>
        <TablePagination
          component="div"
          count={data?.total ?? 0}
          page={serverQuery.page}
          rowsPerPage={serverQuery.size}
          rowsPerPageOptions={[10, 25, 50, 100]}
          onPageChange={(_, p) => setQuery({ ...serverQuery, page: p })}
          onRowsPerPageChange={(e) => set({ size: Number(e.target.value) })}
          labelDisplayedRows={({ from, to, count }) =>
            `${from}–${to} of ${count}${clientText ? ` · ${rows.length} shown on this page` : ""}`
          }
        />
      </Paper>
      <StatePanel query={serverQuery} page={data} clientText={clientText} shown={rows.length} fetching={isFetching} />
      {ui}
    </>
  );
}

// ───────────────────────── Variant B — filters in the header ─────────────────────────
// No toolbar at all: every column filters itself from a second header row. Key fields (name,
// location, status) go to the server; the rest (title, availability) filter only the loaded page,
// and say so with a bolt. Numbered pagination. Row click opens a read-only side drawer; editing
// lives one deliberate step further, behind its "Edit…" button.

function VariantB({ all }: { all: ExpertSummary[] }) {
  const navigate = useNavigate();
  const locations = useLocations(all);
  const [query, setQuery] = useState({ ...initialQuery, size: 20 });
  const [nameText, setNameText] = useState("");
  const [titleText, setTitleText] = useState("");
  const [minCap, setMinCap] = useState("");
  const [preview, setPreview] = useState<ExpertSummary | null>(null);
  const q = useDebounced(nameText);
  const serverQuery = useMemo(() => ({ ...query, q, page: q !== query.q ? 0 : query.page }), [query, q]);
  const { data, isFetching } = useServerPage(all, serverQuery);
  const { edit, ui } = useEditing();
  const set = (patch: Partial<ServerQuery>) => setQuery({ ...query, q, page: 0, ...patch });

  const rows = (data?.rows ?? []).filter(
    (e) =>
      (!titleText || e.title.toLowerCase().includes(titleText.toLowerCase())) &&
      (!minCap || e.currentCapacityPercent >= Number(minCap)),
  );
  const pages = Math.max(1, Math.ceil((data?.total ?? 0) / serverQuery.size));

  const head = (key: SortKey, label: string) => (
    <TableCell sortDirection={serverQuery.sort === key ? serverQuery.dir : false}>
      <TableSortLabel
        active={serverQuery.sort === key}
        direction={serverQuery.sort === key ? serverQuery.dir : "asc"}
        onClick={() => set({ sort: key, dir: serverQuery.sort === key && serverQuery.dir === "asc" ? "desc" : "asc" })}
      >
        {label}
      </TableSortLabel>
    </TableCell>
  );
  const serverMark = <Tooltip title="Filters the whole roster (server)"><CloudIcon fontSize="inherit" color="primary" /></Tooltip>;
  const clientMark = <Tooltip title="Filters only this page (client)"><BoltIcon fontSize="inherit" color="warning" /></Tooltip>;

  return (
    <>
      <Paper>
        {isFetching ? <LinearProgress /> : <Box sx={{ height: 4 }} />}
        <Table size="small">
          <TableHead>
            <TableRow>
              {head("name", "Name")}
              {head("title", "Title")}
              {head("location", "Location")}
              {head("capacity", "Availability")}
              {head("status", "Status")}
            </TableRow>
            <TableRow>
              <TableCell>
                <TextField variant="standard" placeholder="contains…" value={nameText} onChange={(e) => setNameText(e.target.value)}
                  slotProps={{ input: { endAdornment: serverMark } }} fullWidth />
              </TableCell>
              <TableCell>
                <TextField variant="standard" placeholder="contains…" value={titleText} onChange={(e) => setTitleText(e.target.value)}
                  slotProps={{ input: { endAdornment: clientMark } }} fullWidth />
              </TableCell>
              <TableCell>
                <Select variant="standard" displayEmpty fullWidth value={serverQuery.locations[0] ?? ""}
                  onChange={(e) => set({ locations: e.target.value ? [e.target.value] : [] })}
                  endAdornment={serverMark}>
                  <MenuItem value="">any</MenuItem>
                  {locations.map((l) => <MenuItem key={l} value={l}>{l}</MenuItem>)}
                </Select>
              </TableCell>
              <TableCell>
                <TextField variant="standard" placeholder="≥ %" type="number" value={minCap} onChange={(e) => setMinCap(e.target.value)}
                  slotProps={{ input: { endAdornment: clientMark } }} sx={{ width: 90 }} />
              </TableCell>
              <TableCell>
                <Select variant="standard" displayEmpty fullWidth value={serverQuery.statuses[0] ?? ""}
                  onChange={(e) => set({ statuses: e.target.value ? [e.target.value as ExpertStatus] : [] })}
                  endAdornment={serverMark}>
                  <MenuItem value="">any</MenuItem>
                  <MenuItem value="Active">Active</MenuItem>
                  <MenuItem value="Draft">Draft</MenuItem>
                </Select>
              </TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((e) => (
              <TableRow key={e.id} hover selected={preview?.id === e.id} sx={{ cursor: "pointer" }} onClick={() => setPreview(e)}>
                <TableCell>{fullName(e)}</TableCell>
                <TableCell>{e.title}</TableCell>
                <TableCell>{e.location ?? "—"}</TableCell>
                <TableCell>{capacityChip(e.currentCapacityPercent)}</TableCell>
                <TableCell>{statusChip(e.status)}</TableCell>
              </TableRow>
            ))}
            {rows.length === 0 && <TableRow><TableCell colSpan={5}>No experts match.</TableCell></TableRow>}
          </TableBody>
        </Table>
        <Stack direction="row" sx={{ alignItems: "center", justifyContent: "space-between", p: 1.5 }}>
          <Typography variant="body2" color="text.secondary">
            {data?.total ?? 0} on the server{rows.length !== (data?.rows.length ?? 0) && ` · ${rows.length} of this page after column filters`}
          </Typography>
          <Pagination count={pages} page={serverQuery.page + 1} onChange={(_, p) => setQuery({ ...serverQuery, page: p - 1 })} shape="rounded" />
          <Select size="small" value={serverQuery.size} onChange={(e) => set({ size: Number(e.target.value) })}>
            {[10, 20, 50].map((n) => <MenuItem key={n} value={n}>{n} / page</MenuItem>)}
          </Select>
        </Stack>
      </Paper>

      <Drawer anchor="right" open={!!preview} onClose={() => setPreview(null)}>
        {preview && (
          <Box sx={{ width: 380, p: 3 }}>
            <Stack sx={{ alignItems: "center", justifyContent: "space-between" }} direction="row">
              <Typography variant="h6">{fullName(preview)}</Typography>
              <IconButton onClick={() => setPreview(null)}><CloseIcon /></IconButton>
            </Stack>
            <Typography color="text.secondary" gutterBottom>{preview.title}</Typography>
            <Divider sx={{ my: 2 }} />
            <Stack spacing={1}>
              <Typography variant="body2">Location: {preview.location ?? "—"}</Typography>
              <Typography variant="body2">Email: {preview.email}</Typography>
              <Stack direction="row" spacing={1}>{capacityChip(preview.currentCapacityPercent)} {statusChip(preview.status)}</Stack>
            </Stack>
            <Divider sx={{ my: 2 }} />
            <Stack direction="row" spacing={1}>
              <Button variant="contained" startIcon={<EditIcon />} onClick={() => edit(preview)}>Edit…</Button>
              <Button startIcon={<DescriptionIcon />} onClick={() => navigate(`/experts/${preview.id}/cv`)}>View CV</Button>
              <Button onClick={() => navigate(`/experts/${preview.id}`)}>Open</Button>
            </Stack>
          </Box>
        )}
      </Drawer>
      <StatePanel query={serverQuery} page={data} clientText={`title~"${titleText}" cap>=${minCap || "–"}`} shown={rows.length} fetching={isFetching} />
      {ui}
    </>
  );
}

// ───────────────────────── Variant C — facet sidebar ─────────────────────────
// Filters leave the table entirely: a left facet rail with live counts from the server (each facet
// counted against the other active filters). The result pane owns sort (a dropdown, not headers),
// a "refine these results" client box, and prev/next paging. Actions sit behind a per-row menu, so
// nothing on the row surface is one click from a change.

function Facet({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Box sx={{ mb: 2 }}>
      <Typography variant="overline" color="text.secondary">{title}</Typography>
      {children}
    </Box>
  );
}

function VariantC({ all }: { all: ExpertSummary[] }) {
  const navigate = useNavigate();
  const locations = useLocations(all);
  const [query, setQuery] = useState(initialQuery);
  const [search, setSearch] = useState("");
  const [refine, setRefine] = useState("");
  const [menu, setMenu] = useState<{ el: HTMLElement; e: ExpertSummary } | null>(null);
  const q = useDebounced(search);
  const serverQuery = useMemo(() => ({ ...query, q, page: q !== query.q ? 0 : query.page }), [query, q]);
  const { data, isFetching } = useServerPage(all, serverQuery);
  const rows = clientFilter(data?.rows ?? [], refine);
  const { edit, ui } = useEditing();
  const set = (patch: Partial<ServerQuery>) => setQuery({ ...query, q, page: 0, ...patch });
  const toggle = <T,>(list: T[], v: T) => (list.includes(v) ? list.filter((x) => x !== v) : [...list, v]);
  const from = (data?.total ?? 0) === 0 ? 0 : serverQuery.page * serverQuery.size + 1;
  const to = Math.min((serverQuery.page + 1) * serverQuery.size, data?.total ?? 0);
  const sortOptions: { v: string; label: string }[] = [
    { v: "name:asc", label: "Name A→Z" },
    { v: "name:desc", label: "Name Z→A" },
    { v: "capacity:desc", label: "Most available" },
    { v: "capacity:asc", label: "Least available" },
    { v: "location:asc", label: "Location" },
    { v: "title:asc", label: "Title" },
    { v: "status:asc", label: "Status" },
  ];

  return (
    <Stack sx={{ alignItems: "flex-start" }} direction="row" spacing={2}>
      <Paper sx={{ width: 260, flexShrink: 0, p: 2, position: "sticky", top: 16 }}>
        <TextField
          size="small"
          fullWidth
          placeholder="Search the roster"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }}
          sx={{ mb: 2 }}
        />
        <Facet title="Status">
          <FormGroup>
            {(["Active", "Draft"] as const).map((s) => (
              <FormControlLabel key={s}
                control={<Checkbox size="small" checked={serverQuery.statuses.includes(s)} onChange={() => set({ statuses: toggle(serverQuery.statuses, s) })} />}
                label={`${s} (${data?.facets.status[s] ?? 0})`} />
            ))}
          </FormGroup>
        </Facet>
        <Facet title="Availability today">
          <RadioGroup value={serverQuery.band ?? ""} onChange={(e) => set({ band: (e.target.value || null) as Band | null })}>
            <FormControlLabel value="" control={<Radio size="small" />} label="Any" />
            {(["full", "partial", "none"] as const).map((b) => (
              <FormControlLabel key={b} value={b} control={<Radio size="small" />} label={`${bandLabel[b]} (${data?.facets.band[b] ?? 0})`} />
            ))}
          </RadioGroup>
        </Facet>
        <Facet title="Location">
          <FormGroup sx={{ maxHeight: 260, overflow: "auto", flexWrap: "nowrap" }}>
            {locations
              .map((l) => ({ l, n: data?.facets.location[l] ?? 0 }))
              .sort((a, b) => b.n - a.n)
              .map(({ l, n }) => (
                <FormControlLabel key={l} disabled={n === 0 && !serverQuery.locations.includes(l)}
                  control={<Checkbox size="small" checked={serverQuery.locations.includes(l)} onChange={() => set({ locations: toggle(serverQuery.locations, l) })} />}
                  label={`${l} (${n})`} />
              ))}
          </FormGroup>
        </Facet>
        <Button size="small" startIcon={<FilterListIcon />} onClick={() => { setSearch(""); setQuery(initialQuery); }}>
          Reset filters
        </Button>
      </Paper>

      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Stack direction="row" spacing={2} sx={{ alignItems: "center", mb: 1.5 }}>
          <Typography variant="h6">{data?.total ?? "…"} experts</Typography>
          <Box sx={{ flex: 1 }} />
          <TextField size="small" placeholder="Refine these results" value={refine} onChange={(e) => setRefine(e.target.value)}
            slotProps={{ input: { startAdornment: <InputAdornment position="start"><BoltIcon fontSize="small" /></InputAdornment> } }} />
          <FormControl size="small" sx={{ minWidth: 170 }}>
            <InputLabel>Sort by</InputLabel>
            <Select label="Sort by" value={`${serverQuery.sort}:${serverQuery.dir}`}
              onChange={(e) => { const [sort, dir] = String(e.target.value).split(":"); set({ sort: sort as SortKey, dir: dir as Dir }); }}>
              {sortOptions.map((o) => <MenuItem key={o.v} value={o.v}>{o.label}</MenuItem>)}
            </Select>
          </FormControl>
        </Stack>
        <Paper>
          {isFetching ? <LinearProgress /> : <Box sx={{ height: 4 }} />}
          <Table size="small">
            <TableBody>
              {rows.map((e) => (
                <TableRow key={e.id} hover>
                  <TableCell>
                    <Typography sx={{ fontWeight: 600 }}>{fullName(e)}</Typography>
                    <Typography variant="body2" color="text.secondary">{e.title} · {e.location ?? "—"}</Typography>
                  </TableCell>
                  <TableCell>{statusChip(e.status)}</TableCell>
                  <TableCell>{capacityChip(e.currentCapacityPercent)}</TableCell>
                  <TableCell align="right">
                    <IconButton onClick={(ev) => setMenu({ el: ev.currentTarget, e })} aria-label="Row actions"><MoreVertIcon /></IconButton>
                  </TableCell>
                </TableRow>
              ))}
              {rows.length === 0 && <TableRow><TableCell>No experts match.</TableCell></TableRow>}
            </TableBody>
          </Table>
          <Stack direction="row" spacing={1} sx={{ alignItems: "center", p: 1.5 }}>
            <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }}>
              Showing {from}–{to} of {data?.total ?? 0}{refine && ` · ${rows.length} after refine`}
            </Typography>
            <Button size="small" disabled={serverQuery.page === 0} onClick={() => setQuery({ ...serverQuery, page: serverQuery.page - 1 })}>‹ Prev</Button>
            <Button size="small" disabled={to >= (data?.total ?? 0)} onClick={() => setQuery({ ...serverQuery, page: serverQuery.page + 1 })}>Next ›</Button>
          </Stack>
        </Paper>
      </Box>

      <Menu anchorEl={menu?.el} open={!!menu} onClose={() => setMenu(null)}>
        <MenuItem onClick={() => { if (menu) navigate(`/experts/${menu.e.id}`); }}>Open</MenuItem>
        <MenuItem onClick={() => { if (menu) navigate(`/experts/${menu.e.id}/cv`); }}>
          <ListItemIcon><DescriptionIcon fontSize="small" /></ListItemIcon>View CV
        </MenuItem>
        <MenuItem onClick={() => { if (menu) edit(menu.e); setMenu(null); }}>
          <ListItemIcon><EditIcon fontSize="small" /></ListItemIcon>Edit…
        </MenuItem>
      </Menu>
      <StatePanel query={serverQuery} page={data} clientText={refine} shown={rows.length} fetching={isFetching} />
      {ui}
    </Stack>
  );
}

// ───────────────────────── switcher host ─────────────────────────

export const tableVariants = [
  { key: "", name: "Baseline (today)" },
  { key: "A", name: "Toolbar + chips" },
  { key: "B", name: "Filters in the header" },
  { key: "C", name: "Facet sidebar" },
];

export function ExpertsTablePrototype({ variant, all }: { variant: string; all: ExpertSummary[] }) {
  return (
    <PageHeader title="CVs" width="wide">
      {variant === "A" && <VariantA all={all} />}
      {variant === "B" && <VariantB all={all} />}
      {variant === "C" && <VariantC all={all} />}
      <PrototypeSwitcher variants={tableVariants} />
    </PageHeader>
  );
}
