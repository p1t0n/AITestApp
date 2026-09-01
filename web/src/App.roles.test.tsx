import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import App from "./App";
import { setSession } from "./auth/session";

// The pages behind the guard are stand-ins: this file is about which route renders, not what it
// renders. Reaching the network would only add flake to that question.
vi.mock("./pages/ExpertsPage", () => ({ default: () => <div>the roster page</div> }));
vi.mock("./pages/UsersPage", () => ({ default: () => <div>the users page</div> }));
vi.mock("./pages/CatalogPage", () => ({ default: () => <div>the catalog page</div> }));
vi.mock("./components/AgentWidget", () => ({ default: () => null }));
// The real sign-in page drives a React Query mutation; this file mounts no client, and the gate is
// only ever asserted here as a destination.
vi.mock("./pages/SigninPage", () => ({ default: () => <div>the sign-in gate</div> }));

function signedInAs(role: "ServiceManager" | "Expert") {
  localStorage.clear();
  setSession("a-token", `${role}@example.com`, role);
}

describe("route audiences (P1T-181)", () => {
  it("lands a Service Manager on the roster", () => {
    signedInAs("ServiceManager");

    render(
      <MemoryRouter initialEntries={["/"]}>
        <App />
      </MemoryRouter>,
    );

    expect(screen.getByText("the roster page")).toBeInTheDocument();
  });

  // The whole point of the slice: an Expert asking for a staff route is a signed-in person, so the
  // answer is their own page — not the sign-in screen, which would tell them they are signed out.
  it.each(["/", "/users", "/catalog", "/experts/abc"])(
    "sends an Expert asking for %s to their own landing page, not /signin",
    (path) => {
      signedInAs("Expert");

      render(
        <MemoryRouter initialEntries={[path]}>
          <App />
        </MemoryRouter>,
      );

      expect(screen.getByRole("heading", { name: "My workspace" })).toBeInTheDocument();
      expect(screen.queryByText("the sign-in gate")).not.toBeInTheDocument();
      expect(screen.queryByText("the roster page")).not.toBeInTheDocument();
    },
  );

  it("sends a Service Manager asking for the Expert page to the roster", () => {
    signedInAs("ServiceManager");

    render(
      <MemoryRouter initialEntries={["/me"]}>
        <App />
      </MemoryRouter>,
    );

    expect(screen.getByText("the roster page")).toBeInTheDocument();
  });

  it("offers an Expert only the places an Expert can reach", () => {
    signedInAs("Expert");

    render(
      <MemoryRouter initialEntries={["/me"]}>
        <App />
      </MemoryRouter>,
    );

    expect(screen.getByRole("link", { name: "My workspace" })).toBeInTheDocument();
    for (const staffPlace of ["CVs", "Skill Catalog", "Users"]) {
      expect(screen.queryByRole("link", { name: staffPlace })).not.toBeInTheDocument();
    }
  });

  // A session stored before the split carries a token with neither the role nor the token-version
  // claim, so the server refuses it. The gate is the honest destination for it.
  it("treats a session with no stored role as signed out", () => {
    localStorage.clear();
    localStorage.setItem("em.session.token", "a-pre-split-token");

    render(
      <MemoryRouter initialEntries={["/"]}>
        <App />
      </MemoryRouter>,
    );

    expect(screen.getByText("the sign-in gate")).toBeInTheDocument();
  });
});
