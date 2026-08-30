import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ErrorNotice } from "./ErrorNotice";
import { apiErrorMessage } from "../api";
import { SseHttpError } from "../sse";
import { AxiosError, AxiosHeaders } from "axios";

/** The 429 the per-user token cap returns, as axios hands it to a component. */
function usageCapAxiosError(message: string): AxiosError {
  const headers = new AxiosHeaders();
  const config = { headers };
  return new AxiosError("Request failed with status code 429", "ERR_BAD_REQUEST", config, null, {
    status: 429,
    statusText: "Too Many Requests",
    data: { error: message },
    headers,
    config,
  } as never);
}

describe("ErrorNotice (P1T-153)", () => {
  it("renders nothing when there is no message", () => {
    const { container } = render(<ErrorNotice message={null} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders the message as an announced alert", () => {
    render(<ErrorNotice message="Employee not found." />);
    expect(screen.getByRole("alert")).toHaveTextContent("Employee not found.");
  });

  it("renders a detail line under the message when the transport carries both", () => {
    render(<ErrorNotice message="Staffing run failed" detail="The model timed out." />);
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Staffing run failed");
    expect(alert).toHaveTextContent("The model timed out.");
  });

  it("keeps the 429 usage-cap text intact on the axios path", () => {
    const capped = "Daily token cap reached (50,000). Try again tomorrow.";
    render(<ErrorNotice message={apiErrorMessage(usageCapAxiosError(capped))} />);
    expect(screen.getByRole("alert")).toHaveTextContent(capped);
  });

  it("keeps the 429 usage-cap text intact on the streaming path", () => {
    const capped = "Daily token cap reached (50,000). Try again tomorrow.";
    const err = new SseHttpError(429, { error: capped }, capped);
    render(<ErrorNotice message={err.message} />);
    expect(screen.getByRole("alert")).toHaveTextContent(capped);
  });
});
