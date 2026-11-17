function defaultErrorMessage(status) {
  if (status === 401) return "Access denied. Check the Azure application's access settings.";
  if (status === 429) return "The service is busy. Please wait a moment.";
  return `Request failed (${status}).`;
}

export function createApi(getToken, fetcherOrOptions, maybeOptions = {}) {
  const hasCustomFetcher = typeof fetcherOrOptions === "function";
  const options = (hasCustomFetcher ? maybeOptions : fetcherOrOptions) ?? {};
  const fetcher = hasCustomFetcher
    ? fetcherOrOptions
    : (options.fetcher ?? ((path, init) => globalThis.fetch(path, init)));
  const getHeaderName = options.getHeaderName ?? (() => "X-CSRF-TOKEN");
  const onCsrfInvalid = options.onCsrfInvalid;

  async function execute(path, { method = "GET", body, headers, signal, etag } = {}, retried = false) {
    const upperMethod = method.toUpperCase();
    const isSafeMethod = upperMethod === "GET" || upperMethod === "HEAD" || upperMethod === "OPTIONS";
    const requestHeaders = {
      Accept: "application/json",
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      ...(!isSafeMethod ? { [getHeaderName()]: getToken() } : {}),
      ...(etag ? { "If-None-Match": etag } : {}),
      ...(headers ?? {})
    };

    const response = await fetcher(path, {
      method: upperMethod,
      signal,
      credentials: "same-origin",
      headers: requestHeaders,
      ...(body !== undefined ? { body: JSON.stringify(body) } : {})
    });

    const responseEtag = response.headers?.get?.("ETag") ?? null;
    if (response.status === 304) {
      return {
        status: 304,
        notModified: true,
        data: undefined,
        etag: responseEtag ?? etag ?? null
      };
    }

    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      if (!retried && response.status === 400 && problem?.code === "csrf_invalid" && typeof onCsrfInvalid === "function") {
        await onCsrfInvalid();
        return execute(path, { method: upperMethod, body, headers, signal, etag }, true);
      }

      const error = new Error(problem?.title ?? defaultErrorMessage(response.status));
      error.status = response.status;
      error.code = problem?.code ?? null;
      error.traceId = problem?.traceId ?? null;
      error.problem = problem;
      throw error;
    }

    const data = response.status === 204 ? undefined : await response.json();
    return {
      status: response.status,
      notModified: false,
      data,
      etag: responseEtag
    };
  }

  async function api(path, requestOptions = {}) {
    const result = await execute(path, requestOptions, false);
    if (result.notModified) {
      return { notModified: true, etag: result.etag };
    }
    return result.data;
  }

  api.requestWithMeta = (path, requestOptions = {}) => execute(path, requestOptions, false);
  return api;
}
