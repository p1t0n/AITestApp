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
