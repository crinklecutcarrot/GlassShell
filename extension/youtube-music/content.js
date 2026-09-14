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

function publish() { chrome.runtime.sendMessage({ type: "ytm-state", liked: liked() }).catch(() => {}); }
chrome.runtime.onMessage.addListener((message) => {
  if (message?.type !== "toggle-like") return;
  const button = likeButton(); if (button) { button.click(); setTimeout(publish, 300); }
});
new MutationObserver(publish).observe(document.documentElement, { subtree: true, attributes: true, attributeFilter: ["aria-pressed", "like-status", "title", "aria-label"] });
setInterval(publish, 1000);
publish();
