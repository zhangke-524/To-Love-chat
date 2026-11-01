var G = require('../build/games.js');
// UI 级模拟：掷骰 → 点棋子（走 pi）→ 换人；看有没有异常状态
function playGame() {
  var s = G.flight.newState(), steps = 0, issues = [];
  while (!s.over && steps < 3000) {
    steps++;
    if (s.turn === 1) {
      if (!s.rolled) {
        var d = G.flight.roll(s);
        if (!G.flight.moves(s, s.dice).length) { s.rolled = false; s.turn = 2; continue; }
      }
      var mv = G.flight.moves(s, s.dice);
      if (!mv.length) { issues.push('玩家无棋可走但 rolled=' + s.rolled); s.rolled = false; s.turn = 2; continue; }
      // 模拟点棋盘格子：找到该飞机所在格的 pi
      var cells = G.flight.cells(s), pi = -1, ci = -1;
      for (var i = 0; i < cells.length; i++) if (cells[i].pis && cells[i].pis.indexOf(mv[0]) >= 0) { pi = mv[0]; ci = i; break; }
      if (pi < 0) { issues.push('找不到飞机 ' + mv[0] + ' 对应的格子'); break; }
      var hitList = cells[ci].pis; var okClick = false; for (var q = 0; q < hitList.length; q++) { if (G.flight.click(s, hitList[q])) { okClick = true; break; } } if (!okClick) { issues.push('点格子失败 cells=' + hitList.join(',') + ' dice=' + s.dice); break; }
    } else {
      if (!s.rolled) G.flight.roll(s);
      var mv2 = G.flight.moves(s, s.dice);
      if (!mv2.length) { s.rolled = false; s.turn = 1; continue; }
      var m = G.flight.ai(s, 5);
      if (m < 0) { issues.push('AI 说没棋但 moves 非空'); break; }
      G.flight.applyAI(s, m);
    }
  }
  return { over: s.over, win: s.win, steps: steps, issues: issues,
           homes: s.planes.map(function (p) { return p.pos; }) };
}
var ok = 0, bad = 0, stepList = [];
for (var g = 0; g < 30; g++) {
  var r = playGame();
  if (r.over) ok++; else bad++;
  stepList.push(r.steps);
  if (r.issues.length) console.log('第' + g + '局问题: ' + r.issues.join(' | '));
}
console.log('结束 ' + ok + '/30 局，未结束 ' + bad + '，平均步数 ' + Math.round(stepList.reduce(function (a, b) { return a + b; }, 0) / stepList.length));
