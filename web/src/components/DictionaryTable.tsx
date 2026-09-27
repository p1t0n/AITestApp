import { useMemo, useState, type ReactNode } from "react";
import {
  Box,
  Button,
  Chip,
  MenuItem,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TablePagination,
  TableRow,
  TableSortLabel,
  TextField,
} from "@mui/material";

/**
 * The app-wide convention for a small dictionary — Variant A ("toolbar + chips"), chosen on
 * `prototype/table-conventions` (EXP-46).
 *
 * A dictionary is a list short enough to fetch whole: users, skill categories, the lookup tables.
 * So filtering, sorting and paging all happen here, over the complete list, and no page that adopts
 * this needs a server contract for them. Server-side paging is the Experts roster's problem, and
 * deliberately not this component's.
 *
 * The rule this exists to hold is that **the row is never an input**. Nothing rendered by a column
 * writes anything: a write is reached through an explicit action that opens an `EditDialog`, so a
 * stray click mid-journey cannot change data.
 */

export type SortDir = "asc" | "desc";

export interface DictionaryColumn<Row> {
  /** Stable key — the sort state names it, and it is the header's React key. */
  key: string;
  label: string;
  align?: "left" | "right";
  /**
   * What this column orders by. Omit it and the header offers nothing to click, which is the
   * honest rendering of a column that has no order (a cell of buttons, say).
   *
   * Return a lowercased string to sort case-insensitively: the comparison below is ordinal on
   * purpose, so it cannot do it for you.
   */
  sortValue?: (row: Row) => string | number;
  render: (row: Row) => ReactNode;
}

export interface DictionaryFilter<Row> {
  key: string;
  /** Names the control, and prefixes its chip. */
  label: string;
  options: readonly string[];
  valueOf: (row: Row) => string;
}

export interface DictionarySearch<Row> {
  /** Names the box. "Search email", not "Search" — a page can hold more than one list. */
  label: string;
  /** The text one row matches on. Join several fields with a space to search across them. */
  of: (row: Row) => string;
}

export interface DictionaryTableProps<Row> {
  /**
   * What the table is a list of — "Accounts", "Skills". It names the table for a screen reader, and
   * for any suite that has to tell it apart from another table on the same page.
   */
  label: string;
  /** The whole dictionary. `undefined` while it is still being fetched. */
  rows: readonly Row[] | undefined;
  columns: readonly DictionaryColumn<Row>[];
  rowKey: (row: Row) => string;
  filters?: readonly DictionaryFilter<Row>[];
  search?: DictionarySearch<Row>;
  initialSort?: { key: string; dir: SortDir };
  loading?: boolean;
  /** What the body says when the dictionary itself is empty. */
  empty: string;
  /** What it says when the filters excluded everything — a different fact, and a different fix. */
  emptyFiltered?: string;
}

/** The page sizes every dictionary offers, smallest first. */
const PAGE_SIZES = [10, 25, 50, 100];

/**
 * Ordinal rather than `localeCompare`.
 *
 * CI runs in a different culture and time zone from a developer's machine, and this repo has
 * already been bitten three times by that. A collator that disagrees about case or accents would
 * make every sort assertion environment-dependent; an ordinal comparison is the same everywhere.
 */
function compare(a: string | number, b: string | number): number {
  if (typeof a === "number" && typeof b === "number") return a - b;
  const [x, y] = [String(a), String(b)];
  return x < y ? -1 : x > y ? 1 : 0;
}

