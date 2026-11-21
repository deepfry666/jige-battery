// 几格官网：同源 /latest.json 自动同步 + 随指针柔摆背景场 + 优雅微交互 + 真实界面演示（严格 CSP 兼容）
(function () {
  "use strict";

  var reduceMotion = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var finePointer = window.matchMedia && window.matchMedia("(pointer: fine)").matches;

  /* ---------------------------------------------------------
     1. 自动同步最新正式版（同源 /latest.json -> 国内镜像）
     --------------------------------------------------------- */
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

  function applyLatest(data) {
    if (!data || !Array.isArray(data.assets)) return showFallback();
    var exe = null;
    var sums = null;
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
      var lines = data.body
        .split(/\r?\n/)
        .map(function (l) { return l.trim().replace(/^-\s+/, ""); })
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

  if (typeof fetch === "function") {
    var controller = typeof AbortController === "function" ? new AbortController() : null;
    var timer = controller ? setTimeout(function () { controller.abort(); }, 8000) : null;
    fetch("/latest.json", { cache: "no-cache", signal: controller ? controller.signal : undefined })
      .then(function (r) { if (!r.ok) throw new Error(String(r.status)); return r.json(); })
      .then(applyLatest)
      .catch(showFallback)
      .finally(function () { if (timer) clearTimeout(timer); });
  } else {
    showFallback();
  }

  /* ---------------------------------------------------------
     2. 导航滚动收拢（向右上角丝滑缩放）+ 阅读进度条 + 舞台微抬头 + 滚动渐现
     --------------------------------------------------------- */
  var nav = document.getElementById("siteNav");
  var progressEl = document.getElementById("readProgress");
  var stageShell = document.getElementById("stageShell");
  var navSectionLinks = document.querySelectorAll('#navLinks a[href^="#"]');
  var trackedSections = [];
  for (var si = 0; si < navSectionLinks.length; si += 1) {
    var secId = navSectionLinks[si].getAttribute("href").slice(1);
    var secEl = document.getElementById(secId);
    if (secEl) trackedSections.push({ el: secEl, link: navSectionLinks[si] });
  }
  var scrollRaf = 0;

  function updateScrollEffects() {
    scrollRaf = 0;
    var y = window.scrollY || 0;
    // 首屏置顶，往下滑进入下一屏时向右上角缩放收拢为悬浮胶囊
    var compactThreshold = Math.min(460, Math.max(260, Math.round(window.innerHeight * 0.45)));
    if (nav) {
      nav.classList.toggle("is-scrolled", y > 10);
      nav.classList.toggle("is-compact", y > compactThreshold);
    }

    // 当前所在区块导航高亮
    var activeLink = null;
    for (var ti = 0; ti < trackedSections.length; ti += 1) {
      if (trackedSections[ti].el.getBoundingClientRect().top <= 160) {
        activeLink = trackedSections[ti].link;
      }
    }
    for (var tj = 0; tj < trackedSections.length; tj += 1) {
      trackedSections[tj].link.classList.toggle("is-current", trackedSections[tj].link === activeLink);
    }

    if (progressEl) {
      var maxScroll = Math.max(1, document.documentElement.scrollHeight - window.innerHeight);
      var pct = Math.max(0, Math.min(1, y / maxScroll));
      progressEl.style.transform = "scaleX(" + pct.toFixed(4) + ")";
    }

    if (stageShell && !reduceMotion && window.innerWidth >= 900) {
      var rect = stageShell.getBoundingClientRect();
      var t = Math.max(0, Math.min(1, (rect.top - 110) / 360));
      var tilt = (t * 3.2).toFixed(2);
      var scale = (1 - t * 0.014).toFixed(4);
      stageShell.style.setProperty("--stage-tilt", tilt + "deg");
      stageShell.style.setProperty("--stage-scale", scale);
    } else if (stageShell) {
      stageShell.style.setProperty("--stage-tilt", "0deg");
      stageShell.style.setProperty("--stage-scale", "1");
    }
  }

  window.addEventListener("scroll", function () {
    if (!scrollRaf) scrollRaf = requestAnimationFrame(updateScrollEffects);
  }, { passive: true });
  updateScrollEffects();

  // 导航栏跟手淡光胶囊
  var navLinksBox = document.getElementById("navLinks");
  var navPill = document.getElementById("navPill");
  if (navLinksBox && navPill && finePointer) {
    var navAnchors = navLinksBox.querySelectorAll("a");
    for (var ni = 0; ni < navAnchors.length; ni += 1) {
      navAnchors[ni].addEventListener("mouseenter", function () {
        var boxRect = navLinksBox.getBoundingClientRect();
        var aRect = this.getBoundingClientRect();
        navPill.style.width = aRect.width + "px";
        navPill.style.transform = "translate3d(" + (aRect.left - boxRect.left) + "px, -50%, 0)";
        navPill.classList.add("is-show");
      });
    }
    navLinksBox.addEventListener("mouseleave", function () {
      navPill.classList.remove("is-show");
    });
  }

  // 滚动渐现
  var reveals = document.querySelectorAll(".reveal");
  if (reveals.length && "IntersectionObserver" in window) {
    document.documentElement.classList.add("has-reveal");
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          entry.target.classList.add("is-visible");
          io.unobserve(entry.target);
        }
      });
    }, { rootMargin: "0px 0px -50px 0px", threshold: 0.06 });
    reveals.forEach(function (el) { io.observe(el); });
  }

  /* ---------------------------------------------------------
     3. 随指针柔和摆动的丝绸光浪背景 + 惯性延迟漫射光 + 深层视差
     --------------------------------------------------------- */
  var bgFx = document.querySelector(".bg-fx");
  var bgCanvas = document.getElementById("bgCanvas");
  var spotlight = document.getElementById("bgSpotlight");
  var spotCards = document.querySelectorAll("[data-spotlight]");

  var vw = window.innerWidth;
  var vh = window.innerHeight;
  var dpr = Math.min(window.devicePixelRatio || 1, 2);
  var ctx = bgCanvas && bgCanvas.getContext ? bgCanvas.getContext("2d") : null;

  var pointerActive = false;
  var targetX = vw * 0.5;
  var targetY = vh * 0.34;
  var curX = targetX;
  var curY = targetY;
  var parallaxX = 0;
  var parallaxY = 0;

  // 5 条大尺度连续丝绸光带（连续平滑函数，绝无跳变抽搐）
  var RIBBONS = [
    { yFrac: 0.24, amp: 18, freq: 0.0019, speed: 0.18, phase: 0.0,  strokeAlpha: 0.12,  fillAlpha: 0.024 },
    { yFrac: 0.36, amp: 24, freq: 0.0015, speed: 0.14, phase: 1.5,  strokeAlpha: 0.095, fillAlpha: 0.020 },
    { yFrac: 0.49, amp: 20, freq: 0.0021, speed: 0.16, phase: 2.9,  strokeAlpha: 0.08,  fillAlpha: 0.017 },
    { yFrac: 0.64, amp: 26, freq: 0.0014, speed: 0.12, phase: 4.2,  strokeAlpha: 0.06,  fillAlpha: 0.014 },
    { yFrac: 0.79, amp: 20, freq: 0.0017, speed: 0.15, phase: 5.4,  strokeAlpha: 0.045, fillAlpha: 0.010 }
  ];

  function resizeCanvas() {
    vw = window.innerWidth;
    vh = window.innerHeight;
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    if (bgCanvas && ctx) {
      bgCanvas.width = Math.round(vw * dpr);
      bgCanvas.height = Math.round(vh * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }
  }

  if (finePointer) {
    window.addEventListener("pointermove", function (ev) {
      targetX = ev.clientX;
      targetY = ev.clientY;
      if (!pointerActive) {
        pointerActive = true;
        curX = targetX;
        curY = targetY;
        document.body.classList.add("has-pointer");
      }
    }, { passive: true });
    document.addEventListener("pointerleave", function () {
      pointerActive = false;
    });
  }

  var bgRaf = 0;
  var prevTs = 0;

  function drawRibbons(ts) {
    if (document.hidden) {
      bgRaf = 0;
      prevTs = 0;
      return;
    }
    var dt = prevTs ? Math.min(0.05, (ts - prevTs) / 1000) : 0.016;
    prevTs = ts;
    var t = ts * 0.001;

    var fx = targetX;
    var fy = targetY;
    if (!finePointer) {
      fx = vw * (0.5 + 0.28 * Math.sin(t * 0.32));
      fy = vh * (0.38 + 0.16 * Math.cos(t * 0.24));
    }

    // 柔和惯性阻尼跟随（约 0.32s 丝滑延迟，不随帧率抖动）
    var ease = 1 - Math.exp(-dt * 3.2);
    curX += (fx - curX) * ease;
    curY += (fy - curY) * ease;

    // 暖光漫射云团更慢的视差摆动
    var normX = (curX / Math.max(1, vw)) - 0.5;
    var normY = (curY / Math.max(1, vh)) - 0.5;
    var pEase = 1 - Math.exp(-dt * 1.8);
    parallaxX += (normX - parallaxX) * pEase;
    parallaxY += (normY - parallaxY) * pEase;

    if (bgFx) {
      bgFx.style.setProperty("--px", parallaxX.toFixed(4) + "px");
      bgFx.style.setProperty("--py", parallaxY.toFixed(4) + "px");
    }
    if (spotlight && finePointer) {
      spotlight.style.setProperty("--mx", curX.toFixed(1) + "px");
      spotlight.style.setProperty("--my", curY.toFixed(1) + "px");
    }
    if (finePointer && pointerActive) {
      for (var ci = 0; ci < spotCards.length; ci += 1) {
        var card = spotCards[ci];
        var rect = card.getBoundingClientRect();
        if (curY >= rect.top - 160 && curY <= rect.bottom + 160) {
          card.style.setProperty("--cx", (curX - rect.left).toFixed(1) + "px");
          card.style.setProperty("--cy", (curY - rect.top).toFixed(1) + "px");
        }
      }
    }

    if (ctx) {
      ctx.clearRect(0, 0, vw, vh);
      var step = vw < 640 ? 28 : 24;
      var sigmaX = Math.max(220, vw * 0.22);
      var invTwoSigX = 1 / (2 * sigmaX * sigmaX);
      var focusNormX = Math.max(0.08, Math.min(0.92, curX / Math.max(1, vw)));

      for (var r = 0; r < RIBBONS.length; r += 1) {
        var rb = RIBBONS[r];
        var baseY = vh * rb.yFrac + parallaxY * (22 + r * 6);
        var dyRibbon = curY - baseY;
        var vertGlow = Math.exp(-(dyRibbon * dyRibbon) / (2 * 240 * 240));
        // 连续平滑的 Gaussian 导数推力：经过光带中心时平滑过零，绝不跳变抽搐
        var smoothVertBend = (dyRibbon / 150) * Math.exp(-(dyRibbon * dyRibbon) / (2 * 220 * 220));

        var pts = [];
        for (var x = -step; x <= vw + step * 2; x += step) {
          var dx = x - curX;
          var horizGauss = Math.exp(-(dx * dx) * invTwoSigX);
          var wave = Math.sin(x * rb.freq + t * rb.speed + rb.phase) * rb.amp;
          var cursorBend = horizGauss * smoothVertBend * 44;
          var parallaxTilt = ((x / Math.max(1, vw)) - 0.5) * parallaxX * (32 - r * 3);
          pts.push({ x: x, y: baseY + wave + cursorBend + parallaxTilt });
        }

        ctx.beginPath();
        ctx.moveTo(pts[0].x, pts[0].y);
        for (var k = 1; k < pts.length - 1; k += 1) {
          var mx = (pts[k].x + pts[k + 1].x) * 0.5;
          var my = (pts[k].y + pts[k + 1].y) * 0.5;
          ctx.quadraticCurveTo(pts[k].x, pts[k].y, mx, my);
        }
        ctx.lineTo(pts[pts.length - 1].x, pts[pts.length - 1].y);

        var strokeGrad = ctx.createLinearGradient(0, 0, vw, 0);
        var peakAlpha = rb.strokeAlpha + vertGlow * 0.13;
        var leftStop = Math.max(0.02, focusNormX - 0.28);
        var rightStop = Math.min(0.98, focusNormX + 0.28);
        strokeGrad.addColorStop(0, "rgba(245, 240, 230, 0)");
        strokeGrad.addColorStop(leftStop, "rgba(245, 240, 230, " + (rb.strokeAlpha * 0.65).toFixed(3) + ")");
        strokeGrad.addColorStop(focusNormX, "rgba(232, 184, 108, " + peakAlpha.toFixed(3) + ")");
        strokeGrad.addColorStop(rightStop, "rgba(245, 240, 230, " + (rb.strokeAlpha * 0.65).toFixed(3) + ")");
        strokeGrad.addColorStop(1, "rgba(245, 240, 230, 0)");

        ctx.strokeStyle = strokeGrad;
        ctx.lineWidth = 1.05 + vertGlow * 0.35;
        ctx.stroke();

        ctx.lineTo(vw + step * 2, baseY + 190);
        ctx.lineTo(-step, baseY + 190);
        ctx.closePath();
        var fillGrad = ctx.createLinearGradient(0, baseY - 40, 0, baseY + 170);
        var fAlpha = rb.fillAlpha + vertGlow * 0.012;
        fillGrad.addColorStop(0, "rgba(242, 224, 192, " + fAlpha.toFixed(4) + ")");
        fillGrad.addColorStop(1, "rgba(242, 224, 192, 0)");
        ctx.fillStyle = fillGrad;
        ctx.fill();
      }
    }

    if (!reduceMotion) {
      bgRaf = requestAnimationFrame(drawRibbons);
    }
  }

  resizeCanvas();
  if (bgCanvas && ctx) {
    if (reduceMotion) {
      drawRibbons(0);
    } else {
      bgRaf = requestAnimationFrame(drawRibbons);
      document.addEventListener("visibilitychange", function () {
        if (!document.hidden && !bgRaf) {
          prevTs = 0;
          bgRaf = requestAnimationFrame(drawRibbons);
        }
      });
    }
  }

  var resizeTimer = 0;
  window.addEventListener("resize", function () {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () {
      resizeCanvas();
      updateScrollEffects();
      syncStageSlider(true);
      if (reduceMotion && ctx) drawRibbons(0);
    }, 120);
  });

  /* ---------------------------------------------------------
     4. 真实界面交互演示舞台（含滑动底块 + 数字滚动）
     --------------------------------------------------------- */
  var ICONS = {
    mouse: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><rect x="6" y="2.5" width="12" height="19" rx="6"/><path d="M12 2.5v6.5" stroke-linecap="round"/></svg>',
    headset: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"><path d="M4 15v-3a8 8 0 0 1 16 0v3"/><rect x="3" y="14" width="4.5" height="7" rx="1.6"/><rect x="16.5" y="14" width="4.5" height="7" rx="1.6"/></svg>',
    keyboard: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"><rect x="2.5" y="6" width="19" height="12" rx="2.5"/><path d="M6 10h.01M9.3 10h.01M12.6 10h.01M15.9 10h.01M18 10h.01M8 14.2h8"/></svg>'
  };

  var TRAY_PIXELS = {
    mouse: ["..XXXXXX..", ".XXXXXXXX.", "XXXX..XXXX", "XXXX..XXXX", "XXXX..XXXX", "XXXXXXXXXX", "..........", "XXXXXXXXXX", "XXXXXXXXXX", "XXXXXXXXXX", "XXXXXXXXXX", "XXXXXXXXXX", "XXXXXXXXXX", "XXXXXXXXXX", ".XXXXXXXX.", "..XXXXXX.."],
    keyboard: [".XXXXXXXXXXXXX.", "XXXXXXXXXXXXXXX", "XX.X.X.X.X.X.XX", "XXXXXXXXXXXXXXX", "XXX.X.X.X.X.XXX", "XXXXXXXXXXXXXXX", "XXXX.......XXXX", "XXXXXXXXXXXXXXX", ".XXXXXXXXXXXXX."],
    headset: [".....XXXXXX.....", "...XXXXXXXXXX...", "..XXX......XXX..", ".XXX........XXX.", ".XX..........XX.", ".XX..........XX.", "XXXXX......XXXXX", "XXXXX......XXXXX", "XXXXX......XXXXX", "XXXXX......XXXXX", "XXXXX......XXXXX", "XXXXX......XXXXX", ".XXX........XXX."]
  };

  var DEMO_DEVICES = [
    {
      id: "atk",
      name: "ATK 鼠标",
      kind: "mouse",
      pct: 12,
      state: "low",
      conn: "2.4G",
      source: "ATK 无线接收器",
      badge: "通用适配",
      badgeCls: "is-gen",
      status: "电量低，该充电了",
      hoverStatus: "电量低 · 1 分钟前读取",
      inStrip: true
    },
    {
      id: "mx3s",
      name: "MX Anywhere 3S",
      kind: "mouse",
      pct: 20,
      state: "warn",
      conn: "2.4G",
      source: "罗技 Bolt 接收器",
      badge: "按资料适配",
      badgeCls: "",
      status: "休眠 · 上次读数 · 2 分钟前",
      hoverStatus: "电量偏低 · 休眠中 · 2 分钟前读取",
      inStrip: false
    },
    {
      id: "h9",
      name: "INZONE H9",
      kind: "headset",
      pct: 48,
      state: "charge",
      charging: true,
      conn: "2.4G",
      source: "Sony INZONE 接收器",
      badge: "已实机验证",
      badgeCls: "is-ok",
      status: "充电中",
      hoverStatus: "充电中 · 刚刚读取",
      inStrip: true
    },
    {
      id: "ulx",
      name: "Finalmouse ULX",
      kind: "mouse",
      pct: 72,
      est: true,
      state: "ok",
      conn: "2.4G",
      source: "Finalmouse 接收器",
      badge: "通用适配",
      badgeCls: "is-gen",
      status: "2.4G 已连接 · 电压估算",
      hoverStatus: "电压估算 · 3 分钟前读取",
      inStrip: false
    },
    {
      id: "mxks",
      name: "MX Keys S",
      kind: "keyboard",
      pct: 80,
      state: "ok",
      conn: "2.4G",
      source: "罗技 Bolt 接收器",
      badge: "按资料适配",
      badgeCls: "",
      status: "2.4G 已连接",
      hoverStatus: "在线 · 1 分钟前读取",
      inStrip: true
    }
  ];

  var STATE_COLOR_HEX = {
    low: "#ff99a4",
    warn: "#fce100",
    charge: "#6ccb5f",
    ok: "#f5f5f5"
  };

  function trayIconSvg(kind, pct, stateKey, displaySize) {
    var rows = TRAY_PIXELS[kind] || TRAY_PIXELS.mouse;
    var w = rows[0].length;
    var h = rows.length;
    var x = Math.floor((16 - w) / 2);
    var y = Math.floor((16 - h) / 2);
    var filledRows = pct > 0 ? Math.max(1, Math.round(pct * h / 100)) : 0;
    var waterline = y + h - filledRows;
    var fullInk = STATE_COLOR_HEX[stateKey] || "#f5f5f5";
    var dimInk = "rgba(255,255,255,0.28)";
    var rects = "";
    for (var j = 0; j < h; j += 1) {
      var row = rows[j];
      for (var i = 0; i < w; i += 1) {
        if (row.charAt(i) === "X") {
          var fill = (y + j) < waterline ? dimInk : fullInk;
          rects += '<rect x="' + (x + i) + '" y="' + (y + j) + '" width="1" height="1" fill="' + fill + '"/>';
        }
      }
    }
    var sz = displaySize || 16;
    return '<svg width="' + sz + '" height="' + sz + '" viewBox="0 0 16 16" shape-rendering="crispEdges" aria-hidden="true">' + rects + "</svg>";
  }

  function solidGlyphSvg(kind, colorHex, displaySize) {
    var rows = TRAY_PIXELS[kind] || TRAY_PIXELS.mouse;
    var w = rows[0].length;
    var h = rows.length;
    var x = Math.floor((16 - w) / 2);
    var y = Math.floor((16 - h) / 2);
    var rects = "";
    for (var j = 0; j < h; j += 1) {
      var row = rows[j];
      for (var i = 0; i < w; i += 1) {
        if (row.charAt(i) === "X") {
          rects += '<rect x="' + (x + i) + '" y="' + (y + j) + '" width="1" height="1" fill="' + colorHex + '"/>';
        }
      }
    }
    var sz = displaySize || 15;
    return '<svg width="' + sz + '" height="' + sz + '" viewBox="0 0 16 16" shape-rendering="crispEdges" aria-hidden="true">' + rects + "</svg>";
  }

  function cellsHtml(pct, stateKey, count, isMini, charging, animatePop) {
    var filled = Math.round(pct * count / 100);
    var cls = (isMini ? "sim-cells is-mini " : "sim-cells ") + "s-" + stateKey + (animatePop ? " is-pop" : "");
    var out = '<div class="' + cls + '">';
    for (var i = 0; i < count; i += 1) {
      var c = i < filled ? "f" : (charging && i === filled ? "next" : "");
      out += c ? '<i class="' + c + '"></i>' : "<i></i>";
    }
    return out + "</div>";
  }

  function hoverCellsHtml(pct, stateKey, charging) {
    var filled = Math.round(pct * 10 / 100);
    var out = '<div class="hc-cells s-' + stateKey + '">';
    for (var i = 0; i < 10; i += 1) {
      var c = i < filled ? "f" : (charging && i === filled ? "next" : "");
      out += c ? '<i class="' + c + '"></i>' : "<i></i>";
    }
    return out + "</div>";
  }

  function textClassFor(stateKey) {
    if (stateKey === "low") return "txt-low";
    if (stateKey === "warn") return "txt-warn";
    if (stateKey === "charge") return "txt-charge";
    return "";
  }

  var stage = document.getElementById("stage");
  var stageTabsBox = document.getElementById("stageTabs");
  var stageTabSlider = document.getElementById("stageTabSlider");
  var flyout = document.getElementById("simFlyout");
  var heroCardEl = document.getElementById("simHeroCard");
  var listEl = document.getElementById("simDeviceList");
  var hoverCardEl = document.getElementById("simHoverCard");
  var stripEl = document.getElementById("simStrip");
  var trayIconsEl = document.getElementById("simTrayIcons");
  var toastEl = document.getElementById("simToast");
  var gameEl = document.getElementById("simGame");
  var queuedEl = document.getElementById("simQueued");
  var stageHintEl = document.getElementById("stageHint");
  var stageHintTitleEl = document.getElementById("stageHintTitle");
  var tabs = document.querySelectorAll(".stage-tab");

  var activeHeroId = "atk";
  var lastHeroPct = 12;
  var pctAnimRaf = 0;
  var activeScene = "panel";
  var hoverAnchorId = "atk";

  function findDevice(id) {
    for (var i = 0; i < DEMO_DEVICES.length; i += 1) {
      if (DEMO_DEVICES[i].id === id) return DEMO_DEVICES[i];
    }
    return DEMO_DEVICES[0];
  }

  function syncStageSlider(instant) {
    if (!stageTabsBox || !stageTabSlider) return;
    var activeBtn = stageTabsBox.querySelector(".stage-tab.is-active");
    if (!activeBtn) return;
    var boxRect = stageTabsBox.getBoundingClientRect();
    var btnRect = activeBtn.getBoundingClientRect();
    if (!btnRect.width) return;
    stageTabsBox.classList.add("has-slider");
    if (instant) stageTabSlider.style.transition = "none";
    stageTabSlider.style.width = btnRect.width + "px";
    stageTabSlider.style.height = btnRect.height + "px";
    stageTabSlider.style.transform =
      "translate3d(" + (btnRect.left - boxRect.left - 5) + "px, " + (btnRect.top - boxRect.top - 5) + "px, 0)";
    stageTabSlider.classList.add("is-ready");
    if (instant) {
      void stageTabSlider.offsetWidth;
      stageTabSlider.style.transition = "";
    }
  }

  function animateHeroNumber(el, fromVal, toVal) {
    if (pctAnimRaf) cancelAnimationFrame(pctAnimRaf);
    if (reduceMotion || fromVal === toVal) {
      el.textContent = String(toVal);
      return;
    }
    var start = performance.now();
    var dur = 340;
    function tickNum(now) {
      var p = Math.min(1, (now - start) / dur);
      var eased = 1 - Math.pow(1 - p, 3);
      el.textContent = String(Math.round(fromVal + (toVal - fromVal) * eased));
      if (p < 1) {
        pctAnimRaf = requestAnimationFrame(tickNum);
      } else {
        pctAnimRaf = 0;
      }
    }
    pctAnimRaf = requestAnimationFrame(tickNum);
  }

  function renderPanel(animateSwitch) {
    if (!heroCardEl || !listEl) return;
    var hero = findDevice(activeHeroId);
    var tc = textClassFor(hero.state);
    var prevPct = lastHeroPct;
    lastHeroPct = hero.pct;

    heroCardEl.innerHTML =
      '<div class="fo-hero-top">' +
        '<div class="fo-dev-ico">' + ICONS[hero.kind] + "</div>" +
        '<div class="fo-hero-meta">' +
          '<div class="fo-hero-title-row"><span class="fo-hero-name">' + hero.name + '</span><span class="fo-conn">' + hero.conn + "</span></div>" +
          '<span class="fo-badge ' + hero.badgeCls + '">' + hero.badge + "</span>" +
        "</div>" +
      "</div>" +
      '<div class="fo-hero-mid">' +
        '<div class="fo-big ' + tc + '">' + (hero.est ? '<span class="approx">≈</span>' : "") + '<span id="simHeroPct">' + (animateSwitch ? prevPct : hero.pct) + "</span><small>%</small></div>" +
        cellsHtml(hero.pct, hero.state, 10, false, hero.charging, Boolean(animateSwitch)) +
      "</div>" +
      '<div class="fo-sub"><span class="fo-dot ' + (tc || "txt-ok") + '"></span><span>' + hero.status + "</span></div>";

    if (animateSwitch) {
      var pctSpan = document.getElementById("simHeroPct");
      if (pctSpan) animateHeroNumber(pctSpan, prevPct, hero.pct);
    }

    var rowsHtml = "";
    DEMO_DEVICES.forEach(function (d) {
      if (d.id === hero.id) return;
      var rtc = textClassFor(d.state);
      rowsHtml +=
        '<button type="button" class="fo-row" data-pick-dev="' + d.id + '">' +
          '<span class="fo-row-ico">' + ICONS[d.kind] + "</span>" +
          '<span class="fo-row-meta">' +
            '<span class="fo-row-name">' + d.name + "</span>" +
            '<span class="fo-row-st ' + rtc + '">' + d.status + "</span>" +
          "</span>" +
          cellsHtml(d.pct, d.state, 5, true, d.charging, false) +
          '<span class="fo-row-num ' + rtc + '">' + (d.est ? "≈" : "") + d.pct + "<small>%</small></span>" +
        "</button>";
    });
    listEl.innerHTML = rowsHtml;

    var rowBtns = listEl.querySelectorAll("[data-pick-dev]");
    for (var i = 0; i < rowBtns.length; i += 1) {
      rowBtns[i].addEventListener("click", function () {
        activeHeroId = this.getAttribute("data-pick-dev");
        renderPanel(true);
      });
    }
  }

  function renderHoverCard(device, anchorBtn) {
    if (!hoverCardEl || !stage) return;
    var tc = textClassFor(device.state);
    hoverCardEl.innerHTML =
      '<div class="hc-top">' +
        '<div class="hc-ico">' + ICONS[device.kind] + "</div>" +
        '<div class="hc-meta">' +
          '<div class="hc-name">' + device.name + "</div>" +
          '<div class="hc-sub"><span class="fo-conn">' + device.conn + "</span><span>" + device.source + "</span></div>" +
        "</div>" +
      "</div>" +
      '<div class="hc-mid">' +
        '<div class="hc-big ' + tc + '">' + (device.est ? "≈" : "") + device.pct + "<small>%</small></div>" +
        hoverCellsHtml(device.pct, device.state, device.charging) +
      "</div>" +
      '<div class="hc-st"><span class="fo-dot ' + (tc || "txt-ok") + '"></span><span>' + device.hoverStatus + "</span></div>";

    if (anchorBtn) {
      var sr = stage.getBoundingClientRect();
      var br = anchorBtn.getBoundingClientRect();
      var cx = br.left - sr.left + br.width / 2;
      var cardW = hoverCardEl.offsetWidth || Math.min(256, sr.width - 24);
      var left = Math.max(12, Math.min(sr.width - cardW - 12, Math.round(cx - cardW / 2)));
      hoverCardEl.style.left = left + "px";
    }
    hoverCardEl.classList.add("is-show");
  }

  function renderTaskbar() {
    if (stripEl) {
      var stripDevs = [findDevice("mxks"), findDevice("atk"), findDevice("h9")];
      var sHtml = "";
      stripDevs.forEach(function (d, idx) {
        if (idx > 0) sHtml += '<span class="strip-sep" aria-hidden="true"></span>';
        var itemCls = d.state === "low" ? "is-low" : (d.state === "charge" ? "is-charge" : "");
        var glyphCol = d.state === "low" ? "#ff99a4" : "#f3f3f3";
        sHtml +=
          '<button type="button" class="strip-item ' + itemCls + '" data-strip-dev="' + d.id + '" title="' + d.name + " " + d.pct + '%">' +
            solidGlyphSvg(d.kind, glyphCol, 14) +
            (d.charging ? '<span class="strip-bolt" aria-hidden="true">⚡</span>' : "") +
            "<span>" + d.pct + "%</span>" +
          "</button>";
      });
      stripEl.innerHTML = sHtml;
      var sBtns = stripEl.querySelectorAll("[data-strip-dev]");
      for (var i = 0; i < sBtns.length; i += 1) {
        (function (btn) {
          var devId = btn.getAttribute("data-strip-dev");
          btn.addEventListener("mouseenter", function () {
            if (activeScene === "hover" || activeScene === "strip") {
              hoverAnchorId = devId;
              renderHoverCard(findDevice(devId), btn);
            }
          });
          btn.addEventListener("click", function () {
            hoverAnchorId = devId;
            if (activeScene === "panel") {
              activeHeroId = devId;
              renderPanel(true);
            } else if (activeScene !== "game") {
              renderHoverCard(findDevice(devId), btn);
            }
          });
        })(sBtns[i]);
      }
    }

    if (trayIconsEl) {
      var trayDevs = [findDevice("atk"), findDevice("mx3s"), findDevice("h9"), findDevice("mxks")];
      var tHtml = "";
      trayDevs.forEach(function (d) {
        tHtml +=
          '<button type="button" class="tb-icon-btn" data-tray-dev="' + d.id + '" aria-label="' + d.name + " " + d.pct + '%">' +
            trayIconSvg(d.kind, d.pct, d.state, 16) +
          "</button>";
      });
      trayIconsEl.innerHTML = tHtml;
      var tBtns = trayIconsEl.querySelectorAll("[data-tray-dev]");
      for (var j = 0; j < tBtns.length; j += 1) {
        (function (btn) {
          var devId = btn.getAttribute("data-tray-dev");
          btn.addEventListener("mouseenter", function () {
            if (activeScene === "hover") {
              hoverAnchorId = devId;
              renderHoverCard(findDevice(devId), btn);
            }
          });
          btn.addEventListener("click", function () {
            hoverAnchorId = devId;
            if (activeScene === "panel") {
              activeHeroId = devId;
              renderPanel(true);
            } else if (activeScene !== "game") {
              setScene("hover", btn);
            }
          });
        })(tBtns[j]);
      }
    }
  }

  function renderBentoVisuals() {
    var bStrip = document.getElementById("bentoStripDemo");
    if (bStrip) {
      bStrip.innerHTML =
        '<span class="strip-item">' + solidGlyphSvg("keyboard", "#f3f3f3", 14) + "<span>80%</span></span>" +
        '<span class="strip-sep"></span>' +
        '<span class="strip-item is-low">' + solidGlyphSvg("mouse", "#ff99a4", 14) + "<span>12%</span></span>" +
        '<span class="strip-sep"></span>' +
        '<span class="strip-item is-charge">' + solidGlyphSvg("headset", "#f3f3f3", 14) + '<span class="strip-bolt">⚡</span><span>48%</span></span>';
    }
    var bTray = document.getElementById("bentoTrayDemo");
    if (bTray) {
      bTray.innerHTML =
        trayIconSvg("mouse", 9, "low", 20) +
        trayIconSvg("mouse", 20, "warn", 20) +
        trayIconSvg("headset", 48, "charge", 20) +
        trayIconSvg("keyboard", 80, "ok", 20);
    }
  }

  function setScene(scene, explicitAnchor) {
    activeScene = scene;
    for (var i = 0; i < tabs.length; i += 1) {
      var on = tabs[i].getAttribute("data-scene") === scene;
      tabs[i].classList.toggle("is-active", on);
      tabs[i].setAttribute("aria-selected", on ? "true" : "false");
    }
    syncStageSlider(false);

    if (stage) {
      stage.classList.toggle("is-strip-focus", scene === "strip");
      stage.classList.toggle("is-game", scene === "game");
    }
    if (flyout) flyout.classList.toggle("is-open", scene === "panel");
    if (gameEl) {
      gameEl.classList.toggle("is-on", scene === "game");
      gameEl.setAttribute("aria-hidden", scene === "game" ? "false" : "true");
    }
    if (queuedEl) queuedEl.classList.toggle("is-on", scene === "game");
    if (toastEl) toastEl.classList.remove("is-show");

    if (scene === "hover") {
      if (stageHintTitleEl) stageHintTitleEl.textContent = "悬停即看完整读数";
      if (stageHintEl) stageHintEl.textContent = "把鼠标移到任务栏图标或直读条上，悬停卡片会在图标之间平滑原地切换。";
      var anchor = explicitAnchor || (trayIconsEl && trayIconsEl.querySelector('[data-tray-dev="' + hoverAnchorId + '"]')) || (trayIconsEl && trayIconsEl.firstElementChild);
      renderHoverCard(findDevice(hoverAnchorId), anchor);
    } else if (scene === "strip") {
      if (stageHintTitleEl) stageHintTitleEl.textContent = "Windows 11 任务栏直读条";
      if (stageHintEl) stageHintEl.textContent = "在通知区左侧直接横排显示图标与百分比，低电变红、充电变绿带闪电。";
      var stripBtn = (stripEl && stripEl.querySelector('[data-strip-dev="atk"]')) || (stripEl && stripEl.firstElementChild);
      renderHoverCard(findDevice("atk"), stripBtn);
    } else if (scene === "panel") {
      if (stageHintTitleEl) stageHintTitleEl.textContent = "所有无线外设，一处看全";
      if (stageHintEl) stageHintEl.textContent = "点击右侧面板里任意设备行可切换顶部主卡片，也可点击下方按钮触发互动。";
      if (hoverCardEl) hoverCardEl.classList.remove("is-show");
    } else if (scene === "game") {
      if (hoverCardEl) hoverCardEl.classList.remove("is-show");
    }
  }

  for (var t = 0; t < tabs.length; t += 1) {
    tabs[t].addEventListener("click", function () {
      setScene(this.getAttribute("data-scene"));
    });
  }

  var refreshBtn = document.getElementById("simRefresh");
  var simTimeEl = document.getElementById("simTime");
  if (refreshBtn) {
    refreshBtn.addEventListener("click", function () {
      refreshBtn.classList.remove("is-spin");
      void refreshBtn.offsetWidth;
      refreshBtn.classList.add("is-spin");
      if (simTimeEl) simTimeEl.textContent = "刚刚刷新";
    });
  }

  var exitGameBtn = document.getElementById("simExitGame");
  if (exitGameBtn) {
    exitGameBtn.addEventListener("click", function () {
      setScene("panel");
      if (flyout) flyout.classList.remove("is-open");
      if (toastEl) {
        setTimeout(function () { toastEl.classList.add("is-show"); }, 180);
      }
    });
  }

  ["simToastClose", "simToastLater", "simToastOk"].forEach(function (id) {
    var btn = document.getElementById(id);
    if (btn && toastEl) {
      btn.addEventListener("click", function () {
        toastEl.classList.remove("is-show");
        if (activeScene === "panel" && flyout) flyout.classList.add("is-open");
      });
    }
  });

  var triggerToastBtn = document.getElementById("simTriggerToast");
  if (triggerToastBtn && toastEl) {
    triggerToastBtn.addEventListener("click", function () {
      if (flyout) flyout.classList.remove("is-open");
      if (hoverCardEl) hoverCardEl.classList.remove("is-show");
      toastEl.classList.add("is-show");
    });
  }

  var toggleChargeBtn = document.getElementById("simToggleCharge");
  if (toggleChargeBtn) {
    toggleChargeBtn.addEventListener("click", function () {
      var h9 = findDevice("h9");
      h9.charging = !h9.charging;
      h9.state = h9.charging ? "charge" : "ok";
      h9.status = h9.charging ? "充电中" : "2.4G 已连接";
      h9.hoverStatus = h9.charging ? "充电中 · 刚刚读取" : "在线 · 刚刚读取";
      renderPanel(false);
      renderTaskbar();
      if (activeScene === "hover" && hoverAnchorId === "h9") {
        var anchor = trayIconsEl && trayIconsEl.querySelector('[data-tray-dev="h9"]');
        renderHoverCard(h9, anchor);
      }
    });
  }

  // 任务栏实时时钟
  var clockEl = document.getElementById("simClock");
  function updateClock() {
    if (!clockEl) return;
    var now = new Date();
    var pad = function (n) { return n < 10 ? "0" + n : String(n); };
    clockEl.innerHTML = pad(now.getHours()) + ":" + pad(now.getMinutes()) + "<br>" + now.getFullYear() + "/" + (now.getMonth() + 1) + "/" + now.getDate();
  }

  renderPanel(false);
  renderTaskbar();
  renderBentoVisuals();
  updateClock();
  setInterval(updateClock, 30000);
  setScene("panel");
  requestAnimationFrame(function () { syncStageSlider(true); });
})();
