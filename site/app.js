// 几格官网 · 宣纸陶瓷 × 黑曜硬件橱窗交互脚本（严格 CSP 兼容）
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
    each("[data-filename]", function (el) { el.textContent = exe.name; });
    each("[data-hash-cmd]", function (el) {
      el.textContent = 'Get-FileHash "$HOME\\Downloads\\' + exe.name + '" -Algorithm SHA256';
    });

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
     2. 导航滚动收拢（向右上角缩放）+ 阅读进度条 + 橱窗微抬头 + 渐现
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
    var compactThreshold = Math.min(440, Math.max(240, Math.round(window.innerHeight * 0.42)));
    if (nav) {
      nav.classList.toggle("is-scrolled", y > 10);
      nav.classList.toggle("is-compact", y > compactThreshold);
    }

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
      var t = Math.max(0, Math.min(1, (rect.top - 100) / 340));
      stageShell.style.setProperty("--stage-tilt", (t * 2.6).toFixed(2) + "deg");
      stageShell.style.setProperty("--stage-scale", (1 - t * 0.012).toFixed(4));
    } else if (stageShell) {
      stageShell.style.setProperty("--stage-tilt", "0deg");
      stageShell.style.setProperty("--stage-scale", "1");
    }
  }

  window.addEventListener("scroll", function () {
    if (!scrollRaf) scrollRaf = requestAnimationFrame(updateScrollEffects);
  }, { passive: true });
  updateScrollEffects();

  // 导航跟手淡灰胶囊
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
     3. 精密机械表盘弧线背景（随指针缓摆与平滑旋转，零噪点零抽搐）
     --------------------------------------------------------- */
  var bgFx = document.querySelector(".bg-fx");
  var bgCanvas = document.getElementById("bgCanvas");
  var spotlight = document.getElementById("bgSpotlight");
  var ctx = bgCanvas && bgCanvas.getContext ? bgCanvas.getContext("2d") : null;

  var vw = window.innerWidth;
  var vh = window.innerHeight;
  var dpr = Math.min(window.devicePixelRatio || 1, 2);

  var pointerActive = false;
  var targetX = vw * 0.68;
  var targetY = vh * 0.24;
  var curX = targetX;
  var curY = targetY;
  var parallaxX = 0;
  var parallaxY = 0;
  var dialAngle = 0;

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
    document.addEventListener("pointerleave", function () { pointerActive = false; });
  }

  var bgRaf = 0;
  var prevTs = 0;

  function drawKineticDial(ts) {
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
      fx = vw * (0.62 + 0.16 * Math.sin(t * 0.28));
      fy = vh * (0.28 + 0.12 * Math.cos(t * 0.22));
    }

    // 丝滑惯性延迟
    var ease = 1 - Math.exp(-dt * 2.8);
    curX += (fx - curX) * ease;
    curY += (fy - curY) * ease;

    var normX = (curX / Math.max(1, vw)) - 0.5;
    var normY = (curY / Math.max(1, vh)) - 0.5;
    var pEase = 1 - Math.exp(-dt * 1.8);
    parallaxX += (normX - parallaxX) * pEase;
    parallaxY += (normY - parallaxY) * pEase;

    // 机械圆弧随指针水平位置缓慢偏转（像物理阻尼旋钮）
    var targetAngle = normX * 0.42 + normY * 0.22 + t * 0.035;
    dialAngle += (targetAngle - dialAngle) * (1 - Math.exp(-dt * 2.2));

    if (bgFx) {
      bgFx.style.setProperty("--px", parallaxX.toFixed(4) + "px");
      bgFx.style.setProperty("--py", parallaxY.toFixed(4) + "px");
    }
    if (spotlight && finePointer) {
      spotlight.style.setProperty("--mx", curX.toFixed(1) + "px");
      spotlight.style.setProperty("--my", curY.toFixed(1) + "px");
    }

    if (ctx) {
      ctx.clearRect(0, 0, vw, vh);
      var baseUnit = Math.min(vw, vh);
      var cx = vw * 0.78 - parallaxX * 42;
      var cy = vh * 0.22 - parallaxY * 30;
      var radii = [baseUnit * 0.34, baseUnit * 0.54, baseUnit * 0.78];

      ctx.save();
      ctx.translate(cx, cy);

      // 3 条大尺度极细同心机械圆环
      for (var i = 0; i < radii.length; i += 1) {
        var R = radii[i];
        var rot = dialAngle * (i % 2 === 0 ? 1 : -0.75) + i * 0.9;
        ctx.beginPath();
        ctx.arc(0, 0, R, rot, rot + Math.PI * 1.62);
        ctx.strokeStyle = i === 1 ? "rgba(25, 23, 21, 0.075)" : "rgba(25, 23, 21, 0.048)";
        ctx.lineWidth = 1;
        ctx.stroke();

        // 中环上的精密刻度与一枚静谧朱橙游标点
        if (i === 1) {
          var tickCount = 24;
          ctx.strokeStyle = "rgba(25, 23, 21, 0.085)";
          ctx.lineWidth = 1;
          for (var k = 0; k < tickCount; k += 1) {
            var a = rot + (k / tickCount) * Math.PI * 1.62;
            var inner = k % 6 === 0 ? R - 10 : R - 5;
            ctx.beginPath();
            ctx.moveTo(Math.cos(a) * inner, Math.sin(a) * inner);
            ctx.lineTo(Math.cos(a) * R, Math.sin(a) * R);
            ctx.stroke();
          }
          // 柔和朱橙刻度游标
          var markerA = rot + 0.35;
          ctx.beginPath();
          ctx.arc(Math.cos(markerA) * R, Math.sin(markerA) * R, 3.2, 0, Math.PI * 2);
          ctx.fillStyle = "rgba(212, 82, 38, 0.55)";
          ctx.fill();
        }
      }

      // 极细十字基准延长线
      ctx.strokeStyle = "rgba(25, 23, 21, 0.032)";
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(-radii[2] * 0.9, 0);
      ctx.lineTo(radii[2] * 0.9, 0);
      ctx.moveTo(0, -radii[2] * 0.9);
      ctx.lineTo(0, radii[2] * 0.9);
      ctx.stroke();

      ctx.restore();
    }

    if (!reduceMotion) {
      bgRaf = requestAnimationFrame(drawKineticDial);
    }
  }

  resizeCanvas();
  if (bgCanvas && ctx) {
    if (reduceMotion) {
      drawKineticDial(0);
    } else {
      bgRaf = requestAnimationFrame(drawKineticDial);
      document.addEventListener("visibilitychange", function () {
        if (!document.hidden && !bgRaf) {
          prevTs = 0;
          bgRaf = requestAnimationFrame(drawKineticDial);
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
      if (reduceMotion && ctx) drawKineticDial(0);
    }, 120);
  });

  /* ---------------------------------------------------------
     4. 真实设备像素剪影 + 交互舞台 + 可拖动电量水位试验台
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

  var STATE_COLOR_HEX = { low: "#ff99a4", warn: "#fce100", charge: "#6ccb5f", ok: "#f5f5f5" };

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

  /* --- 可拖动实时电量水位试验台 --- */
  var labRange = document.getElementById("labRange");
  var labChargeBtn = document.getElementById("labChargeBtn");
  var labPctText = document.getElementById("labPctText");
  var labStatePill = document.getElementById("labStatePill");
  var labStrip = document.getElementById("labStrip");
  var labTrays = document.getElementById("labTrays");
  var labCharging = false;

  function renderLab() {
    if (!labRange || !labStrip || !labTrays) return;
    var p = parseInt(labRange.value, 10) || 24;
    var stateKey = labCharging ? "charge" : (p <= 15 ? "low" : (p <= 30 ? "warn" : "ok"));
    if (labPctText) labPctText.textContent = p + "%";

    if (labStatePill) {
      labStatePill.className = "lab-state-pill" + (stateKey !== "ok" ? " is-" + stateKey : "");
      if (labCharging) labStatePill.textContent = p >= 100 ? "已充满 · 绿色" : "充电中 · 绿色闪电";
      else if (stateKey === "low") labStatePill.textContent = "电量过低（≤15%）· 红色提醒";
      else if (stateKey === "warn") labStatePill.textContent = "电量偏低（≤30%）· 黄色提示";
      else labStatePill.textContent = "电量充足 · 纯白常驻";
    }

    var stripCls = stateKey === "low" ? "is-low" : (stateKey === "charge" ? "is-charge" : (stateKey === "warn" ? "is-warn" : ""));
    var mouseCol = stateKey === "low" ? "#ff99a4" : "#f5f5f5";
    labStrip.innerHTML =
      '<span class="strip-item">' + solidGlyphSvg("keyboard", "#f5f5f5", 15) + "<span>82%</span></span>" +
      '<span class="strip-sep"></span>' +
      '<span class="strip-item ' + stripCls + '">' + solidGlyphSvg("mouse", mouseCol, 15) + (labCharging ? '<span class="strip-bolt">⚡</span>' : "") + "<span>" + p + "%</span></span>" +
      '<span class="strip-sep"></span>' +
      '<span class="strip-item is-charge">' + solidGlyphSvg("headset", "#f5f5f5", 15) + '<span class="strip-bolt">⚡</span><span>48%</span></span>';

    labTrays.innerHTML =
      '<span class="lab-tray-item">' + trayIconSvg("mouse", p, stateKey, 24) + "<span>鼠标</span></span>" +
      '<span class="lab-tray-item">' + trayIconSvg("keyboard", p, stateKey, 24) + "<span>键盘</span></span>" +
      '<span class="lab-tray-item">' + trayIconSvg("headset", p, stateKey, 24) + "<span>耳机</span></span>";
  }

  if (labRange) labRange.addEventListener("input", renderLab);
  if (labChargeBtn) {
    labChargeBtn.addEventListener("click", function () {
      labCharging = !labCharging;
      labChargeBtn.setAttribute("aria-pressed", labCharging ? "true" : "false");
      renderLab();
    });
  }
  renderLab();

  /* --- 首屏黑曜石交互桌面橱窗 --- */
  var DEMO_DEVICES = [
    { id: "atk", name: "ATK 鼠标", kind: "mouse", pct: 12, state: "low", conn: "2.4G", source: "ATK 无线接收器", badge: "通用适配", badgeCls: "is-gen", status: "电量低，该充电了", hoverStatus: "电量低 · 1 分钟前读取" },
    { id: "mx3s", name: "MX Anywhere 3S", kind: "mouse", pct: 20, state: "warn", conn: "2.4G", source: "罗技 Bolt 接收器", badge: "按资料适配", badgeCls: "", status: "休眠 · 上次读数 · 2 分钟前", hoverStatus: "电量偏低 · 休眠中 · 2 分钟前读取" },
    { id: "h9", name: "INZONE H9", kind: "headset", pct: 48, state: "charge", charging: true, conn: "2.4G", source: "Sony INZONE 接收器", badge: "已实机验证", badgeCls: "is-ok", status: "充电中", hoverStatus: "充电中 · 刚刚读取" },
    { id: "ulx", name: "Finalmouse ULX", kind: "mouse", pct: 72, est: true, state: "ok", conn: "2.4G", source: "Finalmouse 接收器", badge: "通用适配", badgeCls: "is-gen", status: "2.4G 已连接 · 电压估算", hoverStatus: "电压估算 · 3 分钟前读取" },
    { id: "mxks", name: "MX Keys S", kind: "keyboard", pct: 80, state: "ok", conn: "2.4G", source: "罗技 Bolt 接收器", badge: "按资料适配", badgeCls: "", status: "2.4G 已连接", hoverStatus: "在线 · 1 分钟前读取" }
  ];

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
      "translate3d(" + (btnRect.left - boxRect.left - 4) + "px, " + (btnRect.top - boxRect.top - 4) + "px, 0)";
    stageTabSlider.classList.add("is-ready");
    if (instant) {
      void stageTabSlider.offsetWidth;
      stageTabSlider.style.transition = "";
    }
  }

  function animateHeroNumber(el, fromVal, toVal) {
    if (pctAnimRaf) cancelAnimationFrame(pctAnimRaf);
    if (reduceMotion || fromVal === toVal) { el.textContent = String(toVal); return; }
    var start = performance.now();
    var dur = 320;
    function tickNum(now) {
      var p = Math.min(1, (now - start) / dur);
      var eased = 1 - Math.pow(1 - p, 3);
      el.textContent = String(Math.round(fromVal + (toVal - fromVal) * eased));
      if (p < 1) pctAnimRaf = requestAnimationFrame(tickNum);
      else pctAnimRaf = 0;
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
            if (activeScene === "panel") { activeHeroId = devId; renderPanel(true); }
            else if (activeScene !== "game") renderHoverCard(findDevice(devId), btn);
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
            if (activeScene === "panel") { activeHeroId = devId; renderPanel(true); }
            else if (activeScene !== "game") setScene("hover", btn);
          });
        })(tBtns[j]);
      }
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
      if (stageHintEl) stageHintEl.textContent = "把鼠标移到任务栏图标或直读条上，悬停卡片会在图标间平滑原地切换。";
      var anchor = explicitAnchor || (trayIconsEl && trayIconsEl.querySelector('[data-tray-dev="' + hoverAnchorId + '"]')) || (trayIconsEl && trayIconsEl.firstElementChild);
      renderHoverCard(findDevice(hoverAnchorId), anchor);
    } else if (scene === "strip") {
      if (stageHintTitleEl) stageHintTitleEl.textContent = "Windows 11 任务栏直读条";
      if (stageHintEl) stageHintEl.textContent = "在通知区左侧直接横排显示图标与百分比，低电变红、充电变绿带闪电。";
      var stripBtn = (stripEl && stripEl.querySelector('[data-strip-dev="atk"]')) || (stripEl && stripEl.firstElementChild);
      renderHoverCard(findDevice("atk"), stripBtn);
    } else if (scene === "panel") {
      if (stageHintTitleEl) stageHintTitleEl.textContent = "所有无线外设，一处看全";
      if (stageHintEl) stageHintEl.textContent = "点击右侧面板任意设备行切换主视图，或点击下方按钮实时触发交互。";
      if (hoverCardEl) hoverCardEl.classList.remove("is-show");
    } else if (scene === "game") {
      if (hoverCardEl) hoverCardEl.classList.remove("is-show");
    }
  }

  for (var t = 0; t < tabs.length; t += 1) {
    tabs[t].addEventListener("click", function () { setScene(this.getAttribute("data-scene")); });
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
      if (toastEl) setTimeout(function () { toastEl.classList.add("is-show"); }, 180);
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

  var clockEl = document.getElementById("simClock");
  function updateClock() {
    if (!clockEl) return;
    var now = new Date();
    var pad = function (n) { return n < 10 ? "0" + n : String(n); };
    clockEl.innerHTML = pad(now.getHours()) + ":" + pad(now.getMinutes()) + "<br>" + now.getFullYear() + "/" + (now.getMonth() + 1) + "/" + now.getDate();
  }

  renderPanel(false);
  renderTaskbar();
  updateClock();
  setInterval(updateClock, 30000);
  setScene("panel");
  requestAnimationFrame(function () { syncStageSlider(true); });

  /* ---------------------------------------------------------
     5. 下载与安装指南页交互（浏览器自动识别、步骤切换、下载反馈、复制校验）
     --------------------------------------------------------- */
  var ua = typeof navigator === "object" && typeof navigator.userAgent === "string" ? navigator.userAgent : "";
  var isInApp = /MicroMessenger|QQ\//i.test(ua);
  if (isInApp) {
    each("[data-inapp-notice]", function (el) { el.hidden = false; });
  }

  var guideTabs = document.querySelectorAll("[data-browser-tab]");
  if (guideTabs.length) {
    var activateGuideTab = function (name, moveFocus) {
      for (var i = 0; i < guideTabs.length; i += 1) {
        var tab = guideTabs[i];
        var active = tab.getAttribute("data-browser-tab") === name;
        tab.classList.toggle("is-active", active);
        tab.setAttribute("aria-selected", active ? "true" : "false");
        tab.tabIndex = active ? 0 : -1;
        if (active && moveFocus) tab.focus();
      }
      each("[data-browser-panel]", function (panel) {
        panel.hidden = panel.getAttribute("data-browser-panel") !== name;
      });
      each("[data-browser-steps]", function (sheet) {
        var show = sheet.getAttribute("data-browser-steps") === name;
        sheet.hidden = !show;
        if (show) sheet.classList.add("is-visible");
      });
    };

    var detected = "";
    if (isInApp || /Firefox\/|Quark\/|QIHU|360SE|360EE|QQBrowser|MetaSr|UCBrowser/i.test(ua)) {
      detected = "other";
    } else if (/Edg\/|Edge\//i.test(ua)) {
      detected = "edge";
    } else if (/Chrome\/|Chromium\//i.test(ua)) {
      detected = "chrome";
    }

    if (detected) {
      activateGuideTab(detected, false);
      each('[data-current-browser="' + detected + '"]', function (badge) { badge.hidden = false; });
    }

    for (var gt = 0; gt < guideTabs.length; gt += 1) {
      (function (index) {
        var tab = guideTabs[index];
        tab.addEventListener("click", function () {
          activateGuideTab(tab.getAttribute("data-browser-tab"), false);
        });
        tab.addEventListener("keydown", function (e) {
          var next = -1;
          if (e.key === "ArrowRight") next = (index + 1) % guideTabs.length;
          else if (e.key === "ArrowLeft") next = (index - 1 + guideTabs.length) % guideTabs.length;
          else if (e.key === "Home") next = 0;
          else if (e.key === "End") next = guideTabs.length - 1;
          if (next !== -1) {
            e.preventDefault();
            activateGuideTab(guideTabs[next].getAttribute("data-browser-tab"), true);
          }
        });
      })(gt);
    }
  }

  var dlStatus = document.querySelector("[data-dl-status]");
  if (dlStatus) {
    each("[data-download]", function (el) {
      el.addEventListener("click", function () { dlStatus.hidden = false; });
    });
  }

  each("[data-copy-cmd]", function (btn) {
    var resetTimer = null;
    btn.addEventListener("click", function () {
      var code = document.querySelector("[data-hash-cmd]");
      var text = code ? code.textContent : "";
      if (!text) return;
      var done = function () {
        btn.textContent = "已复制";
        if (resetTimer) clearTimeout(resetTimer);
        resetTimer = setTimeout(function () { btn.textContent = "复制"; }, 2000);
      };
      if (navigator.clipboard && typeof navigator.clipboard.writeText === "function") {
        navigator.clipboard.writeText(text).then(done, function () {});
      } else {
        var area = document.createElement("textarea");
        area.value = text;
        area.setAttribute("readonly", "");
        area.className = "skip";
        document.body.appendChild(area);
        area.select();
        try {
          if (document.execCommand("copy")) done();
        } catch (_) {}
        document.body.removeChild(area);
      }
    });
  });
})();
