// 几格官网 · 宣纸陶瓷 × 黑曜硬件橱窗交互脚本（严格 CSP 兼容）
(function () {
  "use strict";

  var reduceMotion = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var finePointer = window.matchMedia && window.matchMedia("(pointer: fine)").matches;

  /* ---------------------------------------------------------
     1. 自动同步最新正式版（同源 /latest.json -> 国内镜像）
     --------------------------------------------------------- */
  function safeReleaseUrl(value) {
    try {
      if (typeof value !== "string") return false;
      var parsed = new URL(value);
      if (parsed.protocol !== "https:" || parsed.username || parsed.password) return false;
      if (parsed.port !== "" && parsed.port !== "443") return false;
      var decoded = decodeURIComponent(parsed.pathname);
      if (/(^|\/)\.\.(\/|$)/.test(decoded) || decoded.indexOf("\\") !== -1) return false;
      var host = parsed.hostname.toLowerCase();
      if (host === "download.imfourj.cn") return decoded.indexOf("/jige-battery/") === 0;
      if (host === "github.com") return decoded.indexOf("/deepfry666/jige-battery/releases/download/") === 0;
      return false;
    } catch (_) {
      return false;
    }
  }

  function safeFilename(name) {
    return typeof name === "string" && /^[-A-Za-z0-9._ ]+\.exe$/i.test(name);
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

  function urlEndsWithPath(value, suffix) {
    try {
      return decodeURIComponent(new URL(value).pathname).endsWith(suffix);
    } catch (_) {
      return false;
    }
  }

  function applyLatest(data) {
    if (!data || !Array.isArray(data.assets)) return showFallback();
    var version = typeof data.tag_name === "string" && data.tag_name.trim() ? data.tag_name.trim() : "";
    if (!/^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/.test(version)) return showFallback();
    var expectedExe = "Jige-Setup-" + version.slice(1) + "-x64.exe";
    var exe = null;
    var sums = null;
    data.assets.forEach(function (a) {
      if (!a || typeof a.name !== "string") return;
      if (a.name === expectedExe) exe = a;
      if (a.name === "SHA256SUMS.txt") sums = a;
    });
    if (!exe || typeof exe.size !== "number" || !isFinite(exe.size) || exe.size <= 0 ||
      !safeReleaseUrl(exe.browser_download_url) ||
      !urlEndsWithPath(exe.browser_download_url, "/" + version + "/" + expectedExe)) return showFallback();

    var hasValidSums = !!(sums && safeReleaseUrl(sums.browser_download_url) &&
      urlEndsWithPath(sums.browser_download_url, "/" + version + "/SHA256SUMS.txt"));
    each("[data-download]", function (el) { el.href = exe.browser_download_url; });
    if (hasValidSums) {
      each("[data-sha]", function (el) { el.href = sums.browser_download_url; });
      each("[data-verify-block]", function (el) { el.hidden = false; });
    } else {
      each("[data-verify-block]", function (el) { el.hidden = true; });
    }
    if (version) each("[data-version]", function (el) { el.textContent = version; });
    var size = megabytes(exe.size);
    if (size) each("[data-size]", function (el) { el.textContent = size; });
    if (safeFilename(exe.name)) {
      each("[data-filename]", function (el) { el.textContent = exe.name; });
      if (hasValidSums) {
        each("[data-hash-cmd]", function (el) {
          el.textContent = 'Get-FileHash "$HOME\\Downloads\\' + exe.name + '" -Algorithm SHA256';
        });
      }
    }

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
    fetch(new URL("latest.json", document.baseURI).href, { cache: "no-cache", signal: controller ? controller.signal : undefined })
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
     4. 首屏交互舞台（任务栏直读条为程序真实导出的像素图）
     --------------------------------------------------------- */
  var ICONS = {
    mouse: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><rect x="6" y="2.5" width="12" height="19" rx="6"/><path d="M12 2.5v6.5" stroke-linecap="round"/></svg>',
    headset: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"><path d="M4 15v-3a8 8 0 0 1 16 0v3"/><rect x="3" y="14" width="4.5" height="7" rx="1.6"/><rect x="16.5" y="14" width="4.5" height="7" rx="1.6"/></svg>',
    keyboard: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"><rect x="2.5" y="6" width="19" height="12" rx="2.5"/><path d="M6 10h.01M9.3 10h.01M12.6 10h.01M15.9 10h.01M18 10h.01M8 14.2h8"/></svg>'
  };

  /* --- 首屏黑曜石交互桌面橱窗 --- */
  var DEMO_DEVICES = [
    { id: "atk", name: "ATK 鼠标", kind: "mouse", pct: 12, state: "low", conn: "2.4G", source: "ATK 无线接收器", badge: "通用适配", badgeCls: "is-gen", status: "电量低，该充电了", hoverStatus: "电量低 · 1 分钟前读取" },
    { id: "mx3s", name: "MX Anywhere 3S", kind: "mouse", pct: 20, state: "warn", conn: "2.4G", source: "罗技 Bolt 接收器", badge: "按资料适配", badgeCls: "", status: "休眠 · 上次读数 · 2 分钟前", hoverStatus: "电量偏低 · 休眠中 · 2 分钟前读取" },
    { id: "h9", name: "INZONE H9", kind: "headset", pct: 45, state: "charge", charging: true, conn: "2.4G", source: "Sony INZONE 接收器", badge: "已实机验证", badgeCls: "is-ok", status: "充电中", hoverStatus: "充电中 · 刚刚读取" },
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

  // 直读条只显示「自定义」里勾选的设备，顺序与主面板一致：ATK 鼠标、INZONE H9、MX Keys S。
  var STRIP_IDS = ["atk", "h9", "mxks"];

  function renderHoverList() {
    if (!hoverCardEl || !stage || !stripEl) return;
    var rows = "";
    STRIP_IDS.forEach(function (id) {
      var d = findDevice(id);
      var tc = textClassFor(d.state);
      rows +=
        '<div class="hl-row">' +
          '<span class="fo-row-ico">' + ICONS[d.kind] + "</span>" +
          '<span class="fo-row-meta">' +
            '<span class="fo-row-name">' + d.name + "</span>" +
            '<span class="fo-row-st ' + tc + '">' + d.status + "</span>" +
          "</span>" +
          cellsHtml(d.pct, d.state, 5, true, d.charging, false) +
          '<span class="fo-row-num ' + tc + '">' + d.pct + "<small>%</small></span>" +
        "</div>";
    });
    hoverCardEl.classList.add("is-list");
    hoverCardEl.innerHTML = '<div class="hl-head">几格 · ' + STRIP_IDS.length + " 台设备</div>" + rows;
    var sr = stage.getBoundingClientRect();
    var br = stripEl.getBoundingClientRect();
    var cardW = hoverCardEl.offsetWidth || Math.min(272, sr.width - 24);
    var cx = br.left - sr.left + br.width / 2;
    hoverCardEl.style.left = Math.max(12, Math.min(sr.width - cardW - 12, Math.round(cx - cardW / 2))) + "px";
    hoverCardEl.classList.add("is-show");
  }

  if (stripEl) {
    stripEl.addEventListener("mouseenter", function () {
      if (activeScene === "hover") renderHoverList();
    });
    stripEl.addEventListener("click", function () {
      if (activeScene === "game") return;
      if (toastEl) toastEl.classList.remove("is-show");
      setScene(activeScene === "panel" && flyout && flyout.classList.contains("is-open") ? "hover" : "panel");
    });
  }

  function setScene(scene) {
    activeScene = scene;
    for (var i = 0; i < tabs.length; i += 1) {
      var on = tabs[i].getAttribute("data-scene") === scene;
      tabs[i].classList.toggle("is-active", on);
      tabs[i].setAttribute("aria-selected", on ? "true" : "false");
      tabs[i].tabIndex = on ? 0 : -1;
    }
    syncStageSlider(false);

    if (stage) {
      stage.classList.toggle("is-hover", scene === "hover");
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
      if (stageHintTitleEl) stageHintTitleEl.textContent = "鼠标移上去，就能看全部读数";
      if (stageHintEl) stageHintEl.textContent = "悬停在直读条上，会列出每台设备的名称、状态和电量。";
      renderHoverList();
    } else {
      if (hoverCardEl) hoverCardEl.classList.remove("is-show");
      if (scene === "panel") {
        if (stageHintTitleEl) stageHintTitleEl.textContent = "所有无线外设，一处看全";
        if (stageHintEl) stageHintEl.textContent = "右下角时钟左边就是几格的任务栏直读条。点它打开面板，点面板里的设备可以切换到最上面看。";
      }
    }
  }

  for (var t = 0; t < tabs.length; t += 1) {
    (function (idx) {
      tabs[idx].addEventListener("click", function () { setScene(this.getAttribute("data-scene")); });
      tabs[idx].addEventListener("keydown", function (ev) {
        var next = -1;
        if (ev.key === "ArrowRight") next = (idx + 1) % tabs.length;
        else if (ev.key === "ArrowLeft") next = (idx - 1 + tabs.length) % tabs.length;
        else if (ev.key === "Home") next = 0;
        else if (ev.key === "End") next = tabs.length - 1;
        if (next >= 0) {
          ev.preventDefault();
          setScene(tabs[next].getAttribute("data-scene"));
          tabs[next].focus();
        }
      });
    })(t);
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

  var clockEl = document.getElementById("simClock");
  function updateClock() {
    if (!clockEl) return;
    var now = new Date();
    var pad = function (n) { return n < 10 ? "0" + n : String(n); };
    clockEl.innerHTML = pad(now.getHours()) + ":" + pad(now.getMinutes()) + "<br>" + now.getFullYear() + "/" + (now.getMonth() + 1) + "/" + now.getDate();
  }

  renderPanel(false);
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
