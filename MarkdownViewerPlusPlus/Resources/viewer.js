// MarkdownViewer++ page script: renders HTML pushed by the plugin and reports
// link clicks / scrolling back so Notepad++ can follow along.
(function () {
  'use strict';

  const host = window.chrome && window.chrome.webview;
  const post = (msg) => { if (host) host.postMessage(msg); };

  let content = null;
  let lineMap = null;           // [{ line, el }] in document order, built lazily
  let userScrollUntil = 0;      // while > now, scroll events come from the user
  let suppressReportUntil = 0;  // while > now, don't report scrolls (we caused them)
  let scrollReportPending = false;

  function $(id) { return document.getElementById(id); }

  // ---------- line <-> position mapping ----------

  function buildLineMap() {
    const map = [];
    let last = -1;
    for (const el of content.querySelectorAll('[data-line]')) {
      const line = parseInt(el.getAttribute('data-line'), 10);
      if (isNaN(line) || line < last) continue;    // keep it monotonic
      if (el.offsetParent === null && el.tagName !== 'TR') continue; // hidden
      map.push({ line, el });
      last = line;
    }
    return map;
  }

  function getMap() { return lineMap || (lineMap = buildLineMap()); }
  function topOf(el) { return el.getBoundingClientRect().top + window.scrollY; }

  function scrollToLine(line, atEnd) {
    const map = getMap();
    let y = 0;
    if (atEnd) {
      y = document.documentElement.scrollHeight;
    } else if (line <= 0) {
      y = 0;
    } else if (map.length) {
      let i = 0;
      while (i + 1 < map.length && map[i + 1].line <= line) i++;
      const a = map[i];
      const b = map[i + 1];
      const ta = topOf(a.el);
      if (line < a.line) {
        y = ta * line / Math.max(1, a.line);          // before the first block
      } else if (b) {
        const tb = topOf(b.el);
        y = ta + (tb - ta) * (line - a.line) / Math.max(1, b.line - a.line);
      } else {
        y = ta + (a.el.offsetHeight || 0) * Math.min(1, (line - a.line) / 20);
      }
    }
    suppressReportUntil = performance.now() + 150;
    window.scrollTo(0, Math.max(0, y - 4));
  }

  function lineAtTop() {
    const map = getMap();
    if (!map.length) return 0;
    const y = window.scrollY + 4;
    let i = 0;
    while (i + 1 < map.length && topOf(map[i + 1].el) <= y) i++;
    const a = map[i];
    const b = map[i + 1];
    const ta = topOf(a.el);
    if (y < ta) return Math.floor(a.line * y / Math.max(1, ta));
    if (!b) return a.line;
    const tb = topOf(b.el);
    return Math.floor(a.line + (b.line - a.line) * (y - ta) / Math.max(1, tb - ta));
  }

  function lineOfElement(el) {
    const withLine = el.closest('[data-line]');
    if (withLine) return parseInt(withLine.getAttribute('data-line'), 10);
    // e.g. a footnote anchor: use the first block after it
    for (const item of getMap()) {
      if (el.compareDocumentPosition(item.el) & Node.DOCUMENT_POSITION_FOLLOWING) return item.line;
    }
    return -1;
  }

  // ---------- internal links ----------

  function slug(s) {
    return (s || '').normalize('NFD').replace(/[̀-ͯ]/g, '')
      .toLowerCase().trim().replace(/[^\p{L}\p{N}\s_-]/gu, '').replace(/\s+/g, '-');
  }

  function findTarget(fragment) {
    let id = fragment;
    try { id = decodeURIComponent(fragment); } catch (e) { }
    if (!id) return null;
    let el = document.getElementById(id) || document.getElementsByName(id)[0];
    if (el) return el;
    // Fallback: tolerate differences in case/accents/punctuation between the link and the heading id
    const wanted = slug(id);
    for (const cand of content.querySelectorAll('[id]')) {
      if (slug(cand.id) === wanted) return cand;
    }
    for (const h of content.querySelectorAll('h1,h2,h3,h4,h5,h6')) {
      if (slug(h.textContent) === wanted) return h;
    }
    return null;
  }

  function goToFragment(fragment, notifyEditor) {
    const el = findTarget(fragment);
    if (!el) return false;
    suppressReportUntil = performance.now() + 400;
    el.scrollIntoView({ block: 'start' });
    el.classList.remove('mdv-target');
    void el.offsetWidth;
    el.classList.add('mdv-target');
    if (notifyEditor) {
      const line = lineOfElement(el);
      if (line >= 0) post({ type: 'line', line });
    }
    return true;
  }

  document.addEventListener('click', (e) => {
    const a = e.target.closest && e.target.closest('a[href]');
    if (!a) return;
    const href = a.getAttribute('href');
    e.preventDefault();
    if (href.startsWith('#')) {
      goToFragment(href.substring(1), true);
    } else {
      post({ type: 'link', href });
    }
  }, true);
  document.addEventListener('auxclick', (e) => {
    if (e.target.closest && e.target.closest('a[href]')) e.preventDefault();
  }, true);

  // ---------- scrolling (viewer -> editor) ----------

  const markUserScroll = () => { userScrollUntil = performance.now() + 600; };
  window.addEventListener('wheel', markUserScroll, { passive: true });
  window.addEventListener('mousedown', markUserScroll, { passive: true });
  window.addEventListener('touchstart', markUserScroll, { passive: true });
  window.addEventListener('keydown', (e) => {
    if (['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', ' '].includes(e.key)) markUserScroll();

    // Viewer shortcuts (also handled by the host; whichever sees the key first wins)
    const k = e.key.toLowerCase();
    let command = null;
    if (k === 'escape' && !e.ctrlKey && !e.shiftKey && !e.altKey) command = 'close';
    else if (k === 'm' && e.ctrlKey && e.shiftKey && !e.altKey) command = 'close';
    else if (k === 'f5' || (k === 'r' && e.ctrlKey && !e.shiftKey)) command = 'refresh';
    else if (k === 's' && e.ctrlKey && !e.shiftKey && !e.altKey) command = 'save';
    if (command) {
      e.preventDefault();
      post({ type: 'command', command });
    }
  });
  window.addEventListener('scroll', () => {
    const now = performance.now();
    if (now > userScrollUntil || now < suppressReportUntil || scrollReportPending) return;
    scrollReportPending = true;
    requestAnimationFrame(() => {
      scrollReportPending = false;
      const atEnd = window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 2;
      post({ type: 'scroll', line: lineAtTop(), atEnd });
    });
  }, { passive: true });

  window.addEventListener('resize', () => { lineMap = null; });

  // ---------- messages from the plugin ----------

  function applyTheme(t) {
    const root = document.documentElement;
    root.setAttribute('data-theme', t.dark ? 'dark' : 'light');
    if (t.bg) root.style.setProperty('--bg', t.bg); else root.style.removeProperty('--bg');
    if (t.fg) root.style.setProperty('--fg', t.fg); else root.style.removeProperty('--fg');
  }

  function render(m) {
    if (m.baseUrl) $('mdv-base').setAttribute('href', m.baseUrl);
    if (typeof m.css === 'string') $('mdv-user-css').textContent = m.css;
    if (m.title) document.title = m.title;
    const y = window.scrollY;
    content.innerHTML = m.html;
    lineMap = null;
    // Recompute the map once images have their real size
    for (const img of content.querySelectorAll('img')) {
      if (!img.complete) img.addEventListener('load', () => { lineMap = null; }, { once: true });
    }
    if (m.fragment) {
      if (!goToFragment(m.fragment, true)) window.scrollTo(0, 0);
    } else if (m.resetScroll) {
      suppressReportUntil = performance.now() + 150;
      window.scrollTo(0, 0);
    } else {
      suppressReportUntil = performance.now() + 150;
      window.scrollTo(0, y);
    }
  }

  function onMessage(m) {
    switch (m && m.type) {
      case 'render': render(m); break;
      case 'theme': applyTheme(m); break;
      case 'syncToLine':
        if (performance.now() > userScrollUntil) scrollToLine(m.line, m.atEnd);
        break;
      case 'fragment': goToFragment(m.fragment, true); break;
    }
  }

  document.addEventListener('DOMContentLoaded', () => {
    content = $('mdv-content');
    if (host) host.addEventListener('message', (e) => onMessage(e.data));
    post({ type: 'ready' });
  });
})();
