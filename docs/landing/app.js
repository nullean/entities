/* Nullean.Entities landing page. Progressive enhancement only: the page reads fully without this file. */
(function () {
  'use strict';
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
  var mq = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : { matches: false };
  var reduced = function () { return mq.matches; };
  var wait = function (ms) { return new Promise(function (r) { setTimeout(r, ms); }); };
  var clamp = function (v, a, b) { return Math.max(a, Math.min(b, v)); };
  var seen = function (el, cb, th) {
    if (!el) return;
    if (!('IntersectionObserver' in window)) { cb(); return; }
    var io = new IntersectionObserver(function (es) { es.forEach(function (e) { if (e.isIntersecting) { io.disconnect(); cb(); } }); }, { threshold: th || 0.4 });
    io.observe(el);
  };
  /* tiny seeded generator: the walks are random-looking but repeatable */
  var rng = function (a) { return function () { a |= 0; a = a + 0x6D2B79F5 | 0; var t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; };

  /* ================= shrinking a failing action sequence (hero stage 5 and the services section) ================= */
  var SEQ = [
    ['Move(9)', 'Move(8)', 'Move(3)', 'Move(5)', 'Move(2)', 'Move(1)'],
    ['Move(9)', 'Move(3)', 'Move(2)', 'Move(1)'],
    ['Move(3)', 'Move(2)', 'Move(1)'],
    ['Move(3)', 'Move(1)'],
    ['Move(1)']
  ];
  var SEQ_PCT = [100, 62, 34, 22, 8];
  var SEED = '7DTWL8nvnax7';
  function shrinker(root) {
    if (!root) return null;
    var stepEl = $('.wal-step', root), seq = $('.wal-seq', root), meter = $('.wal-meter', root), rep = $('.wal-report', root), replay = $('.wal-replay', root);
    var token = 0;
    var paint = function (i) {
      seq.textContent = SEQ[i].join(' → ');
      meter.style.width = SEQ_PCT[i] + '%';
      stepEl.textContent = i === 0 ? 'failing sequence found: ' + SEQ[0].length + ' actions' : 'shrink ' + i + ' of ' + (SEQ.length - 1) + ': ' + SEQ[i].length + ' actions';
    };
    var api = {
      clear: function () { token++; root.classList.remove('is-fail'); rep.classList.remove('is-done'); if (replay) replay.disabled = true; },
      run: async function () {
        var id = ++token;
        root.classList.add('is-fail'); rep.classList.remove('is-done'); if (replay) replay.disabled = true;
        paint(0);
        for (var i = 1; i < SEQ.length; i++) {
          await wait(reduced() ? 200 : 750);
          if (id !== token) return false;
          paint(i);
        }
        await wait(reduced() ? 100 : 350);
        if (id !== token) return false;
        rep.classList.add('is-done');
        if (replay) replay.disabled = false;
        return true;
      }
    };
    if (replay) replay.addEventListener('click', function () { api.run(); });
    return api;
  }
  var WAL_SUMMARY = 'Test failed: Conservation. Rule broken: money-is-conserved did not conserve its value, was 40 and became 39. Smallest failing case: Move(1). Replay with seed ' + SEED + '.';

  /* ================= hero: one Order, seven stages ================= */
  var build = $('#build');
  if (build) {
    try { initBuild(build); } catch (err) { if (window.console) console.error(err); }
  }
  function initBuild(build) {
    var N = 7;
    var NAMES = ['Types only', 'Relations', 'Constraints', 'State machine', 'AutoTests', 'Services', 'Confidence'];
    var SUM = [
      'A record gives the Order its shape and nothing more. Three questions it cannot answer are listed.',
      'Order relates to Line through foreign keys. A dangling Line.OrderId is flagged in red.',
      'Two named rules sit next to the type: total-never-negative and money-is-conserved.',
      'The lifecycle is a state machine. The edge from Cancelled to Shipped is denied.',
      'Five kinds of test are generated: Exhaustive, Sample, Walks, Conform and Commutes.',
      'The same rules run on a mutable, async Wallet service.',
      'Every rule was reached. Hope has become evidence.'
    ];
    var FILES = ['Order.cs', 'Line.cs', 'OrderConstraints.cs', 'OrderMachine.cs', 'OrderTests.cs', 'Wallet.cs', 'CI'];
    var METER = [4, 16, 34, 52, 70, 86, 100];
    var WORDS = ['hope', 'a hunch', 'checked', 'specified', 'tested', 'stress-tested', 'evidence'];
    var DWELL = [5200, 5600, 6800, 6800, 7600, 9500, 0];
    var PLANTABLE = { 2: 1, 4: 1, 5: 1 };

    var stages = $$('.stg', build);
    var layers = $$('.ly', build);
    var views = $$('.vw', build);
    var caps = stages.map(function (s) { return $('.stg-h', s); });
    var capsEl = $('#b-caps'), asksEl = $('#b-asks');

    /* restructure: the stacked reading becomes one live code pane and one live view pane */
    var live = document.createElement('div');
    live.className = 'b-live'; live.id = 'b-live'; live.tabIndex = 0;
    live.setAttribute('role', 'group');
    live.setAttribute('aria-label', 'Order build, stage by stage. Left and right arrow keys change the stage.');
    live.innerHTML = '<div class="win b-code"><div class="win-bar"><span class="file" id="b-file">Order.cs</span><span class="right" id="b-diff">+ added at this stage</span></div><pre class="code" tabindex="0" aria-label="Code, cumulative"><code id="b-code"></code></pre></div><div class="b-view" id="b-view"></div>';
    var codeEl = $('#b-code', live), codePre = codeEl.parentNode, viewEl = $('#b-view', live);
    layers.forEach(function (l) { codeEl.appendChild(l); });
    views.forEach(function (v) { viewEl.appendChild(v); });
    caps.forEach(function (c) { capsEl.appendChild(c); });
    build.insertBefore(live, asksEl);
    var srcAsks = $('.asks', views[0]);
    $$('li', srcAsks).forEach(function (li) { asksEl.appendChild(li); });
    srcAsks.parentNode.removeChild(srcAsks);
    var asks = $$('li', asksEl);

    var btns = $$('.sbtn', build), range = $('#b-range'), fill = $('#b-fill'), stateEl = $('#b-state');
    var playBtn = $('#b-play'), plantBtn = $('#b-plant'), plantLabel = $('#b-plant-label'), say = $('#b-announce');
    var fileEl = $('#b-file'), diffEl = $('#b-diff');
    var cur = 0, playing = false, timer = null, planted = false, autoTimers = [], autoDone = false, token = 0;

    /* ---- per-stage pieces ---- */
    var totalIn = $('#b-total'), viol = $('#b-viol'), rules = $$('.rl', build);
    var wal = shrinker($('#b-wal', build)), wdot = $('#b-wdot');
    var conformTest = $('.test.conform', build), conformTk = $('.tk', conformTest);
    var machine = $('.mch', views[3]);
    var sayT = null;
    var announce = function (t) { say.textContent = ''; setTimeout(function () { say.textContent = t; }, 30); };
    var announceLater = function (t) { clearTimeout(sayT); sayT = setTimeout(function () { announce(t); }, 450); };

    function validate() {
      var v = parseFloat(totalIn.value); if (isNaN(v)) v = 0;
      var neg = v < 0, cons = v !== 42;
      var broken = [];
      rules.forEach(function (r) {
        var name = r.getAttribute('data-rule');
        var bad = name === 'total-never-negative' ? neg : cons;
        r.classList.toggle('is-bad', bad);
        $('.st', r).firstChild.textContent = bad ? '✕' : '✓';
        $('.stt', r).textContent = bad ? 'broken' : 'holds';
        if (bad) broken.push(name);
      });
      if (!broken.length) {
        viol.innerHTML = '<span class="l">// every rule holds. Edit Total, or plant a bug.</span>';
        return;
      }
      var h = '<span class="l hd">Rejected: Order.Total = ' + v + '</span>';
      broken.forEach(function (n) {
        h += '<span class="l"><span class="rk">Rule broken   </span>' + n + '</span>';
        if (n === 'total-never-negative') h += '<span class="l"><span class="rk">Member        </span>Total</span>';
        h += '<span class="l"><span class="rk">Message       </span>' + (n === 'total-never-negative' ? 'An order total is never negative.' : 'The total is the sum of the lines.') + '</span>';
      });
      viol.innerHTML = h;
      announceLater('Rejected. Order.Total ' + v + ' breaks ' + broken.join(' and ') + '.' + (broken.indexOf('total-never-negative') >= 0 ? ' Member: Total.' : ''));
    }
    if (totalIn) {
      totalIn.addEventListener('input', function () { stop(); validate(); });
      $('#b-total-reset').addEventListener('click', function () { stop(); totalIn.value = '42'; validate(); });
      validate();
    }
    var denyBtn = $('#b-try-deny'), denyOut = $('#b-deny-out');
    if (denyBtn) denyBtn.addEventListener('click', function () {
      stop();
      var n = $('[data-n="Cancelled"]', machine), s = $('[data-n="Shipped"]', machine);
      [n, s].forEach(function (g) { g.classList.add('flash'); });
      denyOut.textContent = '✕ no edge from Cancelled to Shipped. Denied.';
      announce('Denied. There is no edge from Cancelled to Shipped.');
      setTimeout(function () { [n, s].forEach(function (g) { g.classList.remove('flash'); }); }, 1400);
    });

    var cntRun = 0;
    function counters() {
      var id = ++cntRun;
      $$('.cnt', build).forEach(function (c, i) {
        var to = +c.getAttribute('data-to');
        if (reduced()) { c.textContent = to; return; }
        c.textContent = '0';
        var start = null, delay = 300 + i * 400, dur = 1500;
        var tick = function (t) {
          if (id !== cntRun) return;
          if (start === null) start = t + delay;
          var p = clamp((t - start) / dur, 0, 1);
          c.textContent = Math.round(to * (1 - Math.pow(1 - p, 3)));
          if (p < 1) requestAnimationFrame(tick);
        };
        requestAnimationFrame(tick);
      });
    }

    /* ---- plant a bug: works at stages 2, 4 and 5 ---- */
    function setPlanted(on, quiet) {
      planted = on;
      plantBtn.setAttribute('aria-pressed', String(on));
      plantLabel.textContent = on ? 'Remove the bug' : 'Plant a bug';
      build.classList.toggle('is-planted', on);
      if (cur === 2 && totalIn) { totalIn.value = on ? '-5' : '42'; validate(); }
      if (cur === 4) {
        conformTest.classList.toggle('is-fail', on);
        conformTk.textContent = on ? '✕' : '✓';
        if (!quiet) announce(on ? 'Conform failed. The implementation allows Cancelled to Shipped. The diagram denies it.' : 'Bug removed. All five generated tests pass.');
      }
      if (cur === 5) {
        wdot.style.background = on ? 'var(--fail)' : '';
        if (on) {
          wal.run().then(function (done) { if (done && !quiet) announce(WAL_SUMMARY); else if (done) announce(WAL_SUMMARY); });
        } else { wal.clear(); if (!quiet) announce('Bug removed. Money is conserved across every sampled sequence.'); }
      }
    }
    plantBtn.addEventListener('click', function () { stop(); setPlanted(!planted); });

    /* ---- stage change ---- */
    function setStage(i, how) {
      var next = clamp(i, 0, N - 1);
      var changed = next !== cur || how === 'init';
      if (planted && changed) setPlanted(false, true);
      cur = next;
      token++;
      build.setAttribute('data-stage', String(cur));
      layers.forEach(function (l) {
        var s = +l.getAttribute('data-s');
        l.classList.toggle('is-on', s <= cur);
        l.classList.remove('is-new');
        if (s === cur) { void l.offsetWidth; l.classList.add('is-new'); }
      });
      views.forEach(function (v) { v.classList.toggle('is-on', +v.getAttribute('data-s') === cur); });
      caps.forEach(function (c, k) { c.classList.toggle('is-on', k === cur); });
      btns.forEach(function (b, k) {
        if (k === cur) b.setAttribute('aria-current', 'step'); else b.removeAttribute('aria-current');
        b.classList.toggle('is-done', k < cur);
      });
      range.value = String(cur);
      range.setAttribute('aria-valuetext', 'Stage ' + cur + ' of ' + (N - 1) + ', ' + NAMES[cur]);
      fill.style.width = METER[cur] + '%';
      stateEl.textContent = WORDS[cur];
      asks.forEach(function (li) { li.classList.toggle('is-ok', +li.getAttribute('data-a') <= cur); });
      var ok = !!PLANTABLE[cur];
      plantBtn.disabled = !ok;
      plantBtn.title = ok ? '' : 'Works at stages 2, 4 and 5';
      fileEl.textContent = FILES[cur];
      diffEl.textContent = '+ stage ' + cur + ' added';
      var nl = $('.ly.is-new', codeEl);
      if (nl) codePre.scrollTop = Math.max(0, nl.offsetTop - 12);
      if (cur === 4) counters();
      if (cur === 5) { wal.clear(); wdot.style.background = ''; }
      if (cur === 3) { denyOut.textContent = ''; }
      if (how !== 'init') announce('Stage ' + cur + ' of ' + (N - 1) + ': ' + NAMES[cur] + '. ' + SUM[cur]);
      if (how === 'auto') autoDemo(cur);
    }

    /* ---- autoplay once, then the reader takes over ---- */
    function clearAuto() { autoTimers.forEach(clearTimeout); autoTimers = []; }
    function autoDemo(s) {
      clearAuto();
      var at = function (ms, fn) { autoTimers.push(setTimeout(function () { if (playing) fn(); }, ms)); };
      if (s === 2) at(2600, function () { setPlanted(true); });
      if (s === 4) at(3800, function () { setPlanted(true); });
      if (s === 5) at(1500, function () { setPlanted(true); });
    }
    function schedule() {
      clearTimeout(timer);
      if (!playing) return;
      if (cur >= N - 1) { playing = false; playBtn.textContent = 'Replay'; autoDone = true; return; }
      timer = setTimeout(function () {
        if (!playing) return;
        setStage(cur + 1, 'auto');
        schedule();
      }, DWELL[cur]);
    }
    function play() {
      if (cur >= N - 1) setStage(0, 'user');
      playing = true; playBtn.textContent = 'Pause';
      autoDemo(cur);
      schedule();
    }
    function stop() {
      if (!playing && !timer) return;
      playing = false; clearTimeout(timer); clearAuto();
      playBtn.textContent = autoDone && cur >= N - 1 ? 'Replay' : 'Play';
    }

    btns.forEach(function (b) { b.addEventListener('click', function () { stop(); setStage(+b.getAttribute('data-go'), 'user'); }); });
    $('#b-prev').addEventListener('click', function () { stop(); setStage(cur - 1, 'user'); });
    $('#b-next').addEventListener('click', function () { stop(); setStage(cur + 1, 'user'); });
    playBtn.addEventListener('click', function () { if (playing) stop(); else play(); });
    range.addEventListener('input', function () { stop(); setStage(+range.value, 'user'); });
    live.addEventListener('keydown', function (e) {
      if (e.target !== live) return;
      if (e.key === 'ArrowRight' || e.key === 'ArrowDown') { e.preventDefault(); stop(); setStage(cur + 1, 'user'); }
      else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') { e.preventDefault(); stop(); setStage(cur - 1, 'user'); }
      else if (e.key === 'Home') { e.preventDefault(); stop(); setStage(0, 'user'); }
      else if (e.key === 'End') { e.preventDefault(); stop(); setStage(N - 1, 'user'); }
    });
    /* drag the code or the view sideways to change stage */
    var dragX = null;
    viewEl.addEventListener('pointerdown', function (e) { if (e.target.closest('input, button, a, pre, label')) return; dragX = e.clientX; });
    window.addEventListener('pointerup', function (e) {
      if (dragX === null) return;
      var dx = e.clientX - dragX; dragX = null;
      if (Math.abs(dx) > 60) { stop(); setStage(cur + (dx < 0 ? 1 : -1), 'user'); }
    });

    setStage(0, 'init');
    playBtn.textContent = 'Play';
    build.classList.add('is-ready');
    if (!reduced()) seen(live, function () { if (!autoDone && !playing && cur === 0) { play(); } }, 0.45);
  }

  /* ================= hero visual: the Order morphs from types to evidence ================= */
  var morph = $('#morph');
  if (morph) {
    var MCAP = [
      'Types hold data. Nothing constrains it beyond its type.',
      'Relations: rules between rows, as plain C# objects. No database involved.',
      'Constraints: named rules sit next to the type and reject bad values.',
      'A state machine: the diagram says what may follow what, and what is denied.',
      'Temporality: what must never go backwards, and when a rule starts to bite.',
      'Services: the same rules hold on a mutable, async service.',
      'AutoTests: CsCheck generates the cases from your rules, shrinks every failure and replays it by seed.',
      'Confidence: every rule was reached. Hope has become evidence.'
    ];
    var MTHINK = ['a plain shape', 'a database model', 'column checks, with names', 'a state diagram', 'a timeline', 'a service contract', 'a test suite nobody wrote', 'evidence'];
    var MFILL = [4, 14, 28, 42, 56, 70, 86, 100], MLAST = 7;
    var mlayers = $$('.mg', morph), mcap = $('#m-cap'), mfill = $('#m-fill'), mlive = $('#m-live'), mplay = $('#m-play'), msteps = $('#m-steps');
    var mcur = MLAST, mtimer = null, mplaying = false, mdone = false;
    $$('li', msteps).forEach(function (li, i) {
      var b = document.createElement('button'); b.type = 'button';
      b.innerHTML = li.innerHTML; b.setAttribute('data-go', i);
      li.innerHTML = ''; li.appendChild(b);
      b.addEventListener('click', function () { mstop(); mset(i, true); });
    });
    var mbtns = $$('button', msteps);
    function mset(i, say) {
      mcur = clamp(i, 0, MLAST);
      morph.setAttribute('data-stage', String(mcur));
      mlayers.forEach(function (g) { var s = +g.getAttribute('data-s'); var cg = g.classList.contains('cg'); g.classList.toggle('is-on', (cg || mcur === MLAST) ? s <= mcur : s === mcur); g.classList.toggle('is-new', s === mcur); });
      var nw = $('.cg.is-new', morph), cb = $('#m-codebox'); if (nw && cb) cb.scrollTop = mcur === 0 ? 0 : Math.max(0, nw.offsetTop - 4);
      mbtns.forEach(function (b, k) { if (k === mcur) b.setAttribute('aria-current', 'step'); else b.removeAttribute('aria-current'); b.classList.toggle('is-done', k < mcur); });
      mcap.textContent = MCAP[mcur]; var mt = $('#m-think'); if (mt) mt.textContent = MTHINK[mcur]; mfill.style.width = MFILL[mcur] + '%';
      if (say) { mlive.textContent = ''; setTimeout(function () { mlive.textContent = 'Layer ' + mcur + ' of 7. ' + MCAP[mcur]; }, 30); }
    }
    var DW = 5600;
    function mflag() { morph.classList.toggle('is-playing', mplaying); morph.style.setProperty('--dwell', DW + 'ms'); }
    function mstop() { mplaying = false; clearTimeout(mtimer); mplay.textContent = mcur >= MLAST ? 'Replay' : 'Play'; mflag(); }
    function mtick() {
      if (!mplaying) return;
      if (mcur >= MLAST) { mplaying = false; mplay.textContent = 'Replay'; mdone = true; mflag(); return; }
      mtimer = setTimeout(function () { mset(mcur + 1); mtick(); }, DW);
    }
    function mrun() { mset(0); mplaying = true; mflag(); mplay.textContent = 'Pause'; clearTimeout(mtimer); mtimer = setTimeout(function () { mset(1); mtick(); }, DW); }
    mplay.addEventListener('click', function () {
      if (mplaying) { mstop(); mplay.textContent = 'Play'; return; }
      if (mcur >= MLAST) mrun(); else { mplaying = true; mflag(); mplay.textContent = 'Pause'; mtick(); }
    });
    mplay.textContent = 'Replay';
    if (reduced()) { mset(MLAST); }
    else { mset(0); mplay.textContent = 'Play'; seen(morph, function () { if (!mdone && !mplaying && mcur === 0) mrun(); }, 0.5); }
  }

  /* ================= plant a bug on a record (kept) ================= */
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

  /* ================= relations: break a key, add a cycle ================= */
  var rel = $('#rel');
  if (rel) {
    var erd = $('#erd'), viols = $('#viols'), rdot = $('#rel-dot'), rlive = $('#rel-live');
    var MEMBER = { dup: 'Order.Id', ref: 'Line.OrderId', cyc: 'Category.ParentId', chain: 'Customer.MergedIntoId' };
    var RMSG = {
      dup: 'Unique failed. Member: Orders. order-ids-are-unique: o1 appears 2 times.',
      ref: 'References failed. Member: Lines. lines-point-at-an-order: o7 refers to nothing.',
      cyc: 'Acyclic failed. Member: Categories. categories-have-no-cycles: cycle Root, Poetry, Fiction, Root.',
      chain: 'NoChains failed. Member: Customers. merges-are-not-chained: c1 maps to c2, which maps onwards.'
    };
    var sbtns = $$('.seg .mini', rel), cur2 = '';
    var applyRel = function (sc) {
      cur2 = sc;
      erd.setAttribute('class', 'erd' + (sc ? ' sc-' + sc : ''));
      $$('.row', erd).forEach(function (r) { r.classList.toggle('bad', !!sc && r.getAttribute('data-m') === MEMBER[sc]); });
      $$('.cl', rel).forEach(function (l) { l.classList.toggle('is-on', !!sc && l.getAttribute('data-k') === sc); });
      sbtns.forEach(function (b) { if (b.hasAttribute('aria-pressed')) b.setAttribute('aria-pressed', String(!!sc && b.getAttribute('data-sc') === sc)); });
      if (sc) viols.setAttribute('data-sc', sc); else viols.removeAttribute('data-sc');
      rdot.style.background = sc ? 'var(--fail)' : '';
      rlive.textContent = '';
      setTimeout(function () { rlive.textContent = sc ? RMSG[sc] : 'Reset. All relations hold.'; }, 30);
    };
    sbtns.forEach(function (b) {
      b.addEventListener('click', function () {
        var sc = b.getAttribute('data-sc');
        applyRel(sc && sc === cur2 ? '' : sc);
      });
    });
  }

  /* ================= state machines: walks and conformance ================= */
  var mc = $('#mc');
  if (mc) {
    var EDGES = [['Draft', 'Placed', 'place'], ['Placed', 'Paid', 'pay'], ['Paid', 'Shipped', 'ship'], ['Placed', 'Cancelled', 'cancel'], ['Paid', 'Cancelled', 'cancel']];
    var svg = $('.mch', mc), trail = $('#mc-trail'), mlive = $('#mc-live'), wlabel = $('#mc-walk-label');
    var walkNo = 0, path, rand, auto = 0;
    var nodeEl = function (n) { return $('[data-n="' + n + '"]', svg); };
    var edgeEl = function (a, b) { return $('[data-e="' + a + '>' + b + '"]', svg); };
    var drawTrail = function () {
      trail.innerHTML = '';
      path.forEach(function (p, i) {
        if (i) { var a = document.createElement('li'); a.className = 'arrow'; a.textContent = p.t + ' →'; trail.appendChild(a); }
        var li = document.createElement('li'); li.textContent = p.s; if (i === path.length - 1) li.className = 'cur'; trail.appendChild(li);
      });
      wlabel.textContent = 'walk ' + walkNo + ' · ' + path.length + ' ' + (path.length === 1 ? 'state' : 'states');
    };
    var newWalk = function () {
      auto++;
      walkNo++;
      rand = rng(walkNo * 2654435761);
      path = [{ s: 'Draft', t: '' }];
      $$('.n, .e', svg).forEach(function (g) { g.classList.remove('active'); });
      nodeEl('Draft').classList.add('active');
      drawTrail();
      mlive.textContent = '';
      setTimeout(function () { mlive.textContent = 'New walk, number ' + walkNo + '. It starts in Draft.'; }, 30);
    };
    var terminal = function (s) { return s === 'Shipped' || s === 'Cancelled'; };
    var step = function () {
      var here = path[path.length - 1].s;
      if (terminal(here)) { mlive.textContent = 'The walk has ended in ' + here + '.'; return false; }
      var out = EDGES.filter(function (e) { return e[0] === here; });
      var e = out[Math.floor(rand() * out.length)];
      path.push({ s: e[1], t: e[2] });
      var el = edgeEl(e[0], e[1]); if (el) el.classList.add('active');
      nodeEl(e[1]).classList.add('active');
      drawTrail();
      mlive.textContent = '';
      var msg = e[2] + ': ' + e[0] + ' to ' + e[1] + '.' + (terminal(e[1]) ? ' The walk ended in ' + e[1] + '. Every step was a drawn edge.' : '');
      setTimeout(function () { mlive.textContent = msg; }, 30);
      return !terminal(e[1]);
    };
    $('#mc-new').addEventListener('click', newWalk);
    $('#mc-step').addEventListener('click', function () { auto++; step(); });
    newWalk();
    if (!reduced()) seen(mc, async function () {
      var id = ++auto;
      for (var k = 0; k < 6; k++) {
        await wait(1000);
        if (id !== auto) return;
        if (!step()) return;
      }
    }, 0.5);

    var conf = $('#conf'), cplant = $('#conf-plant'), clabel = $('#conf-label'), cdot = $('#conf-dot'), clive = $('#conf-live');
    if (conf) cplant.addEventListener('click', function () {
      var on = cplant.getAttribute('aria-pressed') !== 'true';
      cplant.setAttribute('aria-pressed', String(on));
      clabel.textContent = on ? 'Remove the bug' : 'Plant a bug';
      conf.classList.toggle('is-planted', on);
      svg.classList.toggle('is-bad', on);
      cdot.style.background = on ? 'var(--fail)' : '';
      clive.textContent = '';
      setTimeout(function () { clive.textContent = on ? 'ConformTo failed. The implementation allows Cancelled to Shipped. The diagram denies it.' : 'ConformTo passed. Every edge the implementation allows is in the diagram.'; }, 30);
    });
  }

  /* ================= services: lift the rules onto a Wallet ================= */
  var sv = $('#sv');
  if (sv) {
    var swal = shrinker($('#sv-wal')), splant = $('#sv-plant'), slabel = $('#sv-label'), sdot = $('#sv-dot'), slive = $('#sv-live');
    splant.addEventListener('click', function () {
      var on = splant.getAttribute('aria-pressed') !== 'true';
      splant.setAttribute('aria-pressed', String(on));
      slabel.textContent = on ? 'Remove the bug' : 'Plant a bug';
      sv.classList.toggle('is-planted', on);
      sdot.style.background = on ? 'var(--fail)' : '';
      slive.textContent = '';
      if (!on) { swal.clear(); setTimeout(function () { slive.textContent = 'Bug removed. Money is conserved across every sampled sequence.'; }, 30); return; }
      swal.run().then(function (done) { if (done) slive.textContent = WAL_SUMMARY; });
    });
  }

  /* ================= nine questions: hover and focus replay via CSS; tap/click pins one row ================= */
  var rows = $$('#qtable tr.q');
  rows.forEach(function (tr) {
    var btn = $('.qbtn', tr);
    btn.addEventListener('click', function () {
      var was = tr.classList.contains('is-on');
      rows.forEach(function (r) { r.classList.remove('is-on'); $('.qbtn', r).setAttribute('aria-pressed', 'false'); });
      if (!was) { tr.classList.add('is-on'); btn.setAttribute('aria-pressed', 'true'); }
    });
  });

  /* ================= shrinking a record (kept) ================= */
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
    seen($('#shrink'), run, 0.4);
  }

  /* ================= rollout ================= */
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

  /* ================= copy: keep the line breaks of code shown as block spans ================= */
  document.addEventListener('copy', function (e) {
    var sel = window.getSelection(); if (!sel || !sel.rangeCount || sel.isCollapsed || !e.clipboardData) return;
    var r = sel.getRangeAt(0), anc = r.commonAncestorContainer; anc = anc.nodeType === 1 ? anc : anc.parentElement;
    if (!anc || !anc.closest('pre')) return;
    var out = '', w = document.createTreeWalker(anc, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, null), n;
    function nl() { if (out && out.charAt(out.length - 1) !== '\n') out += '\n'; }
    function hidden(el) { for (; el && el !== anc.parentElement; el = el.parentElement) { var cs = getComputedStyle(el); if (cs.display === 'none' || cs.visibility === 'hidden') return true; } return false; }
    var ended = [];
    while ((n = w.nextNode())) {
      if (!r.intersectsNode(n)) continue;
      if (n.nodeType === 1) {
        if (hidden(n)) { continue; }
        if (getComputedStyle(n).display === 'block' && n !== anc) nl();
        continue;
      }
      if (hidden(n.parentElement)) continue;
      var t = n.nodeValue, s = 0, en = t.length;
      if (n === r.startContainer) s = r.startOffset;
      if (n === r.endContainer) en = r.endOffset;
      var piece = t.slice(s, en);
      /* a block element that began earlier ends before this text if its next sibling chain says so */
      out += piece;
      var p = n.parentElement;
      if (p && p !== anc && getComputedStyle(p).display === 'block' && !n.nextSibling && !p.nextSibling) { /* handled by next block start */ }
    }
    out = out.replace(/\n{3,}/g, '\n\n').replace(/\n+$/, '');
    e.clipboardData.setData('text/plain', out);
    e.preventDefault();
  });
})();
