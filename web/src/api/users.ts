// User management — an Administrator's surface, and the only place a role is changed (P1T-239).
// The per-user token caps live on this shape because they are administered here, but they are
// *spent* through the Agents service — see ./agents/usage.
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { SessionRole } from "../auth/roles";
import { http } from "./http";

export type UserStatus = "Active" | "Deactivated";

export interface UserSummary {
  id: string;
  email: string;
  /** Which audience the account belongs to. Changed through `useChangeUserRole`, never `UpdateUser`. */
  role: SessionRole;
  status: UserStatus;
  dailyTokenCap: number | null;
  weeklyTokenCap: number | null;
  monthlyTokenCap: number | null;
  passkeyCount: number;
  createdAt: string;
}

/**
 * Editable fields. The role is deliberately absent, mirroring `UpdateUserDto` on the server: a
 * privilege must not move as a side effect of editing an email or a token cap (P1T-229).
 */
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

/**
 * The two refusals `UserService.ChangeRoleAsync` enforces, as the server words them.
 *
 * They are duplicated here rather than invented, and that duplication is the point: the SPA can
 * see both conditions in the list it already holds, so it can say *why* a control is blocked
 * before anybody clicks it — but the sentence a person reads has to be the same one whether it
 * came from this list or from a 409 the other tab caused. The server remains the authority; these
 * strings are only what that authority sounds like.
 */
export const SELF_ROLE_REFUSAL = "You cannot change your own role.";
export const LAST_ADMINISTRATOR_REFUSAL = "The last Administrator cannot be demoted.";

/**
 * Moves one account between roles, through the endpoint of its own (`PUT /users/{id}/role`).
 *
 * A change ends the target's sessions — the server bumps their token version, because the role
 * travels in the token — so this is never folded into the edit dialog's save.
 */
export function useChangeUserRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, role }: { id: string; role: SessionRole }) =>
      (await http.put<UserSummary>(`/users/${id}/role`, { role })).data,
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
