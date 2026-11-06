// Headless test of the Chrome extension. Uses a throwaway profile, never your own Chrome.
const { spawn } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const chrome = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
const ext = path.resolve(__dirname, "..", "extension");
const dir = fs.mkdtempSync(path.join(os.tmpdir(), "ring-test-"));

const child = spawn(chrome, ["--headless=new", "--remote-debugging-pipe", "--enable-unsafe-extension-debugging",
  `--user-data-dir=${dir}`, "--no-first-run", "about:blank"], { stdio: ["ignore", "ignore", "ignore", "pipe", "pipe"] });

let id = 0, buf = "";
const waiting = new Map();
child.stdio[4].on("data", (d) => {
  buf += d.toString();
  let i;
  while ((i = buf.indexOf("\0")) >= 0) {
    const m = JSON.parse(buf.slice(0, i));
    buf = buf.slice(i + 1);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
  }
});
const send = (method, params = {}, sessionId) => new Promise((res) => {
  const n = ++id;
  waiting.set(n, res);
  child.stdio[3].write(JSON.stringify({ id: n, method, params, sessionId }) + "\0");
});
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

(async () => {
  const load = await send("Extensions.loadUnpacked", { path: ext });
  if (!load.result) throw new Error("load failed " + JSON.stringify(load));
  const extId = load.result.id;
  await sleep(800);

  const pages = async () => (await send("Target.getTargets")).result.targetInfos.filter((t) => t.type === "page" && !t.url.startsWith("chrome"));

  await send("Target.createTarget", { url: "https://example.com/" });
  await send("Target.createTarget", { url: "https://www.iana.org/" });
  await sleep(1500);
  const before = (await pages()).filter((t) => t.url.startsWith("http")).length;

  // 1. A site that is already open: the marked tab disappears.
  await send("Target.createTarget", { url: "https://example.com/#ringjump" });
  await sleep(1500);
  const after = (await pages()).filter((t) => t.url.startsWith("http"));
  const noMark = after.every((t) => !t.url.includes("ringjump"));
  console.log("existing tab: tabs", before, "->", after.length, noMark ? "PASS" : "FAIL");

  // 2. A site that is not open: the tab stays and loses the marker.
  await send("Target.createTarget", { url: "https://example.org/#ringjump" });
  await sleep(2000);
  const third = (await pages()).filter((t) => t.url.startsWith("http"));
  const clean = third.some((t) => t.url.startsWith("https://example.org/") && !t.url.includes("ringjump"));
  console.log("new site: tabs", third.length, clean ? "PASS" : "FAIL");
  console.log("extension id", extId);
})().catch((e) => console.log("ERROR", e.message)).finally(() => {
  child.kill();
  setTimeout(() => { try { fs.rmSync(dir, { recursive: true, force: true }); } catch {} process.exit(0); }, 500);
});
