/* Nullean.Entities landing page. Progressive enhancement only: the page reads fully without this file. */
(function () {
  'use strict';
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
  var mq = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : { matches: false };
  var reduced = function () { return mq.matches; };
  var wait = function (ms) { return new Promise(function (r) { setTimeout(r, ms); }); };

  /* ---- 1. plant a bug ---- */
  var demo = $('#demo'), plant = $('#plant'), report = $('#report'), live = $('#demo-live'), dot = $('#dot');
  var runId = 0;
  if (demo && plant && report) {
    var lines = $$('.l', report);
    var summary = 'Test failed: Monotonicity. Rule broken: version-never-goes-back. Expected NonDecreasing but moved from 1 to 0. Operation: Reset(). Smallest case found after 4 shrinks.';
    dot.style.background = 'var(--fg-3)';
    var showAll = function () { lines.forEach(function (l) { l.classList.add('on'); l.classList.remove('typing'); }); };
    var clear = function () { lines.forEach(function (l) { l.classList.remove('on', 'typing'); }); };
    plant.addEventListener('click', async function () {
      var on = plant.getAttribute('aria-pressed') !== 'true';
      plant.setAttribute('aria-pressed', String(on));
      $('#plant-label').textContent = on ? 'Remove the bug' : 'Plant a bug';
      demo.classList.toggle('is-planted', on);
      var id = ++runId;
      live.textContent = '';
      clear();
      if (!on) { dot.style.background = 'var(--fg-3)'; return; }
      dot.style.background = 'var(--fail)';
      if (reduced()) { showAll(); live.textContent = summary; return; }
      for (var i = 0; i < lines.length; i++) {
        if (id !== runId) return;
        if (i) lines[i - 1].classList.remove('typing');
        lines[i].classList.add('on', 'typing');
        await wait(70 + Math.min(lines[i].textContent.length * 4, 240));
      }
      if (id !== runId) return;
      lines[lines.length - 1].classList.remove('typing');
      live.textContent = summary;
    });
  }

  /* ---- 3. nine questions: hover and focus replay via CSS; tap/click pins one row ---- */
  var rows = $$('#qtable tr.q');
  rows.forEach(function (tr) {
    var btn = $('.qbtn', tr);
    btn.addEventListener('click', function () {
      var was = tr.classList.contains('is-on');
      rows.forEach(function (r) { r.classList.remove('is-on'); $('.qbtn', r).setAttribute('aria-pressed', 'false'); });
      if (!was) { tr.classList.add('is-on'); btn.setAttribute('aria-pressed', 'true'); }
    });
  });

  /* ---- 2. shrinking ---- */
  var sLine = $('#shrink-line'), sMeter = $('#meter'), sSeed = $('#shrink-seed'), sStep = $('#shrink-step'), sLive = $('#shrink-live'), replay = $('#replay');
  if (sLine) {
    var F = function (n) { return '<span class="fz">' + new Array(n + 1).join('▒') + '</span>'; };
    var mk = function (lines, total, label) {
      return 'Order { Lines = [' + lines + '], Status = Cancelled, Version = 1, Total = ' + total + ', Label = "' + label + '" }';
    };
    var steps = [
      mk(F(12), F(6), F(12)),
      mk(F(4), F(6), F(12)),
      mk('', F(6), F(12)),
      mk('', '0', F(12)),
      mk('', '0', '')
    ];
    var pct = [100, 66, 44, 22, 6];
    var seed = '.Seed("7ub38ErvzD2a")';
    var running = 0;
    var paint = function (i) {
      sLine.innerHTML = steps[i];
      sMeter.style.width = pct[i] + '%';
      sStep.textContent = i === 0 ? 'generated value' : 'shrink ' + i + ' of 4';
    };
    var run = async function () {
      var id = ++running;
      replay.disabled = true; sSeed.textContent = ''; sLive.textContent = '';
      paint(0);
      for (var i = 1; i < steps.length; i++) {
        await wait(reduced() ? 200 : 700);
        if (id !== running) return;
        paint(i);
      }
      await wait(reduced() ? 100 : 350);
      if (id !== running) return;
      sSeed.textContent = 'To run this case again: ' + seed;
      sLive.textContent = 'Shrunk in 4 steps to the smallest case. Replay seed ' + seed + ' gives the same result.';
      replay.disabled = false;
    };
    paint(0);
    replay.addEventListener('click', run);
    var started = false;
    var go = function () { if (!started) { started = true; run(); } };
    if ('IntersectionObserver' in window) {
      var io = new IntersectionObserver(function (es) { es.forEach(function (e) { if (e.isIntersecting) { go(); io.disconnect(); } }); }, { threshold: 0.4 });
      io.observe($('#shrink'));
    } else { go(); }
  }

  /* ---- 4. rollout ---- */
  var range = $('#date');
  if (range) {
    var out = $('#date-out'), sev = $('#sev'), sevLabel = $('#sev-label'), eff = $('#dial-effect');
    var base = Date.UTC(2026, 10, 1), THRESH = 30;
    var fmt = function (d) { return new Date(base + d * 864e5).toISOString().slice(0, 10); };
    var update = function () {
      var d = +range.value, err = d >= THRESH;
      out.textContent = fmt(d);
      sev.className = 'sev ' + (err ? 'err' : 'warn');
      sevLabel.textContent = err ? 'ERROR' : 'WARNING';
      eff.textContent = err ? 'Findings are errors from this date.' : 'Findings are reported. Nothing breaks.';
      range.setAttribute('aria-valuetext', fmt(d) + ', ' + (err ? 'error' : 'warning'));
    };
    range.addEventListener('input', update);
    update();
  }

  /* ---- hero story: record -> table -> time -> rules -> guardrails ---- */
  var story = $('#story');
  if (story) {
    var steps = $$('.step', story), btns = $$('.sbtn', story), bar = $('#s-bar'), sl = $('#s-live');
    var playBtn = $('#s-play'), cur = 0, timer = null, playing = false;
    var titles = ['Record', 'Table', 'Time', 'Rules', 'Guardrails'];
    var show = function (i, announce) {
      cur = Math.max(0, Math.min(steps.length - 1, i));
      steps.forEach(function (s, k) { s.classList.toggle('is-active', k === cur); });
      btns.forEach(function (b, k) { if (k === cur) b.setAttribute('aria-current', 'step'); else b.removeAttribute('aria-current'); });
      bar.style.width = ((cur + 1) / steps.length * 100) + '%';
      story.setAttribute('data-stage', String(cur + 1));
      if (announce) sl.textContent = 'Step ' + (cur + 1) + ' of ' + steps.length + ': ' + titles[cur];
    };
    var stop = function (label) { playing = false; clearInterval(timer); playBtn.textContent = label || 'Play'; };
    var play = function () {
      if (cur === steps.length - 1) show(0);
      playing = true; playBtn.textContent = 'Pause';
      clearInterval(timer);
      timer = setInterval(function () {
        if (cur < steps.length - 1) show(cur + 1);
        else stop('Replay');
      }, 6500);
    };
    btns.forEach(function (b) { b.addEventListener('click', function () { stop(); show(+b.getAttribute('data-go'), true); }); });
    $('#s-prev').addEventListener('click', function () { stop(); show(cur - 1, true); });
    $('#s-next').addEventListener('click', function () { stop(); show(cur + 1, true); });
    playBtn.addEventListener('click', function () { if (playing) stop(); else play(); });
    show(0);
    playBtn.textContent = 'Play';
    var autoStarted = false;
    if (!reduced() && 'IntersectionObserver' in window) {
      var io2 = new IntersectionObserver(function (es) {
        es.forEach(function (e) { if (e.isIntersecting && !autoStarted) { autoStarted = true; play(); io2.disconnect(); } });
      }, { threshold: 0.5 });
      io2.observe(story);
    }
  }
})();
