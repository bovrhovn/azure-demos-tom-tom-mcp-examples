export function createSessionsApi(http, getBaseUrl = () => "/api/sessions") {
  const baseUrl = () => (getBaseUrl() || "/api/sessions").replace(/\/+$/, "");

  return {
    list({ continuationToken, signal } = {}) {
      const suffix = continuationToken ? `?continuationToken=${encodeURIComponent(continuationToken)}` : "";
      return http(`${baseUrl()}${suffix}`, { signal });
    },

    async get(id, { signal, ifNoneMatch } = {}) {
      const result = await http.requestWithMeta(`${baseUrl()}/${encodeURIComponent(id)}`, {
        signal,
        etag: ifNoneMatch
      });
      if (result.notModified) {
        return { notModified: true, etag: result.etag };
      }
      return { notModified: false, session: result.data, etag: result.etag };
    },

    async create({ title = "" } = {}) {
      const result = await http.requestWithMeta(baseUrl(), {
        method: "POST",
        body: { title }
      });
      return { session: result.data, etag: result.etag };
    },

    async rename(id, { title }) {
      const result = await http.requestWithMeta(`${baseUrl()}/${encodeURIComponent(id)}`, {
        method: "PUT",
        body: { title }
      });
      return { session: result.data, etag: result.etag };
    },

    remove(id) {
      return http(`${baseUrl()}/${encodeURIComponent(id)}`, {
        method: "DELETE"
      });
    },

    async sendMessage(id, { requestId, content }) {
      const result = await http.requestWithMeta(`${baseUrl()}/${encodeURIComponent(id)}/messages`, {
        method: "POST",
        body: { requestId, content }
      });
      return { session: result.data, etag: result.etag };
    }
  };
}
