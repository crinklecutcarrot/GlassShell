const endpoint = "http://127.0.0.1:45971";
const headers = { "X-GlassShell-Key": "glass-shell-ytm-v1" };
let latest = { liked: false, queue: [] };
let syncing = false;

chrome.runtime.onMessage.addListener((message, sender) => {
  if (message?.type !== "ytm-state") return;
  (async () => {
    let queue = Array.isArray(message.queue) ? message.queue : [];
    if (sender.tab?.id) {
      try {
        const [{ result }] = await chrome.scripting.executeScript({ target: { tabId: sender.tab.id }, world: "MAIN", func: readYouTubeMusicQueue });
        if (Array.isArray(result)) queue = result;
      } catch {}
    }
    latest = { liked: !!message.liked, queue }; sync();
  })();
});

function readYouTubeMusicQueue() {
  const queueElement = document.querySelector("#queue");
  const items = queueElement?.queue?.getItems?.() || [];
  const normalized = items.map(item => {
    const renderer = item?.playlistPanelVideoRenderer || item?.playlistPanelVideoWrapperRenderer?.primaryRenderer?.playlistPanelVideoRenderer;
    if (!renderer) return null;
    const text = value => value?.runs?.map(run => run.text || "").join("") || value?.simpleText || "";
    return { title: text(renderer.title).trim(), artist: text(renderer.longBylineText || renderer.shortBylineText).trim(), selected: !!renderer.selected };
  }).filter(item => item?.title);
  const current = normalized.findIndex(item => item.selected);
  return (current >= 0 ? normalized.slice(current + 1) : normalized).slice(0, 8).map(({ title, artist }) => ({ title, artist }));
}

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
