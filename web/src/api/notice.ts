// The versioned transparency notice (P1T-183). Not a consent control, deliberately: under
// Art. 6(1)(b) necessity does the legal work, and a consent checkbox where another basis applies
// would be misleading. What the person does is acknowledge a text, and the version they
// acknowledged is recorded so it can be proven — and read back — afterwards.
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { http } from "./http";

export interface TransparencyNotice {
  version: string;
  /** The notice itself, as Markdown. */
  text: string;
}

export interface NoticeStatus {
  acknowledgedVersion: string | null;
  currentVersion: string;
  /**
   * A newer notice this account has not acknowledged, or null. Non-null means "show it to them" —
   * never "stop them doing anything". Nothing in the app is gated on acknowledging.
   */
  pendingVersion: string | null;
}

/** The notice as it stands today. Readable signed out — the sign-up form needs it. */
export function useTransparencyNotice() {
  return useQuery({
    queryKey: ["notice", "current"],
    queryFn: async () => (await http.get<TransparencyNotice>("/notice")).data,
    // The text changes when a version ships, not while somebody is looking at it.
    staleTime: Infinity,
  });
}

/** Whether a newer notice is waiting for the signed-in account. */
export function useNoticeStatus(enabled = true) {
  return useQuery({
    queryKey: ["notice", "status"],
    queryFn: async () => (await http.get<NoticeStatus>("/notice/status")).data,
    enabled,
  });
}

/** Records that the signed-in person has read the given version. */
export function useAcknowledgeNotice() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (version: string) =>
      (await http.post<NoticeStatus>("/notice/acknowledge", { version })).data,
    onSuccess: (status) => qc.setQueryData(["notice", "status"], status),
  });
}
