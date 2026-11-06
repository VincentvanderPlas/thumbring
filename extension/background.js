// The Thumbring app opens "https://mail.google.com/#ringjump" in the right Chrome profile.
// Windows brings that profile to the front. This worker then looks for a tab that already
// has the site open: if there is one it jumps there and closes the new tab, if there is
// none the new tab just loads the page without the marker.
// Without this extension the plain page still opens, so a click never dead-ends.

const MARK = "#ringjump";
const busy = new Set();

async function handle(tabId, url) {
  if (!url || !url.endsWith(MARK) || busy.has(tabId)) return;
  busy.add(tabId);
  try {
    const clean = url.slice(0, -MARK.length);
    const host = new URL(clean).host;
    const tabs = await chrome.tabs.query({});
    const match = tabs
      .filter((t) => t.id !== tabId && t.url && sameSite(t.url, host))
      .sort((a, b) => (b.lastAccessed || 0) - (a.lastAccessed || 0))[0];

    if (match) {
      await chrome.tabs.update(match.id, { active: true });
      await chrome.windows.update(match.windowId, { focused: true });
      await chrome.tabs.remove(tabId);
    } else {
      await chrome.tabs.update(tabId, { url: clean });
    }
  } catch (err) {
    console.warn("Thumbring jump failed", err);
  } finally {
    busy.delete(tabId);
  }
}

function sameSite(tabUrl, host) {
  try {
    return new URL(tabUrl).host === host;
  } catch {
    return false;
  }
}

chrome.tabs.onCreated.addListener((tab) => handle(tab.id, tab.pendingUrl || tab.url));

chrome.webNavigation.onBeforeNavigate.addListener((d) => {
  if (d.frameId === 0) handle(d.tabId, d.url);
});
