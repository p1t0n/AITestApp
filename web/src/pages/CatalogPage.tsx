import { useMemo, useState } from "react";
import {
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  List,
  ListItem,
  Menu,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import EditIcon from "@mui/icons-material/Edit";
import DeleteIcon from "@mui/icons-material/Delete";
import CheckIcon from "@mui/icons-material/Check";
import CloseIcon from "@mui/icons-material/Close";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import {
  apiErrorMessage,
  useCategories,
  useCategoryTree,
  useCreateCategory,
  useCreateSkill,
  useDeleteCategory,
  useDeleteSkill,
  useUpdateCategory,
  useUpdateSkill,
} from "../api";
import type { Category, CategoryNode } from "../types";

type EditState = { kind: "category" | "skill"; id: string } | null;
// Adding a new node: a skill or subcategory under `parentId` (a category id),
// or a root category when parentId is null.
type AddState = { kind: "category" | "skill"; parentId: string | null } | null;
type ConfirmState = { kind: "category" | "skill"; id: string; name: string } | null;

// Builds "Languages / JavaScript / React"-style labels so duplicate names across
// branches stay unambiguous in the move dropdown.
function buildPathLabels(categories: Category[]): Map<string, string> {
  const byId = new Map(categories.map((c) => [c.id, c]));
  const labels = new Map<string, string>();
  const path = (c: Category): string => {
    const parent = c.parentId ? byId.get(c.parentId) : undefined;
    return parent ? `${path(parent)} / ${c.name}` : c.name;
  };
  for (const c of categories) labels.set(c.id, path(c));
  return labels;
}

// Set of a category's own id plus all descendants — invalid re-parent targets.
function descendantsOf(id: string, categories: Category[]): Set<string> {
  const childrenOf = new Map<string, string[]>();
  for (const c of categories) {
    if (!c.parentId) continue;
    const siblings = childrenOf.get(c.parentId) ?? [];
    siblings.push(c.id);
    childrenOf.set(c.parentId, siblings);
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

export default function CatalogPage() {
  const { data: tree, isLoading } = useCategoryTree();
  const { data: categories } = useCategories();
  const createCategory = useCreateCategory();
  const createSkill = useCreateSkill();
  const updateCategory = useUpdateCategory();
  const updateSkill = useUpdateSkill();
  const deleteCategory = useDeleteCategory();
  const deleteSkill = useDeleteSkill();

  const [error, setError] = useState<string | null>(null);

  const [editing, setEditing] = useState<EditState>(null);
  const [draftName, setDraftName] = useState("");
  const [draftParent, setDraftParent] = useState(""); // parentId for category, categoryId for skill

  const [adding, setAdding] = useState<AddState>(null);
  const [addName, setAddName] = useState("");

  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const [menu, setMenu] = useState<{ anchor: HTMLElement; categoryId: string } | null>(null);
  const [confirm, setConfirm] = useState<ConfirmState>(null);

  const pathLabels = useMemo(() => buildPathLabels(categories ?? []), [categories]);

  if (isLoading) return <CircularProgress />;

  const run = (fn: () => Promise<unknown>) => async () => {
    setError(null);
    try {
      await fn();
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  };

  const toggleCollapse = (id: string) =>
    setCollapsed((prev) => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });

  const expand = (id: string) =>
    setCollapsed((prev) => {
      if (!prev.has(id)) return prev;
      const next = new Set(prev);
      next.delete(id);
      return next;
    });

  // ---- edit ----

  const startEditCategory = (node: CategoryNode, parent: string | null) => {
    setError(null);
    setAdding(null);
    setEditing({ kind: "category", id: node.id });
    setDraftName(node.name);
    setDraftParent(parent ?? "");
  };

  const startEditSkill = (id: string, name: string, categoryId: string) => {
    setError(null);
    setAdding(null);
    setEditing({ kind: "skill", id });
    setDraftName(name);
    setDraftParent(categoryId);
  };

  const saveEdit = run(async () => {
    if (!editing) return;
    if (editing.kind === "category") {
      await updateCategory.mutateAsync({ id: editing.id, name: draftName, parentId: draftParent || null });
    } else {
      await updateSkill.mutateAsync({ id: editing.id, name: draftName, categoryId: draftParent });
    }
    setEditing(null);
  });

  // ---- add ----

  const startAdd = (state: NonNullable<AddState>) => {
    setError(null);
    setEditing(null);
    setAdding(state);
    setAddName("");
    if (state.parentId) expand(state.parentId);
  };

  const saveAdd = run(async () => {
    if (!adding) return;
    if (adding.kind === "category") {
      await createCategory.mutateAsync({ name: addName, parentId: adding.parentId });
    } else if (adding.parentId) {
      await createSkill.mutateAsync({ name: addName, categoryId: adding.parentId });
    }
    setAdding(null);
  });

  // ---- delete ----

  const confirmDelete = run(async () => {
    if (!confirm) return;
    if (confirm.kind === "category") await deleteCategory.mutateAsync(confirm.id);
    else await deleteSkill.mutateAsync(confirm.id);
    setConfirm(null);
  });

  // Category options for re-parenting a node: root + every category that is
  // neither the node itself nor one of its descendants.
  const parentOptionsFor = (id: string) =>
    (categories ?? []).filter((c) => !descendantsOf(id, categories ?? []).has(c.id));

  const editActions = (
    <>
      <IconButton edge="end" size="small" onClick={saveEdit} aria-label="save">
        <CheckIcon fontSize="small" />
      </IconButton>
      <IconButton edge="end" size="small" onClick={() => setEditing(null)} aria-label="cancel">
        <CloseIcon fontSize="small" />
      </IconButton>
    </>
  );

  const addRow = (depth: number, label: string) => (
    <ListItem
      sx={{ pl: 2 + depth * 2, py: 0.5 }}
      secondaryAction={
        <>
          <IconButton edge="end" size="small" onClick={saveAdd} aria-label="save">
            <CheckIcon fontSize="small" />
          </IconButton>
          <IconButton edge="end" size="small" onClick={() => setAdding(null)} aria-label="cancel">
            <CloseIcon fontSize="small" />
          </IconButton>
        </>
      }
    >
      <TextField
        size="small"
        autoFocus
        label={label}
        value={addName}
        onChange={(e) => setAddName(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && addName) saveAdd();
          if (e.key === "Escape") setAdding(null);
        }}
        sx={{ flex: 1, pr: 8 }}
      />
    </ListItem>
  );

  const renderSkill = (id: string, name: string, categoryId: string, depth: number) => {
    const isEditing = editing?.kind === "skill" && editing.id === id;
    return (
      <ListItem
        key={id}
        sx={{ pl: 2 + depth * 2, py: 0.25 }}
        secondaryAction={
          isEditing ? (
            editActions
          ) : (
            <>
              <IconButton edge="end" size="small" onClick={() => startEditSkill(id, name, categoryId)} aria-label="edit skill">
                <EditIcon fontSize="small" />
              </IconButton>
              <IconButton edge="end" size="small" onClick={() => setConfirm({ kind: "skill", id, name })} aria-label="delete skill">
                <DeleteIcon fontSize="small" />
              </IconButton>
            </>
          )
        }
      >
        {isEditing ? (
          <Stack direction="row" spacing={1} sx={{ flex: 1, pr: 8 }}>
            <TextField size="small" label="Name" value={draftName} onChange={(e) => setDraftName(e.target.value)} />
            <TextField
              select
              size="small"
              label="Category"
              value={draftParent}
              onChange={(e) => setDraftParent(e.target.value)}
              sx={{ minWidth: 200 }}
            >
              {(categories ?? []).map((c) => (
                <MenuItem key={c.id} value={c.id}>
                  {pathLabels.get(c.id) ?? c.name}
                </MenuItem>
              ))}
            </TextField>
          </Stack>
        ) : (
          <Typography variant="body2" color="text.secondary">
            {name}
          </Typography>
        )}
      </ListItem>
    );
  };

  const renderCategory = (node: CategoryNode, depth: number, parent: string | null) => {
    const isEditing = editing?.kind === "category" && editing.id === node.id;
    const hasContent = node.children.length > 0 || node.skills.length > 0;
    const isCollapsed = collapsed.has(node.id);
    return (
      <Box key={node.id}>
        <ListItem
          sx={{ pl: 2 + depth * 2, py: 0.5 }}
          secondaryAction={
            isEditing ? (
              editActions
            ) : (
              <>
                <IconButton
                  edge="end"
                  size="small"
                  onClick={(e) => setMenu({ anchor: e.currentTarget, categoryId: node.id })}
                  aria-label="add to category"
                >
                  <AddIcon fontSize="small" />
                </IconButton>
                <IconButton edge="end" size="small" onClick={() => startEditCategory(node, parent)} aria-label="edit category">
                  <EditIcon fontSize="small" />
                </IconButton>
                <IconButton edge="end" size="small" onClick={() => setConfirm({ kind: "category", id: node.id, name: node.name })} aria-label="delete category">
                  <DeleteIcon fontSize="small" />
                </IconButton>
              </>
            )
          }
        >
          {isEditing ? (
            <Stack direction="row" spacing={1} sx={{ flex: 1, pr: 8 }}>
              <TextField size="small" label="Name" value={draftName} onChange={(e) => setDraftName(e.target.value)} />
              <TextField
                select
                size="small"
                label="Parent"
                value={draftParent}
                onChange={(e) => setDraftParent(e.target.value)}
                sx={{ minWidth: 200 }}
              >
                <MenuItem value="">— none (root) —</MenuItem>
                {parentOptionsFor(node.id).map((c) => (
                  <MenuItem key={c.id} value={c.id}>
                    {pathLabels.get(c.id) ?? c.name}
                  </MenuItem>
                ))}
              </TextField>
            </Stack>
          ) : (
            <Stack direction="row" alignItems="center" spacing={0.5}>
              {hasContent ? (
                <IconButton size="small" onClick={() => toggleCollapse(node.id)} aria-label="toggle">
                  {isCollapsed ? <ChevronRightIcon fontSize="small" /> : <ExpandMoreIcon fontSize="small" />}
                </IconButton>
              ) : (
                <Box sx={{ width: 28 }} />
              )}
              <Typography fontWeight={600}>{node.name}</Typography>
            </Stack>
          )}
        </ListItem>

        {!isCollapsed && (
          <>
            {node.skills.map((s) => renderSkill(s.id, s.name, s.categoryId, depth + 1))}
            {adding?.kind === "skill" && adding.parentId === node.id && addRow(depth + 1, "New skill")}
            {node.children.map((c) => renderCategory(c, depth + 1, node.id))}
            {adding?.kind === "category" && adding.parentId === node.id && addRow(depth + 1, "New subcategory")}
          </>
        )}
      </Box>
    );
  };

  return (
    <Box>
      <Stack direction="row" alignItems="center" justifyContent="space-between" mb={3}>
        <Typography variant="h4">Skill Catalog</Typography>
        <Button
          variant="contained"
          startIcon={<AddIcon />}
          onClick={() => startAdd({ kind: "category", parentId: null })}
        >
          Add category
        </Button>
      </Stack>

      {error && (
        <Typography color="error" mb={2}>
          {error}
        </Typography>
      )}

      <Paper sx={{ p: 3 }}>
        <List dense>
          {tree?.map((n) => renderCategory(n, 0, null))}
          {adding?.kind === "category" && adding.parentId === null && addRow(0, "New category")}
          {!tree?.length && adding === null && (
            <Typography color="text.secondary">No categories yet. Add one to get started.</Typography>
          )}
        </List>
      </Paper>

      <Menu anchorEl={menu?.anchor} open={!!menu} onClose={() => setMenu(null)}>
        <MenuItem
          onClick={() => {
            if (menu) startAdd({ kind: "skill", parentId: menu.categoryId });
            setMenu(null);
          }}
        >
          Add skill
        </MenuItem>
        <MenuItem
          onClick={() => {
            if (menu) startAdd({ kind: "category", parentId: menu.categoryId });
            setMenu(null);
          }}
        >
          Add subcategory
        </MenuItem>
      </Menu>

      <Dialog open={!!confirm} onClose={() => setConfirm(null)}>
        <DialogTitle>Delete {confirm?.kind}</DialogTitle>
        <DialogContent>
          <DialogContentText>
            Delete &ldquo;{confirm?.name}&rdquo;? This cannot be undone.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirm(null)}>Cancel</Button>
          <Button color="error" variant="contained" onClick={confirmDelete}>
            Delete
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
