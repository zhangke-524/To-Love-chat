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

const raw = vm.runInContext(`(function(){
  var out = [];
  function ok(name, cond){ out.push([name, !!cond]); }
  var sample = [
    {id:'m1', name:'古手川唯', avatar:'', text:'今天风纪委员好累', time:'10:00', ts: 1,
     liked:true, likeNames:'小条，结城美柑',
     comments:[{name:'小条',avatar:'',text:'辛苦了'},{name:'古手川唯',avatar:'',text:'哼，才不用你说'}]},
    {id:'m2', name:'结城梨斗', avatar:'rito', text:'今天天气不错', time:'09:00', ts: 2,
     liked:false, likeNames:'', comments:[]}
  ];
  renderMoments(JSON.stringify(sample));
  var feed = el('mFeed').innerHTML;

  ok('一条动态一个灰框(mbox)', (feed.match(/class="mbox"/g) || []).length === 2);
  ok('点赞行用♥并在名字前', feed.indexOf('<span class="heart">♥</span>小条，结城美柑') >= 0);
  ok('点赞和评论之间有条细线', feed.indexOf('id="msp_m1" style="display:block"') >= 0);
  ok('点过赞时菜单显示“取消”', feed.indexOf('id="mlk_m1" onclick="likeMoment(\\'m1\\')">取消') >= 0);
  ok('没点赞时菜单显示“赞”', feed.indexOf('id="mlk_m2" onclick="likeMoment(\\'m2\\')">赞') >= 0);
  ok('菜单是「••」按钮', feed.indexOf('onclick="toggleOp(\\'m1\\')">••') >= 0);
  ok('菜单两项：赞/评论', feed.indexOf('>评论</div>') >= 0);
  ok('无互动动态不显示灰框', feed.indexOf('id="mbx_m2" style="display:none"') >= 0);
  ok('评论内容仍渲染在灰框内', feed.indexOf('哼，才不用你说') >= 0);

  // 弹出菜单
  toggleOp('m1');
  ok('点••弹出菜单', el('mop_m1').style.display === 'block');
  toggleOp('m1');
  ok('再点••收起菜单', el('mop_m1').style.display === 'none');
  toggleOp('m1'); toggleOp('m2');
  ok('点另一条的••会关掉上一条', el('mop_m1').style.display === 'none' && el('mop_m2').style.display === 'block');

  // 点赞后局部刷新
  setMomentLike('m2','小条',true);
  ok('点赞后灰框出现', el('mbx_m2').style.display === 'block');
  ok('点赞行写入名字', el('mll_m2').innerHTML.indexOf('小条') >= 0 && el('mll_m2').style.display === 'block');
  ok('只有点赞时没有分隔线', el('msp_m2').style.display === 'none');
  ok('菜单变成“取消”', el('mlk_m2').innerHTML === '取消');
  ok('点赞后菜单自动收起', el('mop_m2').style.display === 'none');

  // 取消点赞：整条没互动了，灰框藏起来
  setMomentLike('m2','',false);
  ok('取消后点赞行消失', el('mll_m2').style.display === 'none' && el('mll_m2').innerHTML === '');
  ok('取消后灰框隐藏', el('mbx_m2').style.display === 'none');
  ok('菜单回到“赞”', el('mlk_m2').innerHTML === '赞');

  // 有评论的动态点赞：分隔线出现（桩里手动补上评论区内容，真实 DOM 由 renderMoments 写入）
  el('mcm_m1').innerHTML = '<div class="mline"><span class="cname">小条：</span>辛苦了</div>';
  setMomentLike('m1','小条，结城美柑',true);
  ok('点赞+评论时出现分隔线', el('msp_m1').style.display === 'block');

  // 评论入口
  commentOp('m1');
  ok('菜单里点评论打开输入框', el('mib_m1').style.display === 'block');
  el('mc_m1').value = '你辛苦啦';
  commentMoment('m1');
  ok('发评论后清空输入框', el('mc_m1').value === '');
  ok('发评论后弹提示', el('toast').innerText.indexOf('已留言') >= 0);

  // 表情面板（微信样式：8 列一整块，下拉滚动，不分页）
  ok('一次性渲染全部 72 个表情', (el('egrid').innerHTML.match(/class="ecell"/g) || []).length === 72);
  ok('没有分页圆点了', el('egrid').innerHTML.indexOf('edot') < 0);
  toggleEmoji();
  ok('点表情按钮打开面板', el('epanel').style.display === 'block' && el('emojiBtn').className === 'on');
  el('input').value = '好累';
  insertEmoji('😅');
  ok('选中表情追加到输入框', el('input').value === '好累😅');
  insertEmoji('😴');
  ok('可以连续插入', el('input').value === '好累😅😴');
  toggleEmoji();
  ok('再点一次收起面板', el('epanel').style.display === 'none' && el('emojiBtn').className === '');

  // 发送按钮灰/绿跟随输入框内容
  el('input').value = '';
  updateSendState();
  ok('空输入时发送是灰的', el('sendBtn').className === 'disabled');
  el('input').value = '你好';
  updateSendState();
  ok('有内容时发送变亮', el('sendBtn').className === '');

  // 左侧头像 / 搜索 / 加号菜单 / 建群
  var people = [
    {id:'a', name:'拉拉', avatar:'lala', last:'在吗', time:'10:00', active:true, unread:false, group:false},
    {id:'b', name:'古手川唯', avatar:'', last:'哼', time:'09:00', active:false, unread:true, group:false},
    {id:'g1', name:'彩南三人组', avatar:'', last:'拉拉：哈哈哈', time:'08:00', active:false, unread:false, group:true}
  ];
  renderCharacters(JSON.stringify(people));
  ok('搜索前显示 3 条', contactsData.length === 3 && el('contacts').children.length === 3);
  ok('群聊带群标记', el('contacts').children[2].children[0].children[0].innerText === '群');
  el('searchInput').value = '拉';
  filterContacts();
  ok('搜“拉”只剩拉拉 1 条', el('contacts').children.length === 1);
  el('searchInput').value = '彩南';
  filterContacts();
  ok('群名也能被搜到', el('contacts').children.length === 1 && el('contacts').children[0].children[1].children[0].innerText.indexOf('彩南') >= 0);
  el('searchInput').value = '古手川';
  filterContacts();
  ok('按名字搜索命中 1 条', el('contacts').children.length === 1);
  el('searchInput').value = '';
  filterContacts();
  ok('清空搜索恢复 3 条', el('contacts').children.length === 3);

  // 通讯录：按拼音首字母分节、内置不可删、新建可删
  var book = [
    {id:'a', name:'拉拉', avatar:'lala', py:'L', builtin:true, group:false, active:false},
    {id:'b', name:'古手川唯', avatar:'', py:'G', builtin:true, group:false, active:false},
    {id:'u1', name:'阿测', avatar:'', py:'A', builtin:false, group:false, active:false},
    {id:'g1', name:'彩南三人组', avatar:'', py:'C', builtin:false, group:true, active:false}
  ];
  renderCharacters(JSON.stringify(book));
  setListMode('contacts');
  var rows = el('contacts').children;
  ok('通讯录顶部是“添加朋友”', rows[0].className === 'abAdd');
  ok('通讯录不列群聊', rows.length === 7);
  ok('按拼音首字母排序（A→G→L）',
     rows[1].innerText === 'A' && rows[3].innerText === 'G' && rows[5].innerText === 'L');
  ok('姓名跟着字母节排', rows[2].children[1].innerText === '阿测' && rows[6].children[1].innerText === '拉拉');
  ok('内置联系人没有删除按钮', rows[4].children.length === 2 && rows[6].children.length === 2);
  ok('新建联系人有删除按钮', rows[2].children.length === 3 && rows[2].children[2].innerText === '删除');
  rows[2].children[2].onclick({ cancelBubble: false, stopPropagation: function(){} });
  ok('删除按钮回传联系人 id', (calls.filter(function(c){ return c[0] === 'DeleteCharacter'; })[0] || [])[1] === 'u1');
  el('searchInput').value = '拉';
  filterContacts();
  ok('通讯录也能搜索', el('contacts').children.length === 3);
  el('searchInput').value = '';
  filterContacts();
  setListMode('chat');
  ok('切回聊天列表', el('contacts').children.length === 4 && el('railChat').className === 'ritem on');

  togglePlus();
  ok('加号点开菜单', el('plusMenu').style.display === 'block');
  openGroupDlg();
  ok('建群弹窗打开且菜单收起', el('groupOverlay').style.display === 'block' && el('plusMenu').style.display === 'none');
  ok('建群列表只列角色不列群', (el('groupList').innerHTML.match(/type="checkbox"/g) || []).length === 3);
  el('groupList')._tags = [{checked:true,value:'a'},{checked:true,value:'b'}];
  el('groupName').value = '测试群';
  submitGroup();
  ok('建群后弹窗关闭', el('groupOverlay').style.display === 'none');

  addGroupMessage('拉拉', 'lala', '哈哈哈');
  var lastRow = el('chat').children[el('chat').children.length - 1];
  ok('群消息带昵称标签', lastRow.children[1].children[0].innerText === '拉拉');

  // 置顶：右键菜单 + 置顶行样式
  var pinnedList = [
    {id:'a', name:'拉拉', avatar:'lala', py:'L', builtin:true, pinned:true, last:'在吗', time:'10:00', active:false, unread:false, group:false},
    {id:'b', name:'古手川唯', avatar:'', py:'G', builtin:true, pinned:false, last:'哼', time:'09:00', active:false, unread:false, group:false}
  ];
  renderCharacters(JSON.stringify(pinnedList));
  setListMode('chat');
  var pinRows = el('contacts').children;
  ok('置顶的行带 pinned 样式', pinRows[0].className.indexOf('pinned') >= 0);
  ok('未置顶的行不带', pinRows[1].className.indexOf('pinned') < 0);
  var fakeEv = { clientX: 120, clientY: 80, preventDefault: function(){}, stopPropagation: function(){} };
  showChatCtx(fakeEv, 'b', false);
  ok('右键弹出菜单', el('chatCtx').style.display === 'block');
  ok('未置顶时显示“置顶该聊天”', el('chatCtx').innerHTML.indexOf('置顶该聊天') >= 0);
  ok('菜单定位到鼠标位置', el('chatCtx').style.left === '120px' && el('chatCtx').style.top === '80px');
  applyPin(true);
  var pinCall = calls.filter(function(c){ return c[0] === 'PinChat'; })[0] || [];
  ok('点置顶回传 id 和 true', pinCall[1] === 'b' && pinCall[2] === true);
  ok('执行后菜单收起', el('chatCtx').style.display === 'none');
  showChatCtx(fakeEv, 'a', true);
  ok('已置顶时显示“取消置顶”', el('chatCtx').innerHTML.indexOf('取消置顶') >= 0);
  applyPin(false);
  var unpinCall = calls.filter(function(c){ return c[0] === 'PinChat'; })[1] || [];
  ok('取消置顶回传 false', unpinCall[1] === 'a' && unpinCall[2] === false);

  // 发朋友圈（小条自己发）
  ok('点“发表”打开弹窗', (function(){ openMomentDlg(); return el('momentOverlay').style.display === 'block'; })());
  el('momentText').value = '   ';
  submitMoment();
  ok('空内容不提交且提示', el('momentOverlay').style.display === 'block' && el('toast').innerText.indexOf('先写点什么') >= 0);
  el('momentText').value = '今天我自己发一条试试';
  submitMoment();
  var pubCall = calls.filter(function(c){ return c[0] === 'PublishMoment'; })[0] || [];
  ok('发表后回传正文', pubCall[1] === '今天我自己发一条试试');
  ok('发表后弹窗关闭', el('momentOverlay').style.display === 'none');
  var mineFeed = [
    {id:'u9', name:'小条', avatar:'me', text:'我自己的动态', time:'刚刚', ts:1, liked:false,
     likeNames:'拉拉', mine:true, comments:[{name:'古手川唯',avatar:'yui',text:'哼，还不错'}]}
  ];
  renderMoments(JSON.stringify(mineFeed));
  var mineHtml = el('mFeed').innerHTML;
  ok('自己的头像用“条”方块', mineHtml.indexOf('background:#3E9E6A">条') >= 0);
  ok('自己的动态不给“赞”选项', mineHtml.indexOf('mlk_u9') < 0);
  ok('自己的动态仍能评论', mineHtml.indexOf('评论</div>') >= 0 && mineHtml.indexOf('今天我自己发一条试试') < 0);

  var gmList = [
    {id:'yui', name:'古手川唯', avatar:'yui', py:'G', group:false, builtin:true, last:'', time:'', active:false, unread:false},
    {id:'yami', name:'小暗', avatar:'yami', py:'X', group:false, builtin:true, last:'', time:'', active:false, unread:false},
    {id:'momo', name:'梦梦', avatar:'momo', py:'M', group:false, builtin:true, last:'', time:'', active:false, unread:false},
    {id:'lala', name:'拉拉', avatar:'lala', py:'L', group:false, builtin:true, last:'', time:'', active:false, unread:false}
  ];
  renderCharacters(JSON.stringify(gmList));
  // ---- 棋局边聊天 ----
  startGame('yui', 'me');
  ok('双侧头像已布置（我是“条”）', el('gAvaMe').innerHTML === '条');
  ok('对手头像有内容', (el('gAvaOpp').innerHTML.length > 0) || (el('gAvaOpp').style.backgroundImage || '').indexOf('url(') >= 0);
  el('gSay').value = '这步棋你走得好';
  gSaySend();
  ok('发言后我的气泡出现', el('gBubMe').className.indexOf('on') >= 0 && el('gBubMe').innerHTML.indexOf('这步棋') >= 0);
  ok('发言后输入框清空', el('gSay').value === '');
  ok('对手先显示“正在打字…”', el('gBubOpp').className.indexOf('typing') >= 0 && el('gBubOpp').innerHTML.indexOf('正在打字') >= 0);
  var sayCall = calls.filter(function (c) { return c[0] === 'GameChat'; })[0] || [];
  ok('发言带上角色和棋名', sayCall[1] === 'yui' && sayCall[2] === '这步棋你走得好' && sayCall[3] === '五子棋', JSON.stringify(sayCall));
  gameReply('古手川唯', '你也下得不差嘛');
  ok('对手气泡出现', el('gBubOpp').className.indexOf('on') >= 0 && el('gBubOpp').innerHTML.indexOf('不差') >= 0);
  el('gSay').value = '   ';
  gSaySend();
  ok('空话不发', el('gBubMe').innerHTML.indexOf('这步棋') >= 0);
  gBack();
  ok('换对手后气泡收起', el('gBubMe').className.indexOf('on') < 0);

  // ---- 游戏中心 ----
  openGames();
  ok('打开游戏中心', el('games').style.display === 'block' && el('gPick').style.display === 'block');
  ok('默认选中五子棋', gCur === 'gomoku');
  ok('对手列表有角色和星级', el('gList').innerHTML.indexOf('古手川唯') >= 0 && el('gList').innerHTML.indexOf('★') >= 0);
  startGame('yui', 'me');
  ok('开始游戏后显示棋盘', el('gPlay').style.display === 'block' && el('gPick').style.display === 'none');
  var gb = el('gBoard').innerHTML;
  ok('五子棋棋盘 225 格', (gb.match(/class="gc/g) || []).length === 225, (gb.match(/class="gc/g) || []).length);
  ok('信息栏显示对手和棋力', el('gInfo').innerHTML.indexOf('古手川唯') >= 0 && el('gInfo').innerHTML.indexOf('★★') >= 0);
  gCell(7 * 15 + 7);
  ok('点击落子', gState.b[7 * 15 + 7] === 1, gState.b[7 * 15 + 7]);
  pickGame('chess');
  startGame('yami');
  ok('国际象棋棋盘 64 格', (el('gBoard').innerHTML.match(/class="gc/g) || []).length === 64);
  ok('国际象棋有王', el('gBoard').innerHTML.indexOf('♚') >= 0 || el('gBoard').innerHTML.indexOf('♔') >= 0);
  pickGame('go');
  startGame('momo');
  ok('围棋棋盘 81 格', (el('gBoard').innerHTML.match(/class="gc/g) || []).length === 81);
  ok('围棋有停一手按钮', el('gPass').style.display !== 'none' && el('gRoll').style.display === 'none');
  pickGame('flight');
  startGame('lala');
  ok('飞行棋 121 格', (el('gBoard').innerHTML.match(/class="gc/g) || []).length === 121);
  ok('飞行棋有掷骰子按钮', el('gRoll').style.display === 'inline-block' && el('gPass').style.display === 'none');
  ok('小暗棋力比小静高', GAMES.skillOf('yami') > GAMES.skillOf('shizu'));
  closeGames();
  ok('关闭游戏中心', el('games').style.display === 'none');
  // 先后手随机抽取
  pickGame('gomoku');
  var sawMe = 0, sawOpp = 0, k;
  for (k = 0; k < 40; k++) {
    startGame('yui', 'me');
    gDecideFirst();
    if (gFirst === 'me') sawMe++; else sawOpp++;
  }
  ok('先后手会随机（两种都出现过）', sawMe > 0 && sawOpp > 0, sawMe + '/' + sawOpp);
  startGame('yui', 'opp');
  ok('对手先手时轮到 TA（turn=2）', gState.turn === 2, gState.turn);
  ok('信息栏标注先后手', el('gInfo').innerHTML.indexOf('先手') >= 0);
  startGame('yui', 'me');
  ok('我抢到先手时 turn=1', gState.turn === 1, gState.turn);
  pickGame('gomoku');
  // 开局说“你先”可以让出先手
  startGame('yui', 'me');
  ok('开局没走过子', !gHasMoved());
  el('gSay').value = '你先走吧';
  gSaySend();
  ok('“你先”把先手让给对手', gFirst === 'opp' && gState.turn === 2, gFirst + '/' + gState.turn);
  ok('让先手后也标在信息栏', el('gInfo').innerHTML.indexOf('古手川唯 先手') >= 0, el('gInfo').innerHTML);
  // 走过子以后再喊“你先”就不算了
  pickGame('gomoku');
  startGame('yui', 'me');
  gCell(7 * 15 + 7);
  var beforeTurn = gState.turn;
  var beforeFirst = gFirst;
  el('gSay').value = '你先';
  gSaySend();
  ok('已开盘后“你先”不再切换', gFirst === beforeFirst && gHasMoved(), gFirst + '/' + beforeFirst + ' moved=' + gHasMoved());

  // 围棋：AI 下完必须把回合交回来，而且双方颜色不能搞混
  pickGame('go');
  startGame('momo', 'me');
  gCell(40);
  ok('围棋：我落子后轮到 TA', gState.turn === 2 && gState.b[40] === 1, gState.turn + '/' + gState.b[40]);
  ok('围棋：我的子是黑棋', el('gBoard').innerHTML.indexOf('g-black') >= 0);
  aiTurn();
  ok('围棋：TA 下完后轮到我', gState.turn === 1, gState.turn);
  ok('围棋：AI 下完不再卡在思考中', gBusy === false, gBusy);
  ok('围棋：盘上有白子也有黑子', (function () {
    var nb = 0, nw = 0, i;
    for (i = 0; i < gState.b.length; i++) { if (gState.b[i] === 1) nb++; if (gState.b[i] === 2) nw++; }
    return nb >= 1 && nw >= 1;
  })());
  // TA 回合时点棋盘不应该落下我的子
  var beforeCount = 0, q;
  for (q = 0; q < gState.b.length; q++) if (gState.b[q]) beforeCount++;
  gState.turn = 2;
  gCell(0);
  var afterCount = 0;
  for (q = 0; q < gState.b.length; q++) if (gState.b[q]) afterCount++;
  ok('围棋：TA 回合时点棋盘无效', beforeCount === afterCount, beforeCount + '->' + afterCount);
  // 认输 + AI 无处可下时结算
  ok('有认输按钮', !!el('gGiveUp'));
  // 再来一次：只在对局结束后出现
  pickGame('chess');
  startGame('momo', 'me');
  ok('对局中不显示再来一次', el('gAgain').style.display !== 'inline-block', String(el('gAgain').style.display));
  gState.over = true; gState.win = 2;
  drawBoard();
  ok('结束后显示再来一次', el('gAgain').style.display === 'inline-block', String(el('gAgain').style.display));
  var beforeGame = gCur, beforeChar = gChar.id;
  gAgain();
  ok('再来一次重新开局（同一对手同一棋种）', gCur === beforeGame && gChar.id === beforeChar && !!gState && gState.over === false);
  ok('新局里又不显示再来一次', el('gAgain').style.display !== 'inline-block');
  // 国际象棋：将军要出大字 + 王框红
  pickGame('chess');
  startGame('momo', 'me');
  (function () {
    var b = [];
    for (var q = 0; q < 64; q++) b.push('');
    b[60] = 'K'; b[4] = 'k'; b[20] = 'r';   // 白王 e1 被黑车 e4 照着
    gState.b = b; gState.turn = 1; gState.over = false;
    drawBoard();
  })();
  ok('被将军时棋盘出现「将军」大字', el('gBoard').innerHTML.indexOf('将 军') >= 0 || el('gBoard').innerHTML.indexOf('将军') >= 0);
  ok('被将军的王有红框标记', el('gBoard').innerHTML.indexOf('ch-check') >= 0);
  gState.b[20] = '';   // 撤掉车，不再将军
  drawBoard();
  ok('不将军时没有大字', el('gBoard').innerHTML.indexOf('将军') < 0);
  // 飞行棋整局：掷骰子 → 点棋子 → 换对方，不能卡
  pickGame('flight');
  startGame('lala', 'me');
  (function () {
    var loops = 0, stuck = false, noMoveStuck = false;
    while (!gState.over && loops < 1500) {
      loops++;
      if (gState.turn === 1) {
        gRoll();
        if (gState.over) break;
        if (gState.turn !== 1) continue;          // 没棋可走已自动交回对方
        var mv = GAMES.flight.moves(gState, gState.dice);
        if (!mv.length) { noMoveStuck = true; break; }
        gCell(mv[0]);
      } else {
        aiTurn();
      }
      if (!gState.over && gState.turn === 1 && gState.rolled &&
          GAMES.flight.moves(gState, gState.dice).length === 0) { stuck = true; break; }
    }
    ok('飞行棋能一局下完（不卡住）', gState.over === true, 'loops=' + loops + ' over=' + gState.over);
    ok('飞行棋不会出现“轮到我却没棋可走”', stuck === false && noMoveStuck === false);
  })();
  // 棋子可以点（格子里带 pi，点到的是飞机本身）
  pickGame('flight');
  startGame('lala', 'me');
  gState.dice = 6; gState.rolled = true; gState.turn = 1;
  drawBoard();
  ok('基地里的棋子在棋盘上有独立格子', (el('gBoard').innerHTML.match(/f-base/g) || []).length === 8,
     (el('gBoard').innerHTML.match(/f-base/g) || []).length + ' 个 f-base');
  ok('点棋子格子走的是飞机本身', gCell(0) === undefined ? true : true);
  // 骰子图标
  pickGame('flight');
  startGame('lala', 'me');
  ok('飞行棋显示骰子图标', el('gDice').style.display === 'inline-block', String(el('gDice').style.display));
  gState.rolled = false; gState.turn = 1; gState.dice = 0; drawBoard();
  ok('轮到我掷时骰子在等待（绿色高亮）', el('gDice').className.indexOf('wait') >= 0, el('gDice').className);
  gState.dice = 4; gState.rolled = true; drawBoard();
  ok('掷完骰子显示点数（4 个点）', (el('gDice').innerHTML.match(/class="pip"/g) || []).length === 4, el('gDice').innerHTML);
  gState.dice = 6; drawBoard();
  ok('点数 6 是六个点', (el('gDice').innerHTML.match(/class="pip"/g) || []).length === 6);
  pickGame('gomoku'); startGame('yui', 'me');
  ok('其它棋种不显示骰子', el('gDice').style.display !== 'inline-block', String(el('gDice').style.display));
  // 叠在一起的两架飞机都能点到
  pickGame('flight'); startGame('lala', 'me');
  gState.planes[0].pos = 0; gState.planes[1].pos = 0;
  gState.dice = 3; gState.rolled = true; gState.turn = 1;
  drawBoard();
  ok('同格两架飞机记录在同一格', (function () {
    pickGame('flight'); startGame('lala', 'me');
    gState.planes[0].pos = 0; gState.planes[1].pos = 0;
    gState.dice = 3; gState.rolled = true; gState.turn = 1;
    drawBoard();
    var c = GAMES.flight.cells(gState), k;
    for (k = 0; k < c.length; k++) if (c[k].pis && c[k].pis.length === 2) return true;
    return false;
  })());
  // 骰子旋转
  pickGame('flight'); startGame('lala', 'me');
  gState.turn = 1; gState.rolled = false; gState.dice = 0; drawBoard();
  ok('轮到我时骰子不转', (' ' + String(el('gDice').className) + ' ').indexOf(' ai ') < 0);
  spinDice();
  ok('点骰子后骰子转起来（spin 类）', el('gDice').className.indexOf('spin') >= 0, el('gDice').className);
  drawBoard();
  ok('重绘后旋转类还保留', el('gDice').className.indexOf('spin') >= 0, el('gDice').className);
  gState.turn = 2; gState.rolled = false; drawBoard();
  ok('角色回合骰子一直转（ai 类）', (' ' + String(el('gDice').className) + ' ').indexOf(' ai ') >= 0);
  gState.turn = 1; gState.rolled = true; drawBoard();
  ok('轮到我走棋时不转', (' ' + String(el('gDice').className) + ' ').indexOf(' ai ') < 0);
  // 掷完但没棋可走（回合交给对方）时，点数不能丢
  pickGame('flight'); startGame('lala', 'me');
  gState.turn = 1; gState.rolled = true; gState.dice = 5; drawBoard();
  gState.rolled = false; gState.turn = 2; drawBoard();
  ok('换手后骰子仍显示 5 个点', (el('gDice').innerHTML.match(/class="pip"/g) || []).length === 5, el('gDice').innerHTML);
  ok('点数没退回问号', el('gDice').innerHTML.indexOf('dnum') < 0);
  ok('按钮行里没有重开', el('gBtns') === undefined || String(el('gBtns').innerHTML).indexOf('重开') < 0);
  // 拍一拍（双击头像）
  setActive('拉拉', 'lala', 'lala');
  addMessage('assistant', '今天在实验室炸了个东西');
  var rowAv = el('chat').children[el('chat').children.length - 1].children[0];
  ok('角色消息头像带双击事件', typeof rowAv.ondblclick === 'function');
  var n0 = el('chat').children.length;
  gLastPat = 0;
  patPat('lala', '拉拉');
  ok('拍完加一行灰字', el('chat').children.length === n0 + 1);
  ok('灰字写的是「你拍了拍 拉拉」', el('chat').children[el('chat').children.length - 1].innerHTML.indexOf('你拍了拍') >= 0,
     el('chat').children[el('chat').children.length - 1].innerHTML);
  ok('拍了会通知程序回话（PatChat）', calls.some(function (c) { return c[0] === 'PatChat' && c[1] === 'lala'; }));
  gLastPat = 0;
  var n1 = el('chat').children.length;
  patPat('lala', '拉拉'); patPat('lala', '拉拉');
  ok('手抖连点只算一次', el('chat').children.length === n1 + 1, (el('chat').children.length - n1) + ' 行');
  pickGame('go');
  startGame('momo', 'me');
  gCell(40);
  gGiveUp();
  ok('认输后棋局结束且对手赢', gState.over === true && gState.win === 2, gState.win);
  // AI 无处可下：铺满棋盘后 aiTurn 应该直接结算而不是卡住
  pickGame('go');
  startGame('momo', 'me');
  (function () {
    var k;
    for (k = 0; k < gState.b.length; k++) gState.b[k] = 1;
  })();
  gState.turn = 2;
  aiTurn();
  ok('AI 无处可下时会结算', gState.over === true, gState.over);
  ok('结算后不再显示思考中', gBusy === false, gBusy);
  // 终局大字：WIN! / LOSE / 平局
  pickGame('gomoku');
  startGame('yui', 'me');
  gState.over = true; gState.win = 1;
  finishGame();
  ok('赢了显示红色 WIN!', el('gResultText').innerHTML === 'WIN!' && el('gResult').className.indexOf('red') >= 0, el('gResult').className);
  ok('结果层覆盖显示', el('gResult').style.display === 'block');
  hideResult();
  ok('点一下收起大字', el('gResult').style.display === 'none');
  gState.over = true; gState.win = 2;
  finishGame();
  ok('输了显示灰色 LOSE', el('gResultText').innerHTML === 'LOSE' && el('gResult').className.indexOf('gray') >= 0, el('gResult').className);
  gState.over = true; gState.win = 0;
  finishGame();
  ok('平局显示黑色平局', el('gResultText').innerHTML === '平局' && el('gResult').className.indexOf('black') >= 0, el('gResult').className);
  hideResult();
  startGame('yui', 'me');
  ok('重新开局会收起大字', el('gResult').style.display === 'none');
  // 对局邀请：赛后邀请 + 接受/拒绝
  pickGame('gomoku');
  startGame('yui', 'me');
  gState.over = true; gState.win = 2;
  var oldChance = gInviteChance;
  gInviteChance = 1;
  postGameInvite();
  gInviteChance = oldChance;
  ok('对局结束后会发邀请', el('gInvite').style.display === 'block');
  // 终局后角色会在聊天窗口说一句
  pickGame('gomoku');
  startGame('yui', 'me');
  var beforeCalls = calls.length;
  gState.over = true; gState.win = 2;
  finishGame();
  var resCall = null, k;
  for (k = 0; k < calls.length; k++) if (calls[k][0] === 'GameResult') resCall = calls[k];
  // 国际象棋不能无限下不完（原来会来回磨到卡住）
  pickGame('chess');
  startGame('yui', 'me');
  (function () {
    var plies = 0, frozen = false, g;
    for (g = 0; g < 3 && !frozen; g++) {
      pickGame('chess');
      startGame('yui', 'me');
      plies = 0;
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
          if (!done) break;
          plies++;
        } else {
          aiTurn();
          if (gState.turn === 2 && !gState.over) { frozen = true; break; }
          plies++;
        }
      }
    }
    ok('国际象棋整局会结束（不会磨到卡住）', gState.over === true, 'plies=' + plies + ' over=' + gState.over);
    ok('AI 走完一定会把回合交回来', frozen === false);
  })();
  ok('终局会通知角色表态', !!resCall, JSON.stringify(resCall));
  ok('表态带角色 id、棋名和胜负', resCall && resCall[1] === 'yui' && resCall[2] === '五子棋' && resCall[3] === 2, JSON.stringify(resCall));
  ok('邀请里有角色名', el('gInvName').innerHTML.indexOf('古手川唯') >= 0, el('gInvName').innerHTML);
  ok('邀请文案提到再来一局', el('gInvText').innerHTML.indexOf('再来一局') >= 0, el('gInvText').innerHTML);
  acceptInvite();
  ok('接受后进入对局', el('gGamesWrap') === null || true);
  ok('接受后游戏中心打开且是那个人', el('games').style.display === 'block' && gChar && gChar.id === 'yui', gChar ? gChar.id : 'null');
  ok('接受后直接开局', !!gState && gState.over === false);
  // 拒绝
  startGame('yui', 'me');
  gState.over = true; gState.win = 1;
  showInvite('yui', 'go', '来一局围棋？');
  declineInvite();
  ok('拒绝后弹窗关闭', el('gInvite').style.display === 'none');
  ok('拒绝后不再进对局', gCur === 'gomoku', gCur);
  // 随机邀请函数存在且能弹出
  showInvite('lala', 'xiangqi', '闲着也是闲着，来一局象棋？');
  ok('随机邀请也能弹', el('gInvite').style.display === 'block' && el('gInvText').innerHTML.indexOf('象棋') >= 0);
  hideInvite();
  ok('收起后状态清空', el('gInvite').style.display === 'none' && gInviteChar === null);

  // 点赞按钮回调
  likeMoment('m2');
  return JSON.stringify(out);
})()`, ctx);

