/* 小游戏引擎：五子棋 / 象棋 / 围棋 / 飞行棋
   纯函数 + 统一网格输出，IE11 兼容（var / function，无箭头函数、无模板串） */
(function (root) {
  var G = {};

  // ===================== 公共 =====================
  function rnd(n) { return Math.floor(Math.random() * n); }
  function skillNoise(skill) { return Math.max(0, 5 - (skill || 3)); } // 0(强)~4(弱)

  // ===================== 五子棋 15x15 =====================
  var GO_SIZE = 15;
  function bmNew() {
    var b = [];
    for (var i = 0; i < GO_SIZE * GO_SIZE; i++) b.push(0);
    return { b: b, turn: 1, over: false, win: 0, last: -1, moves: 0 };
  }
  var DIRS = [[1, 0], [0, 1], [1, 1], [1, -1]];
  function bmWin(b, idx, p) {
    var x = idx % GO_SIZE, y = Math.floor(idx / GO_SIZE), d, k, s, cnt, nx, ny;
    for (d = 0; d < 4; d++) {
      cnt = 1;
      for (s = -1; s <= 1; s += 2) {
        for (k = 1; k < 5; k++) {
          nx = x + DIRS[d][0] * k * s; ny = y + DIRS[d][1] * k * s;
          if (nx < 0 || nx >= GO_SIZE || ny < 0 || ny >= GO_SIZE) break;
          if (b[ny * GO_SIZE + nx] === p) cnt++; else break;
        }
      }
      if (cnt >= 5) return true;
    }
    return false;
  }
  // 某点对 p 的进攻/防守价值
  function bmPointScore(b, idx, p) {
    var x = idx % GO_SIZE, y = Math.floor(idx / GO_SIZE), d, k, s, cnt, open, sc = 0, nx, ny, val;
    for (d = 0; d < 4; d++) {
      cnt = 1; open = 0;
      for (s = -1; s <= 1; s += 2) {
        for (k = 1; k < 5; k++) {
          nx = x + DIRS[d][0] * k * s; ny = y + DIRS[d][1] * k * s;
          if (nx < 0 || nx >= GO_SIZE || ny < 0 || ny >= GO_SIZE) break;
          if (b[ny * GO_SIZE + nx] === p) cnt++;
          else { if (b[ny * GO_SIZE + nx] === 0) open++; break; }
        }
      }
      if (cnt >= 5) val = 100000;
      else if (cnt === 4 && open >= 1) val = 10000;
      else if (cnt === 3 && open >= 2) val = 1000;
      else if (cnt === 3 && open === 1) val = 200;
      else if (cnt === 2 && open >= 2) val = 60;
      else if (cnt === 2 && open === 1) val = 15;
      else if (cnt === 1 && open >= 2) val = 4;
      else val = 0;
      sc += val;
    }
    return sc;
  }
  function bmNeighbors(b) {
    var out = [], i, x, y, dx, dy, nx, ny, any = false;
    for (i = 0; i < b.length; i++) if (b[i]) { any = true; break; }
    if (!any) { out.push(7 * GO_SIZE + 7); return out; }
    for (y = 0; y < GO_SIZE; y++) for (x = 0; x < GO_SIZE; x++) {
      if (b[y * GO_SIZE + x]) continue;
      for (dy = -2; dy <= 2; dy++) for (dx = -2; dx <= 2; dx++) {
        nx = x + dx; ny = y + dy;
        if (nx < 0 || nx >= GO_SIZE || ny < 0 || ny >= GO_SIZE) continue;
        if (b[ny * GO_SIZE + nx]) { out.push(y * GO_SIZE + x); dy = 3; break; }
      }
    }
    return out;
  }
  G.gomoku = {
    id: 'gomoku', name: '五子棋', cols: GO_SIZE, rows: GO_SIZE,
    newState: bmNew,
    click: function (s, idx) {
      if (s.over || idx < 0 || idx >= GO_SIZE * GO_SIZE || s.b[idx] !== 0) return false;
      s.b[idx] = s.turn; s.last = idx; s.moves++;
      if (bmWin(s.b, idx, s.turn)) { s.over = true; s.win = s.turn; }
      else if (s.moves >= GO_SIZE * GO_SIZE) { s.over = true; s.win = 0; }
      else s.turn = 3 - s.turn;
      return true;
    },
    ai: function (s, skill) {
      var cand = bmNeighbors(s.b), best = [], bestScore = -1e9, i, idx, sc, noise = skillNoise(skill);
      if (!cand.length) return -1;
      var b = s.b;
      // 1) 自己能连五 → 直接赢
      for (i = 0; i < cand.length; i++) {
        idx = cand[i];
        b[idx] = 2;
        var win = bmWin(b, idx, 2);
        b[idx] = 0;
        if (win) return idx;
      }
      // 2) 对手下一步能连五 → 必须堵（堵不住就随便下）
      var mustBlock = -1;
      for (i = 0; i < cand.length; i++) {
        idx = cand[i];
        b[idx] = 1;
        var lose = bmWin(b, idx, 1);
        b[idx] = 0;
        if (lose) { mustBlock = idx; break; }
      }
      if (mustBlock >= 0) {
        if (noise >= 3) return mustBlock;              // 弱角色才可能漏
        return mustBlock;
      }
      // 3) 评估：进攻×权重 - 对手威胁 + 两步验证
      var scores = [];
      for (i = 0; i < cand.length; i++) {
        idx = cand[i];
        var atk = bmPointScore(b, idx, 2);
        var def = bmPointScore(b, idx, 1);
        sc = atk * 1.15 + def;
        // 造双威胁（活四 / 双活三）加很多分
        b[idx] = 2;
        if (atk >= 10000) sc += 5000;
        // 我下完后，对手是否立刻能连五？（送人头）
        var oppWin = false, j2;
        for (j2 = 0; j2 < cand.length && !oppWin; j2++) {
          if (cand[j2] === idx) continue;
          b[cand[j2]] = 1;
          if (bmWin(b, cand[j2], 1)) oppWin = true;
          b[cand[j2]] = 0;
        }
        b[idx] = 0;
        if (oppWin) sc -= 9000;
        scores.push([idx, sc]);
      }
      scores.sort(function (a, c) { return c[1] - a[1]; });
      var span = Math.max(1, 1 + noise);
      var top = scores.slice(0, span);
      var pick = top[0][0];
      if (noise > 0 && Math.random() < noise * 0.15) pick = top[rnd(top.length)][0];
      return pick;
    },
    status: function (s) {
      if (s.over) return s.win === 1 ? '你赢了' : (s.win === 2 ? 'TA 赢了' : '平局');
      return s.turn === 1 ? '该你下（黑）' : 'TA 正在想…';
    },
    cells: function (s) {
      var out = [], i;
      for (i = 0; i < GO_SIZE * GO_SIZE; i++) {
        out.push({ x: i % GO_SIZE, y: Math.floor(i / GO_SIZE), cls: s.b[i] === 1 ? 'g-black' : (s.b[i] === 2 ? 'g-white' : ''), txt: '' });
      }
      return out;
    }
  };

  // ===================== 国际象棋 8x8 =====================
  // 大写=白（玩家），小写=黑（AI）
  var CH_N = 8;
  var CH_VAL = { p: 100, n: 320, b: 330, r: 500, q: 900, k: 0 };
  var CH_GLYPH = { k: '♚', q: '♛', r: '♜', b: '♝', n: '♞', p: '♟' };

  function chNew() {
    var b = [], i, back = ['r', 'n', 'b', 'q', 'k', 'b', 'n', 'r'];
    for (i = 0; i < 64; i++) b.push('');
    for (i = 0; i < 8; i++) {
      b[i] = back[i];
      b[8 + i] = 'p';
      b[48 + i] = 'P';
      b[56 + i] = back[i].toUpperCase();
    }
    return { b: b, turn: 1, over: false, win: 0, last: -1, from: -1, ep: -1, half: 0, hist: {},
      castle: { wk: true, wq: true, bk: true, bq: true } };
  }
  function chIdx(x, y) { return y * CH_N + x; }
  function chAt(s, x, y) {
    if (x < 0 || x > 7 || y < 0 || y > 7) return null;
    return s.b[y * CH_N + x];
  }
  function chIsWhite(p) { return p >= 'A' && p <= 'Z'; }
  function chOwner(p) { return p ? (chIsWhite(p) ? 1 : 2) : 0; }
  function chCopy(s) {
    return { b: s.b.slice(), turn: s.turn, ep: s.ep,
      castle: { wk: s.castle.wk, wq: s.castle.wq, bk: s.castle.bk, bq: s.castle.bq } };
  }

  // 这个格子有没有被 byColor 攻击
  function chAttacked(s, sq, byColor) {
    var x = sq % CH_N, y = Math.floor(sq / CH_N), i, nx, ny, p, dx, dy, g, dirs, low;
    var pd = byColor === 1 ? [[-1, 1], [1, 1]] : [[-1, -1], [1, -1]];
    for (i = 0; i < 2; i++) {
      nx = x + pd[i][0]; ny = y + pd[i][1];
      p = chAt(s, nx, ny);
      if (p && chOwner(p) === byColor && p.toLowerCase() === 'p') return true;
    }
    var kn = [[1, 2], [2, 1], [2, -1], [1, -2], [-1, -2], [-2, -1], [-2, 1], [-1, 2]];
    for (i = 0; i < 8; i++) {
      nx = x + kn[i][0]; ny = y + kn[i][1];
      p = chAt(s, nx, ny);
      if (p && chOwner(p) === byColor && p.toLowerCase() === 'n') return true;
    }
    for (dy = -1; dy <= 1; dy++) for (dx = -1; dx <= 1; dx++) {
      if (!dx && !dy) continue;
      p = chAt(s, x + dx, y + dy);
      if (p && chOwner(p) === byColor && p.toLowerCase() === 'k') return true;
    }
    dirs = [
      [[1, 0], [-1, 0], [0, 1], [0, -1], 'r'],
      [[1, 1], [1, -1], [-1, 1], [-1, -1], 'b']
    ];
    for (g = 0; g < 2; g++) {
      var set = dirs[g], kind = set[4];
      for (i = 0; i < 4; i++) {
        nx = x + set[i][0]; ny = y + set[i][1];
        while (nx >= 0 && nx < 8 && ny >= 0 && ny < 8) {
          p = chAt(s, nx, ny);
          if (p) {
            if (chOwner(p) === byColor) {
              low = p.toLowerCase();
              if (low === 'q' || low === kind) return true;
            }
            break;
          }
          nx += set[i][0]; ny += set[i][1];
        }
      }
    }
    return false;
  }

  function chKingSq(s, owner) {
    for (var i = 0; i < 64; i++) {
      var p = s.b[i];
      if (p && p.toLowerCase() === 'k' && chOwner(p) === owner) return i;
    }
    return -1;
  }
  function chInCheck(s, owner) {
    var k = chKingSq(s, owner);
    if (k < 0) return true;
    return chAttacked(s, k, 3 - owner);
  }

  // 伪合法走法（不检查送将）
  function chPseudo(s, sq) {
    var x = sq % CH_N, y = Math.floor(sq / CH_N), p = s.b[sq], own = chOwner(p), out = [], low = p.toLowerCase();
    var i, nx, ny, t, dx, dy;
    function add(nx2, ny2) {
      var q = chAt(s, nx2, ny2);
      if (q === null) return;
      if (q && chOwner(q) === own) return;
      out.push(chIdx(nx2, ny2));
    }
    if (low === 'p') {
      var dir = own === 1 ? -1 : 1;
      var startRow = own === 1 ? 6 : 1;
      if (!chAt(s, x, y + dir)) {
        out.push(chIdx(x, y + dir));
        if (y === startRow && !chAt(s, x, y + 2 * dir)) out.push(chIdx(x, y + 2 * dir));
      }
      var caps = [[-1, dir], [1, dir]];
      for (i = 0; i < 2; i++) {
        nx = x + caps[i][0]; ny = y + caps[i][1];
        t = chAt(s, nx, ny);
        if (t && chOwner(t) !== own) out.push(chIdx(nx, ny));
        else if (!t && s.ep === chIdx(nx, ny)) out.push(chIdx(nx, ny));
      }
    } else if (low === 'n') {
      var kn = [[1, 2], [2, 1], [2, -1], [1, -2], [-1, -2], [-2, -1], [-2, 1], [-1, 2]];
      for (i = 0; i < 8; i++) add(x + kn[i][0], y + kn[i][1]);
    } else if (low === 'b' || low === 'r' || low === 'q') {
      var dirs = [];
      if (low === 'b' || low === 'q') dirs.push([1, 1], [1, -1], [-1, 1], [-1, -1]);
      if (low === 'r' || low === 'q') dirs.push([1, 0], [-1, 0], [0, 1], [0, -1]);
      for (i = 0; i < dirs.length; i++) {
        nx = x + dirs[i][0]; ny = y + dirs[i][1];
        while (nx >= 0 && nx < 8 && ny >= 0 && ny < 8) {
          t = chAt(s, nx, ny);
          if (!t) out.push(chIdx(nx, ny));
          else { if (chOwner(t) !== own) out.push(chIdx(nx, ny)); break; }
          nx += dirs[i][0]; ny += dirs[i][1];
        }
      }
    } else if (low === 'k') {
      for (dy = -1; dy <= 1; dy++) for (dx = -1; dx <= 1; dx++) {
        if (!dx && !dy) continue;
        add(x + dx, y + dy);
      }
      // 王车易位
      var homeRow = own === 1 ? 7 : 0;
      if (y === homeRow && x === 4) {
        var kR = own === 1 ? s.castle.wk : s.castle.bk;
        var qR = own === 1 ? s.castle.wq : s.castle.bq;
        var enemy = 3 - own;
        var rookK = s.b[chIdx(7, homeRow)], rookQ = s.b[chIdx(0, homeRow)];
        var myR = own === 1 ? 'R' : 'r';
        if (kR && rookK === myR && !chAt(s, 5, homeRow) && !chAt(s, 6, homeRow) &&
            !chAttacked(s, chIdx(4, homeRow), enemy) && !chAttacked(s, chIdx(5, homeRow), enemy) &&
            !chAttacked(s, chIdx(6, homeRow), enemy)) out.push(chIdx(6, homeRow));
        if (qR && rookQ === myR && !chAt(s, 3, homeRow) && !chAt(s, 2, homeRow) && !chAt(s, 1, homeRow) &&
            !chAttacked(s, chIdx(4, homeRow), enemy) && !chAttacked(s, chIdx(3, homeRow), enemy) &&
            !chAttacked(s, chIdx(2, homeRow), enemy)) out.push(chIdx(2, homeRow));
      }
    }
    return out;
  }

  // 执行一步（含过路兵/升变/易位/易位权）
  function chApply(s, from, to) {
    var p = s.b[from], low = p.toLowerCase(), own = chOwner(p);
    var tx = to % CH_N, ty = Math.floor(to / CH_N);
    var captured = s.b[to];
    s.b[to] = p; s.b[from] = '';
    if (low === 'p' && s.ep === to && !captured) {
      var capSq = chIdx(tx, Math.floor(from / CH_N));
      captured = s.b[capSq];
      s.b[capSq] = '';
    }
    if (low === 'p' && (ty === 0 || ty === 7)) s.b[to] = own === 1 ? 'Q' : 'q';
    if (low === 'k' && Math.abs(to - from) === 2) {
      if (to > from) { s.b[from + 1] = s.b[from + 3]; s.b[from + 3] = ''; }
      else { s.b[from - 1] = s.b[from - 4]; s.b[from - 4] = ''; }
    }
    if (low === 'k') {
      if (own === 1) { s.castle.wk = false; s.castle.wq = false; }
      else { s.castle.bk = false; s.castle.bq = false; }
    }
    if (low === 'r') {
      if (from === 63) s.castle.wk = false;
      if (from === 56) s.castle.wq = false;
      if (from === 7) s.castle.bk = false;
      if (from === 0) s.castle.bq = false;
    }
    if (to === 63) s.castle.wk = false;
    if (to === 56) s.castle.wq = false;
    if (to === 7) s.castle.bk = false;
    if (to === 0) s.castle.bq = false;
    s.ep = -1;
    if (low === 'p' && Math.abs(to - from) === 16) s.ep = (from + to) / 2;
    s.last = to;
    s.from = -1;
    return captured;
  }

  function chLegal(s, owner) {
    var out = [], i, j, mv, p;
    for (i = 0; i < 64; i++) {
      p = s.b[i];
      if (!p || chOwner(p) !== owner) continue;
      mv = chPseudo(s, i);
      for (j = 0; j < mv.length; j++) {
        var c = chCopy(s);
        chApply(c, i, mv[j]);
        if (!chInCheck(c, owner)) out.push([i, mv[j]]);
      }
    }
    return out;
  }

  // 子力不足（只剩王 / 王+单象 / 王+单马）→ 和棋
  function chInsufficient(s) {
    var heavy = 0, minors = 0, i, p, low;
    for (i = 0; i < 64; i++) {
      p = s.b[i];
      if (!p) continue;
      low = p.toLowerCase();
      if (low === 'k') continue;
      if (low === 'p' || low === 'r' || low === 'q') return false;
      minors++;
    }
    return minors <= 1;
  }
  // ---------- 搜索用：走一步 / 撤回一步（不复制数组，快很多） ----------
  function chMake(s, from, to) {
    var p = s.b[from], low = p.toLowerCase();
    var u = {
      from: from, to: to, p: p, cap: s.b[to], ep: s.ep, capSq: -1, rookFrom: -1, rookTo: -1,
      ck: s.castle.wk, cq: s.castle.wq, bk: s.castle.bk, bq: s.castle.bq
    };
    s.b[to] = p;
    s.b[from] = '';
    if (low === 'p' && s.ep === to && !u.cap) {
      u.capSq = chIdx(to % 8, Math.floor(from / 8));
      u.cap = s.b[u.capSq];
      s.b[u.capSq] = '';
    }
    if (low === 'p' && (Math.floor(to / 8) === 0 || Math.floor(to / 8) === 7)) {
      s.b[to] = chIsWhite(p) ? 'Q' : 'q';
    }
    if (low === 'k' && Math.abs(to - from) === 2) {
      if (to > from) { u.rookFrom = from + 3; u.rookTo = from + 1; }
      else { u.rookFrom = from - 4; u.rookTo = from - 1; }
      s.b[u.rookTo] = s.b[u.rookFrom];
      s.b[u.rookFrom] = '';
    }
    if (low === 'k') {
      if (chIsWhite(p)) { s.castle.wk = false; s.castle.wq = false; }
      else { s.castle.bk = false; s.castle.bq = false; }
    }
    if (low === 'r') {
      if (from === 63) s.castle.wk = false;
      if (from === 56) s.castle.wq = false;
      if (from === 7) s.castle.bk = false;
      if (from === 0) s.castle.bq = false;
    }
    if (to === 63) s.castle.wk = false;
    if (to === 56) s.castle.wq = false;
    if (to === 7) s.castle.bk = false;
    if (to === 0) s.castle.bq = false;
    s.ep = -1;
    if (low === 'p' && Math.abs(to - from) === 16) s.ep = (from + to) / 2;
    return u;
  }

  function chUnmake(s, u) {
    s.b[u.from] = u.p;
    s.b[u.to] = u.cap;
    if (u.capSq >= 0) { s.b[u.to] = ''; s.b[u.capSq] = u.cap; }
    if (u.rookFrom >= 0) { s.b[u.rookFrom] = s.b[u.rookTo]; s.b[u.rookTo] = ''; }
    s.ep = u.ep;
    s.castle.wk = u.ck; s.castle.wq = u.cq; s.castle.bk = u.bk; s.castle.bq = u.bq;
  }

  // 合法着法（用 make/unmake，避免每次复制棋盘）
  function chGenLegal(s, owner) {
    var out = [], i, j, mv, u;
    for (i = 0; i < 64; i++) {
      var p = s.b[i];
      if (!p || chOwner(p) !== owner) continue;
      mv = chPseudo(s, i);
      for (j = 0; j < mv.length; j++) {
        u = chMake(s, i, mv[j]);
        if (!chInCheck(s, owner)) out.push([i, mv[j]]);
        chUnmake(s, u);
      }
    }
    return out;
  }

  // ---------- 位置评估（简化评估函数 + 位置表） ----------
  var CH_PST = {
    p: [0, 0, 0, 0, 0, 0, 0, 0,
      50, 50, 50, 50, 50, 50, 50, 50,
      10, 10, 20, 30, 30, 20, 10, 10,
      5, 5, 10, 25, 25, 10, 5, 5,
      0, 0, 0, 20, 20, 0, 0, 0,
      5, -5, -10, 0, 0, -10, -5, 5,
      5, 10, 10, -20, -20, 10, 10, 5,
      0, 0, 0, 0, 0, 0, 0, 0],
    n: [-50, -40, -30, -30, -30, -30, -40, -50,
      -40, -20, 0, 0, 0, 0, -20, -40,
      -30, 0, 10, 15, 15, 10, 0, -30,
      -30, 5, 15, 20, 20, 15, 5, -30,
      -30, 0, 15, 20, 20, 15, 0, -30,
      -30, 5, 10, 15, 15, 10, 5, -30,
      -40, -20, 0, 5, 5, 0, -20, -40,
      -50, -40, -30, -30, -30, -30, -40, -50],
    b: [-20, -10, -10, -10, -10, -10, -10, -20,
      -10, 0, 0, 0, 0, 0, 0, -10,
      -10, 0, 5, 10, 10, 5, 0, -10,
      -10, 5, 5, 10, 10, 5, 5, -10,
      -10, 0, 10, 10, 10, 10, 0, -10,
      -10, 10, 10, 10, 10, 10, 10, -10,
      -10, 5, 0, 0, 0, 0, 5, -10,
      -20, -10, -10, -10, -10, -10, -10, -20],
    r: [0, 0, 0, 0, 0, 0, 0, 0,
      5, 10, 10, 10, 10, 10, 10, 5,
      -5, 0, 0, 0, 0, 0, 0, -5,
      -5, 0, 0, 0, 0, 0, 0, -5,
      -5, 0, 0, 0, 0, 0, 0, -5,
      -5, 0, 0, 0, 0, 0, 0, -5,
      -5, 0, 0, 0, 0, 0, 0, -5,
      0, 0, 0, 5, 5, 0, 0, 0],
    q: [-20, -10, -10, -5, -5, -10, -10, -20,
      -10, 0, 0, 0, 0, 0, 0, -10,
      -10, 0, 5, 5, 5, 5, 0, -10,
      -5, 0, 5, 5, 5, 5, 0, -5,
      0, 0, 5, 5, 5, 5, 0, -5,
      -10, 5, 5, 5, 5, 5, 0, -10,
      -10, 0, 5, 0, 0, 0, 0, -10,
      -20, -10, -10, -5, -5, -10, -10, -20],
    k: [-30, -40, -40, -50, -50, -40, -40, -30,
      -30, -40, -40, -50, -50, -40, -40, -30,
      -30, -40, -40, -50, -50, -40, -40, -30,
      -30, -40, -40, -50, -50, -40, -40, -30,
      -20, -30, -30, -40, -40, -30, -30, -20,
      -10, -20, -20, -20, -20, -20, -20, -10,
      20, 20, 0, 0, 0, 20, 20, 20,
      20, 30, 10, 0, 0, 10, 30, 20]
  };

  // 黑方视角为正
  var CH_MATE = 30000;
  var chDeadline = 0, chNodes = 0, chAbort = false;
  function chNow() { return (new Date()).getTime(); }
  function chTimeUp() {
    if (chDeadline <= 0) return false;
    if ((++chNodes & 127) !== 0) return false;
    return chNow() > chDeadline;
  }

  // 静止搜索：只展开吃子/升变，避免“换子算漏”
  function chQuiesce(s, alpha, beta, ply) {
    if (chAbort) return 0;
    var side = s.turn === 1 ? -1 : 1;
    var stand = side * chEvalFull(s);
    if (stand >= beta) return beta;
    if (stand > alpha) alpha = stand;
    if (ply > 5) return alpha;
    var out = [], i, j, mv, u, sc, target, p;
    for (i = 0; i < 64; i++) {
      p = s.b[i];
      if (!p || chOwner(p) !== s.turn) continue;
      mv = chPseudo(s, i);
      for (j = 0; j < mv.length; j++) {
        target = s.b[mv[j]];
        var promo = p.toLowerCase() === 'p' && (Math.floor(mv[j] / 8) === 0 || Math.floor(mv[j] / 8) === 7);
        if (!target && !promo && s.ep !== mv[j]) continue;
        out.push([i, mv[j], target ? (CH_VAL[target.toLowerCase()] || 0) * 10 - (CH_VAL[p.toLowerCase()] || 0) : 900]);
      }
    }
    out.sort(function (a, b) { return b[2] - a[2]; });
    for (i = 0; i < out.length; i++) {
      u = chMake(s, out[i][0], out[i][1]);
      if (chInCheck(s, s.turn)) { chUnmake(s, u); continue; }
      s.turn = 3 - s.turn;
      sc = -chQuiesce(s, -beta, -alpha, ply + 1);
      s.turn = 3 - s.turn;
      chUnmake(s, u);
      if (chAbort) return 0;
      if (chTimeUp()) { chAbort = true; return 0; }
      if (sc >= beta) return beta;
      if (sc > alpha) alpha = sc;
    }
    return alpha;
  }

  // 负极大值 + alpha-beta
  function chNeg(s, depth, alpha, beta, ply) {
    if (chAbort) return 0;
    var mv = chGenLegal(s, s.turn), i, u, sc, best = -CH_MATE * 2, target, p, sc2;
    if (!mv.length) return chInCheck(s, s.turn) ? -(CH_MATE - ply) : 0;
    if (depth <= 0) return chQuiesce(s, alpha, beta, 0);
    var ord = [];
    for (i = 0; i < mv.length; i++) {
      target = s.b[mv[i][1]];
      p = s.b[mv[i][0]];
      sc2 = 0;
      if (target) sc2 = (CH_VAL[target.toLowerCase()] || 0) * 10 - (CH_VAL[p.toLowerCase()] || 0);
      if (p.toLowerCase() === 'p' && Math.floor(mv[i][1] / 8) <= 1) sc2 += 800;
      ord.push([mv[i][0], mv[i][1], sc2]);
    }
    ord.sort(function (a, b) { return b[2] - a[2]; });
    for (i = 0; i < ord.length; i++) {
      u = chMake(s, ord[i][0], ord[i][1]);
      if (chInCheck(s, s.turn)) { chUnmake(s, u); continue; }
      s.turn = 3 - s.turn;
      sc = -chNeg(s, depth - 1, -beta, -alpha, ply + 1);
      s.turn = 3 - s.turn;
      chUnmake(s, u);
      if (chAbort) return 0;
      if (chTimeUp()) { chAbort = true; return 0; }
      if (sc > best) best = sc;
      if (best > alpha) alpha = best;
      if (alpha >= beta) break;
    }
    return best;
  }

  // 一层根搜索（黑方视角），pvFirst 用来把上一轮最优着法排前面
  function chRootLayer(s, depth, pvFirst) {
    var mv = chGenLegal(s, 2), i, u, sc, best = null, bestSc = -CH_MATE * 2;
    if (!mv.length) return { mv: null, sc: 0, aborted: false };
    var ord = [], target, p, sc2;
    for (i = 0; i < mv.length; i++) {
      target = s.b[mv[i][1]];
      p = s.b[mv[i][0]];
      sc2 = 0;
      if (target) sc2 = (CH_VAL[target.toLowerCase()] || 0) * 10 - (CH_VAL[p.toLowerCase()] || 0);
      if (p.toLowerCase() === 'p' && Math.floor(mv[i][1] / 8) <= 1) sc2 += 800;
      if (pvFirst && mv[i][0] === pvFirst[0] && mv[i][1] === pvFirst[1]) sc2 += 100000;
      ord.push([mv[i][0], mv[i][1], sc2]);
    }
    ord.sort(function (a, b) { return b[2] - a[2]; });
    var alpha = -CH_MATE * 2, beta = CH_MATE * 2;
    for (i = 0; i < ord.length; i++) {
      u = chMake(s, ord[i][0], ord[i][1]);
      if (chInCheck(s, 2)) { chUnmake(s, u); continue; }
      s.turn = 1;
      sc = -chNeg(s, depth - 1, -beta, -alpha, 1);
      s.turn = 2;
      chUnmake(s, u);
      if (chAbort) return { mv: best, sc: bestSc, aborted: true };
      if (sc > bestSc) { bestSc = sc; best = [ord[i][0], ord[i][1]]; if (sc > alpha) alpha = sc; }
      if (alpha >= beta) break;
    }
    return { mv: best, sc: bestSc, aborted: false };
  }

  // 迭代加深 + 时限：时间到就停在上一次的深度
  function chRootSearch(s, maxDepth, maxMs) {
    var best = null, sc = 0, d, res;
    var start = chNow();
    chAbort = false;
    for (d = 1; d <= maxDepth; d++) {
      chDeadline = start + maxMs;
      chNodes = 0;
      res = chRootLayer(s, d, best);
      if (res.aborted) break;
      if (res.mv) best = res.mv;
      sc = res.sc;
      if (Math.abs(sc) > CH_MATE - 1000) break;   // 已经算到杀
      if (chNow() - start > maxMs * 0.5) break;   // 余下时间不够再深一层
    }
    chDeadline = 0;
    return best;
  }

  function chEvalFull(s) {
    var sc = 0, i, p, low, v, pst;
    for (i = 0; i < 64; i++) {
      p = s.b[i];
      if (!p) continue;
      low = p.toLowerCase();
      v = CH_VAL[low] || 0;
      pst = CH_PST[low] ? CH_PST[low][chIsWhite(p) ? i : (i ^ 56)] : 0;
      sc += chIsWhite(p) ? -(v + pst) : (v + pst);
    }
    return sc;
  }

  // 兼容旧调用：不限时的根搜索
  function chRoot(s, depth) { return chRootSearch(s, depth, 100000); }
  function chEval(s) {
    var sc = 0, i, p, val;
    for (i = 0; i < 64; i++) {
      p = s.b[i];
      if (!p) continue;
      val = CH_VAL[p.toLowerCase()] || 0;
      if (p.toLowerCase() === 'p') {
        var y = Math.floor(i / CH_N);
        val += (chIsWhite(p) ? (6 - y) : (y - 1)) * 8;
      }
      sc += chIsWhite(p) ? -val : val;
    }
    return sc;
  }


  G.chess = {
    id: 'chess', name: '国际象棋', cols: CH_N, rows: CH_N,
    newState: chNew,
    apply: function (s, from, to) {
      if (s.over) return false;
      var p = s.b[from];
      if (!p) return false;
      if (chOwner(p) !== s.turn) return false;
      var own = chOwner(p), mv = chPseudo(s, from), ok = false, i;
      for (i = 0; i < mv.length; i++) if (mv[i] === to) ok = true;
      if (!ok) return false;
      var c = chCopy(s);
      chApply(c, from, to);
      if (chInCheck(c, own)) return false;
      var capped = chApply(s, from, to);
      var opp = 3 - own;
      // 和棋：无合法着法（将死/逼和）
      if (chLegal(s, opp).length === 0) {
        s.over = true;
        s.win = chInCheck(s, opp) ? (own === 1 ? 1 : 2) : 0;
        s.end = chInCheck(s, opp) ? 'checkmate' : 'stalemate';
        return true;
      }
      s.turn = opp;
      // 和棋：50 回合无吃子无动兵 / 三次重复 / 子力不足
      if (p.toLowerCase() === 'p' || capped) s.half = 0;
      else s.half = (s.half || 0) + 1;
      if (!s.hist) s.hist = {};
      var key = s.b.join('') + s.turn;
      s.hist[key] = (s.hist[key] || 0) + 1;
      if (s.half >= 100) { s.over = true; s.win = 0; s.end = 'fifty'; }
      else if (s.hist[key] >= 3) { s.over = true; s.win = 0; s.end = 'repeat'; }
      else if (chInsufficient(s)) { s.over = true; s.win = 0; s.end = 'material'; }
      return true;
    },
    // 调试/测试用
    _root: function (s, d) { return chRoot(s, d); },
    _gen: function (s, o) { return chGenLegal(s, o); },
    _eval: function (s) { return chEvalFull(s); },
    _make: function (s, f, t2) { return chMake(s, f, t2); },
    _unmake: function (s, u) { chUnmake(s, u); },
    _inCheck: function (s, o) { return chInCheck(s, o); },
    // 当前方所有合法着法 [[from,to], ...]
    legalFor: function (s, owner) { return chLegal(s, owner === undefined ? s.turn : owner); },
    inCheck: function (s, owner) { return chInCheck(s, owner === undefined ? s.turn : owner); },
    click: function (s, idx) {
      if (s.over) return false;
      var p = s.b[idx];
      if (p && chIsWhite(p)) { s.from = idx; return true; }
      if (s.from < 0) return false;
      return G.chess.apply(s, s.from, idx);
    },
    ai: function (s, skill) {
      var noise = skillNoise(skill);
      // 棋力 → 迭代加深层数上限 + 思考时限（按真实时间算，界面不会卡太久）
      var depth = skill >= 5 ? 6 : (skill === 4 ? 5 : (skill === 3 ? 4 : 2));
      var budget = skill >= 5 ? 900 : (skill === 4 ? 500 : (skill === 3 ? 220 : 90));
      var mv = chRootSearch(s, depth, budget);
      if (!mv) return null;
      // 棋力低的角色偶尔不选最优着法
      if (noise > 0 && Math.random() < Math.min(0.25, noise * 0.06)) {
        var all = chGenLegal(s, 2);
        if (all.length) mv = all[rnd(all.length)];
      }
      return mv;
    },
    status: function (s) {
      if (s.over) {
        var why = s.end === 'checkmate' ? '将死' : (s.end === 'stalemate' ? '逼和'
          : (s.end === 'fifty' ? '50 回合' : (s.end === 'repeat' ? '三次重复' : (s.end === 'material' ? '子力不足' : ''))));
        return (s.win === 1 ? '你赢了' : (s.win === 2 ? 'TA 赢了' : '和棋')) + (why ? '（' + why + '）' : '');
      }
      return (chInCheck(s, s.turn) ? '被将军！' : '') + (s.turn === 1 ? '该你走（白）' : 'TA 正在想…');
    },
    cells: function (s) {
      var out = [], i, p, cls, txt, low;
      for (i = 0; i < 64; i++) {
        var x = i % CH_N, y = Math.floor(i / CH_N);
        cls = ((x + y) % 2 === 0) ? 'sq-d' : 'sq-l';
        txt = '';
        p = s.b[i];
        if (p) {
          low = p.toLowerCase();
          txt = CH_GLYPH[low] || '';
          cls += chIsWhite(p) ? ' pc-white' : ' pc-black';
        }
        if (i === s.from) cls += ' ch-sel';
        if (i === s.last) cls += ' ch-last';
        if (!s.over && chInCheck(s, s.turn) && i === chKingSq(s, s.turn)) cls += ' ch-check';
        out.push({ x: x, y: y, cls: cls, txt: txt });
      }
      return out;
    }
  };

  // ===================== 围棋 9x9 =====================
  var WG = 9;
  function wqNew() {
    var b = [];
    for (var i = 0; i < WG * WG; i++) b.push(0);
    return { b: b, turn: 1, over: false, win: 0, pass: 0, ko: -1, last: -1, caps: [0, 0] };
  }
  function wqNbrs(i) {
    var x = i % WG, y = Math.floor(i / WG), out = [];
    if (x > 0) out.push(i - 1);
    if (x < WG - 1) out.push(i + 1);
    if (y > 0) out.push(i - WG);
    if (y < WG - 1) out.push(i + WG);
    return out;
  }
  function wqGroup(b, start) {
    var color = b[start], seen = {}, stack = [start], stones = [], libs = 0, i, nb, nbs;
    seen[start] = 1;
    while (stack.length) {
      i = stack.pop(); stones.push(i);
      nbs = wqNbrs(i);
      for (var k = 0; k < nbs.length; k++) {
        nb = nbs[k];
        if (b[nb] === 0) libs++;
        else if (b[nb] === color && !seen[nb]) { seen[nb] = 1; stack.push(nb); }
      }
    }
    return { stones: stones, libs: libs };
  }
  function wqTry(s, idx, color) {
    if (idx < 0 || idx >= WG * WG || s.b[idx] !== 0) return null;
    if (idx === s.ko) return null;
    var b = s.b.slice();
    b[idx] = color;
    var captured = [], i, g, opp = 3 - color;
    var nbs = wqNbrs(idx);
    for (i = 0; i < nbs.length; i++) {
      if (b[nbs[i]] === opp) {
        g = wqGroup(b, nbs[i]);
        if (g.libs === 0) {
          for (var j = 0; j < g.stones.length; j++) { b[g.stones[j]] = 0; captured.push(g.stones[j]); }
        }
      }
    }
    g = wqGroup(b, idx);
    if (g.libs === 0 && captured.length === 0) return null;  // 自杀
    var ko = (captured.length === 1 && g.stones.length === 1 && g.libs === 1) ? captured[0] : -1;
    return { b: b, captured: captured, ko: ko };
  }
  function wqScore(s) {
    // 简化数子：子 + 只被一方围住的空点
    var own = [0, 0, 0], seen = {}, i;
    for (i = 0; i < s.b.length; i++) if (s.b[i]) own[s.b[i]]++;
    for (i = 0; i < s.b.length; i++) {
      if (s.b[i] !== 0 || seen[i]) continue;
      var stack = [i], region = [], border = {}, j, nb, nbs;
      seen[i] = 1;
      while (stack.length) {
        j = stack.pop(); region.push(j);
        nbs = wqNbrs(j);
        for (var k = 0; k < nbs.length; k++) {
          nb = nbs[k];
          if (s.b[nb] === 0) { if (!seen[nb]) { seen[nb] = 1; stack.push(nb); } }
          else border[s.b[nb]] = 1;
        }
      }
      var keys = [];
      for (var c in border) if (border.hasOwnProperty(c)) keys.push(c);
      if (keys.length === 1) own[parseInt(keys[0], 10)] += region.length;
    }
    return own;
  }
  G.go = {
    id: 'go', name: '围棋', cols: WG, rows: WG,
    newState: wqNew,
    click: function (s, idx) {
      if (s.over) return false;
      var r = wqTry(s, idx, s.turn);
      if (!r) return false;
      s.b = r.b; s.caps[0] += r.captured.length; s.ko = r.ko; s.last = idx; s.pass = 0; s.turn = 3 - s.turn;
      return true;
    },
    pass: function (s) {
      s.pass++;
      if (s.pass >= 2) G.go.finish(s);
      else s.turn = 3 - s.turn;
    },
    // 数子结算
    finish: function (s) {
      var sc = wqScore(s);
      s.over = true; s.score = sc;
      s.win = sc[1] > sc[2] ? 1 : (sc[2] > sc[1] ? 2 : 0);
    },
    // 认输：who=1 我认输，who=2 对手认输
    resign: function (s, who) {
      if (s.over) return;
      s.over = true; s.score = wqScore(s);
      s.win = who === 1 ? 2 : 1;
    },
    // 当前该下的一方还有没有合法的点
    hasMove: function (s) {
      var color = 3 - s.turn, i;
      for (i = 0; i < s.b.length; i++) {
        if (s.b[i] !== 0 || i === s.ko) continue;
        if (wqTry(s, i, color)) return true;
      }
      return false;
    },
    ai: function (s, skill) {
      var cand = [], i, noise = skillNoise(skill), j, k;
      for (i = 0; i < s.b.length; i++) {
        if (s.b[i] !== 0 || i === s.ko) continue;
        // 只考虑已有棋子附近的点，空盘走星位
        var near = false, nbs = wqNbrs(i);
        for (j = 0; j < nbs.length; j++) if (s.b[nbs[j]]) { near = true; break; }
        if (near || Math.random() < 0.25) cand.push(i);
      }
      var best = -1, bestSc = -1e9;
      for (i = 0; i < cand.length; i++) {
        var idx = cand[i];
        var r = wqTry(s, idx, 2);
        if (!r) continue;
        var sc = r.captured.length * 40;          // 吃子价值最高
        var nbs2 = wqNbrs(idx);
        for (j = 0; j < nbs2.length; j++) {
          if (s.b[nbs2[j]] === 2) sc += 8;        // 连接自己
          else if (s.b[nbs2[j]] === 1) sc += 5;   // 贴身紧气
        }
        // 下完之后自己这块的气：1 气（自己送吃）重罚
        var myG = wqGroup(r.b, idx);
        if (myG.libs === 1 && r.captured.length === 0) sc -= 60;
        else if (myG.libs === 2) sc -= 8;
        // 打吃对手（对手这块只剩 1 气）
        for (j = 0; j < nbs2.length; j++) {
          if (r.b[nbs2[j]] !== 1) continue;
          var og = wqGroup(r.b, nbs2[j]);
          if (og.libs === 1) sc += 30;
          else if (og.libs === 2) sc += 8;
        }
        // 别往自己眼里填
        var ownNbr = 0, empNbr = 0;
        for (j = 0; j < nbs2.length; j++) {
          if (s.b[nbs2[j]] === 2) ownNbr++;
          else if (s.b[nbs2[j]] === 0) empNbr++;
        }
        if (ownNbr === 4 && r.captured.length === 0) sc -= 500;   // 自己真眼
        else if (ownNbr === 3 && empNbr === 1 && r.captured.length === 0) sc -= 40;
        // 中腹/边角倾向
        sc -= Math.abs((idx % WG) - 4) * 0.6;
        sc += Math.random() * (noise * 6);
        if (sc > bestSc) { bestSc = sc; best = idx; }
      }
      // 候选里一个能下的点都没有时，退回全盘找，别出现“对面不动了”
      if (best < 0) {
        for (i = 0; i < s.b.length; i++) {
          if (s.b[i] !== 0 || i === s.ko) continue;
          var rr = wqTry(s, i, 2);
          if (!rr) continue;
          var s2 = rr.captured.length * 20;
          if (s2 > bestSc) { bestSc = s2; best = i; }
        }
      }
      return best;
    },
    status: function (s) {
      if (s.over) {
        var sc = s.score || wqScore(s);
        return (s.win === 1 ? '你赢了' : (s.win === 2 ? 'TA 赢了' : '平局')) + '（' + sc[1] + ' : ' + sc[2] + '）';
      }
      return s.turn === 1 ? '该你下（黑）' : 'TA 正在想…';
    },
    cells: function (s) {
      var out = [], i;
      for (i = 0; i < s.b.length; i++) {
        out.push({ x: i % WG, y: Math.floor(i / WG), cls: s.b[i] === 1 ? 'g-black' : (s.b[i] === 2 ? 'g-white' : ''), txt: '' });
      }
      return out;
    }
  };

  // ===================== 飞行棋（11x11 跑道 + 归巢） =====================
  var FL_N = 11, FL_TRACK = 40, FL_HOME = 44;
  function flTrackPos() {
    // 顺时针一圈：上边(11)、右边(10)、下边(10)、左边(9)
    var p = [], x, y;
    for (x = 0; x < FL_N; x++) p.push([x, 0]);
    for (y = 1; y < FL_N; y++) p.push([FL_N - 1, y]);
    for (x = FL_N - 2; x >= 0; x--) p.push([x, FL_N - 1]);
    for (y = FL_N - 2; y >= 1; y--) p.push([0, y]);
    return p;   // 40 个
  }
  var FL_POS = flTrackPos();
  function flNew() {
    var planes = [], i;
    for (i = 0; i < 4; i++) planes.push({ c: 1, pos: -1 });
    for (i = 0; i < 4; i++) planes.push({ c: 2, pos: -1 });
    return { planes: planes, turn: 1, over: false, win: 0, dice: 0, rolled: false, msg: '掷骰子开始' };
  }
  function flMoves(s, dice) {
    var out = [], i, p;
    for (i = 0; i < s.planes.length; i++) {
      p = s.planes[i];
      if (p.c !== s.turn) continue;
      if (p.pos === FL_HOME) continue;
      if (p.pos < 0) { if (dice === 6) out.push(i); continue; }
      if (p.pos + dice <= FL_HOME) out.push(i);
    }
    return out;
  }
  // 跑道上的绝对格子（各色起点错开 10 格）
  function flAbs(planeIdx, pos) {
    if (pos < 0 || pos >= FL_TRACK) return -1;
    var color = planeIdx < 4 ? 1 : 2;
    var base = color === 1 ? 0 : 20;
    return (pos + base) % FL_TRACK;
  }
  G.flight = {
    id: 'flight', name: '飞行棋', cols: FL_N, rows: FL_N,
    newState: flNew,
    roll: function (s) {
      if (s.over || s.rolled) return 0;
      s.dice = 1 + rnd(6);
      s.rolled = true;
      var mv = flMoves(s, s.dice);
      s.msg = '掷出 ' + s.dice + (mv.length ? '' : '，没有能走的棋');
      return s.dice;
    },
    // 玩家点自己的棋子
    click: function (s, idx) {
      if (s.over || !s.rolled) return false;
      var mv = flMoves(s, s.dice), k;
      for (k = 0; k < mv.length; k++) if (mv[k] === idx) { flApply(s, idx); return true; }
      return false;
    },
    ai: function (s, skill) {
      // 由调用方先 roll
      var mv = flMoves(s, s.dice);
      if (!mv.length) return -1;
      var noise = skillNoise(skill), best = mv[0], bestSc = -1e9, k, pl, sc;
      for (k = 0; k < mv.length; k++) {
        pl = s.planes[mv[k]];
        var land = pl.pos < 0 ? -1 : pl.pos + s.dice;
        var absLand = land >= 0 ? flAbs(mv[k], land) : -1;
        sc = land >= 0 ? land : 4;                    // 起步：能起飞就起飞
        // 撞对手（送他回基地）很赚
        for (var q = 0; q < s.planes.length; q++) {
          var op = s.planes[q];
          if (op.c === 2 || op.pos < 0 || absLand < 0) continue;
          if (absLand === flAbs(q, op.pos)) sc += 60;
        }
        // 落地后别站在对手下一步能踩到的地方（对手要扔到差值）
        if (absLand >= 0) {
          for (var q2 = 0; q2 < s.planes.length; q2++) {
            var op2 = s.planes[q2];
            if (op2.c === 2 || op2.pos < 0) continue;
            var gap = (absLand - flAbs(q2, op2.pos) + FL_TRACK) % FL_TRACK;
            if (gap >= 1 && gap <= 6) sc -= 22;      // 一颗子有 1/6 机会踩到
          }
        }
        if (land === FL_HOME) sc += 60;              // 正好归巢
        else if (land === FL_HOME - 1 && pl.pos === FL_HOME - 2) sc += 8;
        sc += Math.random() * (noise * 6);
        if (sc > bestSc) { bestSc = sc; best = mv[k]; }
      }
      return best;
    },
    applyAI: function (s, planeIdx) { flApply(s, planeIdx); },
    status: function (s) {
      if (s.over) return s.win === 1 ? '你赢了（四架都归巢）' : 'TA 赢了（四架都归巢）';
      return s.msg + (s.turn === 1 ? ' ｜ 轮到你' : ' ｜ TA 的回合');
    },
    cells: function (s) {
      var out = [], i, x, y;
      // 清空 11x11（每格带 pis：这一格上有哪几架飞机，供点击用）
      for (y = 0; y < FL_N; y++) for (x = 0; x < FL_N; x++) out.push({ x: x, y: y, cls: '', txt: '', pis: [] });
      function setCell(x, y, cls, txt) { out[y * FL_N + x] = { x: x, y: y, cls: cls, txt: txt, pis: [] }; }
      // 同一格可以叠多架：累加进去，不能互相覆盖
      function putPlane(x, y, cls, planeIdx) {
        var c = out[y * FL_N + x];
        c.pis.push(planeIdx);
        c.cls = cls;
        c.txt = c.pis.length > 1 ? '●' + c.pis.length : '●';
      }
      // 跑道
      for (i = 0; i < FL_TRACK; i++) {
        var col = (i % 4 === 0) ? 'f-c1' : ((i % 4 === 2) ? 'f-c2' : 'f-track');
        setCell(FL_POS[i][0], FL_POS[i][1], 'f-cell ' + col, '');
      }
      // 中心归巢区
      for (y = 4; y <= 6; y++) for (x = 4; x <= 6; x++) setCell(x, y, 'f-home', '');
      var done = 0;
      for (i = 0; i < s.planes.length; i++) {
        var pl = s.planes[i];
        var cls = pl.c === 1 ? 'f-p1' : 'f-p2';
        if (pl.pos < 0) {
          // 基地：每种颜色四架各占一格
          var n = i % 4, bx, by;
          if (pl.c === 1) { bx = 1 + (n % 2); by = 1 + Math.floor(n / 2); }
          else { bx = 8 + (n % 2); by = 8 + Math.floor(n / 2); }
          putPlane(bx, by, 'f-base ' + cls, i);
          continue;
        }
        if (pl.pos === FL_HOME) { done++; continue; }        // 已归巢，单独统计
        if (pl.pos >= FL_TRACK) {
          // 归巢跑道 4 格
          var k = pl.pos - FL_TRACK;
          putPlane(4 + (k % 3), 4 + (k >= 3 ? 1 : 0), 'f-home ' + cls, i);
          continue;
        }
        var abs = flAbs(i, pl.pos);
        putPlane(FL_POS[abs][0], FL_POS[abs][1], 'f-cell ' + cls, i);
      }
      if (done > 0) setCell(5, 5, 'f-home', '归巢 ' + done);
      return out;
    }
  };
  function flApply(s, planeIdx) {
    var pl = s.planes[planeIdx], d = s.dice;
    pl.pos = pl.pos < 0 ? 0 : pl.pos + d;
    // 撞子：同格对手回基地
    for (var i = 0; i < s.planes.length; i++) {
      var op = s.planes[i];
      if (op.c === pl.c || op.pos < 0 || op.pos >= FL_TRACK) continue;
      if (flAbs(i, op.pos) === flAbs(planeIdx, pl.pos)) op.pos = -1;
    }
    s.rolled = false;
    var allHome = true;
    for (var k = 0; k < s.planes.length; k++) if (s.planes[k].c === pl.c && s.planes[k].pos !== FL_HOME) allHome = false;
    if (allHome) { s.over = true; s.win = pl.c; return; }
    // 掷 6 可以再掷一次
    if (d === 6) { s.msg = '掷出 6，还能再掷一次'; return; }
    s.turn = 3 - s.turn;
    s.msg = '轮到' + (s.turn === 1 ? '你' : ' TA');
  }
  G.flight.moves = flMoves;
  G.flight.home = FL_HOME;
  G.flight.track = FL_TRACK;

  // ===================== 角色棋力 =====================
    G.skill = { rito: 4, yui: 5, yami: 5, nemesis: 5, ren: 4, lala: 3, haruna: 3, mikan: 4, nana: 4, momo: 5, mea: 4, saki: 3, zastin: 5, shizu: 2 };
  G.skillOf = function (id) { return G.skill[id] || 3; };
  G.stars = function (n) {
    var s = '';
    for (var i = 0; i < 5; i++) s += i < n ? '★' : '☆';
    return s;
  };

  if (typeof module !== 'undefined' && module.exports) module.exports = G;
  else root.GAMES = G;
})(this);
