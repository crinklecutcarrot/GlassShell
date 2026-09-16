const endpoint = "http://127.0.0.1:45971";
const headers = { "X-GlassShell-Key": "glass-shell-ytm-v1" };
let latest = { liked: false, queue: [] };
let syncing = false;

chrome.runtime.onConnect.addListener(port => {
  if (port.name !== "ytm-bridge") return;
  port.onMessage.addListener((message) => {
    if (message?.type !== "ytm-state") return;
    (async () => {
      let queue = Array.isArray(message.queue) ? message.queue : [];
      if (port.sender?.tab?.id) {
        try {
          const [{ result }] = await chrome.scripting.executeScript({ target: { tabId: port.sender.tab.id }, world: "MAIN", func: readYouTubeMusicQueue });
          if (Array.isArray(result)) queue = result;
        } catch {}
      }
      latest = { liked: !!message.liked, queue }; sync();
    })();
  });
});

function readYouTubeMusicQueue() {
  const queueElement = document.querySelector("#queue");
  const items = queueElement?.queue?.getItems?.() || [];
  const normalized = items.map(item => {
    const renderer = item?.playlistPanelVideoRenderer || item?.playlistPanelVideoWrapperRenderer?.primaryRenderer?.playlistPanelVideoRenderer;
    if (!renderer) return null;
    const text = value => value?.runs?.map(run => run.text || "").join("") || value?.simpleText || "";
    const thumbnails = renderer.thumbnail?.thumbnails || renderer.thumbnailRenderer?.musicThumbnailRenderer?.thumbnail?.thumbnails || renderer.thumbnailRenderer?.croppedSquareThumbnailRenderer?.thumbnail?.thumbnails || [];
    const artwork = thumbnails.at(-1)?.url || "";
    return { title: text(renderer.title).trim(), artist: text(renderer.longBylineText || renderer.shortBylineText).trim(), artwork, duration: text(renderer.lengthText).trim(), selected: !!renderer.selected };
  }).filter(item => item?.title);
  const current = normalized.findIndex(item => item.selected);
  const first = current >= 0 ? Math.max(0, current - 5) : 0;
  return normalized.slice(first).map((item, offset) => ({ ...item, index: first + offset }));
}

async function sync() {
  if (syncing) return;
  syncing = true;
  try {
    await fetch(`${endpoint}/state`, { method: "POST", headers: { ...headers, "Content-Type": "application/json" }, body: JSON.stringify(latest) });
    const command = await fetch(`${endpoint}/command`, { headers }).then(r => r.json());
    if (command.toggleLike) {
      const tabs = await chrome.tabs.query({ url: "https://music.youtube.com/*" });
      for (const tab of tabs) chrome.scripting.executeScript({ target: { tabId: tab.id }, world: "MAIN", func: toggleYouTubeMusicLike }).catch(() => {});
    }
    if (command.moveQueue) {
      const tabs = await chrome.tabs.query({ url: "https://music.youtube.com/*" });
      for (const tab of tabs) chrome.scripting.executeScript({ target: { tabId: tab.id }, world: "MAIN", func: moveYouTubeMusicQueue, args: [command.moveQueue.from, command.moveQueue.to] }).catch(() => {});
    }
    if (command.playQueue) {
      const tabs = await chrome.tabs.query({ url: "https://music.youtube.com/*" });
      for (const tab of tabs) chrome.scripting.executeScript({ target: { tabId: tab.id }, world: "MAIN", func: playYouTubeMusicQueue, args: [command.playQueue.index] }).catch(() => {});
    }
  } catch {}
  finally { syncing = false; }
}
sync();

function moveYouTubeMusicQueue(from, to) {
  document.querySelector("#queue")?.dispatch?.({ type: "MOVE_ITEM", payload: { fromIndex: from, toIndex: to } });
}

function playYouTubeMusicQueue(index) {
  document.querySelector("#queue")?.dispatch?.({ type: "SET_INDEX", payload: index });
}

function toggleYouTubeMusicLike() {
  const renderer = document.querySelector("ytmusic-player-bar ytmusic-like-button-renderer");
  const buttons = [...(renderer?.querySelectorAll("button") || [])];
  const button = buttons.find(candidate => {
    const label = `${candidate.getAttribute("aria-label") || ""} ${candidate.getAttribute("title") || ""}`.toLowerCase();
    return label.includes("like") && !label.includes("dislike");
  }) || buttons[0];
  button?.click();
}
