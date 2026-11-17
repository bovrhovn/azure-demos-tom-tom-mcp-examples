import { test } from "node:test";
import assert from "node:assert/strict";
import { createApi } from "../api.js";
import { createSessionsApi } from "../services/sessionsApi.js";

test("POST uses same-origin cookies, JSON and the server-issued CSRF header", async () => {
  const api = createApi(() => "verification", async (path, request) => {
    assert.equal(path, "/api/sessions/");
    assert.equal(request.credentials, "same-origin");
    assert.equal(request.headers["X-CSRF-TOKEN"], "verification");
    assert.equal(request.headers["Content-Type"], "application/json");
    assert.deepEqual(JSON.parse(request.body), { title: "Routes" });
    return Response.json({ id: "session" });
  });
  assert.deepEqual(await api("/api/sessions/", { method: "POST", body: { title: "Routes" } }), { id: "session" });
});

test("Problem Details errors are surfaced and never automatically retried", async () => {
  let calls = 0;
  const api = createApi(() => "", async () => {
    calls++;
    return Response.json({ title: "Conversation changed." }, { status: 409 });
  });
  await assert.rejects(api("/api/sessions/"), { message: "Conversation changed." });
  assert.equal(calls, 1);
});

test("non-JSON platform access failures are explicit", async () => {
  const api = createApi(() => "", async () => new Response("", { status: 401 }));
  await assert.rejects(api("/api/runtime"), /Access denied/);
});

test("PUT and DELETE send CSRF and handle empty deletion responses", async () => {
  const requests = [];
  const api = createApi(() => "verification", async (_, request) => {
    requests.push(request);
    return request.method === "DELETE" ? new Response(null, { status: 204 }) : Response.json({ title: "Renamed" });
  });
  assert.deepEqual(await api("/api/sessions/id", { method: "PUT", body: { title: "Renamed" } }), { title: "Renamed" });
  assert.equal(await api("/api/sessions/id", { method: "DELETE" }), undefined);
  assert.deepEqual(requests.map((request) => request.headers["X-CSRF-TOKEN"]), ["verification", "verification"]);
  assert.equal(requests[1].body, undefined);
});

test("requestWithMeta sends If-None-Match and handles 304 Not Modified without parsing JSON", async () => {
  const api = createApi(() => "", async (path, request) => {
    assert.equal(path, "/api/sessions/one");
    assert.equal(request.headers["If-None-Match"], '"etag-1"');
    return new Response(null, { status: 304, headers: { ETag: '"etag-1"' } });
  });
  const result = await api.requestWithMeta("/api/sessions/one", { etag: '"etag-1"' });
  assert.deepEqual(result, { data: undefined, etag: '"etag-1"', notModified: true, status: 304 });
});

test("csrf_invalid refreshes CSRF token once and retries with the identical payload", async () => {
  let token = "stale-token";
  let refreshCount = 0;
  const seen = [];
  const api = createApi(
    () => token,
    async (path, request) => {
      seen.push({ path, csrf: request.headers["X-CSRF-TOKEN"], body: JSON.parse(request.body) });
      if (seen.length === 1) {
        return Response.json({ title: "Invalid or missing antiforgery token.", code: "csrf_invalid" }, { status: 400 });
      }
      return Response.json({ id: "one" }, { headers: { ETag: '"etag-2"' } });
    },
    {
      onCsrfInvalid: async () => {
        refreshCount++;
        token = "fresh-token";
      }
    }
  );

  const payload = { requestId: "req-123", content: "Route to Utrecht" };
  const result = await api.requestWithMeta("/api/sessions/one/messages", { method: "POST", body: payload });
  assert.equal(refreshCount, 1);
  assert.equal(seen.length, 2);
  assert.equal(seen[0].csrf, "stale-token");
  assert.equal(seen[1].csrf, "fresh-token");
  assert.deepEqual(seen[0].body, payload);
  assert.deepEqual(seen[1].body, payload);
  assert.equal(result.etag, '"etag-2"');
  assert.deepEqual(result.data, { id: "one" });
});

test("direct api() and sessionsApi.get() send If-None-Match and handle 304 without calling response.json()", async () => {
  const seenHeaders = [];
  const api = createApi(() => "", async (path, request) => {
    seenHeaders.push({ path, ifNoneMatch: request.headers["If-None-Match"] });
    return {
      ok: false,
      status: 304,
      headers: new Headers({ ETag: '"etag-opaque"' }),
      json: async () => {
        throw new SyntaxError("Unexpected end of JSON input");
      }
    };
  });

  const direct = await api("/api/sessions/one", { etag: '"etag-opaque"' });
  assert.deepEqual(direct, { notModified: true, etag: '"etag-opaque"' });

  const sessionsApi = createSessionsApi(api, () => "/api/sessions/");
  const sessionResult = await sessionsApi.get("one", { ifNoneMatch: '"etag-opaque"' });
  assert.deepEqual(sessionResult, { notModified: true, etag: '"etag-opaque"' });
  assert.deepEqual(seenHeaders, [
    { path: "/api/sessions/one", ifNoneMatch: '"etag-opaque"' },
    { path: "/api/sessions/one", ifNoneMatch: '"etag-opaque"' }
  ]);
});

test("csrf_invalid does not loop infinitely when the retried request also fails with csrf_invalid", async () => {
  let refreshCount = 0;
  let fetchCalls = 0;
  const api = createApi(
    () => `token-${refreshCount}`,
    async () => {
      fetchCalls++;
      return Response.json(
        { title: "Invalid request verification token.", code: "csrf_invalid", traceId: "trace-1" },
        { status: 400 }
      );
    },
    {
      onCsrfInvalid: async () => {
        refreshCount++;
      }
    }
  );

  await assert.rejects(
    api("/api/sessions", { method: "POST", body: { title: "Loop check" } }),
    (error) => {
      assert.equal(error.status, 400);
      assert.equal(error.code, "csrf_invalid");
      assert.equal(error.traceId, "trace-1");
      return true;
    }
  );
  assert.equal(refreshCount, 1);
  assert.equal(fetchCalls, 2);
});

test("non-csrf 400 Problem Details errors do not trigger onCsrfInvalid", async () => {
  let refreshCount = 0;
  let fetchCalls = 0;
  const api = createApi(
    () => "valid-token",
    async () => {
      fetchCalls++;
      return Response.json({ title: "Titles must not exceed 120 characters.", code: "invalid_title" }, { status: 400 });
    },
    {
      onCsrfInvalid: async () => {
        refreshCount++;
      }
    }
  );

  await assert.rejects(
    api("/api/sessions", { method: "POST", body: { title: "Too long" } }),
    (error) => error.status === 400 && error.code === "invalid_title"
  );
  assert.equal(refreshCount, 0);
  assert.equal(fetchCalls, 1);
});
