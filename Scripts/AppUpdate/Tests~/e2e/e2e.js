// node Scripts/AppUpdate/Tests~/e2e/e2e.js  (needs Mono, Node 18+ and the VRSP repo next to this one, or VRSP_BACKEND)
// End-to-end: real VRSP DAS-app controller over HTTP (in-memory DB, a local stand-in for the GCS bucket with signed
// links), the real DAS release code (E2eTool.exe built from AppReleaseCore.cs) and the real DASUpdater.exe changing a
// DAS folder.  publish 1.0.0 -> PC on 1.0.0 -> publish 1.0.1 (pilot) -> pilot PC updates, others don't ->
// 1.0.1 for everyone -> PC updates -> withdraw 1.0.1 -> PC goes back to 1.0.0; plus refusals (bad signature, damaged
// download, non-admin publish, version not higher).
const path = require("path"), fs = require("fs"), crypto = require("crypto"), http = require("http");
const { execFileSync, spawn } = require("child_process");

const DAS = process.env.DAS_REPO || path.resolve(__dirname, "../../../..");
const VRSP = path.resolve(process.env.VRSP_BACKEND || path.join(DAS, "../my-new-repo/backend"));
const UPDATER = path.join(DAS, "Scripts/AppUpdate/Updater~/DASUpdater.exe");
const W = fs.mkdtempSync(path.join(require("os").tmpdir(), "das-e2e-"));
const TOOL = path.join(W, "E2eTool.exe");
const express = require(path.join(VRSP, "node_modules/express"));

let pass = 0, fail = 0;
const check = (name, ok, extra) => { if (ok) { pass++; console.log("  ok  ", name); } else { fail++; console.log("  FAIL", name, extra !== undefined ? JSON.stringify(extra) : ""); } };
const sha = (buf) => crypto.createHash("sha256").update(buf).digest("hex");

