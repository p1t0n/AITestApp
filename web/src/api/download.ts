// One place a response becomes a file the browser saves. Fetched through axios rather than linked
// to directly so the session token rides along on the request, and named from the server's own
// Content-Disposition so the filename is decided in one place.
import type { AxiosResponse } from "axios";

export function saveAsFile(response: AxiosResponse<Blob>, fallbackName: string): void {
  const disposition = (response.headers["content-disposition"] as string | undefined) ?? "";
  const filename = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? fallbackName;
  const url = URL.createObjectURL(response.data);
  try {
    const link = document.createElement("a");
    link.href = url;
    link.download = filename;
    link.click();
  } finally {
    URL.revokeObjectURL(url);
  }
}
