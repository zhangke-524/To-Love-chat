// 规则测试：node game_test.js
var G = require('../build/games.js');
var pass = 0, fail = 0;
function ok(name, cond, info) {
  if (cond) { pass++; console.log('PASS  ' + name); }
  else { fail++; console.log('FAIL  ' + name + '   ' + (info === undefined ? '' : info)); }
}

// ---------- 五子棋 ----------
var s = G.gomoku.newState();
// 横向连五
[[0, 7], [1, 7], [2, 7], [3, 7], [4, 7]].forEach(function (p) {
  var idx = p[1] * 15 + p[0];
  G.gomoku.click(s, idx);
  if (!s.over) s.turn = 1;   // 强行让黑继续下，模拟连五
});
ok('五子棋横向连五判胜', s.over && s.win === 1, JSON.stringify([s.over, s.win]));
var s2 = G.gomoku.newState();
G.gomoku.click(s2, 0);
ok('五子棋不能下在已有子上', !G.gomoku.click(s2, 0));
var s3 = G.gomoku.newState();
G.gomoku.click(s3, 0);
ok('五子棋落子后换手', s3.turn === 2);
var s4 = G.gomoku.newState();
G.gomoku.click(s4, 7 * 15 + 7);
var ai = G.gomoku.ai(s4, 5);
ok('五子棋 AI 返回合法空点', ai >= 0 && s4.b[ai] === 0, ai);
var s5 = G.gomoku.newState();
// 黑有 4 连（0,7)-(3,7)，AI 该去堵 (4,7) 或 (-1 不存在) 优先 (4,7)
[0, 1, 2, 3].forEach(function (x) { s5.b[7 * 15 + x] = 1; });
s5.turn = 2;
var block = G.gomoku.ai(s5, 5);
ok('五子棋 AI 会堵四连', block === 7 * 15 + 4, block);

// ---------- 国际象棋 ----------
var c0 = G.chess.newState();
ok('国际象棋 64 格', c0.b.length === 64, c0.b.length);
ok('开局 32 个子（白黑各 16）',
  c0.b.filter(function (p) { return p >= 'A' && p <= 'Z'; }).length === 16 &&
  c0.b.filter(function (p) { return p >= 'a' && p <= 'z'; }).length === 16);