// ── build the C# tool from the real core ──
execFileSync("mcs", ["-nologo", "-out:" + TOOL, "-r:System.Core", path.join(__dirname, "E2eTool.cs"), path.join(DAS, "Scripts/AppUpdate/AppReleaseCore.cs")], { stdio: "inherit" });
const tool = (...a) => execFileSync("mono", [TOOL, ...a], { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
const keys = path.join(W, "keys"); fs.mkdirSync(keys);
tool("keypair", keys);
const otherKeys = path.join(W, "other-keys"); fs.mkdirSync(otherKeys);
tool("keypair", otherKeys);
process.env.DAS_APP_PUBLIC_KEY = fs.readFileSync(path.join(keys, "pub.xml"), "utf8");   // VRSP's .env

// ── stand-ins: bucket (signed links served by this process), Mongo models ──
const objects = new Map();
let base = "", gets = 0;
const linkSecret = crypto.randomBytes(16).toString("hex");
const signLink = (op, p, ttl) => { const exp = Date.now() + ttl; return `${base}/gcs/${op}/${encodeURIComponent(p)}?exp=${exp}&sig=${crypto.createHmac("sha256", linkSecret).update(op + p + exp).digest("hex")}`; };
const gcs = {
  bucket: { file: (name) => ({ exists: async () => [objects.has(name)], getMetadata: async () => [{ size: String(objects.get(name)?.length ?? 0) }] }) },
  getSignedUrl: async (p, ttl = 7 * 864e5) => signLink("get", p, ttl),
  getUploadUrl: async (p) => signLink("put", p, 15 * 60e3),
};
const releases = [];
const matches = (r, q) => Object.entries(q || {}).every(([k, v]) => (v && typeof v === "object" && "$in" in v ? v.$in.includes(r[k]) : r[k] === v));
const cursor = (rows) => { const c = { select() { return c; }, sort() { return c; }, lean: async () => rows.map((r) => ({ ...r })), then: (a, b) => Promise.resolve(rows).then(a, b) }; return c; };
class Rel { constructor(o) { Object.assign(this, { history: [], ...o, createdAt: new Date(), updatedAt: new Date() }); } async save() { this.updatedAt = new Date(); return this; } }
const DasAppRelease = {
  async create(o) { const d = new Rel(o); releases.push(d); return d; },
  findOne(q) { const r = releases.find((x) => matches(x, q)) || null; const c = { select() { return c; }, lean: async () => (r ? { ...r } : null), then: (a, b) => Promise.resolve(r).then(a, b) }; return c; },
  find(q) { return cursor(releases.filter((x) => matches(x, q))); },
};
const users = new Map();
const User = {
  async updateOne(q, u) {
    const p = users.get(String(q._id));
    if (!p || (q["das.enabled"] && !p.das.enabled)) return { matchedCount: 0 };
    for (const [k, v] of Object.entries(u.$set)) p.das[k.replace("das.", "")] = v;
    return { matchedCount: 1 };
  },
  find() { return cursor([...users.values()].filter((p) => p.das.enabled)); },
};
for (const [file, exp] of Object.entries({
  [path.join(VRSP, "models/DasAppRelease.js")]: DasAppRelease,
  [path.join(VRSP, "models/User.js")]: User,
  [path.join(VRSP, "utils/gcs.js")]: gcs,
})) require.cache[file] = { id: file, filename: file, loaded: true, exports: exp };
const ctl = require(path.join(VRSP, "controllers/dasAppController.js"));

const person = (id, role, extra = {}) => ({ _id: id, name: id.toUpperCase(), email: `${id}@m.com`, das: { enabled: true, role, appVersion: "", appPilot: false, ...extra } });
[person("pk", "Admin"), person("eng", "User"), person("pilot", "User", { appPilot: true }), person("viewer", "Viewer")].forEach((p) => users.set(p._id, p));

// ── the server: same paths as routes/dasRoutes.js; sign-in stands in as an x-user header ──
const app = express();
app.use(express.json({ limit: "5mb" }));
const das = (req, res, next) => { req.dasUser = users.get(req.get("x-user")); if (!req.dasUser) return res.status(401).json({ error: "sign in" }); next(); };
const admin = (req, res, next) => { req.user = { name: "VRSP Admin", role: "ADMIN" }; next(); };
app.get("/api/das/app/releases", das, ctl.publisherList);
app.post("/api/das/app/releases/start", das, ctl.start);
app.post("/api/das/app/releases/:version/commit", das, ctl.commit);
app.get("/api/das/app/latest", das, ctl.latest);
app.post("/api/das/app/download-links", das, ctl.downloadLinks);
app.get("/api/das/admin/app", admin, ctl.adminOverview);
app.post("/api/das/admin/app/releases/:version/state", admin, ctl.adminSetState);
app.post("/api/das/admin/app/pilot/:id", admin, ctl.adminSetPilot);
const linkOk = (op, req) => Number(req.query.exp) > Date.now() && req.query.sig === crypto.createHmac("sha256", linkSecret).update(op + req.params.p + req.query.exp).digest("hex");
app.put("/gcs/put/:p", express.raw({ type: "*/*", limit: "50mb" }), (req, res) => { if (!linkOk("put", req)) return res.sendStatus(403); objects.set(req.params.p, Buffer.from(req.body)); res.sendStatus(200); });
app.get("/gcs/get/:p", (req, res) => { if (!linkOk("get", req)) return res.sendStatus(403); const b = objects.get(req.params.p); if (!b) return res.sendStatus(404); gets++; res.end(b); });

async function api(method, url, user, body) {
  const r = await fetch(base + url, { method, headers: { "content-type": "application/json", ...(user ? { "x-user": user } : {}) }, body: body ? JSON.stringify(body) : undefined });
  return { status: r.status, body: await r.json().catch(() => null) };
}

// ── what the Unity editor window does: manifest + sign (C#), start, upload missing, commit ──
async function publish(dir, version, rollout, { user = "pk", keyDir = keys, notes = "" } = {}) {
  const out = path.join(W, `pub-${version}-${Date.now()}.txt`);
  tool("publish", dir, version, path.join(keyDir, "priv.xml"), out);
  const [signature, ...lines] = fs.readFileSync(out, "utf8").split("\n").filter(Boolean);
  const files = lines.map((l) => { const a = l.indexOf(" "), b = l.indexOf(" ", a + 1); return { sha256: l.slice(0, a), size: +l.slice(a + 1, b), path: l.slice(b + 1) }; });
  const s = await api("POST", "/api/das/app/releases/start", user, { version, notes, rollout, files, signature });
  if (s.status !== 200) return { start: s };
  for (const u of s.body.uploads) {
    const f = files.find((x) => x.sha256 === u.sha256);
    const r = await fetch(u.url, { method: "PUT", headers: { "content-type": u.contentType }, body: fs.readFileSync(path.join(dir, f.path)) });
    if (r.status !== 200) throw new Error("upload failed " + r.status);
  }
  const c = await api("POST", `/api/das/app/releases/${version}/commit`, user, {});
  return { start: s, commit: c, files };
}

// ── what DAS on a PC does: check, verify + plan (C#), download changed files, run DASUpdater.exe ──
const readRecord = (dir) => { try { return fs.readFileSync(path.join(dir, "das-release.txt"), "utf8").split("\n")[1].replace("version=", ""); } catch { return ""; } };
async function pcUpdate(pc, user, { corrupt = false, waitFor = null, wait = 20 } = {}) {
  const installed = readRecord(pc);
  const l = await api("GET", `/api/das/app/latest?version=${installed}`, user);
  if (l.body.kind === "none") return { kind: "none" };
  const rel = l.body.release;
  const relFile = path.join(W, `rel-${user}-${rel.version}.txt`);
  fs.writeFileSync(relFile, [rel.version, rel.signature, ...rel.files.map((f) => `${f.sha256} ${f.size} ${f.path}`)].join("\n"));
  const staging = path.join(W, `staging-${user}-${rel.version}-${Date.now()}`);
  const result = path.join(W, `result-${user}.txt`);
  let need;
  try {
    need = tool("plan", pc, relFile, path.join(keys, "pub.xml"), staging, result, installed, waitFor ? "sleep.exe" : "DAS.exe", ...(waitFor ? [String(waitFor.pid)] : [])).split("\n").filter(Boolean);
  } catch (e) { return { kind: l.body.kind, refused: (e.stderr || "").trim() }; }
  const before = gets;
  if (need.length) {
    const d = await api("POST", "/api/das/app/download-links", user, { version: rel.version, files: need });
    if (d.status !== 200) return { kind: l.body.kind, linksStatus: d.status };
    for (const s of need) {
      let buf = Buffer.from(await (await fetch(d.body.links[s])).arrayBuffer());
      if (corrupt) buf = Buffer.concat([buf.subarray(0, buf.length - 1), Buffer.from([buf[buf.length - 1] ^ 1])]);
      fs.writeFileSync(path.join(staging, "blobs", s), buf);   // (DAS checks each download's checksum; the updater checks again)
    }
  }
  const t0 = Date.now();
  const code = await new Promise((res) => spawn("mono", [UPDATER, "--plan", path.join(staging, "plan.txt"), "--no-relaunch", "--no-dialog", "--wait", String(wait)], { stdio: "ignore" }).on("exit", res));
  return { kind: l.body.kind, version: rel.version, downloaded: gets - before, need: need.length, code, ms: Date.now() - t0, result: fs.readFileSync(result, "utf8"), log: fs.readFileSync(path.join(staging, "update.log"), "utf8") };
}

// ── folders ──
function writeTree(dir, spec) {
  fs.rmSync(dir, { recursive: true, force: true });
  for (const [p, c] of Object.entries(spec)) { const f = path.join(dir, p); fs.mkdirSync(path.dirname(f), { recursive: true }); fs.writeFileSync(f, c); }
  fs.copyFileSync(UPDATER, path.join(dir, "DASUpdater.exe"));
}
const tree = (dir) => {
  const out = {};
  const walk = (d) => { for (const e of fs.readdirSync(d, { withFileTypes: true })) { const f = path.join(d, e.name); if (e.isDirectory()) walk(f); else out[path.relative(dir, f).split(path.sep).join("/")] = sha(fs.readFileSync(f)); } };
  walk(dir); return out;
};
const sameAs = (pc, build, ignore = []) => {
  const a = tree(pc), b = tree(build);
  for (const k of ["das-release.txt", ...ignore]) { delete a[k]; delete b[k]; }
  return JSON.stringify(Object.entries(a).sort()) === JSON.stringify(Object.entries(b).sort());
};
const big = crypto.randomBytes(3 * 1024 * 1024);   // a large file that never changes (like a Unity resources file)
const v100 = { "DAS.exe": "exe v1", "DAS_Data/Managed/Assembly-CSharp.dll": "code v1", "DAS_Data/resources.assets": big, "DAS_Data/old-plugin.dll": "goes away in 1.0.1" };
const v101 = { "DAS.exe": "exe v1", "DAS_Data/Managed/Assembly-CSharp.dll": "code v2 - measurement export fix", "DAS_Data/resources.assets": big, "DAS_Data/Plugins/new lib (x64).dll": "new in 1.0.1" };
const b100 = path.join(W, "build-1.0.0"), b101 = path.join(W, "build-1.0.1");
writeTree(b100, v100); writeTree(b101, v101);

(async () => {
  const srv = http.createServer(app).listen(0, "127.0.0.1");
  await new Promise((r) => srv.once("listening", r));
  base = `http://127.0.0.1:${srv.address().port}`;
  console.log("Work folder:", W);

  console.log("\n1. Publishing 1.0.0 (everyone)");
  let p = await publish(b100, "1.0.0", "live", { notes: "First version with app updates" });
  check("start accepted and asked for every file", p.start.status === 200 && p.start.body.uploads.length === 5, p.start);
  check("commit ok, live", p.commit?.status === 200 && p.commit.body.state === "live", p.commit);
  const s = await api("POST", "/api/das/app/releases/start", "eng", { version: "9.9.9", files: [], signature: "x" });
  check("a DAS User (not Admin) can't publish", s.status === 403, s);

  console.log("\n2. Two PCs get the first build by hand (the last manual copy), with das-release.txt from the build step");
  const record = (dir, v) => fs.writeFileSync(path.join(dir, "das-release.txt"), fs.readFileSync(path.join(W, fs.readdirSync(W).find((f) => f.startsWith(`pub-${v}-`))), "utf8").split("\n").slice(1).filter(Boolean).reduce((t, l) => t + l + "\n", `DAS-APP-RELEASE v1\nversion=${v}\n`));
  const pcEng = path.join(W, "pc-eng/DAS"), pcPilot = path.join(W, "pc-pilot/DAS");
  for (const pc of [pcEng, pcPilot]) { fs.cpSync(b100, pc, { recursive: true }); record(pc, "1.0.0"); }
  fs.mkdirSync(path.join(pcEng, "Logs")); fs.writeFileSync(path.join(pcEng, "Logs/session.log"), "user data - never touched");
  let r = await pcUpdate(pcEng, "eng");
  check("PC on 1.0.0: nothing to do", r.kind === "none", r);

  console.log("\n3. Publishing 1.0.1 to the pilot group only");
  p = await publish(b101, "1.0.1", "pilot", { notes: "Measurement export fix" });
  check("only the 2 changed/new files are uploaded (3 already there)", p.start.status === 200 && p.start.body.uploads.length === 2 && p.start.body.alreadyThere === 3, p.start.body);
  check("commit ok, pilot", p.commit?.body?.state === "pilot", p.commit);
  r = await pcUpdate(pcEng, "eng");
  check("not in the pilot group: still nothing", r.kind === "none", r);
  const sneaky = await api("POST", "/api/das/app/download-links", "eng", { version: "1.0.1", files: [p.files[0].sha256] });
  check("not in the pilot group: download links refused", sneaky.status === 404, sneaky);

  console.log("\n4. The pilot PC updates while DAS is still open");
  const stuck = spawn("sleep", ["30"]);
  r = await pcUpdate(pcPilot, "pilot", { waitFor: stuck, wait: 2 });
  stuck.kill();
  check("DAS never closes: updater gives up and changes nothing", r.code === 1 && /didn't close/.test(r.result) && sameAs(pcPilot, b100) && readRecord(pcPilot) === "1.0.0", r.result);
  const fakeDas = spawn("sleep", ["5"]);   // stands in for the running DAS
  r = await pcUpdate(pcPilot, "pilot", { waitFor: fakeDas });
  check("offered 1.0.1 as an update", r.kind === "update" && r.version === "1.0.1", r);
  check("downloads only the 2 files that differ", r.need === 2 && r.downloaded === 2, r);
  check("updater waited for DAS to close", r.ms >= 1000 && /Waiting for DAS/.test(r.log) && fakeDas.exitCode !== null, { ms: r.ms, log: r.log });
  check("updater finished ok", r.code === 0 && r.result.startsWith("ok"), r.result);
  check("pilot PC folder now matches the 1.0.1 build exactly (old plugin removed)", sameAs(pcPilot, b101), tree(pcPilot));
  check("das-release.txt says 1.0.1", readRecord(pcPilot) === "1.0.1");
  r = await pcUpdate(pcPilot, "pilot");
  check("then nothing more to do", r.kind === "none", r);

  console.log("\n5. 1.0.1 for everyone; the other PC updates");
  let a = await api("POST", "/api/das/admin/app/releases/1.0.1/state", null, { state: "live" });
  check("admin: 1.0.1 live", a.status === 200, a);
  r = await pcUpdate(pcEng, "eng");
  check("offered 1.0.1", r.kind === "update" && r.version === "1.0.1", r);
  check("updated ok, folder matches 1.0.1, Logs kept", r.code === 0 && sameAs(pcEng, b101, ["Logs/session.log"]) && fs.readFileSync(path.join(pcEng, "Logs/session.log"), "utf8").includes("never touched"), { r: r.result, t: tree(pcEng) });
  const ov = await api("GET", "/api/das/admin/app");
  check("admin overview sees who runs what", ov.status === 200 && JSON.stringify(ov.body).includes("1.0.1"), ov.body);

  console.log("\n6. Withdrawing 1.0.1: PCs go back to 1.0.0");
  a = await api("POST", "/api/das/admin/app/releases/1.0.1/state", null, { state: "withdrawn" });
  check("admin: 1.0.1 withdrawn", a.status === 200, a);
  r = await pcUpdate(pcEng, "eng");
  check("offered 1.0.0 as a rollback", r.kind === "rollback" && r.version === "1.0.0", r);
  check("rolled back ok: folder matches 1.0.0 (old plugin back, new lib gone), Logs kept", r.code === 0 && sameAs(pcEng, b100, ["Logs/session.log"]) && fs.existsSync(path.join(pcEng, "Logs/session.log")), tree(pcEng));
  check("rollback downloaded 2 files, not the big one", r.downloaded === 2, r);
  a = await api("POST", "/api/das/admin/app/releases/1.0.0/state", null, { state: "withdrawn" });
  check("can't withdraw the only live version", a.status === 409, a);

  console.log("\n7. Refusals");
  const b102 = path.join(W, "build-1.0.2"); writeTree(b102, { ...v100, "DAS.exe": "exe v3" });
  p = await publish(b102, "1.0.2", "live", { keyDir: otherKeys });
  check("release signed with another key: server refuses", p.start.status === 400 && p.start.body.code === "DAS_APP_BAD_SIGNATURE", p.start);
  p = await publish(b100, "1.0.0", "live");
  check("same version again: refused", p.start.status === 409, p.start);
  p = await publish(b102, "1.0.2", "live");
  check("1.0.2 published", p.commit?.status === 200, p.commit);
  const pcBad = path.join(W, "pc-bad/DAS"); fs.cpSync(pcEng, pcBad, { recursive: true });
  r = await pcUpdate(pcBad, "eng", { corrupt: true });
  check("damaged download: updater refuses and changes nothing", r.code === 1 && /damaged/.test(r.result) && sameAs(pcBad, b100, ["Logs/session.log"]), r.result);
  const rel = releases.find((x) => x.version === "1.0.2");
  const goodSig = rel.signature; rel.signature = Buffer.from(goodSig, "base64").map((x, i) => (i === 5 ? x ^ 1 : x)).toString("base64");
  r = await pcUpdate(pcBad, "eng");
  check("server hands out a bad signature: DAS refuses before downloading", /SIGNATURE REJECTED/.test(r.refused || ""), r);
  rel.signature = goodSig;
  r = await pcUpdate(pcBad, "eng");
  check("good again: updates to 1.0.2", r.code === 0 && sameAs(pcBad, b102, ["Logs/session.log"]) && readRecord(pcBad) === "1.0.2", r.result);

  srv.close();
  console.log(`\nEnd-to-end: Passed ${pass} | Failed ${fail}`);
  if (!fail) fs.rmSync(W, { recursive: true, force: true });
  process.exit(fail ? 1 : 0);
})().catch((e) => { console.error(e); process.exit(1); });
