// PROTOTYPE — throwaway (branch `prototype/table-conventions`, never main).
//
// Question: the skill catalog as an indented tree "looks weird". It is ~80 skills in ~13 mostly-flat
// categories — a small dictionary — so it takes the Variant A table convention (toolbar filters,
// chips, sortable headers, pagination, edit only in a popup). What is still open is where the
// *categories* live. Three answers, `?variant=A|B|C` on /catalog (no param = today's tree):
//
//   A  Two tabs: a Skills table and a Categories table, each Variant A.
//   B  One Skills table, grouped: category header rows with counts, collapse per group.
//   C  Master–detail: a category list on the left filters the Skills table on the right.
//
// Stubs: the whole catalog is already fetched, so filtering/sorting/paging here is client-side over
// the complete list (no fake server). Save/create/delete never mutate.
import { useMemo, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  IconButton,
  InputAdornment,
  InputLabel,
  List,
  ListItemButton,
  ListItemText,
  MenuItem,
  Paper,
  Select,
  Snackbar,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TablePagination,
  TableRow,
  TableSortLabel,
  Tabs,
  TextField,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import EditIcon from "@mui/icons-material/Edit";
import SearchIcon from "@mui/icons-material/Search";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import { useCategories, useSkills } from "../api";
import type { Category, SkillDto } from "../types";
import PageHeader from "../components/PageHeader";
import PrototypeSwitcher from "../components/PrototypeSwitcher";

// ───────────────────────── data shaping ─────────────────────────

interface SkillRow extends SkillDto {
  path: string; // "Parent / Child" category path
}

interface CategoryRow extends Category {
  path: string;
  parentPath: string;
  skillCount: number;
}

function useCatalog() {
  const { data: categories = [] } = useCategories();
  const { data: skills = [] } = useSkills();
  return useMemo(() => {
    const byId = new Map(categories.map((c) => [c.id, c]));
    const path = (c: Category): string => {
      const p = c.parentId ? byId.get(c.parentId) : undefined;
      return p ? `${path(p)} / ${c.name}` : c.name;
    };
    const counts = skills.reduce<Record<string, number>>((a, s) => ((a[s.categoryId] = (a[s.categoryId] ?? 0) + 1), a), {});
    const cats: CategoryRow[] = categories
      .map((c) => ({
        ...c,
        path: path(c),
        parentPath: c.parentId && byId.get(c.parentId) ? path(byId.get(c.parentId)!) : "—",
        skillCount: counts[c.id] ?? 0,
      }))
      .sort((a, b) => a.path.localeCompare(b.path));
    const pathOf = new Map(cats.map((c) => [c.id, c.path]));
    const rows: SkillRow[] = skills.map((s) => ({ ...s, path: pathOf.get(s.categoryId) ?? s.categoryName }));
    return { skills: rows, categories: cats };
  }, [categories, skills]);
}

type Dir = "asc" | "desc";
function sortBy<T>(rows: T[], key: (r: T) => string | number, dir: Dir) {
  const s = dir === "asc" ? 1 : -1;
  return [...rows].sort((a, b) => {
    const x = key(a);
    const y = key(b);
    return (x < y ? -1 : x > y ? 1 : 0) * s;
  });
}

// ───────────────────────── popups (edit only here) ─────────────────────────

type Editing =
  | { kind: "skill"; row: Partial<SkillRow> }
  | { kind: "category"; row: Partial<CategoryRow> }
  | null;

function EditDialog({ editing, categories, onClose, onSaved }: {
  editing: Editing;
  categories: CategoryRow[];
  onClose: () => void;
  onSaved: (m: string) => void;
}) {
  const [name, setName] = useState("");
  const [parent, setParent] = useState("");
  const [key, setKey] = useState<string | null>(null);
  const [confirm, setConfirm] = useState(false);
  const k = editing ? `${editing.kind}:${editing.row.id ?? "new"}` : null;
  if (k !== key) {
    setKey(k);
    setConfirm(false);
    setName(editing?.row.name ?? "");
    setParent(editing?.kind === "skill" ? editing.row.categoryId ?? "" : editing?.row.parentId ?? "");
  }
  if (!editing) return null;

  const isNew = !editing.row.id;
  const origParent = editing.kind === "skill" ? editing.row.categoryId ?? "" : editing.row.parentId ?? "";
  const dirty = name !== (editing.row.name ?? "") || parent !== origParent;
  const tryClose = () => (dirty ? setConfirm(true) : onClose());
  const noun = editing.kind === "skill" ? "skill" : "category";
  const parentChoices = editing.kind === "category" ? categories.filter((c) => c.id !== editing.row.id) : categories;

  return (
    <Dialog open onClose={tryClose} maxWidth="xs" fullWidth>
      <DialogTitle>
        {isNew ? `New ${noun}` : `Edit ${noun}`}
        {dirty && !isNew && <Chip size="small" color="warning" label="Unsaved changes" sx={{ ml: 1 }} />}
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label="Name" value={name} onChange={(e) => setName(e.target.value)} autoFocus size="small" />
          <TextField select size="small" label={editing.kind === "skill" ? "Category" : "Parent category"} value={parent}
            onChange={(e) => setParent(e.target.value)}>
            {editing.kind === "category" && <MenuItem value="">— none (top level) —</MenuItem>}
            {parentChoices.map((c) => <MenuItem key={c.id} value={c.id}>{c.path}</MenuItem>)}
          </TextField>
          {confirm && (
            <Alert severity="warning" action={<>
              <Button color="inherit" size="small" onClick={() => setConfirm(false)}>Keep editing</Button>
              <Button color="inherit" size="small" onClick={onClose}>Discard</Button>
            </>}>Discard your changes?</Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions sx={{ justifyContent: "space-between" }}>
        {!isNew ? (
          <Button color="error" onClick={() => { onSaved(`PROTOTYPE — would DELETE this ${noun} (after its own confirm)`); onClose(); }}>
            Delete…
          </Button>
        ) : <span />}
        <Box>
          <Button onClick={tryClose}>Cancel</Button>
          <Button variant="contained" disabled={!dirty || !name.trim() || (editing.kind === "skill" && !parent)}
            onClick={() => { onSaved(`PROTOTYPE — would ${isNew ? "POST" : "PUT"} ${noun} "${name}" (nothing saved)`); onClose(); }}>
            Save
          </Button>
        </Box>
      </DialogActions>
    </Dialog>
  );
}

function useEditor(categories: CategoryRow[]) {
  const [editing, setEditing] = useState<Editing>(null);
  const [toast, setToast] = useState<string | null>(null);
  const ui = (
    <>
      <EditDialog editing={editing} categories={categories} onClose={() => setEditing(null)} onSaved={setToast} />
      <Snackbar open={!!toast} autoHideDuration={4000} onClose={() => setToast(null)} message={toast} />
    </>
  );
  return { setEditing, ui };
}

// ───────────────────────── the Variant A skills table (shared by A and C) ─────────────────────────

type SkillSort = "name" | "path";

function SkillsTable({ skills, categories, lockedCategory, onEdit, onNew }: {
  skills: SkillRow[];
  categories: CategoryRow[];
  lockedCategory?: string | null; // Variant C: category chosen in the left list
  onEdit: (s: SkillRow) => void;
  onNew: () => void;
}) {
  const [q, setQ] = useState("");
  const [cats, setCats] = useState<string[]>([]);
  const [sort, setSort] = useState<SkillSort>("name");
  const [dir, setDir] = useState<Dir>("asc");
  const [page, setPage] = useState(0);
  const [size, setSize] = useState(25);

  const active = lockedCategory ? [lockedCategory] : cats;
  const filtered = skills.filter(
    (s) =>
      (!q || s.name.toLowerCase().includes(q.toLowerCase())) &&
      (!active.length || active.includes(s.categoryId)),
  );
  const sorted = sortBy(filtered, (s) => (sort === "name" ? s.name.toLowerCase() : `${s.path} ${s.name}`.toLowerCase()), dir);
  const pageRows = sorted.slice(page * size, (page + 1) * size);
  const head = (key: SkillSort, label: string) => (
    <TableCell sortDirection={sort === key ? dir : false}>
      <TableSortLabel active={sort === key} direction={sort === key ? dir : "asc"}
        onClick={() => { setDir(sort === key && dir === "asc" ? "desc" : "asc"); setSort(key); setPage(0); }}>
        {label}
      </TableSortLabel>
    </TableCell>
  );

  return (
    <Paper>
      <Stack direction="row" spacing={2} sx={{ p: 2, alignItems: "center", flexWrap: "wrap" }}>
        <TextField size="small" placeholder="Search skills" value={q} onChange={(e) => { setQ(e.target.value); setPage(0); }}
          slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }} />
        {!lockedCategory && (
          <FormControl size="small" sx={{ minWidth: 220 }}>
            <InputLabel>Category</InputLabel>
            <Select multiple label="Category" value={cats} onChange={(e) => { setCats(e.target.value as string[]); setPage(0); }}>
              {categories.map((c) => <MenuItem key={c.id} value={c.id}>{c.path} ({c.skillCount})</MenuItem>)}
            </Select>
          </FormControl>
        )}
        <Box sx={{ flex: 1 }} />
        <Button variant="outlined" startIcon={<AddIcon />} onClick={onNew}>New skill</Button>
      </Stack>
      {!lockedCategory && (q || cats.length > 0) && (
        <Stack direction="row" spacing={1} sx={{ px: 2, pb: 1.5, flexWrap: "wrap" }}>
          {q && <Chip size="small" label={`Search: "${q}"`} onDelete={() => setQ("")} />}
          {cats.map((id) => (
            <Chip key={id} size="small" label={categories.find((c) => c.id === id)?.path} onDelete={() => setCats(cats.filter((x) => x !== id))} />
          ))}
          <Button size="small" onClick={() => { setQ(""); setCats([]); }}>Clear all</Button>
        </Stack>
      )}
      <Table size="small">
        <TableHead>
          <TableRow>
            {head("name", "Skill")}
            {head("path", "Category")}
            <TableCell align="right">Actions</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {pageRows.map((s) => (
            <TableRow key={s.id} hover>
              <TableCell>{s.name}</TableCell>
              <TableCell sx={{ color: "text.secondary" }}>{s.path}</TableCell>
              <TableCell align="right">
                <IconButton size="small" title="Edit…" onClick={() => onEdit(s)}><EditIcon fontSize="small" /></IconButton>
              </TableCell>
            </TableRow>
          ))}
          {pageRows.length === 0 && <TableRow><TableCell colSpan={3}>No skills match.</TableCell></TableRow>}
        </TableBody>
      </Table>
      <TablePagination component="div" count={sorted.length} page={page} rowsPerPage={size} rowsPerPageOptions={[10, 25, 50, 100]}
        onPageChange={(_, p) => setPage(p)} onRowsPerPageChange={(e) => { setSize(Number(e.target.value)); setPage(0); }} />
    </Paper>
  );
}

// ───────────────────────── Variant A — two tabs ─────────────────────────

function CategoriesTable({ categories, onEdit, onNew }: {
  categories: CategoryRow[];
  onEdit: (c: CategoryRow) => void;
  onNew: () => void;
}) {
  const [q, setQ] = useState("");
  const [sort, setSort] = useState<"path" | "skillCount">("path");
  const [dir, setDir] = useState<Dir>("asc");
  const rows = sortBy(
    categories.filter((c) => !q || c.path.toLowerCase().includes(q.toLowerCase())),
    (c) => (sort === "path" ? c.path.toLowerCase() : c.skillCount),
    dir,
  );
  const head = (key: "path" | "skillCount", label: string, align?: "right") => (
    <TableCell align={align} sortDirection={sort === key ? dir : false}>
      <TableSortLabel active={sort === key} direction={sort === key ? dir : "asc"}
        onClick={() => { setDir(sort === key && dir === "asc" ? "desc" : "asc"); setSort(key); }}>{label}</TableSortLabel>
    </TableCell>
  );
  return (
    <Paper>
      <Stack direction="row" spacing={2} sx={{ p: 2, alignItems: "center" }}>
        <TextField size="small" placeholder="Search categories" value={q} onChange={(e) => setQ(e.target.value)}
          slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }} />
        <Box sx={{ flex: 1 }} />
        <Button variant="outlined" startIcon={<AddIcon />} onClick={onNew}>New category</Button>
      </Stack>
      <Table size="small">
        <TableHead>
          <TableRow>
            {head("path", "Category")}
            <TableCell>Parent</TableCell>
            {head("skillCount", "Skills", "right")}
            <TableCell align="right">Actions</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((c) => (
            <TableRow key={c.id} hover>
              <TableCell>{c.name}</TableCell>
              <TableCell sx={{ color: "text.secondary" }}>{c.parentPath}</TableCell>
              <TableCell align="right">{c.skillCount}</TableCell>
              <TableCell align="right">
                <IconButton size="small" title="Edit…" onClick={() => onEdit(c)}><EditIcon fontSize="small" /></IconButton>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Paper>
  );
}

function VariantA() {
  const { skills, categories } = useCatalog();
  const { setEditing, ui } = useEditor(categories);
  const [tab, setTab] = useState(0);
  return (
    <>
      <Tabs value={tab} onChange={(_, v: number) => setTab(v)} sx={{ mb: 2 }}>
        <Tab label={`Skills (${skills.length})`} />
        <Tab label={`Categories (${categories.length})`} />
      </Tabs>
      {tab === 0 ? (
        <SkillsTable skills={skills} categories={categories}
          onEdit={(s) => setEditing({ kind: "skill", row: s })} onNew={() => setEditing({ kind: "skill", row: {} })} />
      ) : (
        <CategoriesTable categories={categories}
          onEdit={(c) => setEditing({ kind: "category", row: c })} onNew={() => setEditing({ kind: "category", row: {} })} />
      )}
      {ui}
    </>
  );
}

// ───────────────────────── Variant B — grouped table ─────────────────────────
// One table, no tree indentation: a full-width header row per category (path, count, its own ✎),
// skills beneath it. Search filters skills and hides empty groups. No paging — groups are the unit.

function VariantB() {
  const { skills, categories } = useCatalog();
  const { setEditing, ui } = useEditor(categories);
  const [q, setQ] = useState("");
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const [dir, setDir] = useState<Dir>("asc");
  const match = (s: SkillRow) => !q || s.name.toLowerCase().includes(q.toLowerCase()) || s.path.toLowerCase().includes(q.toLowerCase());
  const groups = sortBy(categories, (c) => c.path.toLowerCase(), dir)
    .map((c) => ({ c, items: sortBy(skills.filter((s) => s.categoryId === c.id && match(s)), (s) => s.name.toLowerCase(), "asc") }))
    .filter((g) => !q || g.items.length > 0);
  const toggle = (id: string) => setCollapsed((p) => { const n = new Set(p); if (n.has(id)) n.delete(id); else n.add(id); return n; });

  return (
    <Paper>
      <Stack direction="row" spacing={2} sx={{ p: 2, alignItems: "center" }}>
        <TextField size="small" placeholder="Search skills or categories" value={q} onChange={(e) => setQ(e.target.value)} sx={{ minWidth: 280 }}
          slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }} />
        <Button size="small" onClick={() => setCollapsed(new Set(categories.map((c) => c.id)))}>Collapse all</Button>
        <Button size="small" onClick={() => setCollapsed(new Set())}>Expand all</Button>
        <Box sx={{ flex: 1 }} />
        <Button variant="outlined" startIcon={<AddIcon />} onClick={() => setEditing({ kind: "category", row: {} })}>New category</Button>
        <Button variant="outlined" startIcon={<AddIcon />} onClick={() => setEditing({ kind: "skill", row: {} })}>New skill</Button>
      </Stack>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>
              <TableSortLabel active direction={dir} onClick={() => setDir(dir === "asc" ? "desc" : "asc")}>Category / Skill</TableSortLabel>
            </TableCell>
            <TableCell align="right">Actions</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {groups.map(({ c, items }) => {
            const open = !collapsed.has(c.id) || !!q;
            return [
              <TableRow key={c.id} sx={{ bgcolor: "action.hover" }}>
                <TableCell sx={{ fontWeight: 600, cursor: "pointer" }} onClick={() => toggle(c.id)}>
                  <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                    {open ? <ExpandMoreIcon fontSize="small" /> : <ChevronRightIcon fontSize="small" />}
                    <span>{c.path}</span>
                    <Chip size="small" label={q ? `${items.length} / ${c.skillCount}` : c.skillCount} />
                  </Stack>
                </TableCell>
                <TableCell align="right">
                  <IconButton size="small" title="Add skill here" onClick={() => setEditing({ kind: "skill", row: { categoryId: c.id } })}><AddIcon fontSize="small" /></IconButton>
                  <IconButton size="small" title="Edit category…" onClick={() => setEditing({ kind: "category", row: c })}><EditIcon fontSize="small" /></IconButton>
                </TableCell>
              </TableRow>,
              ...(open
                ? items.map((s) => (
                    <TableRow key={s.id} hover>
                      <TableCell sx={{ pl: 6 }}>{s.name}</TableCell>
                      <TableCell align="right">
                        <IconButton size="small" title="Edit…" onClick={() => setEditing({ kind: "skill", row: s })}><EditIcon fontSize="small" /></IconButton>
                      </TableCell>
                    </TableRow>
                  ))
                : []),
            ];
          })}
        </TableBody>
      </Table>
      {ui}
    </Paper>
  );
}

