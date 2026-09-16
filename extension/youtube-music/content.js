function likeButton() {
  const player = document.querySelector("ytmusic-player-bar");
  const renderer = player?.querySelector("ytmusic-like-button-renderer");
  if (!renderer) return null;
  return [...renderer.querySelectorAll("button")].find(button => {
    const label = `${button.getAttribute("aria-label") || ""} ${button.getAttribute("title") || ""}`.toLowerCase();
    return label.includes("like") && !label.includes("dislike");
  }) || renderer.querySelector("button");
}

function liked() {
  const button = likeButton();
  const renderer = button?.closest("ytmusic-like-button-renderer");
  const status = renderer?.getAttribute("like-status") || renderer?.likeStatus || "";
  const label = `${button?.getAttribute("aria-label") || ""} ${button?.getAttribute("title") || ""}`.toLowerCase();
  return button?.getAttribute("aria-pressed") === "true" || String(status).toUpperCase() === "LIKE" || label.includes("remove like") || label.includes("unlike");
}

function queue() {
  const items = [...document.querySelectorAll("ytmusic-player-queue-item")];
  const current = items.findIndex(item => item.hasAttribute("selected") || item.getAttribute("play-button-state") === "playing" || item.querySelector("[icon='pause']"));
  return items.map((item, index) => ({
    title: (item.querySelector("#song-title, .song-title, [slot='title']")?.textContent || "").trim(),
    artist: (item.querySelector("#byline, .byline, [slot='subtitle']")?.textContent || "").trim(),
    artwork: item.querySelector("img")?.src || "",
    duration: (item.querySelector(".duration")?.textContent || "").trim(),
    selected: index === current,
    index
  })).filter(item => item.title).slice(Math.max(0, current - 5));
}

let active = true;
let publishTimer;
let observer;

function stop() {
  if (!active) return;
  active = false;
  observer?.disconnect();
  if (publishTimer) clearInterval(publishTimer);
}

function publish() {
  if (!active) return;
  try {
    // Chrome leaves the old isolated world alive briefly when an unpacked
    // extension is reloaded. Do not call any runtime API from that world.
    if (!chrome?.runtime?.id) { stop(); return; }
    const pending = chrome.runtime.sendMessage({ type: "ytm-state", liked: liked(), queue: queue() });
    pending?.catch(error => {
      if (String(error).includes("Extension context invalidated")) stop();
    });
  } catch (error) {
    if (String(error).includes("Extension context invalidated")) stop();
  }
}
try {
  if (!chrome?.runtime?.id) stop();
  else chrome.runtime.onMessage.addListener((message) => {
    if (message?.type !== "toggle-like") return;
    const button = likeButton(); if (button) { button.click(); setTimeout(publish, 300); }
  });
} catch { stop(); }
observer = new MutationObserver(publish);
if (active) {
  observer.observe(document.documentElement, { subtree: true, attributes: true, attributeFilter: ["aria-pressed", "like-status", "title", "aria-label"] });
  publishTimer = setInterval(publish, 1000);
  window.addEventListener("pagehide", stop, { once: true });
  publish();
}