export default function DictionaryTable<Row>({
  label,
  rows,
  columns,
  rowKey,
  filters,
  search,
  initialSort,
  loading,
  empty,
  emptyFiltered,
}: DictionaryTableProps<Row>) {
  const [query, setQuery] = useState("");
  const [chosen, setChosen] = useState<Record<string, string>>({});
  const [sort, setSort] = useState<{ key: string; dir: SortDir } | null>(initialSort ?? null);
  const [page, setPage] = useState(0);
  const [size, setSize] = useState(PAGE_SIZES[0]);

  // Narrowing the list moves the last page; staying put would strand the person on an empty one.
  const narrow = (apply: () => void) => {
    apply();
    setPage(0);
  };

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase();
    return (rows ?? []).filter(
      (row) =>
        (needle === "" || !search || search.of(row).toLowerCase().includes(needle))
        && (filters ?? []).every((f) => !chosen[f.key] || f.valueOf(row) === chosen[f.key]),
    );
  }, [rows, query, chosen, filters, search]);

  const ordered = columns.find((c) => c.key === sort?.key);
  const sorted = useMemo(() => {
    const read = ordered?.sortValue;
    if (!sort || !read) return filtered;
    const factor = sort.dir === "asc" ? 1 : -1;
    // A copy: `filtered` is memoised, and sorting in place would reorder a cached array.
    return [...filtered].sort((a, b) => factor * compare(read(a), read(b)));
  }, [filtered, sort, ordered]);

  // The bound, derived rather than stored: a filter can shorten the list under a page number that
  // was valid when it was set, and clamping on read is the only version that cannot go stale.
  const lastPage = Math.max(0, Math.ceil(sorted.length / size) - 1);
  const safePage = Math.min(page, lastPage);
  const shown = sorted.slice(safePage * size, safePage * size + size);

  const chips: { key: string; label: string; clear: () => void }[] = [
    ...(query.trim()
      ? [{ key: "search", label: `Search: "${query.trim()}"`, clear: () => narrow(() => setQuery("")) }]
      : []),
    ...(filters ?? [])
      .filter((f) => chosen[f.key])
      .map((f) => ({
        key: f.key,
        label: `${f.label}: ${chosen[f.key]}`,
        clear: () => narrow(() => setChosen((c) => ({ ...c, [f.key]: "" }))),
      })),
  ];

  const clearAll = () =>
    narrow(() => {
      setQuery("");
      setChosen({});
    });

  const toggleSort = (key: string) =>
    setSort((s) => ({ key, dir: s?.key === key && s.dir === "asc" ? "desc" : "asc" }));

  return (
    <>
      {(search || (filters?.length ?? 0) > 0) && (
        <Paper sx={{ p: 2, mb: 2 }}>
          <Stack
            direction={{ xs: "column", md: "row" }}
            spacing={2}
            sx={{ alignItems: { md: "center" } }}
          >
            {search && (
              <TextField
                label={search.label}
                value={query}
                onChange={(e) => narrow(() => setQuery(e.target.value))}
                sx={{ minWidth: 260 }}
              />
            )}
            {(filters ?? []).map((f) => (
              <TextField
                key={f.key}
                select
                label={f.label}
                value={chosen[f.key] ?? ""}
                onChange={(e) => narrow(() => setChosen((c) => ({ ...c, [f.key]: e.target.value })))}
                sx={{ minWidth: 160 }}
              >
                <MenuItem value="">Any</MenuItem>
                {f.options.map((o) => (
                  <MenuItem key={o} value={o}>
                    {o}
                  </MenuItem>
                ))}
              </TextField>
            ))}
          </Stack>
          {chips.length > 0 && (
            <Stack direction="row" spacing={1} sx={{ mt: 2, flexWrap: "wrap", rowGap: 1 }}>
              {chips.map((c) => (
                <Chip key={c.key} size="small" label={c.label} onDelete={c.clear} />
              ))}
              <Button size="small" onClick={clearAll}>
                Clear all
              </Button>
            </Stack>
          )}
        </Paper>
      )}

      <Paper>
        <Table aria-label={label}>
          <TableHead>
            <TableRow>
              {columns.map((c) => (
                <TableCell
                  key={c.key}
                  align={c.align}
                  sortDirection={sort?.key === c.key ? sort.dir : false}
                >
                  {c.sortValue ? (
                    <TableSortLabel
                      active={sort?.key === c.key}
                      direction={sort?.key === c.key ? sort.dir : "asc"}
                      onClick={() => toggleSort(c.key)}
                    >
                      {c.label}
                    </TableSortLabel>
                  ) : (
                    c.label
                  )}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {loading && (
              <TableRow>
                <TableCell colSpan={columns.length}>Loading…</TableCell>
              </TableRow>
            )}
            {!loading
              && shown.map((row) => (
                <TableRow key={rowKey(row)} hover>
                  {columns.map((c) => (
                    <TableCell key={c.key} align={c.align}>
                      {c.render(row)}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            {!loading && shown.length === 0 && (
              <TableRow>
                <TableCell colSpan={columns.length}>
                  {(rows?.length ?? 0) === 0 ? empty : (emptyFiltered ?? empty)}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
        <TablePagination
          component="div"
          count={sorted.length}
          page={safePage}
          rowsPerPage={size}
          rowsPerPageOptions={PAGE_SIZES}
          showFirstButton
          showLastButton
          onPageChange={(_, p) => setPage(p)}
          onRowsPerPageChange={(e) => narrow(() => setSize(Number(e.target.value)))}
        />
      </Paper>
      {/* Keeps the footer clear of the dock's floating entry point on a short page. */}
      <Box sx={{ height: 8 }} />
    </>
  );
}
