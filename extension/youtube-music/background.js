const endpoint = "http://127.0.0.1:45971";
const headers = { "X-GlassShell-Key": "glass-shell-ytm-v1" };
let latest = { liked: false };
let syncing = false;

chrome.runtime.onMessage.addListener((message) => {
  if (message?.type === "ytm-state") { latest = { liked: !!message.liked }; sync(); }
});

async function sync() {
  if (syncing) return;
  syncing = true;
  try {
    await fetch(`${endpoint}/state`, { method: "POST", headers: { ...headers, "Content-Type": "application/json" }, body: JSON.stringify(latest) });
    const command = await fetch(`${endpoint}/command`, { headers }).then(r => r.json());
    if (command.toggleLike) {
      const tabs = await chrome.tabs.query({ url: "https://music.youtube.com/*" });
      for (const tab of tabs) chrome.tabs.sendMessage(tab.id, { type: "toggle-like" }).catch(() => {});
    }
  } catch {}
  finally { syncing = false; }
}
sync();
