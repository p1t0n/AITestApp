// The skill catalog: a category tree plus the skills hanging off it. Category and skill writes
// invalidate each other because a skill row carries its category's name.
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Category, CategoryNode, SkillDto } from "../types";
import { http } from "./http";

export function useCategories() {
  return useQuery({
    queryKey: ["categories"],
    queryFn: async () => (await http.get<Category[]>("/catalog/categories")).data,
  });
}

export function useCategoryTree() {
  return useQuery({
    queryKey: ["categories", "tree"],
    queryFn: async () => (await http.get<CategoryNode[]>("/catalog/categories/tree")).data,
  });
}

export function useSkills() {
  return useQuery({
    queryKey: ["skills"],
    queryFn: async () => (await http.get<SkillDto[]>("/catalog/skills")).data,
  });
}

export function useCreateCategory() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: { name: string; parentId: string | null }) =>
      (await http.post<Category>("/catalog/categories", dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["categories"] }),
  });
}

export function useUpdateCategory() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: { id: string; name: string; parentId: string | null }) =>
      (await http.put<Category>(`/catalog/categories/${id}`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["categories"] });
      qc.invalidateQueries({ queryKey: ["skills"] });
    },
  });
}

export function useDeleteCategory() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/catalog/categories/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["categories"] }),
  });
}

export function useCreateSkill() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: { name: string; categoryId: string }) =>
      (await http.post<SkillDto>("/catalog/skills", dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["skills"] });
      qc.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}

export function useUpdateSkill() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: { id: string; name: string; categoryId: string }) =>
      (await http.put<SkillDto>(`/catalog/skills/${id}`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["skills"] });
      qc.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}

export function useDeleteSkill() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/catalog/skills/${id}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["skills"] });
      qc.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}
