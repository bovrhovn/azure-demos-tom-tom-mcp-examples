export function createApi(getToken, fetcher = fetch) {
  return async function api(path, { method = "GET", body, signal } = {}) {
    const response = await fetcher(path, {
      method,
      signal,
      credentials: "same-origin",
      headers: {
        Accept: "application/json",
        ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
        ...(method !== "GET" ? { "X-CSRF-TOKEN": getToken() } : {})
      },
      ...(body !== undefined ? { body: JSON.stringify(body) } : {})
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.title ?? (response.status === 401
        ? "Access denied. Check the Azure application's access settings."
        : response.status === 429 ? "The service is busy. Please wait a moment."
        : `Request failed (${response.status}).`));
    }
    return response.status === 204 ? undefined : response.json();
  };
}
