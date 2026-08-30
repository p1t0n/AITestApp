// User management. Flat roles: any signed-in user can manage any user (P1T-21). The per-user token
// caps live on this shape because they are administered here, but they are *spent* through the
// Agents service — see ./agents/usage.
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { http } from "./http";

export type UserStatus = "Active" | "Deactivated";

export interface UserSummary {
  id: string;
  email: string;
  status: UserStatus;
  dailyTokenCap: number | null;
  weeklyTokenCap: number | null;
  monthlyTokenCap: number | null;
  passkeyCount: number;
  createdAt: string;
}

export interface UpdateUser {
  email: string;
  status: UserStatus;
  dailyTokenCap: number | null;
  weeklyTokenCap: number | null;
  monthlyTokenCap: number | null;
}

export function useUsers() {
  return useQuery({
    queryKey: ["users"],
    queryFn: async () => (await http.get<UserSummary[]>("/users")).data,
  });
}

export function useUpdateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: { id: string } & UpdateUser) =>
      (await http.put<UserSummary>(`/users/${id}`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }),
  });
}

export function useDeleteUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/users/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }),
  });
}
