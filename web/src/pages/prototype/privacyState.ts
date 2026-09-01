/**
 * PROTOTYPE — throwaway. Fake state for the Privacy & data page variants (P1T-175).
 *
 * The question this prototype answers is not "does the backend work" — nothing here talks to it.
 * It is: do the five profile states read at a glance, and are pause and delete visibly different
 * kinds of thing. So the state is a plain object the switcher can mutate, and every variant renders
 * from the same object.
 */

export type Basis = "contract" | "legitimate-interest";

export interface ScoredAgainst {
  job: string;
  score: number;
  band: string;
  rationale: string;
}

export interface PrivacyState {
  /** false = registered but owns no Expert row (claim pending, or nothing matched). */
  ownsRow: boolean;
  claimPending: boolean;
  /** null = visible. A timestamp = paused by the Expert. */
  hiddenAt: string | null;
  /** contract = 6(1)(b), self-registered. legitimate-interest = staff-created, unclaimed. */
  basis: Basis;
  expiresOn: string;
  daysToExpiry: number;
  scored: ScoredAgainst[];
}

export const INITIAL: PrivacyState = {
  ownsRow: true,
  claimPending: false,
  hiddenAt: null,
  basis: "contract",
  expiresOn: "2028-09-01",
  daysToExpiry: 730,
  scored: [
    {
      job: "Senior platform engineer, Rotterdam",
      score: 78,
      band: "Strong",
      rationale:
        "Eight years on distributed .NET services and two Kubernetes migrations line up with the " +
        "platform requirement. No Terraform evidence in the CV, which the role asks for explicitly.",
    },
    {
      job: "Data engineer, remote",
      score: 41,
      band: "Weak",
      rationale:
        "Pipeline work is incidental rather than primary. The Spark requirement is unmet and the " +
        "recent roles move away from data toward platform.",
    },
  ],
};

/** The five states the surface has to make legible, plus the ones that coincide. */
export interface Derived {
  paused: boolean;
  expiring: boolean;
  canObject: boolean;
  canExport: "right" | "courtesy";
  scannable: boolean;
}

export function derive(s: PrivacyState): Derived {
  return {
    paused: s.hiddenAt !== null,
    expiring: s.daysToExpiry <= 30,
    // Art. 21 objection exists only under legitimate interest.
    canObject: s.basis === "legitimate-interest",
    // Art. 20 is owed under contract; offered as a courtesy otherwise.
    canExport: s.basis === "contract" ? "right" : "courtesy",
    // Only a 6(1)(b) row carries an Art. 22(2) route, so only it is scanned.
    scannable: s.basis === "contract",
  };
}

export interface Actions {
  pause(): void;
  unpause(): void;
  exportData(): void;
  object(): void;
  deleteAll(controlWord: string): void;
  contest(job: string): void;
}

/**
 * This project has no `vite-env.d.ts`, so `import.meta.env` is untyped here. A throwaway prototype
 * is not a reason to add vite/client types to the whole project, so the cast stays local.
 */
export const PROTOTYPES_ENABLED =
  (import.meta as unknown as { env?: { PROD?: boolean } }).env?.PROD !== true;