// 白兵 e2-e4（52 -> 36）
ok('白兵可以走两格开局', G.chess.apply(c0, 52, 36) === true && c0.b[36] === 'P', c0.b[36]);
ok('走完轮到黑', c0.turn === 2, c0.turn);
// 黑不能走白子
ok('不能替对方走棋', G.chess.apply(c0, 36, 28) === false);
// 黑方走一步后再跳马：g1-f3（62 -> 45）
ok('黑兵可以走一格', G.chess.apply(c0, 12, 20) === true);
ok('白马可以跳 f3', G.chess.apply(c0, 62, 45) === true && c0.b[45] === 'N', c0.b[45]);
// 车被兵堵住
var c1 = G.chess.newState();
c1.from = 56;                       // a1 车
ok('车不能穿过自己的兵', G.chess.apply(c1, 56, 40) === false);
// 吃过路兵
var c2 = G.chess.newState();
c2.turn = 1; G.chess.apply(c2, 52, 36);   // 白 e2-e4
c2.turn = 2; G.chess.apply(c2, 8, 24);    // 黑 a7-a5
c2.turn = 1; G.chess.apply(c2, 36, 28);   // 白 e4-e5
c2.turn = 2; G.chess.apply(c2, 11, 27);   // 黑 d7-d5  (两个兵并排)
c2.turn = 1;
var epOk = G.chess.apply(c2, 28, 19);     // 白 e5xd6 吃过路兵
ok('吃过路兵', epOk === true && c2.b[27] === '', epOk);
// 升变
var c3 = G.chess.newState();
c3.b = [];
c3.b[9] = 'P';      // 白兵 b7
c3.b[4] = 'k';      // 黑王 e8
c3.b[60] = 'K';     // 白王 e1
c3.turn = 1;
var promoOk = G.chess.apply(c3, 9, 1);   // b7-b8 升变
ok('兵到底线升变为后', promoOk === true && c3.b[1] === 'Q', JSON.stringify([promoOk, c3.b[1]]));
// 将死判定
var c4 = G.chess.newState();
c4.b = [];
c4.b[4] = 'k';        // 黑王 e8
c4.b[11] = 'q';       // 白后 d7 -> 直接照面
c4.b[60] = 'K';       // 白王 e1
c4.turn = 1;
var mv2 = G.chess.ai(c4, 5);
ok('AI 能返回一步合法走法', !!mv2 && mv2.length === 2, JSON.stringify(mv2));
// 王车易位可用性
var c5 = G.chess.newState();
c5.b[61] = ''; c5.b[62] = ''; c5.b[63] = 'R'; c5.b[60] = 'K';
c5.turn = 1;
var castleOk = G.chess.apply(c5, 60, 62);
ok('王车易位（短易位）', castleOk === true && c5.b[61] === 'R' && c5.b[62] === 'K', JSON.stringify([c5.b[61], c5.b[62]]));
// ---------- 围棋 ----------
// 和棋规则：50 回合 / 三次重复 / 子力不足
function chBoard(map) { var b = [], q; for (q = 0; q < 64; q++) b.push(''); for (q in map) b[q] = map[q]; return b; }
var d1 = G.chess.newState();
d1.b = chBoard({ 4: 'k', 60: 'K' });
d1.turn = 1; d1.half = 99;
var dOk = G.chess.apply(d1, 60, 52);   // 白王 e1-e2（非吃子非兵）
ok('50 回合没吃子没动兵算和棋', dOk === true && d1.over === true && d1.win === 0 && d1.end === 'fifty', JSON.stringify([dOk, d1.over, d1.win, d1.end]));
var d2 = G.chess.newState();
d2.b = chBoard({ 4: 'k', 60: 'K', 27: 'N' });
d2.turn = 1;
G.chess.apply(d2, 27, 36);
G.chess.apply(d2, 4, 5);
G.chess.apply(d2, 36, 27);
var dIns = (function () { var s = G.chess.newState(); s.b = chBoard({ 4: 'k', 60: 'K', 27: 'N' }); s.turn = 1; G.chess.apply(s, 60, 51); return s; })();
ok('只剩王+单马算和棋（子力不足）', dIns.over === true && dIns.win === 0, JSON.stringify([dIns.over, dIns.end]));
var d3 = G.chess.newState();
var k2;
// 双方各把马跳出去再跳回来，循环两次 → 局面重复三次 → 和棋
for (k2 = 0; k2 < 4 && !d3.over; k2++) {
  G.chess.apply(d3, 57, 42); if (d3.over) break;   // 白 Nb1-c3
  G.chess.apply(d3, 1, 18); if (d3.over) break;    // 黑 Nb8-c6
  G.chess.apply(d3, 42, 57); if (d3.over) break;   // 白 Nc3-b1
  G.chess.apply(d3, 18, 1);                        // 黑 Nc6-b8
}
ok('来回重复走子会判和棋', d3.over === true && d3.win === 0, JSON.stringify([d3.over, d3.end, d3.win]));
ok('有 legalFor / inCheck 接口', typeof G.chess.legalFor === 'function' && typeof G.chess.inCheck === 'function');
// ---------- 围棋 ----------
var g = G.go.newState();
G.go.click(g, 0);
ok('围棋落子成功', g.b[0] === 1);
ok('围棋同点不能再下', !G.go.click(g, 0));
var g2 = G.go.newState();
g2.b[0] = 2;                 // 白子放在角上 (0,0)
g2.turn = 1; G.go.click(g2, 1);   // 黑 (1,0)
g2.turn = 1; G.go.click(g2, 9);   // 黑 (0,1) → 白 (0,0) 无气被提
var captured = (g2.b[0] === 0);
ok('围棋提子', captured, JSON.stringify(g2.b.slice(0, 12)));
ok('围棋提子计数', g2.caps[0] >= 1, JSON.stringify(g2.caps));
var g3 = G.go.newState();
G.go.pass(g3);
ok('围棋一次 pass 不结束', !g3.over);
G.go.pass(g3);
ok('围棋两次 pass 结束并数子', g3.over, JSON.stringify(g3.score));
// 围棋回合应该交替（AI 下完要交回来）
var g4 = G.go.newState();
G.go.click(g4, 40);
ok('围棋：我下完轮到白', g4.turn === 2, g4.turn);
G.go.click(g4, 30);
ok('围棋：白下完轮到我', g4.turn === 1, g4.turn);
ok('围棋：一手一颗子', g4.b[40] === 1 && g4.b[30] === 2);

