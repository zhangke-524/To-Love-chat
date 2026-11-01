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
  pickGame('chess');
  startGame('yui', 'me');
  var plies = 0, stalled = false, noMove = false, i, to, log = [];
  while (!gState.over && plies < 260) {
    if (gState.turn === 1) {
      var done = false;
      for (i = 0; i < 64 && !done; i++) {
        var pc = gState.b[i];
        if (!pc || !(pc >= 'A' && pc <= 'Z')) continue;
        for (to = 0; to < 64 && !done; to++) {
          var snap = JSON.stringify(gState);
          gCell(i); gCell(to);
          if (gState.turn === 2) done = true;
          else gState = JSON.parse(snap);
        }
      }
      if (!done) { noMove = true; break; }
      plies++;
    } else {
      aiTurn();
      if (gState.turn === 1 || gState.over) plies++;
      else stalled = true;
    }
  }
  return 'plies=' + plies + ' over=' + gState.over + ' win=' + gState.win + ' stalled=' + stalled + ' noMove=' + noMove + ' busy=' + gBusy;
})()`, ctx);
console.log('模拟: ' + sim);
process.exit(0);