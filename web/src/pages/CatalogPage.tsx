import { useMemo, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Chip,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import DeleteIcon from "@mui/icons-material/Delete";
import EditIcon from "@mui/icons-material/Edit";
import {
  apiErrorMessage,
  useCategories,
  useCreateCategory,
  useCreateSkill,
  useDeleteCategory,
  useDeleteSkill,
  useSkills,
  useUpdateCategory,
  useUpdateSkill,
} from "../api";
import type { Category, SkillDto } from "../types";
import { ErrorNotice } from "../components/ErrorNotice";
import PageHeader from "../components/PageHeader";
import DictionaryTable, { type DictionaryColumn } from "../components/DictionaryTable";
import EditDialog from "../components/EditDialog";

/**
 * The skill catalog: a category list beside a skills table (EXP-48, Variant C on
 * `prototype/table-conventions`).
 *
 * It used to be an indented tree whose rows were also its inputs — clicking a name swapped it for
 * text fields, so a stray click mid-journey was one keystroke from a write. Both of those are gone
 * for the same reason. ~80 skills in ~13 near-flat categories is a *dictionary*, not a hierarchy to
 * navigate: it belongs in the shared `DictionaryTable`, which filters, sorts and pages it like
 * every other dictionary and keeps writes off the row. What is genuinely a tree — a category's
 * place under its parent — survives as the **path** ("Frontend / React"), which reads the nesting
 * out in full instead of asking anybody to count indents.
 */

/** A skill as the table shows it: the server's row plus its category's full path. */
interface SkillRow extends SkillDto {
  path: string;
}

/** A category as the list shows it: its full path, and how many skills sit directly under it. */
interface CategoryRow extends Category {
  path: string;
  skillCount: number;
}

/**
 * Ordinal, like `DictionaryTable`'s own comparison and for the same reason: CI runs in a different
 * culture from a developer's machine, and a collator that disagrees about case would make the
 * category list's order environment-dependent.
 */
const byPath = (a: CategoryRow, b: CategoryRow) => {
  const [x, y] = [a.path.toLowerCase(), b.path.toLowerCase()];
  return x < y ? -1 : x > y ? 1 : 0;
};

/**
 * The catalog, shaped for display: every category with its "Languages / JavaScript / React" path
 * and its own skill count, and every skill carrying the path of the category it sits in.
 *
 * The paths are what make duplicate names across branches unambiguous — two "React" categories are
 * two different rows in the list, and in the Category dropdown.
 */
function shape(categories: Category[], skills: SkillDto[]) {
  const byId = new Map(categories.map((c) => [c.id, c]));
  const path = (c: Category): string => {
    const parent = c.parentId ? byId.get(c.parentId) : undefined;
    return parent ? `${path(parent)} / ${c.name}` : c.name;
  };
  const counts = new Map<string, number>();
  for (const s of skills) counts.set(s.categoryId, (counts.get(s.categoryId) ?? 0) + 1);

  const categoryRows: CategoryRow[] = categories
    .map((c) => ({ ...c, path: path(c), skillCount: counts.get(c.id) ?? 0 }))
    .sort(byPath);
  const pathOf = new Map(categoryRows.map((c) => [c.id, c.path]));
  // A skill whose category is not in the list falls back to the name the server sent with it,
  // rather than rendering blank.
  const skillRows: SkillRow[] = skills.map((s) => ({
    ...s,
    path: pathOf.get(s.categoryId) ?? s.categoryName,
  }));
  return { categoryRows, skillRows };
}

/** A category's own id plus all of its descendants — the re-parent targets that would make a cycle. */
function descendantsOf(id: string, categories: readonly Category[]): Set<string> {
  const childrenOf = new Map<string, string[]>();
  for (const c of categories) {
    if (!c.parentId) continue;
    childrenOf.set(c.parentId, [...(childrenOf.get(c.parentId) ?? []), c.id]);
  }
  const blocked = new Set<string>([id]);
  const walk = (cur: string) => {
    for (const child of childrenOf.get(cur) ?? []) {
      blocked.add(child);
      walk(child);
    }
  };
  walk(id);
  return blocked;
}

/** What the open popup is editing, or creating. `undefined` row means "new". */
type Editing =
  | { kind: "skill"; skill?: SkillRow; categoryId?: string }
  | { kind: "category"; category?: CategoryRow }
  | null;

export default function CatalogPage() {
  const categories = useCategories();
  const skills = useSkills();
  const createCategory = useCreateCategory();
  const updateCategory = useUpdateCategory();
  const deleteCategory = useDeleteCategory();
  const createSkill = useCreateSkill();
  const updateSkill = useUpdateSkill();
  const deleteSkill = useDeleteSkill();

  /** The category the table is narrowed to, or null for the whole catalog. */
  const [selected, setSelected] = useState<string | null>(null);
  const [editing, setEditing] = useState<Editing>(null);
  const [error, setError] = useState<string | null>(null);

  const { categoryRows, skillRows } = useMemo(
    () => shape(categories.data ?? [], skills.data ?? []),
    [categories.data, skills.data],
  );

  const chosen = categoryRows.find((c) => c.id === selected) ?? null;
  // The category list is the table's category filter, so the rows are narrowed before the table
  // sees them and no second control competes with the list for the same job.
  const visible = selected ? skillRows.filter((s) => s.categoryId === selected) : skillRows;

  /**
   * Every write, with the same two guarantees: a failure is shown in the page's own words, and the
   * popup only closes when the server accepted it. A dialog that closed on a 400 would take the
   * typed work with it.
   */
  const write = async (fn: () => Promise<unknown>) => {
    setError(null);
    try {
      await fn();
      setEditing(null);
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  };

  /** Opens a popup, clearing whatever the last write failed with — that message is spent. */
  const open = (next: NonNullable<Editing>) => {
    setError(null);
    setEditing(next);
  };

  const columns: DictionaryColumn<SkillRow>[] = useMemo(
    () => [
      { key: "name", label: "Skill", sortValue: (s) => s.name.toLowerCase(), render: (s) => s.name },
      {
        key: "path",
        label: "Category",
        sortValue: (s) => s.path.toLowerCase(),
        render: (s) => (
          <Typography variant="body2" component="span" sx={{ color: "text.secondary" }}>
            {s.path}
          </Typography>
        ),
      },
      {
        // The row's only interactive thing, and it opens a popup rather than writing anything.
        key: "actions",
        label: "Actions",
        align: "right",
        render: (s) => (
          <Tooltip title="Edit…">
            <IconButton aria-label={`Edit ${s.name}`} onClick={() => open({ kind: "skill", skill: s })}>
              <EditIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        ),
      },
    ],
    [],
  );

  const loadError = categories.isError
    ? apiErrorMessage(categories.error)
    : skills.isError
      ? apiErrorMessage(skills.error)
      : null;

  return (
    // A list beside a table, both of which want room — the same wide cap as the roster.
    <PageHeader title="Skill Catalog" width="wide">
      <ErrorNotice message={loadError ?? error} sx={{ mb: 2 }} />

      <Stack
        direction={{ xs: "column", md: "row" }}
        spacing={2}
        sx={{ alignItems: { md: "flex-start" } }}
      >
        <Paper sx={{ width: { xs: "100%", md: 280 }, flexShrink: 0 }}>
          <Stack direction="row" sx={{ pl: 2, pr: 1, py: 1, alignItems: "center" }}>
            <Typography variant="overline" sx={{ flexGrow: 1 }}>
              Categories
            </Typography>
            <Tooltip title="New category">
              <IconButton aria-label="New category" onClick={() => open({ kind: "category" })}>
                <AddIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          </Stack>
          <List dense disablePadding aria-label="Categories">
            <ListItemButton selected={selected === null} onClick={() => setSelected(null)}>
              <ListItemText primary="All skills" />
              <Chip size="small" label={skillRows.length} />
            </ListItemButton>
            {categoryRows.map((c) => (
              <ListItemButton
                key={c.id}
                selected={selected === c.id}
                onClick={() => setSelected(c.id)}
              >
                <ListItemText primary={c.path} />
                <Chip size="small" label={c.skillCount} />
              </ListItemButton>
            ))}
          </List>
          {!categories.isLoading && categoryRows.length === 0 && (
            <Typography variant="body2" sx={{ color: "text.secondary", p: 2 }}>
              No categories yet. Add one to get started.
            </Typography>
          )}
        </Paper>

        <Box sx={{ flexGrow: 1, minWidth: 0, width: "100%" }}>
          {chosen && (
            <Stack direction="row" spacing={1} sx={{ mb: 2, alignItems: "center" }}>
              <Typography variant="h6" component="h2">
                {chosen.path}
              </Typography>
              <Button
                size="small"
                startIcon={<EditIcon />}
                onClick={() => open({ kind: "category", category: chosen })}
              >
                Edit category…
              </Button>
            </Stack>
          )}
          <DictionaryTable
            label="Skills"
            rows={visible}
            columns={columns}
            rowKey={(s) => s.id}
            search={{ label: "Search skills", of: (s) => `${s.name} ${s.path}` }}
            initialSort={{ key: "name", dir: "asc" }}
            loading={skills.isLoading}
            // A new skill lands in whatever the list is showing, so creating several in one
            // category does not mean picking it out of the dropdown every time.
            actions={
              <Button
                variant="outlined"
                startIcon={<AddIcon />}
                onClick={() => open({ kind: "skill", categoryId: selected ?? undefined })}
              >
                New skill
              </Button>
            }
            empty={selected ? "No skills in this category yet." : "No skills yet."}
            emptyFiltered="No skills match."
          />
        </Box>
      </Stack>

      {editing?.kind === "skill" && (
        <SkillDialog
          skill={editing.skill}
          defaultCategoryId={editing.categoryId}
          categories={categoryRows}
          saving={createSkill.isPending || updateSkill.isPending || deleteSkill.isPending}
          onClose={() => setEditing(null)}
          onSave={(dto) =>
            write(() =>
              editing.skill
                ? updateSkill.mutateAsync({ id: editing.skill.id, ...dto })
                : createSkill.mutateAsync(dto),
            )
          }
          onDelete={() => write(() => deleteSkill.mutateAsync(editing.skill!.id))}
        />
      )}

      {editing?.kind === "category" && (
        <CategoryDialog
          category={editing.category}
          categories={categoryRows}
          saving={createCategory.isPending || updateCategory.isPending || deleteCategory.isPending}
          onClose={() => setEditing(null)}
          onSave={(dto) =>
            write(() =>
              editing.category
                ? updateCategory.mutateAsync({ id: editing.category.id, ...dto })
                : createCategory.mutateAsync(dto),
            )
          }
          onDelete={() =>
            write(async () => {
              await deleteCategory.mutateAsync(editing.category!.id);
              // The list cannot stay pointed at a category that no longer exists.
              if (selected === editing.category!.id) setSelected(null);
            })
          }
        />
      )}
    </PageHeader>
  );
}

/**
 * Deleting, from inside the popup that edits the thing (EXP-48).
 *
 * It asks in an `Alert` here rather than in a second `Dialog` over the first, for the reason
 * `EditDialog` already gives about its discard question: a modal over a modal takes focus away from
 * what it is asking about. The row it used to live on is gone entirely — a delete one click deep on
 * a row, behind nothing but a browser `confirm()`, is exactly the accident this page was changed to
 * prevent.
 */
function DeleteInDialog({
  noun,
  name,
  busy,
  onDelete,
}: {
  noun: string;
  name: string;
  busy: boolean;
  onDelete: () => void;
}) {
  const [asking, setAsking] = useState(false);

  if (!asking) {
    return (
      <Box>
        <Button color="error" size="small" startIcon={<DeleteIcon />} onClick={() => setAsking(true)}>
          Delete {noun}…
        </Button>
      </Box>
    );
  }
  return (
    <Alert
      severity="error"
      action={
        <>
          <Button color="inherit" size="small" onClick={() => setAsking(false)}>
            Keep it
          </Button>
          <Button color="inherit" size="small" disabled={busy} onClick={onDelete}>
            Delete
          </Button>
        </>
      }
    >
      Delete &ldquo;{name}&rdquo;? This cannot be undone.
    </Alert>
  );
}

/** One skill's whole editable surface, and the only place any of it can be changed. */
function SkillDialog({
  skill,
  defaultCategoryId,
  categories,
  saving,
  onClose,
  onSave,
  onDelete,
}: {
  skill?: SkillRow;
  /** Pre-filled for a new skill from the category the list is showing, when it is showing one. */
  defaultCategoryId?: string;
  categories: readonly CategoryRow[];
  saving: boolean;
  onClose: () => void;
  onSave: (dto: { name: string; categoryId: string }) => void;
  onDelete: () => void;
}) {
  const initialCategory = skill?.categoryId ?? defaultCategoryId ?? "";
  const [name, setName] = useState(skill?.name ?? "");
  const [categoryId, setCategoryId] = useState(initialCategory);

  const dirty = name !== (skill?.name ?? "") || categoryId !== initialCategory;

  return (
    <EditDialog
      title={skill ? `Edit ${skill.name}` : "New skill"}
      dirty={dirty}
      // A skill without a category has nowhere to live, and the server would refuse it. The name is
      // checked the same way rather than trimmed into existence.
      canSave={name.trim() !== "" && categoryId !== ""}
      saving={saving}
      onClose={onClose}
      onSave={() => onSave({ name: name.trim(), categoryId })}
    >
      <TextField
        label="Name"
        value={name}
        onChange={(e) => setName(e.target.value)}
        autoFocus
        fullWidth
      />
      <TextField
        label="Category"
        select
        value={categoryId}
        onChange={(e) => setCategoryId(e.target.value)}
        fullWidth
      >
        {categories.map((c) => (
          <MenuItem key={c.id} value={c.id}>
            {c.path}
          </MenuItem>
        ))}
      </TextField>
      {skill && <DeleteInDialog noun="skill" name={skill.name} busy={saving} onDelete={onDelete} />}
    </EditDialog>
  );
}

/** One category's whole editable surface — its name, and where it sits. */
function CategoryDialog({
  category,
  categories,
  saving,
  onClose,
  onSave,
  onDelete,
}: {
  category?: CategoryRow;
  categories: readonly CategoryRow[];
  saving: boolean;
  onClose: () => void;
  onSave: (dto: { name: string; parentId: string | null }) => void;
  onDelete: () => void;
}) {
  const [name, setName] = useState(category?.name ?? "");
  const [parentId, setParentId] = useState(category?.parentId ?? "");

  const dirty = name !== (category?.name ?? "") || parentId !== (category?.parentId ?? "");

  // Re-parenting a category under itself or under one of its own descendants would detach that
  // whole branch from the root, so those are not offered at all.
  const blocked = category ? descendantsOf(category.id, categories) : new Set<string>();
  const parents = categories.filter((c) => !blocked.has(c.id));

  return (
    <EditDialog
      title={category ? `Edit ${category.path}` : "New category"}
      dirty={dirty}
      canSave={name.trim() !== ""}
      saving={saving}
      onClose={onClose}
      onSave={() => onSave({ name: name.trim(), parentId: parentId === "" ? null : parentId })}
    >
      <TextField
        label="Name"
        value={name}
        onChange={(e) => setName(e.target.value)}
        autoFocus
        fullWidth
      />
      <TextField
        label="Parent"
        select
        value={parentId}
        onChange={(e) => setParentId(e.target.value)}
        fullWidth
      >
        <MenuItem value="">— none (top level) —</MenuItem>
        {parents.map((c) => (
          <MenuItem key={c.id} value={c.id}>
            {c.path}
          </MenuItem>
        ))}
      </TextField>
      {category && (
        <DeleteInDialog noun="category" name={category.path} busy={saving} onDelete={onDelete} />
      )}
    </EditDialog>
  );
}