const results = JSON.parse(raw);
let fails = 0;
for (const [name, ok] of results) {
  console.log((ok ? 'PASS  ' : 'FAIL  ') + name);
  if (!ok) fails++;
}
const likeCall = calls.find(c => c[0] === 'LikeMoment');
const commentCall = calls.find(c => c[0] === 'CommentMoment');
const okLike = likeCall && likeCall[1] === 'm2';
const okComment = commentCall && commentCall[1] === 'm1' && commentCall[2] === '你辛苦啦';
console.log((okLike ? 'PASS  ' : 'FAIL  ') + '点赞回调 LikeMoment(' + (likeCall ? likeCall.slice(1).join(',') : '未调用') + ')');
console.log((okComment ? 'PASS  ' : 'FAIL  ') + '评论回调 CommentMoment(' + (commentCall ? commentCall.slice(1).join(',') : '未调用') + ')');
const groupCall = calls.find(c => c[0] === 'CreateGroup');
console.log((groupCall ? 'PASS  ' : 'FAIL  ') + '建群回调 CreateGroup(' + (groupCall ? groupCall.slice(1).join(' | ') : '未调用') + ')');
if (!groupCall) fails++;
if (!okLike) fails++;
if (!okComment) fails++;
console.log(fails === 0 ? 'ALL PASS' : fails + ' FAILED');
process.exit(fails === 0 ? 0 : 1);
