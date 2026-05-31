import { useState } from "react";
import {
  Box,
  Button,
  CircularProgress,
  List,
  ListItem,
  ListItemText,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { useCategories, useCategoryTree, useCreateCategory, useCreateSkill } from "../api";
import type { CategoryNode } from "../types";
import { apiErrorMessage } from "../api";

function TreeNode({ node, depth }: { node: CategoryNode; depth: number }) {
  return (
    <>
      <ListItem sx={{ pl: 2 + depth * 2, py: 0.5 }}>
        <ListItemText
          primary={<Typography fontWeight={600}>{node.name}</Typography>}
          secondary={node.skills.map((s) => s.name).join(", ") || undefined}
        />
      </ListItem>
      {node.children.map((c) => (
        <TreeNode key={c.id} node={c} depth={depth + 1} />
      ))}
    </>
  );
}

export default function CatalogPage() {
  const { data: tree, isLoading } = useCategoryTree();
  const { data: categories } = useCategories();
  const createCategory = useCreateCategory();
  const createSkill = useCreateSkill();

  const [catName, setCatName] = useState("");
  const [parentId, setParentId] = useState("");
  const [skillName, setSkillName] = useState("");
  const [skillCat, setSkillCat] = useState("");
  const [error, setError] = useState<string | null>(null);

  if (isLoading) return <CircularProgress />;

  const run = (fn: () => Promise<unknown>) => async () => {
    setError(null);
    try {
      await fn();
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  };

  return (
    <Box>
      <Typography variant="h4" mb={3}>
        Skill Catalog
      </Typography>

      {error && (
        <Typography color="error" mb={2}>
          {error}
        </Typography>
      )}

      <Stack direction={{ xs: "column", md: "row" }} spacing={3}>
        <Paper sx={{ p: 3, flex: 1 }}>
          <Typography variant="h6" gutterBottom>
            Categories &amp; skills
          </Typography>
          <List dense>
            {tree?.map((n) => (
              <TreeNode key={n.id} node={n} depth={0} />
            ))}
          </List>
        </Paper>

        <Stack spacing={3} sx={{ width: { md: 340 } }}>
          <Paper sx={{ p: 3 }}>
            <Typography variant="h6" gutterBottom>
              Add category
            </Typography>
            <Stack spacing={2}>
              <TextField label="Name" value={catName} onChange={(e) => setCatName(e.target.value)} />
              <TextField
                select
                label="Parent (optional)"
                value={parentId}
                onChange={(e) => setParentId(e.target.value)}
              >
                <MenuItem value="">— none (root) —</MenuItem>
                {categories?.map((c) => (
                  <MenuItem key={c.id} value={c.id}>
                    {c.name}
                  </MenuItem>
                ))}
              </TextField>
              <Button
                variant="contained"
                disabled={!catName}
                onClick={run(async () => {
                  await createCategory.mutateAsync({ name: catName, parentId: parentId || null });
                  setCatName("");
                  setParentId("");
                })}
              >
                Add category
              </Button>
            </Stack>
          </Paper>

          <Paper sx={{ p: 3 }}>
            <Typography variant="h6" gutterBottom>
              Add skill
            </Typography>
            <Stack spacing={2}>
              <TextField label="Name" value={skillName} onChange={(e) => setSkillName(e.target.value)} />
              <TextField
                select
                label="Category"
                value={skillCat}
                onChange={(e) => setSkillCat(e.target.value)}
              >
                {categories?.map((c) => (
                  <MenuItem key={c.id} value={c.id}>
                    {c.name}
                  </MenuItem>
                ))}
              </TextField>
              <Button
                variant="contained"
                disabled={!skillName || !skillCat}
                onClick={run(async () => {
                  await createSkill.mutateAsync({ name: skillName, categoryId: skillCat });
                  setSkillName("");
                })}
              >
                Add skill
              </Button>
            </Stack>
          </Paper>
        </Stack>
      </Stack>
    </Box>
  );
}