// ───────────────────────── Variant C — master–detail ─────────────────────────

function VariantC() {
  const { skills, categories } = useCatalog();
  const { setEditing, ui } = useEditor(categories);
  const [selected, setSelected] = useState<string | null>(null);
  const sel = categories.find((c) => c.id === selected);
  return (
    <Stack direction="row" spacing={2} sx={{ alignItems: "flex-start" }}>
      <Paper sx={{ width: 280, flexShrink: 0 }}>
        <Stack direction="row" sx={{ p: 1.5, alignItems: "center" }}>
          <Typography variant="overline" sx={{ flex: 1 }}>Categories</Typography>
          <IconButton size="small" title="New category" onClick={() => setEditing({ kind: "category", row: {} })}><AddIcon fontSize="small" /></IconButton>
        </Stack>
        <List dense disablePadding>
          <ListItemButton selected={!selected} onClick={() => setSelected(null)}>
            <ListItemText primary="All skills" />
            <Chip size="small" label={skills.length} />
          </ListItemButton>
          {categories.map((c) => (
            <ListItemButton key={c.id} selected={selected === c.id} onClick={() => setSelected(c.id)}>
              <ListItemText primary={c.path} />
              <Chip size="small" label={c.skillCount} />
            </ListItemButton>
          ))}
        </List>
      </Paper>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        {sel && (
          <Stack direction="row" spacing={1} sx={{ mb: 1.5, alignItems: "center" }}>
            <Typography variant="h6">{sel.path}</Typography>
            <Button size="small" startIcon={<EditIcon />} onClick={() => setEditing({ kind: "category", row: sel })}>Edit category…</Button>
          </Stack>
        )}
        <SkillsTable key={selected ?? "all"} skills={skills} categories={categories} lockedCategory={selected}
          onEdit={(s) => setEditing({ kind: "skill", row: s })}
          onNew={() => setEditing({ kind: "skill", row: { categoryId: selected ?? undefined } })} />
      </Box>
      {ui}
    </Stack>
  );
}

// ───────────────────────── host ─────────────────────────

export const catalogVariants = [
  { key: "", name: "Baseline (tree, today)" },
  { key: "A", name: "Tabs: Skills | Categories" },
  { key: "B", name: "Grouped table" },
  { key: "C", name: "Categories list + Skills table" },
];

export function CatalogPagePrototype({ variant }: { variant: string }) {
  return (
    <PageHeader title="Skill Catalog" width="wide">
      {variant === "A" && <VariantA />}
      {variant === "B" && <VariantB />}
      {variant === "C" && <VariantC />}
      <PrototypeSwitcher variants={catalogVariants} />
    </PageHeader>
  );
}
