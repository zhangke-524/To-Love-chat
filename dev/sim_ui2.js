// 朋友圈界面逻辑测试（微信样式版）：最小 DOM 桩跑 wechat.html 的 <script>
const fs = require('fs');
const vm = require('vm');
const path = require('path');

const html = fs.readFileSync(path.join(__dirname, '..', 'app', 'data', 'wechat.html'), 'utf8');
// 页面里有多段 <script>（主逻辑 + 游戏引擎 + 游戏 UI），全部拼起来一起跑
const blocks = [];
const re = /<script>([\s\S]*?)<\/script>/g;
let mm;
while ((mm = re.exec(html)) !== null) blocks.push(mm[1]);
if (!blocks.length) { console.log('FAIL 没找到 <script> 块'); process.exit(1); }
const code = blocks.join('\n');

class FakeEl {
  constructor(tag) {
    this.tag = tag; this.style = {}; this.className = ''; this._html = '';
    this.innerText = ''; this.value = ''; this.children = [];
    this.disabled = false; this.scrollTop = 0; this.scrollHeight = 0;
  }
  get innerHTML() { return this._html; }
  set innerHTML(v) { this._html = v; if (v === '') this.children = []; }
  appendChild(c) { this.children.push(c); return c; }
  getBoundingClientRect() { return { top: 0, bottom: 0, left: 0, right: 0 }; }
  scrollIntoView() { }
  focus() { }
  getElementsByClassName() { return [new FakeEl('div')]; }
  getElementsByTagName() { return this._tags || []; }
  setAttribute() { }
}

const els = Object.create(null);
const calls = [];
const documentStub = {
  getElementById(id) { if (!els[id]) els[id] = new FakeEl('div'); return els[id]; },
  createElement(tag) { return new FakeEl(tag); }
};
const windowStub = {
  external: new Proxy({}, { get: (t, k) => (...a) => { calls.push([k, ...a]); return ''; } })
};

const ctx = vm.createContext({
  document: documentStub, window: windowStub, setTimeout, clearTimeout, setInterval, clearInterval,
  JSON, Date, Math, console, confirm: () => true, alert: () => { }, calls
});
vm.runInContext(code, ctx);
const sim = vm.runInContext(`(function(){
  renderCharacters(JSON.stringify([{id:'yui',name:'古手川唯',avatar:'yui',py:'G',builtin:true,last:'',time:'',active:false,unread:false,group:false}]));
  var out = [], g, freezes = 0, longest = 0, ends = {1:0,2:0,0:0}, noEnd = 0;
  for (g = 0; g < 20; g++) {
    pickGame('chess');
    startGame('yui', Math.random() < 0.5 ? 'me' : 'opp');
    var plies = 0;
    while (!gState.over && plies < 400) {
      if (gState.turn === 1) {
        var done = false, i, to;
        for (i = 0; i < 64 && !done; i++) {
          var pc = gState.b[i];
          if (!pc || !(pc >= 'A' && pc <= 'Z')) continue;
          for (to = 0; to < 64 && !done; to++) {
            var snap = JSON.stringify(gState);
            gCell(i); gCell(to);
            if (gState.turn === 2) done = true; else gState = JSON.parse(snap);
          }
        }
        if (!done) { noEnd++; break; }
        plies++;
      } else {
        aiTurn();
        if (gState.over) { plies++; break; }
        if (gState.turn === 2) { freezes++; break; }   // 冻结：AI 走完还没轮到我
        plies++;
      }
    }
    if (plies > longest) longest = plies;
    if (gState.over) ends[gState.win] = (ends[gState.win] || 0) + 1; else if (!freezes) noEnd++;
  }
  return 'froze=' + freezes + ' longest=' + longest + ' win1=' + ends[1] + ' win2=' + (ends[2]||0) + ' draw=' + (ends[0]||0) + ' stuckGames=' + noEnd;
})()`, ctx);
console.log('20 局结果: ' + sim);
process.exit(0);