(function () {
  "use strict";

  var FALLBACK = {
    version: "v0.2.10",
    sizeBytes: 5242198,
    exeUrl: "https://download.imfourj.cn/jige-battery/releases/v0.2.10/Jige-Setup-0.2.10-x64.exe",
    shaUrl: "https://download.imfourj.cn/jige-battery/releases/v0.2.10/SHA256SUMS.txt",
    githubLatestUrl: "https://github.com/deepfry666/jige-battery/releases/latest"
  };

  function isSafeHttpsUrl(value) {
    if (typeof value !== "string" || value.length === 0) {
      return false;
    }
    try {
      var parsed = new URL(value);
      return parsed.protocol === "https:";
    } catch (_err) {
      return false;
    }
  }

  function formatMegabytes(bytes) {
    if (typeof bytes !== "number" || !isFinite(bytes) || bytes <= 0) {
      return "";
    }
    return (bytes / (1024 * 1024)).toFixed(1) + " MB";
  }

  function setText(id, text) {
    var el = document.getElementById(id);
    if (el && typeof text === "string") {
      el.textContent = text;
    }
  }

  function renderReleaseNotes(body) {
    if (typeof body !== "string") {
      return;
    }
    var lines = body
      .split(/\r?\n/)
      .map(function (line) {
        return line.trim();
      })
      .filter(function (line) {
        return line.length > 0;
      })
      .map(function (line) {
        return line.replace(/^-\s+/, "");
      })
      .filter(function (line) {
        return line.length > 0;
      });

    if (lines.length === 0) {
      return;
    }

    var list = document.getElementById("changelog-list");
    if (!list) {
      return;
    }

    var fragment = document.createDocumentFragment();
    for (var i = 0; i < lines.length; i += 1) {
      var li = document.createElement("li");
      li.textContent = lines[i];
      fragment.appendChild(li);
    }

    while (list.firstChild) {
      list.removeChild(list.firstChild);
    }
    list.appendChild(fragment);
  }

  function applyFallback() {
    var downloadBtn = document.getElementById("download-btn");
    var shaLink = document.getElementById("sha256-link");
    var githubFallback = document.getElementById("github-fallback-link");
    var sizeText = formatMegabytes(FALLBACK.sizeBytes);

    if (downloadBtn) {
      downloadBtn.href = FALLBACK.exeUrl;
    }
    if (shaLink) {
      shaLink.href = FALLBACK.shaUrl;
    }
    if (githubFallback) {
      githubFallback.href = FALLBACK.githubLatestUrl;
      githubFallback.hidden = false;
    }

    setText("download-btn-meta", FALLBACK.version + " · " + sizeText);
    setText("meta-version", FALLBACK.version);
    setText("meta-size", sizeText);
    setText("changelog-version", FALLBACK.version);
  }

  function applyReleaseData(data) {
    if (!data || typeof data !== "object" || !Array.isArray(data.assets)) {
      applyFallback();
      return;
    }

    var exeAsset = data.assets.find(function (asset) {
      return asset && typeof asset.name === "string" && /\.exe$/i.test(asset.name);
    });
    var shaAsset = data.assets.find(function (asset) {
      return asset && typeof asset.name === "string" && /^SHA256SUMS\.txt$/i.test(asset.name);
    });

    if (!exeAsset || !isSafeHttpsUrl(exeAsset.browser_download_url)) {
      applyFallback();
      return;
    }

    var version =
      typeof data.tag_name === "string" && data.tag_name.trim().length > 0
        ? data.tag_name.trim()
        : FALLBACK.version;
    var sizeText = formatMegabytes(exeAsset.size);

    var downloadBtn = document.getElementById("download-btn");
    if (downloadBtn) {
      downloadBtn.href = exeAsset.browser_download_url;
    }

    if (shaAsset && isSafeHttpsUrl(shaAsset.browser_download_url)) {
      var shaLink = document.getElementById("sha256-link");
      if (shaLink) {
        shaLink.href = shaAsset.browser_download_url;
      }
    }

    var githubFallback = document.getElementById("github-fallback-link");
    if (githubFallback) {
      githubFallback.hidden = true;
    }

    setText("download-btn-meta", sizeText ? version + " · " + sizeText : version);
    setText("meta-version", version);
    if (sizeText) {
      setText("meta-size", sizeText);
    }
    setText("changelog-version", version);
    setText("changelog-note", "最新正式版更新内容。");

    renderReleaseNotes(data.body);
  }

  function loadLatestRelease() {
    if (typeof window.fetch !== "function") {
      applyFallback();
      return;
    }

    var controller = typeof AbortController === "function" ? new AbortController() : null;
    var timerId = controller
      ? window.setTimeout(function () {
          controller.abort();
        }, 8000)
      : null;

    var options = controller ? { signal: controller.signal, cache: "no-cache" } : { cache: "no-cache" };

    window
      .fetch("/latest.json", options)
      .then(function (response) {
        if (!response.ok) {
          throw new Error("HTTP " + response.status);
        }
        return response.json();
      })
      .then(function (data) {
        applyReleaseData(data);
      })
      .catch(function () {
        applyFallback();
      })
      .finally(function () {
        if (timerId !== null) {
          window.clearTimeout(timerId);
        }
      });
  }

  loadLatestRelease();
})();
