// Registers jest-dom matchers (toBeInTheDocument, …) on vitest's expect, and unmounts rendered
// trees between tests (testing-library's auto-cleanup needs global afterEach, which we don't use).
import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(cleanup);
