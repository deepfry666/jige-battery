// 几格官网：读取同源 /latest.json，把下载按钮指向国内镜像上的最新正式版。
// 读不到时保留页面里写好的 v0.2.10 链接，并显示 GitHub 备用下载。
(function () {
  "use strict";

  function safeHttps(value) {
    try {
      return typeof value === "string" && new URL(value).protocol === "https:";
    } catch (_) {
      return false;
    }
  }

  function megabytes(bytes) {
    return typeof bytes === "number" && isFinite(bytes) && bytes > 0
      ? (bytes / 1048576).toFixed(1) + " MB"
      : "";
  }

  function each(selector, fn) {
    var nodes = document.querySelectorAll(selector);
    for (var i = 0; i < nodes.length; i += 1) fn(nodes[i]);
  }

  function showFallback() {
    each("[data-fallback]", function (el) { el.hidden = false; });
  }

  function apply(data) {
    if (!data || !Array.isArray(data.assets)) return showFallback();
    var exe = null, sums = null;
    data.assets.forEach(function (a) {
      if (!a || typeof a.name !== "string") return;
      if (/\.exe$/i.test(a.name)) exe = a;
      if (/^SHA256SUMS\.txt$/i.test(a.name)) sums = a;
    });
    if (!exe || !safeHttps(exe.browser_download_url)) return showFallback();

    var version = typeof data.tag_name === "string" && data.tag_name.trim() ? data.tag_name.trim() : "";
    each("[data-download]", function (el) { el.href = exe.browser_download_url; });
    if (sums && safeHttps(sums.browser_download_url)) {
      each("[data-sha]", function (el) { el.href = sums.browser_download_url; });
    }
    if (version) each("[data-version]", function (el) { el.textContent = version; });
    var size = megabytes(exe.size);
    if (size) each("[data-size]", function (el) { el.textContent = size; });

    if (typeof data.body === "string") {
      var lines = data.body.split(/\r?\n/).map(function (l) { return l.trim().replace(/^-\s+/, ""); })
        .filter(function (l) { return l.length > 0; });
      var list = document.querySelector("[data-changes]");
      if (list && lines.length) {
        while (list.firstChild) list.removeChild(list.firstChild);
        lines.forEach(function (line) {
          var li = document.createElement("li");
          li.textContent = line;
          list.appendChild(li);
        });
      }
    }
  }

  // 导航在页面滚动后加一条分隔线。
  var nav = document.querySelector(".nav");
  if (nav) {
    var onScroll = function () { nav.classList.toggle("is-scrolled", window.scrollY > 8); };
    window.addEventListener("scroll", onScroll, { passive: true });
    onScroll();
  }

  if (typeof fetch !== "function") return showFallback();
  var controller = typeof AbortController === "function" ? new AbortController() : null;
  var timer = controller ? setTimeout(function () { controller.abort(); }, 8000) : null;
  fetch("/latest.json", { cache: "no-cache", signal: controller ? controller.signal : undefined })
    .then(function (r) { if (!r.ok) throw new Error(String(r.status)); return r.json(); })
    .then(apply)
    .catch(showFallback)
    .finally(function () { if (timer) clearTimeout(timer); });
})();