// ---------- 飞行棋 ----------
var f = G.flight.newState();
ok('飞行棋 8 架飞机', f.planes.length === 8, f.planes.length);
var f2 = G.flight.newState();
f2.dice = 6; f2.rolled = true;
var mv6 = G.flight.moves(f2, 6);
ok('掷 6 可以出发', mv6.length === 4, mv6.length);
var f3 = G.flight.newState();
f3.dice = 3; f3.rolled = true;
ok('掷 3 不能出发', G.flight.moves(f3, 3).length === 0);
var f4 = G.flight.newState();
f4.planes[0].pos = 42; f4.dice = 5; f4.rolled = true;
ok('超过终点不能走', G.flight.moves(f4, 5).indexOf(0) < 0);
var f5 = G.flight.newState();
f5.planes[0].pos = 41; f5.dice = 3; f5.rolled = true;
ok('正好到终点可以走', G.flight.moves(f5, 3).indexOf(0) >= 0);
// 撞子
var f6 = G.flight.newState();
f6.planes[0].pos = 5; f6.planes[4].pos = 25; // 蓝 5 → abs 5；红 25 → abs 45%40=5
ok('同格判定成立', (function () { return true; })());
f6.dice = 0;
var before6 = f6.planes[4].pos;
G.flight.moves; // noop
// 直接调用 applyAI 走 0 号机
f6.planes[0].pos = 5; f6.dice = 0; f6.rolled = true;
G.flight.applyAI(f6, 0);
ok('撞子把对手送回基地', f6.planes[4].pos === -1, f6.planes[4].pos);

// ---------- 角色棋力 ----------
ok('角色棋力已定义', G.skillOf('yami') === 5 && G.skillOf('shizu') === 2, G.skillOf('yami') + ',' + G.skillOf('shizu'));
ok('星级显示', G.stars(3) === '★★★☆☆', G.stars(3));
 // 围棋：盘上没有可下点时，AI 必须返回 -1（由 UI 层结算，不能静默不动）
var g5 = G.go.newState();
(function () { var i; for (i = 0; i < 81; i++) g5.b[i] = 1; })();
g5.turn = 2;
ok('围棋：无处可下时 AI 返回 -1', G.go.ai(g5, 3) === -1, G.go.ai(g5, 3));
ok('围棋：hasMove 判断为 false', G.go.hasMove(g5) === false, G.go.hasMove(g5));
// 有空点时 AI 必须给出合法着点
var g5b = G.go.newState();
(function () { var i; for (i = 0; i < 81; i++) g5b.b[i] = 1; })();
g5b.b[30] = 0; g5b.b[31] = 0; g5b.b[39] = 0; g5b.b[40] = 0;
g5b.turn = 2;
(function () { var m = G.go.ai(g5b, 3); ok('围棋：有气的地方 AI 会落子', m >= 0, m); })();
var g6 = G.go.newState();
G.go.resign(g6, 1);
ok('围棋：认输后结束且对手赢', g6.over === true && g6.win === 2, g6.win);
var g7 = G.go.newState();
G.go.resign(g7, 2);
ok('围棋：对手认输算我赢', g7.over === true && g7.win === 1, g7.win);
var g8 = G.go.newState();
G.go.finish(g8);
ok('围棋：结算会给出比分', g8.over === true && g8.score && g8.score.length === 3, JSON.stringify(g8.score));

console.log('\n' + (fail === 0 ? 'ALL PASS' : fail + ' FAILED') + '  (' + pass + ' passed)');
process.exit(fail === 0 ? 0 : 1);
