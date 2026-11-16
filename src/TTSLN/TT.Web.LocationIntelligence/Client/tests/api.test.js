import { test } from "node:test";
import assert from "node:assert/strict";
import { createApi } from "../api.js";

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
