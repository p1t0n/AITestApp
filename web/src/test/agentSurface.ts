import { screen } from "@testing-library/react";
import { SURFACE_PICKER_LABEL } from "../components/AgentWidget";

/** Both `userEvent` and a `userEvent.setup()` instance satisfy this — specs use each. */
type Clicker = { click: (element: Element) => Promise<unknown> };

/** The picker button, whose accessible name is `"Agent surface: <current label>"`. */
export const SURFACE_PICKER_NAME = new RegExp(`^${SURFACE_PICKER_LABEL}: `);

/**
 * Navigate the agent dock to a surface by its label (P1T-152). The dock's navigation is a grouped
 * picker rather than a tab strip, so every spec that used to click a tab goes through here — one
 * place to change if the navigation shape moves again.
 */
export async function selectAgentSurface(user: Clicker, label: string) {
  await user.click(screen.getByRole("button", { name: SURFACE_PICKER_NAME }));
  await user.click(await screen.findByRole("menuitem", { name: label }));
}

/** The surface the dock is currently showing, as the picker reports it. */
export function currentAgentSurface(): string {
  return screen.getByRole("button", { name: SURFACE_PICKER_NAME }).textContent ?? "";
}

/**
 * The dock pane a surface is showing in, by its label (EXP-30). Every visited surface stays
 * mounted, so a page-wide query can hit a value two panes are both holding — a JD that was drilled
 * in from the Shortlist is genuinely in both forms. Inactive panes are `hidden`, so only the active
 * one has a `region` in the accessibility tree: asking for the pane by name is both the scope and
 * the assertion that the dock is pointed at it.
 */
export function agentSurfacePane(label: string): HTMLElement {
  return screen.getByRole("region", { name: label });
}
