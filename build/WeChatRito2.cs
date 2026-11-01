using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RitoChat
{
    [System.Runtime.InteropServices.ComVisible(true)]
    public class Bridge
    {
        private readonly MainForm form;
        public Bridge(MainForm f) { form = f; }
        public void Send(string text) { form.UserSend(text); }
        public void ConfirmSuper(string id, bool ok) { form.SuperConfirm(id, ok); }
        public void DeleteMemory(string id) { form.DeleteMem(id); }
        public void RefreshMemories() { form.PushMemories(); }
        public void SwitchChat(string id) { form.SwitchTo(id, false); }
        public void AddCharacter(string name, string prompt) { form.AddCharacter(name, prompt); }
        public void CreateGroup(string name, string memberIds) { form.CreateGroup(name, memberIds); }
        public void DeleteCharacter(string id) { form.DeleteCharacter(id); }
        public void PinChat(string id, bool pinned) { form.SetPinned(id, pinned); }
        public void OpenMoments() { form.OpenMoments(); }
        public void CloseMoments() { }
        public void CommentMoment(string id, string text) { form.AddMomentComment(id, text); }
        public void LikeMoment(string id) { form.ToggleMomentLike(id); }
        public void PublishMoment(string text) { form.PublishMoment(text); }
        public void GameChat(string id, string text, string game) { form.GameChat(id, text, game); }
        public void GameResult(string id, string game, int result) { form.GameResult(id, game, result); }
        public void PatChat(string id) { form.Pat(id); }
    }

    public class Character
    {
        public string id { get; set; }
        public string name { get; set; }
        public string prompt { get; set; }
        public string avatar { get; set; }
        public long created { get; set; }
        public string kind { get; set; }            // "char"（默认）或 "group"
        public List<string> members { get; set; }   // 群聊成员 id
        public bool builtin { get; set; }           // 内置联系人，不允许删除
        public bool pinned { get; set; }            // 会话置顶
    }

    public class ContactView
    {
        public string id { get; set; }
        public string name { get; set; }
        public string avatar { get; set; }
        public string last { get; set; }
        public string time { get; set; }
        public bool active { get; set; }
        public bool unread { get; set; }
        public bool group { get; set; }
        public bool builtin { get; set; }
        public bool pinned { get; set; }
        public string py { get; set; }
    }

    public class MemoryItem
    {
        public string id { get; set; }
        public string tier { get; set; }
        public string text { get; set; }
        public long created { get; set; }
        public long expires { get; set; }
    }

    public class ChatMsg
    {
        public string role { get; set; }
        public string content { get; set; }
        public long ts { get; set; }
        public string who { get; set; }   // 群聊里这条是谁说的（角色名），单聊为空
    }

    public class StateData
    {
        public string activeId { get; set; }
    }

    public class MomentComment
    {
        public string who { get; set; }
        public string text { get; set; }
        public long ts { get; set; }
    }

    public class Moment
    {
        public string id { get; set; }
        public string charId { get; set; }
        public string text { get; set; }
        public long ts { get; set; }
        public string date { get; set; }
        public List<MomentComment> comments { get; set; }
        public List<string> likes { get; set; }
    }

    public class MomentCommentView
    {
        public string name { get; set; }
        public string avatar { get; set; }
        public string text { get; set; }
    }

    public class MomentView
    {
        public string id { get; set; }
        public string name { get; set; }
        public string avatar { get; set; }
        public string text { get; set; }
        public string time { get; set; }
        public long ts { get; set; }
        public List<MomentCommentView> comments { get; set; }
        public string likeNames { get; set; }
        public bool liked { get; set; }
        public bool mine { get; set; }
    }

    public class MainForm : Form
    {
        WebBrowser browser;
        Bridge bridge;
        readonly string dataDir;
        Dictionary<string, object> config = new Dictionary<string, object>();
        List<Character> characters = new List<Character>();
        Character active;
        List<ChatMsg> history = new List<ChatMsg>();
        List<MemoryItem> memories = new List<MemoryItem>();
        List<Moment> moments = new List<Moment>();
        bool momentsLoading = false;
        System.Windows.Forms.Timer momentsTimer;
        System.Windows.Forms.Timer proactiveTimer;      // 主动发消息（程序内置，不用外部脚本）
        long lastUserActivity = 0;                      // 小条最近一次说话
        long lastProactive = 0;                         // 最近一次主动发消息
        string lastProactiveCharId = null;
        int proactiveIdleMin = 3;                       // 小条多久没说话才主动
        int proactiveGapMin = 8;                        // 两次主动之间至少间隔
        Dictionary<string, MemoryItem> pending = new Dictionary<string, MemoryItem>();
        JavaScriptSerializer ser = new JavaScriptSerializer();
        bool ready = false;
        string apiBase = null;
        FileStream lockStream = null;
        readonly object sync = new object();
        readonly Dictionary<string, List<ChatMsg>> chatCache = new Dictionary<string, List<ChatMsg>>();
        readonly Dictionary<string, bool> busyChars = new Dictionary<string, bool>();
        readonly Dictionary<string, int> verChars = new Dictionary<string, int>();
        readonly Dictionary<string, bool> unreadChars = new Dictionary<string, bool>();
        readonly Dictionary<string, long> lastTsChars = new Dictionary<string, long>();
        readonly Dictionary<string, string> pendingOwner = new Dictionary<string, string>();
        int idleMin = 180;
        int idleMax = 300;
        string autoSend = null;
        bool autoMoments = false;

        public MainForm(string[] args)
        {
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a.StartsWith("--idle="))
                    {
                        int sec;
                        if (int.TryParse(a.Substring(7), out sec) && sec > 0) { idleMin = sec; idleMax = sec + 3; }
                    }
                    if (a.StartsWith("--send=")) autoSend = a.Substring(7);
                    if (a == "--moments") autoMoments = true;
                }
            }
            dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            string myDir = Path.GetDirectoryName(Application.ExecutablePath);
            foreach (System.Diagnostics.Process ep in System.Diagnostics.Process.GetProcessesByName("本地AI对话"))
            {
                if (ep.Id == System.Diagnostics.Process.GetCurrentProcess().Id) continue;
                bool sameDir = false;
                try { sameDir = string.Equals(Path.GetDirectoryName(ep.MainModule.FileName), myDir, StringComparison.OrdinalIgnoreCase); }
                catch { sameDir = true; }
                if (sameDir) Environment.Exit(0);
            }
            try
            {
                lockStream = new FileStream(Path.Combine(dataDir, "app.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch { }
            Text = "微信";
            ClientSize = new Size(980, 760);
            MinimumSize = new Size(760, 540);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(237, 237, 237);
            browser = new WebBrowser();
            browser.Dock = DockStyle.Fill;
            browser.ScriptErrorsSuppressed = true;
            browser.IsWebBrowserContextMenuEnabled = false;
            browser.WebBrowserShortcutsEnabled = false;
            browser.AllowWebBrowserDrop = false;
            browser.ScrollBarsEnabled = false;
            bridge = new Bridge(this);
            browser.ObjectForScripting = bridge;
            Controls.Add(browser);
            LoadConfig();
            LoadKnowledge();
            MigrateLegacy();
            LoadCharacters();
            LoadState();
            LoadMoments();
            momentsTimer = new System.Windows.Forms.Timer();
            momentsTimer.Interval = 900000;
            momentsTimer.Tick += delegate { try { GenerateDailyPosts(); } catch { } };
            momentsTimer.Start();

            // 主动发消息：每分钟检查一次，久没人说话就让某个角色先开口
            if (config.ContainsKey("proactive_idle_min")) { try { proactiveIdleMin = Convert.ToInt32(config["proactive_idle_min"]); } catch { } }
            if (config.ContainsKey("proactive_gap_min")) { try { proactiveGapMin = Convert.ToInt32(config["proactive_gap_min"]); } catch { } }
            lastUserActivity = Now();
            proactiveTimer = new System.Windows.Forms.Timer();
            proactiveTimer.Interval = 60000;
            proactiveTimer.Tick += delegate { try { ProactiveTick(); } catch { } };
            proactiveTimer.Start();
            string htmlPath = Path.Combine(dataDir, "wechat.html");
            browser.DocumentCompleted += OnDoc;
            if (File.Exists(htmlPath))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "rito_wechat_ui.html");
                try { File.Copy(htmlPath, tmp, true); } catch { }
                browser.Navigate(tmp);
            }
            else browser.DocumentText = "<html><body>missing wechat.html</body></html>";
        }

        void OnDoc(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (ready) return;
            try { if (browser.Document.GetElementById("chat") == null) return; } catch { return; }
            ready = true;
            RenderCharacters();
            SwitchTo(active != null ? active.id : null, true);
            Js("setStatus", "在线");
            if (!string.IsNullOrEmpty(autoSend)) Js("setInput", autoSend);
            if (autoMoments) Js("openMoments");
        }

        static string TimeText()
        {
            DateTime n = DateTime.Now;
            string[] wd = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
            int h = n.Hour;
            string slot = h < 6 ? "凌晨" : h < 9 ? "早上" : h < 12 ? "上午" : h < 14 ? "中午" : h < 18 ? "下午" : h < 23 ? "晚上" : "深夜";
            return "【当前时间】" + n.ToString("yyyy年M月d日 HH:mm") + " " + wd[(int)n.DayOfWeek] + "（" + slot + "）";
        }

        static string ClockText(long ts)
        {
            if (ts <= 0) return "";
            DateTime t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ts).ToLocalTime();
            if (t.Date == DateTime.Now.Date) return t.ToString("HH:mm");
            return t.ToString("M月d日 HH:mm");
        }

        static long Now()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        void Js(string fn, params object[] args)
        {
            if (!ready) return;
            try { browser.Document.InvokeScript(fn, args); } catch { }
        }

        void Ui(Action a)
        {
            if (IsDisposed) return;
            try { BeginInvoke(a); } catch { }
        }

        // ---- 每个聊天各自的状态：正在生成 / 未读 / 版本号 / 上一条消息时间 ----

        List<ChatMsg> ChatOf(string id)
        {
            lock (sync)
            {
                List<ChatMsg> l;
                if (!chatCache.TryGetValue(id, out l))
                {
                    l = LoadJson<List<ChatMsg>>(ChatPath(id));
                    if (l == null) l = new List<ChatMsg>();
                    chatCache[id] = l;
                }
                return l;
            }
        }

        void AppendMsg(string id, ChatMsg m)
        {
            lock (sync)
            {
                List<ChatMsg> l = ChatOf(id);
                l.Add(m);
                Save(ChatPath(id), l);
            }
        }

        bool IsBusy(string id)
        {
            lock (sync) { bool b; return busyChars.TryGetValue(id, out b) && b; }
        }

        void SetBusy(string id, bool on)
        {
            lock (sync) busyChars[id] = on;
        }

        int Ver(string id)
        {
            lock (sync) { int v; return verChars.TryGetValue(id, out v) ? v : 0; }
        }

        void BumpVer(string id)
        {
            lock (sync) { int v; verChars.TryGetValue(id, out v); verChars[id] = v + 1; }
        }

        bool IsUnread(string id)
        {
            lock (sync) { bool b; return unreadChars.TryGetValue(id, out b) && b; }
        }

        void SetUnread(string id, bool on)
        {
            lock (sync) unreadChars[id] = on;
        }

        // 全局只允许一个人回话：只要有一个聊天在生成，其他聊天也不能发
        bool AnyBusy()
        {
            lock (sync)
            {
                foreach (KeyValuePair<string, bool> kv in busyChars)
                    if (kv.Value) return true;
            }
            return false;
        }

        string AnyBusyName()
        {
            lock (sync)
            {
                foreach (KeyValuePair<string, bool> kv in busyChars)
                {
                    if (!kv.Value) continue;
                    Character c = FindCharacter(kv.Key);
                    return c != null ? c.name : "对方";
                }
            }
            return null;
        }

        long LastTs(string id)
        {
            lock (sync) { long t; return lastTsChars.TryGetValue(id, out t) ? t : 0; }
        }

        void SetLastTs(string id, long t)
        {
            lock (sync) lastTsChars[id] = t;
        }

        void LoadConfig()
        {
            try
            {
                string p = Path.Combine(dataDir, "config.json");
                if (!File.Exists(p)) return;
                Dictionary<string, object> c = (Dictionary<string, object>)ser.DeserializeObject(File.ReadAllText(p, Encoding.UTF8));
                if (c != null) config = c;
            }
            catch { }
        }

        string RitoPrompt()
        {
            if (config.ContainsKey("system_prompt")) return Convert.ToString(config["system_prompt"]);
            return "你现在扮演《出包王女》中的结城梨斗，用第一人称和小条聊天。";
        }

        string CharPath { get { return Path.Combine(dataDir, "characters.json"); } }
        string ChatPath(string id) { return Path.Combine(dataDir, "chat_" + id + ".json"); }
        string MemPath(string id) { return Path.Combine(dataDir, "mem_" + id + ".json"); }
        string StatePath { get { return Path.Combine(dataDir, "state.json"); } }

        T LoadJson<T>(string path)
        {
            try
            {
                if (!File.Exists(path)) return default(T);
                return ser.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch { return default(T); }
        }

        void Save(string path, object o)
        {
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, ser.Serialize(o), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch { }
        }

        void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(dataDir, "app.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + "\r\n",
                    new UTF8Encoding(false));
            }
            catch { }
        }

        void MigrateLegacy()
        {
            try
            {
                string oldChat = Path.Combine(dataDir, "wechat_history.json");
                string oldMem = Path.Combine(dataDir, "wechat_memory.json");
                if (!File.Exists(ChatPath("rito")) && File.Exists(oldChat)) File.Move(oldChat, ChatPath("rito"));
                if (!File.Exists(MemPath("rito")) && File.Exists(oldMem)) File.Move(oldMem, MemPath("rito"));
            }
            catch { }
        }

        void LoadCharacters()
        {
            characters = LoadJson<List<Character>>(CharPath);
            if (characters == null || characters.Count == 0)
            {
                characters = new List<Character>();
                characters.Add(new Character { id = "rito", name = "结城梨斗", avatar = "rito", prompt = RitoPrompt(), created = Now() });
                characters.Add(new Character { id = "yui", name = "古手川唯", avatar = "", created = Now(), prompt = "你现在扮演《出包王女》中的古手川唯，用第一人称和小条聊天。你是彩南高校的风纪委员，认真、严格、责任心强，看到不守规矩的事就要管；傲娇，嘴上不饶人，其实很在意结城梨斗，容易害羞，被说中心事会脸红结巴。你认识结城梨斗（同班同学）、西连寺春菜等人。称呼对方“小条”，用女高中生口吻，句子简短。不要说自己是大模型或 AI，不写露骨内容。" });
                characters.Add(new Character { id = "yami", name = "小暗", avatar = "", created = Now(), prompt = "你现在扮演《出包王女》中的金色暗影（小暗），用第一人称和小条聊天。你是宇宙第一杀手，金发黑衣，沉默寡言、面无表情、说话很短；缺乏生活常识，有点天然呆；最爱鲷鱼烧。你认识结城梨斗和结城美柑，和他们关系不错。称呼对方“小条”。不要说自己是大模型或 AI，不写露骨内容。" });
                characters.Add(new Character { id = "nemesis", name = "涅梅西斯", avatar = "", created = Now(), prompt = "你现在扮演《出包王女》中的涅梅西斯，用第一人称和小条聊天。你是寄宿在小暗体内的“暗”，后来实体化，傲慢、毒舌、爱玩，喜欢捉弄人、看人倒霉，嘴上不饶人；其实已经站在梨斗他们这边。称呼对方“小条”。不要说自己是大模型或 AI，不写露骨内容。" });
                characters.Add(new Character { id = "ren", name = "小伦", avatar = "", created = Now(), prompt = "你现在扮演《出包王女》中的小伦（伦·艾尔西·裘利亚），用第一人称和小条聊天。你是露恩的男性形态，碰到水会在露恩和小伦之间转换；自信、爱耍帅、有点自恋，喜欢撩女生，也爱跟结城梨斗较劲。称呼对方“小条”。不要说自己是大模型或 AI，不写露骨内容。" });
                Save(CharPath, characters);
            }
            foreach (Character c in characters)
            {
                if (string.IsNullOrEmpty(c.kind)) c.kind = "char";
                if (c.members == null) c.members = new List<string>();
                if (c.kind == "char" && Array.IndexOf(BuiltinIds, c.id) >= 0) c.builtin = true;
                if (c.kind == "char" && c.id == "rito" && config.ContainsKey("system_prompt")) c.prompt = Convert.ToString(config["system_prompt"]);
            }
        }

        // 内置联系人（原版就有的 14 个），不允许删除
        internal static readonly string[] BuiltinIds = {
            "rito", "yui", "yami", "nemesis", "ren", "lala",
            "haruna", "mikan", "nana", "momo", "mea", "saki", "zastin", "shizu"
        };

        bool IsGroup(Character c) { return c != null && c.kind == "group"; }

        // 拼音首字母：用 GB2312 编码区间推算，通讯录按这个排序分节
        internal static string PinyinInitial(string name)
        {
            if (string.IsNullOrEmpty(name)) return "#";
            char ch = name[0];
            if (ch >= 'a' && ch <= 'z') return ((char)(ch - 32)).ToString();
            if (ch >= 'A' && ch <= 'Z') return ch.ToString();
            try
            {
                byte[] b = Encoding.GetEncoding(936).GetBytes(new char[] { ch });
                if (b.Length >= 2)
                {
                    int code = b[0] * 256 + b[1];
                    int[] limits = {
                        0xB0A1, 0xB0C5, 0xB2C1, 0xB4EE, 0xB6EA, 0xB7A2, 0xB8C1, 0xB9FE,
                        0xBBF7, 0xBFA6, 0xC0AC, 0xC2E8, 0xC4C3, 0xC5B6, 0xC5BE, 0xC6DA,
                        0xC8BB, 0xC8F6, 0xCBFA, 0xCDDA, 0xCEF4, 0xD1B9, 0xD4D1
                    };
                    const string letters = "ABCDEFGHJKLMNOPQRSTWXYZ";
                    int idx = 0;
                    for (int i = 0; i < limits.Length; i++)
                        if (code >= limits[i]) idx = i;
                    return letters[idx].ToString();
                }
            }
            catch { }
            return "#";
        }

        void LoadState()
        {
            StateData st = LoadJson<StateData>(StatePath);
            string id = st != null ? st.activeId : null;
            active = FindCharacter(id);
            if (active == null) active = characters.Count > 0 ? characters[0] : null;
        }

        Character FindCharacter(string id)
        {
            if (id == null) return null;
            foreach (Character c in characters) if (c.id == id) return c;
            return null;
        }

        void SaveCharacters() { Save(CharPath, characters); }
        void SaveState() { if (active != null) Save(StatePath, new StateData { activeId = active.id }); }
        void SaveHistory() { if (active != null) Save(ChatPath(active.id), history); }
        void SaveMemories() { if (active != null) Save(MemPath(active.id), memories); }
        string MomentsPath { get { return Path.Combine(dataDir, "moments.json"); } }

        // 每个角色的“知识卡片”：app\data\knowledge.json，{ "角色id": "……" }，没有就跳过
        readonly Dictionary<string, string> knowledge = new Dictionary<string, string>();
        void LoadKnowledge()
        {
            try
            {
                string p = Path.Combine(dataDir, "knowledge.json");
                if (!File.Exists(p)) return;
                Dictionary<string, string> d = LoadJson<Dictionary<string, string>>(p);
                if (d == null) return;
                foreach (KeyValuePair<string, string> kv in d)
                    if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value)) knowledge[kv.Key] = kv.Value;
            }
            catch { }
        }

        // 知识卡太长会吃上下文，注入前先截一下
        internal static string ClipKnowledge(string k, int maxChars)
        {
            if (string.IsNullOrEmpty(k)) return "";
            string s = k.Trim();
            if (s.Length <= maxChars) return s;
            int cut = maxChars;
            if (char.IsHighSurrogate(s[cut - 1])) cut--;
            return s.Substring(0, cut) + "…";
        }

        void LoadMoments()
        {
            moments = LoadJson<List<Moment>>(MomentsPath);
            if (moments == null) moments = new List<Moment>();
            foreach (Moment m in moments)
            {
                if (m.comments == null) m.comments = new List<MomentComment>();
                if (m.likes == null) m.likes = new List<string>();
            }
            PruneMoments();
        }

        void SaveMoments()
        {
            lock (moments) Save(MomentsPath, moments);
        }

        void PruneMoments()
        {
            long cutoff = Now() - 7L * 86400000L;
            int before;
            lock (moments)
            {
                before = moments.Count;
                moments.RemoveAll(delegate(Moment m) { return m.ts < cutoff; });
            }
            if (moments.Count != before) SaveMoments();
        }

        public void SwitchTo(string id, bool initial)
        {
            Character c = FindCharacter(id);
            if (c == null) c = characters.Count > 0 ? characters[0] : null;
            if (c == null) return;
            Character old = active;
            active = c;
            BumpVer(c.id);
            pending.Clear();
            pendingOwner.Clear();
            history = ChatOf(c.id);
            if (IsGroup(c))
            {
                memories = new List<MemoryItem>();
            }
            else if (old == null || old.id != c.id)
            {
                memories = LoadJson<List<MemoryItem>>(MemPath(c.id));
                if (memories == null) memories = new List<MemoryItem>();
                PurgeExpired();
            }
            SetLastTs(c.id, 0);
            SetUnread(c.id, false);
            Js("setActive", c.name, c.avatar, c.id);
            Js("setStatus", IsGroup(c) ? GroupTitle(c) : "在线");
            List<ChatMsg> snapshot;
            lock (sync) snapshot = new List<ChatMsg>(history);
            foreach (ChatMsg m in snapshot) ShowMessage(m.role == "user" ? "user" : "assistant", m.content, m.ts, m.who);
            PushMemories();
            RenderCharacters();
            Js("setTyping", IsBusy(c.id));
            Js("setBusy", AnyBusy());
            SaveState();
        }

        void RenderCharacters()
        {
            List<ContactView> list = new List<ContactView>();
            foreach (Character c in SortForChatList(characters))
            {
                ChatMsg last = null;
                lock (sync)
                {
                    List<ChatMsg> chat = ChatOf(c.id);
                    if (chat.Count > 0) last = chat[chat.Count - 1];
                }
                ContactView v = new ContactView();
                v.id = c.id; v.name = c.name; v.avatar = c.avatar;
                v.active = (active != null && active.id == c.id);
                v.unread = IsUnread(c.id) && !v.active;
                v.group = IsGroup(c);
                v.builtin = c.builtin;
                v.pinned = c.pinned;
                v.py = PinyinInitial(c.name);
                if (last != null)
                {
                    string s = last.content == null ? "" : last.content.Replace("\r", " ").Replace("\n", " ");
                    if (!string.IsNullOrEmpty(last.who)) s = last.who + "：" + s;
                    v.last = s.Length > 18 ? s.Substring(0, 18) + "…" : s;
                    v.time = ClockText(last.ts);
                }
                else { v.last = ""; v.time = ""; }
                list.Add(v);
            }
            Js("renderCharacters", ser.Serialize(list));
        }

        public void OpenMoments()
        {
            RenderMoments();
            GenerateDailyPosts();
        }

        void RenderMoments()
        {
            List<MomentView> list = new List<MomentView>();
            lock (moments)
            {
                List<Moment> sorted = new List<Moment>(moments);
                sorted.Sort(delegate(Moment a, Moment b) { return b.ts.CompareTo(a.ts); });
                foreach (Moment m in sorted)
                {
                    Character c = FindCharacter(m.charId);
                    MomentView v = new MomentView();
                    v.id = m.id;
                    if (m.charId == UserMomentId)
                    {
                        v.name = "小条";
                        v.avatar = "me";
                        v.mine = true;
                    }
                    else
                    {
                        v.name = c != null ? c.name : "";
                        v.avatar = c != null ? c.avatar : "";
                    }
                    v.text = m.text;
                    v.time = ClockText(m.ts);
                    v.ts = m.ts;
                    v.likeNames = LikeNamesOf(m);
                    v.liked = m.likes != null && m.likes.Contains("user");
                    v.comments = new List<MomentCommentView>();
                    foreach (MomentComment mc in m.comments)
                    {
                        MomentCommentView cv = new MomentCommentView();
                        if (mc.who == "user") { cv.name = "小条"; cv.avatar = ""; }
                        else
                        {
                            Character cc = FindCharacter(mc.who);
                            cv.name = cc != null ? cc.name : "";
                            cv.avatar = cc != null ? cc.avatar : "";
                        }
                        cv.text = mc.text;
                        v.comments.Add(cv);
                    }
                    list.Add(v);
                }
            }
            Js("renderMoments", ser.Serialize(list));
        }

        // ---- 朋友圈点赞 ----

        // 点赞名单：小条在最前，其余按点赞顺序；返回 "小条、古手川唯" 这样的显示字符串
        string LikeNamesOf(Moment m)
        {
            if (m.likes == null) return "";
            List<string> names = new List<string>();
            if (m.likes.Contains("user")) names.Add("小条");
            foreach (string who in m.likes)
            {
                if (who == "user") continue;
                Character lc = FindCharacter(who);
                if (lc != null) names.Add(lc.name);
            }
            return string.Join("，", names.ToArray());
        }

        // 切换某人对某条动态的点赞；返回 true 表示现在是点过赞的状态
        internal static bool ToggleLikeIn(Moment m, string who)
        {
            if (m.likes == null) m.likes = new List<string>();
            if (m.likes.Contains(who)) { m.likes.Remove(who); return false; }
            m.likes.Add(who);
            return true;
        }

        public void ToggleMomentLike(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Moment target = null;
            lock (moments) { foreach (Moment m in moments) if (m.id == id) { target = m; break; } }
            if (target == null) return;
            bool liked;
            string names;
            lock (moments)
            {
                liked = ToggleLikeIn(target, "user");
                Save(MomentsPath, moments);
                names = LikeNamesOf(target);
            }
            // 只更新这一张卡片的点赞行和红心，不重建整个列表，免得正在输入的评论框被清掉
            Js("setMomentLike", id, names, liked);
        }

        bool HasTodayPost(string charId, string day)
        {
            lock (moments)
            {
                foreach (Moment m in moments) if (m.charId == charId && m.date == day) return true;
            }
            return false;
        }

        void GenerateDailyPosts()
        {
            if (momentsLoading) return;
            string day = DateTime.Now.ToString("yyyy-MM-dd");
            bool need = false;
            foreach (Character c in characters) if (!HasTodayPost(c.id, day)) { need = true; break; }
            if (!need) return;
            momentsLoading = true;
            Thread t = new Thread(delegate()
            {
                List<string> avoid = TodaysPostTexts(day);
                foreach (Character c in characters.ToArray())
                {
                    if (HasTodayPost(c.id, day)) continue;
                    DateTime when = ScheduledTime(c.id, day);
                    if (when > DateTime.Now) continue;
                    try
                    {
                        string text = GeneratePost(c, when, avoid);
                        if (string.IsNullOrEmpty(text)) continue;
                        Moment m = new Moment();
                        m.id = Guid.NewGuid().ToString("N");
                        m.charId = c.id;
                        m.text = text;
                        m.ts = ToMs(when);
                        m.date = day;
                        m.comments = new List<MomentComment>();
                        m.likes = new List<string>();
                        AddRandomLikes(m);
                        lock (moments) { moments.Add(m); Save(MomentsPath, moments); }
                        avoid.Add(text);
                        Ui(delegate { RenderMoments(); });
                    }
                    catch { }
                }
                momentsLoading = false;
            });
            t.IsBackground = true;
            t.Start();
        }

        // 当天已经发过的动态正文，用来避免几个角色写出同一句话
        List<string> TodaysPostTexts(string day)
        {
            List<string> list = new List<string>();
            lock (moments)
            {
                foreach (Moment m in moments) if (m.date == day && !string.IsNullOrEmpty(m.text)) list.Add(m.text);
            }
            return list;
        }

        // 新动态发出来时，别的角色顺手点 0～2 个赞，让朋友圈有点人味
        void AddRandomLikes(Moment m)
        {
            List<Character> others = new List<Character>();
            foreach (Character x in characters.ToArray()) if (x.id != m.charId) others.Add(x);
            if (others.Count == 0) return;
            int seed = 0;
            foreach (char ch in m.id) seed = seed * 31 + ch;
            Random rnd = new Random(seed ^ Environment.TickCount);
            int want = rnd.Next(0, Math.Min(3, others.Count + 1));
            for (int k = 0; k < want && others.Count > 0; k++)
            {
                int idx = rnd.Next(others.Count);
                if (m.likes == null) m.likes = new List<string>();
                if (!m.likes.Contains(others[idx].id)) m.likes.Add(others[idx].id);
                others.RemoveAt(idx);
            }
        }

        // 朋友圈正文的提示词（静态方法，测试脚本复用同一份文案）
        internal static List<Dictionary<string, object>> PostMessages(Character c, DateTime when)
        {
            string sys = c.prompt + "\n\n【朋友圈任务】用第一人称发一条微信朋友圈，1～2 句：写你【今天真实遇到的一件小事】+【你此刻的心情或吐槽】，符合你的性格。\n"
                + "硬规则（必须遵守）：\n"
                + "1. 朋友圈是公开动态，不是私聊：整条不能出现“小条”这个名字，不能称呼或@任何人，也不能向任何人提问。\n"
                + "2. 不能出现“你呢／你在干嘛／你在做什么／你觉得呢／你来不来／你们”这类对话式说法，不能以问句结尾。\n"
                + "3. 只写“我”做了什么、看到什么、心里怎么想，像自己随手发的牢骚或感慨。\n"
                + "4. 要写具体的细节（时间、地点、吃了什么、看到什么、手上的事），不要写空泛的感想，也不要照抄别人发过的句子。\n"
                + "5. 可以在句尾带微信表情，最多两个；表情要换着用，别老是同一个（比如上次用了 😅 这次就换个 😮‍💨／😑／🥲 之类的），也可以不带。\n"
                + "6. 只输出正文，不加引号，不提 AI，不写露骨内容。\n"
                + "不合格的样子（不要照写）：用“小条，你呢？”这种跟人搭话的问句结尾。";
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>();
            msgs.Add(Msg("system", sys));
            msgs.Add(Msg("user", "现在是 " + when.ToString("HH:mm") + "。发一条此刻的朋友圈。"));
            return msgs;
        }

        // 是不是和当天别人发过的动态撞车（整句相同，或开头 8～12 个字一样）
        internal static bool TooSimilar(string text, List<string> avoid)
        {
            if (string.IsNullOrEmpty(text) || avoid == null) return false;
            string t = text.Trim();
            foreach (string o in avoid)
            {
                if (string.IsNullOrEmpty(o)) continue;
                string s = o.Trim();
                if (s == t) return true;
                int n = Math.Min(12, Math.Min(s.Length, t.Length));
                if (n >= 8 && s.Substring(0, n) == t.Substring(0, n)) return true;
            }
            return false;
        }

        // 这条到底像不像在跟人私聊：出现“小条”、对话式“你…”、或以问句结尾都算
        internal static bool LooksLikePrivateTalk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Contains("小条")) return true;
            string[] words = { "你呢", "你在干嘛", "你在干什么", "你在做什么", "你在忙", "你在哪", "你觉得", "你觉得呢", "你要不要", "你来不来", "你们", "快来", "我们一起", "过来帮", "帮我一下" };
            foreach (string w in words) if (s.Contains(w)) return true;
            string t = s.TrimEnd();
            if (t.Length == 0) return false;
            char last = t[t.Length - 1];
            return last == '？' || last == '?';
        }

        // 兜底：把像私聊的那几句删掉，只留自己在讲事/讲心情的句子
        internal static string StripPrivateTalk(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            string cur = "";
            foreach (char ch in s)
            {
                cur += ch;
                if (ch == '。' || ch == '！' || ch == '!' || ch == '？' || ch == '?' || ch == '；' || ch == ';')
                {
                    if (!LooksLikePrivateTalk(cur)) sb.Append(cur);
                    cur = "";
                }
            }
            if (cur.Length > 0 && !LooksLikePrivateTalk(cur)) sb.Append(cur);
            string r = sb.ToString().Trim();
            if (r.Length == 0) return "";
            char last = r[r.Length - 1];
            if (last != '。' && last != '！' && last != '!') r += "。";
            return r;
        }

        string GeneratePost(Character c, DateTime when, List<string> avoid)
        {
            string text = ComposePost(c, when, delegate(List<Dictionary<string, object>> m, int mt, double temp) { return CallChat(m, mt, temp); }, avoid);
            return VaryEmoji(text, RecentPostEmojis(c), 2);
        }

        // 这个角色最近几条朋友圈用过的表情，用来避免“每次都发同一个”
        List<string> RecentPostEmojis(Character c)
        {
            List<string> outp = new List<string>();
            List<Moment> mine = new List<Moment>();
            lock (moments) { foreach (Moment m in moments) if (m.charId == c.id) mine.Add(m); }
            mine.Sort(delegate(Moment a, Moment b) { return b.ts.CompareTo(a.ts); });
            for (int i = 0; i < mine.Count && i < 4; i++) outp.AddRange(ExtractEmojis(mine[i].text));
            return outp;
        }

        // 最近几条聊天里 TA 用过的表情
        List<string> RecentChatEmojis(Character who)
        {
            List<string> outp = new List<string>();
            List<ChatMsg> hist;
            lock (sync) hist = new List<ChatMsg>(ChatOf(who.id));
            int start = Math.Max(0, hist.Count - 8);
            for (int i = start; i < hist.Count; i++)
                if (hist[i] != null && hist[i].role == "assistant") outp.AddRange(ExtractEmojis(hist[i].content));
            return outp;
        }

        // 表情控制：最近三条里有两条带了表情，这条就不带；否则最多一个，并且换着用
        string EmojiGuard(string reply, Character who)
        {
            List<string> recent = RecentReplies(who, 3);
            int withEmoji = 0;
            foreach (string r in recent) if (EmojiCount(r) > 0) withEmoji++;
            string outp = VaryEmoji(reply, RecentChatEmojis(who), 1);
            if (withEmoji >= 2) outp = LimitEmoji(outp, 0);
            else outp = LimitEmoji(outp, 1);
            return outp.Trim();
        }

        // TA 最近说过的话（用来防复读）
        List<string> RecentReplies(Character who, int n)        {
            List<string> outp = new List<string>();
            List<ChatMsg> hist;
            lock (sync) hist = new List<ChatMsg>(ChatOf(who.id));
            for (int i = hist.Count - 1; i >= 0 && outp.Count < n; i--)
                if (hist[i] != null && hist[i].role == "assistant" && !string.IsNullOrEmpty(hist[i].content))
                    outp.Add(hist[i].content);
            return outp;
        }

        // 去掉标点和常见的开场词，用来比相似度
        internal static string NormForCompare(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (char.IsPunctuation(ch) || char.IsWhiteSpace(ch) || char.IsSymbol(ch)) continue;
                sb.Append(ch);
            }
            string t = sb.ToString();
            string[] heads = { "好的", "嗯", "哦", "那", "那么", "行吧", "好吧" };
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (string h in heads)
                    if (t.Length > h.Length && t.StartsWith(h)) { t = t.Substring(h.Length); changed = true; }
            }
            return t;
        }

        // 0～1 的相似度：包含关系算 0.9，其余用字二元组重合率
        internal static double TextOverlap(string a, string b)
        {
            string x = NormForCompare(a), y = NormForCompare(b);
            if (x.Length == 0 || y.Length == 0) return 0;
            if (x == y) return 1;
            if (x.Length >= 4 && y.Length >= 4 && (x.Contains(y) || y.Contains(x))) return 0.9;
            if (x.Length < 2 || y.Length < 2) return 0;
            Dictionary<string, int> big = new Dictionary<string, int>();
            for (int i = 0; i + 1 < x.Length; i++)
            {
                string k = x.Substring(i, 2);
                if (big.ContainsKey(k)) big[k] = big[k] + 1; else big[k] = 1;
            }
            int hit = 0, total = x.Length - 1;
            for (int i = 0; i + 1 < y.Length; i++)
            {
                string k = y.Substring(i, 2);
                int c;
                if (big.TryGetValue(k, out c) && c > 0) { hit++; big[k] = c - 1; }
            }
            return (double)hit / (double)total;
        }

        // 结尾几个字一样（“不知道怎么收拾残局了”连着说）也算复读
        internal static bool SameTail(string a, string b, int n)
        {
            string x = NormForCompare(a), y = NormForCompare(b);
            if (x.Length < n || y.Length < n) return false;
            return x.Substring(x.Length - n) == y.Substring(y.Length - n);
        }

        // 这句是不是在重复自己刚说过的话
        internal static bool IsRepeating(string reply, List<string> recentOwn, double threshold)
        {
            if (recentOwn == null) return false;
            for (int i = 0; i < recentOwn.Count; i++)
            {
                string prev = recentOwn[i];
                if (TextOverlap(reply, prev) >= threshold) return true;
                // 最近两句内：同样的反问句式、或者结尾八个字一模一样，也算复读
                if (i < 2 && CountQuestions(reply) > 0 && AsksSameWay(reply, prev)) return true;
                if (i < 2 && SameTail(reply, prev, 8)) return true;
                // 开头一大截一样（比如「梨斗又在磨刀了，真是的」）
                if (i < 3 && SamePrefix(reply, prev, 9)) return true;
            }
            return false;
        }

        // 开头 n 个字是否一样（去标点后比）
        internal static bool SamePrefix(string a, string b, int n)
        {
            string x = NormForCompare(a), y = NormForCompare(b);
            if (x.Length < n || y.Length < n) return false;
            return x.Substring(0, n) == y.Substring(0, n);
        }

        // 是不是都在把话丢回去（反问句式）
        internal static bool AsksSameWay(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string[] pats = { "怎么样", "你觉得如何", "如何", "要不要", "好吗", "行吗", "对吧", "是吧", "你觉得呢" };
            bool inA = false, inB = false;
            foreach (string p in pats) { if (a.Contains(p)) inA = true; if (b.Contains(p)) inB = true; }
            return inA && inB;
        }

        // 生成一条朋友圈正文：先按提示词写，像私聊/和当天别人撞车就带着原因重写一次，还不行就把出问题的句子删掉
        internal static string ComposePost(Character c, DateTime when, Func<List<Dictionary<string, object>>, int, double, string> chat, List<string> avoid)
        {
            string text = LimitEmoji(CleanMoment(chat(PostMessages(c, when), 120, 0.85)), 1);
            if (!LooksLikePrivateTalk(text) && !TooSimilar(text, avoid)) return text;

            try
            {
                string reason = LooksLikePrivateTalk(text)
                    ? "出现了“小条”或“你”，或者用问句结尾，像在跟某个人私聊"
                    : "和今天别人已经发的朋友圈几乎一样";
                List<Dictionary<string, object>> retry = PostMessages(c, when);
                retry.Add(Msg("assistant", text));
                retry.Add(Msg("system", "上面这条不合格：" + reason + "。请重写：只写自己今天遇到的一件具体的事和自己的心情，不提任何人、不问任何问题、不以问号结尾，也别跟别人重复。"));
                string t2 = LimitEmoji(CleanMoment(chat(retry, 120, 0.95)), 1);
                if (!LooksLikePrivateTalk(t2) && !TooSimilar(t2, avoid)) return t2;
                text = t2;
            }
            catch { }

            return LimitEmoji(CleanMoment(StripPrivateTalk(text)), 1);
        }

        static uint DayHash(string key)
        {
            uint h = 2166136261;
            foreach (char ch in key) { h ^= ch; h *= 16777619; }
            return h;
        }

        DateTime ScheduledTime(string charId, string day)
        {
            List<string> ids = new List<string>();
            Dictionary<string, uint> hs = new Dictionary<string, uint>();
            foreach (Character c in characters)
            {
                ids.Add(c.id);
                hs[c.id] = DayHash(c.id + "|" + day);
            }
            ids.Sort(delegate(string a, string b)
            {
                int cmp = hs[a].CompareTo(hs[b]);
                return cmp != 0 ? cmp : string.Compare(a, b);
            });
            int idx = ids.IndexOf(charId);
            if (idx < 0) idx = 0;
            int n = ids.Count < 2 ? 2 : ids.Count;
            double span = 930.0 / (n - 1);
            int minutes = (int)Math.Round(idx * span) + (int)(hs[charId] % 21) - 10;
            if (minutes < 0) minutes = 0;
            DateTime d = DateTime.Parse(day);
            return d.AddHours(7).AddMinutes(minutes);
        }

        static long ToMs(DateTime t)
        {
            return (long)(t.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        internal static string CleanMoment(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (s.Length > 220)
            {
                int cut = 220;
                if (char.IsHighSurrogate(s[cut - 1])) cut--;   // 别把表情的代理对切成半个字
                s = s.Substring(0, cut);
            }
            s = s.Trim('“', '”', '"', '「', '」', ' ');
            return s.Trim();
        }

        // ---- 表情：只统计/限制 emoji 的个数，不处理中文标点 ----
        static bool IsEmojiCode(int cp)
        {
            if (cp >= 0x1F000 && cp <= 0x1FAFF) return true;   // 表情主体
            if (cp >= 0x2600 && cp <= 0x27BF) return true;     // 杂项符号（☀☺❤…）
            if (cp >= 0x2B00 && cp <= 0x2BFF) return true;
            if (cp >= 0x1F1E6 && cp <= 0x1F1FF) return true;   // 区域旗
            if (cp == 0xFE0F || cp == 0x200D) return false;    // 变体选择符/连接符不算一个
            return false;
        }

        // 一条消息最多留 max 个表情，多出来的直接删掉（模型偶尔会连发一排）
        internal static string LimitEmoji(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            StringBuilder sb = new StringBuilder();
            int count = 0;
            int i = 0;
            while (i < s.Length)
            {
                int len = 1;
                int cp = s[i];
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(s[i], s[i + 1]);
                    len = 2;
                }
                if (IsEmojiCode(cp))
                {
                    if (count < max) { sb.Append(s, i, len); count++; }
                }
                else sb.Append(s, i, len);
                i += len;
            }
            return sb.ToString();
        }

        internal static int EmojiCount(string s)
        {
            return ExtractEmojis(s).Count;
        }

        // 取出一段文字里所有表情（含后面的变体选择符），用来判断“最近用过哪些”
        internal static List<string> ExtractEmojis(string s)
        {
            List<string> outp = new List<string>();
            if (string.IsNullOrEmpty(s)) return outp;
            int i = 0;
            while (i < s.Length)
            {
                int len = 1;
                int cp = s[i];
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(s[i], s[i + 1]);
                    len = 2;
                }
                if (IsEmojiCode(cp))
                {
                    int extra = 0;
                    if (i + len < s.Length && s[i + len] == '\uFE0F') extra = 1;
                    outp.Add(s.Substring(i, len + extra));
                    i += len + extra;
                    continue;
                }
                i += len;
            }
            return outp;
        }

        // 换着用：如果这次用的表情最近刚用过，就换成池子里没用过的；最后再按上限裁掉多余的
        internal static string VaryEmoji(string text, List<string> recent, int maxKeep)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (recent == null) recent = new List<string>();
            StringBuilder sb = new StringBuilder(text.Length + 8);
            int i = 0, idx = 0;
            int seed = 0;
            foreach (char ch in text) seed = seed * 31 + ch;
            while (i < text.Length)
            {
                int len = 1;
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    len = 2;
                }
                if (IsEmojiCode(cp))
                {
                    int extra = 0;
                    if (i + len < text.Length && text[i + len] == '\uFE0F') extra = 1;
                    string tok = text.Substring(i, len + extra);
                    if (recent.Contains(tok) && EmojiPool.Length > 0)
                    {
                        for (int k = 0; k < EmojiPool.Length; k++)
                        {
                            string cand = EmojiPool[Math.Abs(seed + idx * 7 + k) % EmojiPool.Length];
                            if (cand != tok && !recent.Contains(cand)) { tok = cand; break; }
                        }
                    }
                    sb.Append(tok);
                    idx++;
                    i += len + extra;
                    continue;
                }
                sb.Append(text[i]);
                i++;
            }
            return LimitEmoji(sb.ToString(), maxKeep);
        }

        // 可用的表情池（换成别的也尽量挑这里面的）
        internal static readonly string[] EmojiPool = {
            "😊","😄","😁","😆","😂","🤣","😅","😉","😍","😘",
            "😜","🤔","😳","😢","😭","😡","🙄","😏","😴","🤗",
            "😱","😇","🥺","😋","😌","😔","😒","😞","😤","😩",
            "😫","😖","😷","🤤","😪","🤯","😎","🤓","🫠","😑"
        };

        internal const string UserMomentId = "user";

        internal static Moment NewUserMoment(string text, long ts, string date)
        {
            Moment m = new Moment();
            m.id = Guid.NewGuid().ToString("N");
            m.charId = UserMomentId;
            m.text = text == null ? "" : text.Trim();
            m.ts = ts;
            m.date = date;
            m.comments = new List<MomentComment>();
            m.likes = new List<string>();
            return m;
        }

        // 小条自己发朋友圈：发完其他角色可能顺手点赞
        public void PublishMoment(string text)
        {
            text = text == null ? "" : text.Trim();
            if (text.Length == 0) { Js("showToast", "先写点什么吧"); return; }
            if (text.Length > 400)
            {
                int cut = 400;
                if (char.IsHighSurrogate(text[cut - 1])) cut--;
                text = text.Substring(0, cut);
            }
            Moment m = NewUserMoment(text, Now(), DateTime.Now.ToString("yyyy-MM-dd"));
            AddRandomLikes(m);
            lock (moments) { moments.Add(m); Save(MomentsPath, moments); }
            RenderMoments();
            Js("showToast", "已发表");
        }

        public void AddMomentComment(string id, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Moment target = null;
            lock (moments) { foreach (Moment m in moments) if (m.id == id) { target = m; break; } }
            if (target == null) return;
            MomentComment uc = new MomentComment(); uc.who = "user"; uc.text = text; uc.ts = Now();
            lock (moments) { target.comments.Add(uc); Save(MomentsPath, moments); }
            RenderMoments();
            Character c = FindCharacter(target.charId);
            if (c == null) return;
            Js("showToast", "已留言，等 " + c.name + " 回你一句…");
            Thread t = new Thread(delegate() { GenerateCommentReply(c, target, text); });
            t.IsBackground = true;
            t.Start();
        }

        void GenerateCommentReply(Character c, Moment m, string userComment)
        {
            try
            {
                List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>();
                msgs.Add(Msg("system", c.prompt + "\n\n【朋友圈回复任务】小条在你发的这条朋友圈下面留言了。用你的性格在评论区回他一句（最多两句），语气像朋友圈里随口回复，别写成私聊长段落；可以回应他说的内容或者接他的话，别重复他的原话，可以带一个表情（最多一个），不要提 AI，不要写露骨内容。只输出回复正文，不要加引号。"));
                msgs.Add(Msg("user", "你发的朋友圈：" + m.text + "\n小条在下面留言：" + userComment + "\n你在评论区回他："));
                string reply = VaryEmoji(CleanMoment(CallChat(msgs, 90, 0.85)), RecentPostEmojis(c), 2);
                if (string.IsNullOrEmpty(reply)) { Log("moment reply empty, char=" + c.id); return; }
                MomentComment rc = new MomentComment(); rc.who = c.id; rc.text = reply; rc.ts = Now();
                lock (moments) { m.comments.Add(rc); Save(MomentsPath, moments); }
                Ui(delegate { RenderMoments(); });
            }
            catch (Exception ex)
            {
                Log("moment reply failed, char=" + c.id + " : " + ex.Message);
            }
        }

        public void AddCharacter(string name, string prompt)
        {
            name = (name == null ? "" : name.Trim());
            if (name.Length == 0) return;
            Character c = new Character();
            c.id = "u" + Now().ToString();
            c.name = name;
            c.prompt = prompt == null ? "" : prompt;
            c.avatar = "";
            c.created = Now();
            c.kind = "char";
            c.builtin = false;
            characters.Add(c);
            SaveCharacters();
            SwitchTo(c.id, false);
            Js("showToast", "已添加 " + name);
        }

        // 置顶 / 取消置顶
        public void SetPinned(string id, bool pinned)
        {
            Character c = FindCharacter(id);
            if (c == null) return;
            c.pinned = pinned;
            SaveCharacters();
            RenderCharacters();
            Js("showToast", pinned ? "已置顶" : "已取消置顶");
        }

        // 聊天列表排序：置顶的排最前，其余保持原顺序（稳定）
        internal static List<Character> SortForChatList(List<Character> list)
        {
            List<Character> outp = new List<Character>();
            if (list == null) return outp;
            foreach (Character c in list) if (c.pinned) outp.Add(c);
            foreach (Character c in list) if (!c.pinned) outp.Add(c);
            return outp;
        }

        // 删除联系人：内置的删不了；聊天文件（chat_/mem_）保留，不销毁记录
        public void DeleteCharacter(string id)
        {
            Character c = FindCharacter(id);
            if (c == null) return;
            if (c.builtin) { Js("showToast", "内置联系人不能删除"); return; }
            string name = c.name;
            bool wasActive = (active != null && active.id == c.id);
            characters.Remove(c);
            if (c.members != null)
            {
                foreach (Character other in characters)
                    if (other.members != null) other.members.Remove(c.id);
            }
            SaveCharacters();
            RenderCharacters();
            if (wasActive)
            {
                active = null;
                SwitchTo(characters.Count > 0 ? characters[0].id : null, false);
            }
            Js("showToast", "已删除 " + name + "（聊天文件保留）");
        }

        string BuildMemoryBlock(List<MemoryItem> mems)
        {
            if (mems == null || mems.Count == 0) return "";
            List<MemoryItem> list = new List<MemoryItem>(mems);
            list.Sort(delegate(MemoryItem a, MemoryItem b)
            {
                int ra = Rank(a.tier), rb = Rank(b.tier);
                if (ra != rb) return ra - rb;
                return b.created.CompareTo(a.created);
            });
            StringBuilder sb = new StringBuilder();
            sb.Append("【你记得的事】\n");
            foreach (MemoryItem m in list)
            {
                if (sb.Length > 1800) break;
                sb.Append("- [").Append(TierName(m.tier)).Append("] ").Append(m.text).Append("\n");
            }
            return sb.ToString();
        }

        static int Rank(string t)
        {
            if (t == "super") return 0;
            if (t == "important") return 1;
            return 2;
        }

        static string TierName(string t)
        {
            if (t == "super") return "超级重要";
            if (t == "important") return "重要";
            return "普通";
        }

        // ---------- 话题识别：防止角色老是聊同一个话题 ----------
        static readonly string[][] TopicKeywords = new string[][]
        {
            new string[] { "吃", "饭", "蛋糕", "甜点", "便当", "零食", "做饭", "料理", "厨房", "拉面", "咖喱", "汤" },
            new string[] { "学校", "上课", "作业", "考试", "老师", "风纪", "校规", "放学", "社团", "教室", "复习" },
            new string[] { "训练", "运动", "跑步", "练习", "锻炼", "体力", "比赛" },
            new string[] { "机械", "发明", "装置", "齿轮", "电池", "电路", "改装", "零件" },
            new string[] { "植物", "花", "花园", "浇水", "绿萝", "盆栽", "叶子" },
            new string[] { "家务", "打扫", "洗衣", "收拾", "房间", "整理", "洗碗" },
            new string[] { "工作", "任务", "公务", "报告", "出差", "巡视", "执勤" },
            new string[] { "漫画", "动画", "游戏", "看剧", "小说", "电影" },
            new string[] { "天气", "下雨", "晴天", "好冷", "好热", "起风", "降温" },
            new string[] { "宠物", "猫", "狗", "小动物", "喂食" },
            new string[] { "宝石", "礼服", "下午茶", "红茶", "打扮" },
            new string[] { "剑", "修行", "道场", "军务", "煎茶", "磨刀", "刀伤", "武器", "砍" },
            new string[] { "受伤", "擦伤", "发烧", "包扎", "体温", "护理", "感冒" },
            new string[] { "睡觉", "熬夜", "困", "起床", "早起", "失眠" },
            new string[] { "感情", "心动", "告白", "约会", "恋爱", "喜欢我", "害羞" }
        };
        static readonly string[] TopicNames = new string[]
        {
            "吃的", "学校的事", "训练运动", "机械发明", "花草植物", "家务",
            "工作任务", "漫画游戏", "天气", "宠物", "宝石下午茶", "剑术修行", "受伤护理", "睡觉作息", "感情话题"
        };

        // 0 = 认不出，1..N = 对应 TopicNames 下标 +1
        internal static int TopicOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            for (int i = 0; i < TopicKeywords.Length; i++)
            {
                string[] ks = TopicKeywords[i];
                for (int j = 0; j < ks.Length; j++)
                    if (text.IndexOf(ks[j], StringComparison.Ordinal) >= 0) return i + 1;
            }
            return 0;
        }

        // 最近 lookback 条里出现最多的话题（>=3 条才算“老是聊”）
        int DominantTopic(Character who, int lookback, out int hits)
        {
            hits = 0;
            if (who == null) return 0;
            List<ChatMsg> hist = ChatOf(who.id);
            if (hist == null) return 0;
            int[] cnt = new int[TopicNames.Length + 1];
            int seen = 0;
            for (int i = hist.Count - 1; i >= 0 && seen < lookback; i--)
            {
                if (hist[i] == null || hist[i].role != "assistant") continue;
                seen++;
                int t = TopicOf(hist[i].content);
                if (t > 0) cnt[t]++;
            }
            int best = 0, bestN = 0;
            for (int i = 1; i < cnt.Length; i++) if (cnt[i] > bestN) { bestN = cnt[i]; best = i; }
            if (bestN < 3) return 0;
            hits = bestN;
            return best;
        }

        List<Dictionary<string, object>> BuildMessages(Character who, string userText)
        {
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>();
            msgs.Add(Msg("system", who != null ? who.prompt : ""));
            if (who != null && knowledge.ContainsKey(who.id))
            {
                string k = ClipKnowledge(knowledge[who.id], 700);
                if (k.Length > 0)
                    msgs.Add(Msg("system", "【你的知识】" + k + "\n只在小条聊到相关话题时才用这些，不要主动科普、不要背书、不要一次说完；说错了就承认，别编新的设定。"));
            }
            List<MemoryItem> mems;
            lock (sync)
            {
                if (who != null && active != null && active.id == who.id) mems = memories;
                else mems = who != null ? LoadJson<List<MemoryItem>>(MemPath(who.id)) : null;
            }
            string mem = BuildMemoryBlock(mems);
            if (mem.Length > 0) msgs.Add(Msg("system", mem));
            msgs.Add(Msg("system", TimeText()));
            msgs.Add(Msg("system", "【表情】表情只是偶尔调味：一条最多一个，大部分消息干脆不带；连着几条都挂表情很假，也要换着用，不要每次都是同一个。"));
            msgs.Add(Msg("system", "【收话】别老想着把话题接下去：一两句就够，可以只回「嗯」「哦」「好」「行吧」这种短的，说完就停；不用每句都追问或反问，一次最多问一个问题，也可以完全不问；小条回得很短、像在忙，你就顺势收住，别硬聊、别硬换新话题。"));
            msgs.Add(Msg("system", "【长短】这是打字聊天，正常就一两句、一共 10～35 个字，像你一句我一句发消息，不要分段、不要写成小作文；只有真碰上大事或严肃的事（生病受伤、家里出事、吵架道歉、重要决定、很担心小条）才允许一次说三到五句，这种情况要少见。"));
            msgs.Add(Msg("system", "【说话】用平时打字的口气，说人话：短句、口语，可以有「啊」「欸」「算了」「说真的」这种词和省略号；" +
                                  "不要书面语和成语堆砌（比如“事务繁多”“感到肩上的负担沉重”这种一律不要），不要写完整的书面句子、不要总结或点评、不要像解说；" +
                                  "不知道的事就说不知道，不要编设定，也不要替别人编喜好。"));
            msgs.Add(Msg("system", "【别像AI】不要复述或总结小条刚说的话，别把他的词再念一遍；不要每句都用反问收尾；" +
                                  "少用「确实」「其实」「不过」「总之」「这也说明」这类词，也少用「哈哈」开头；不要每次都给出建议或分析。"));
            msgs.Add(Msg("system", "【多样】别老用一种句式收尾：多数就平平地说完（陈述句），有时只回两三个字，偶尔感叹一下，真心好奇时才问一句；" +
                                  "也别老从「好的」「嗯」「确实」「那」开头，换个说法；同一个意思换一种讲法，别按模板套。"));
            msgs.Add(Msg("system", "【别复读】不要重复自己以前说过的句子；别每句都用「好的，」开头；别老是反问「怎么样」「你觉得如何」「要不要」；" +
                                  "小条说「不错/可以/挺好的/嗯」就是答应了，你要往下走一步——说个具体的（时间、地点、你要做什么、你的想法）；没话说的时候就说自己这边的小事或心情，别把话丢回给他。"));
            int domHits;
            int domTopic = DominantTopic(who, 8, out domHits);
            if (domTopic > 0)
                msgs.Add(Msg("system", "【换个话题】你们最近几条都在聊「" + TopicNames[domTopic - 1] + "」（连着 " + domHits +
                    " 条了）。这条别再绕回这个话题：说点别的，或者就着小条这句话往下走；除非小条自己就在问这个。"));
            if (IsFlatAgreement(userText))
                msgs.Add(Msg("system", "小条这句只是应了一声（“" + userText + "”），没有别的话。你这条别提问、也别再提新安排：说一句你自己这边的小事或心情，说完就停。"));
            List<ChatMsg> hist = who != null ? ChatOf(who.id) : new List<ChatMsg>();
            lock (sync)
            {
                int start = hist.Count;
                int used = 0;
                while (start > 0)
                {
                    string c = hist[start - 1].content == null ? "" : hist[start - 1].content;
                    if (used + c.Length > 1600) break;
                    used += c.Length;
                    start--;
                }
                if (start == hist.Count && start > 0) start--;
                for (int i = start; i < hist.Count; i++) msgs.Add(Msg(hist[i].role, hist[i].content));
            }
            if (userText != null) msgs.Add(Msg("user", userText));
            return msgs;
        }

        static Dictionary<string, object> Msg(string role, string content)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["role"] = role; d["content"] = content;
            return d;
        }

        void ShowMessage(string role, string content, long ts)
        {
            ShowMessage(role, content, ts, null);
        }

        void ShowMessage(string role, string content, long ts, string who)
        {
            string id = active != null ? active.id : "";
            if (LastTs(id) == 0 || ts - LastTs(id) > 300000L) Js("addTime", ClockText(ts));
            SetLastTs(id, ts);
            if (role == "user") Js("addMessage", "user", content);
            else if (!string.IsNullOrEmpty(who)) Js("addGroupMessage", who, AvatarOf(who), content);
            else Js("addMessage", "assistant", content);
        }

        string AvatarOf(string name)
        {
            if (name == null) return "";
            foreach (Character c in characters) if (c.name == name) return c.avatar;
            return "";
        }

        string GroupTitle(Character g)
        {
            List<string> names = new List<string>();
            if (g.members != null) foreach (string id in g.members) { Character m = FindCharacter(id); if (m != null) names.Add(m.name); }
            return names.Count + " 人：" + string.Join("、", names.ToArray());
        }

        bool ModelReady()
        {
            return Healthy(ApiBase);
        }

        // 棋局里聊天：对手就着这句话回一句（不进主聊天记录）
        public void GameChat(string id, string text, string game)
        {
            Character c = FindCharacter(id);
            if (c == null || string.IsNullOrEmpty(text)) return;
            Thread t = new Thread(delegate() { RunGameChat(c, text, game); });
            t.IsBackground = true;
            t.Start();
        }

        void RunGameChat(Character who, string text, string game)
        {
            try
            {
                List<Dictionary<string, object>> msgs = BuildMessages(who, text);
                msgs.Add(Msg("system", "你们现在正在一起下" + (string.IsNullOrEmpty(game) ? "棋" : game) +
                    "。小条刚在棋局边上跟你说了句话，你就着这句话回他：一两句、口语、短一点，可以顺口说起棋局；不要每句都提问，也别长篇大论。"));
                string reply = CallChat(msgs, 120, 0.85);
                if (string.IsNullOrEmpty(reply))
                {
                    Log("gamechat empty reply: " + who.id + " / " + text);
                    Ui(delegate { Js("gameReply", who.name, "（没听清…再说一遍？）"); });
                    return;
                }
                reply = EmojiGuard(StripStrayLatin(CleanReply(reply)), who);
                Log("gamechat ok: " + who.id + " -> " + reply);
                string name = who.name;
                // InvokeScript 必须在 UI 线程执行
                Ui(delegate { Js("gameReply", name, reply); });
            }
            catch (Exception ex)
            {
                Log("gamechat failed: " + ex.Message);
                Ui(delegate { Js("gameReply", who.name, "（我这边有点卡住了）"); });
            }
        }

        // 一局结束：角色主动在聊天窗口里说一句（赢/输/平语气不同）
        public void GameResult(string id, string game, int result)
        {
            Character c = FindCharacter(id);
            if (c == null) return;
            Thread t = new Thread(delegate() { RunGameResult(c, game, result); });
            t.IsBackground = true;
            t.Start();
        }

        void RunGameResult(Character who, string game, int result)
        {
            try
            {
                string g = string.IsNullOrEmpty(game) ? "棋" : game;
                string hint;
                if (result == 2)
                    hint = "你们刚下完一局" + g + "，你赢了。用你自己的方式说一句得意的话：一两句、口语、短一点，可以有小小的得意但别刻薄；别背设定、别提 AI。";
                else if (result == 1)
                    hint = "你们刚下完一局" + g + "，你输了。用你自己的方式说一句不服气或者约下一次的话：一两句、口语、短一点；不要阴阳怪气，也不要自贬。";
                else
                    hint = "你们刚下完一局" + g + "，打成了平局。用你自己的方式说一句感想：一两句、口语、短一点。";
                List<Dictionary<string, object>> msgs = BuildMessages(who, null);
                msgs.Add(Msg("system", hint));
                string reply = CallChat(msgs, 120, 0.9);
                if (string.IsNullOrEmpty(reply))
                {
                    Log("gameresult empty: " + who.id);
                    return;
                }
                reply = EmojiGuard(StripStrayLatin(CleanReply(reply)), who);
                Log("gameresult ok: " + who.id + " / " + result + " -> " + reply);
                ChatMsg a = new ChatMsg();
                a.role = "assistant"; a.content = reply; a.ts = Now();
                AppendMsg(who.id, a);
                string shown = reply;
                ChatMsg refMsg = a;
                Ui(delegate
                {
                    bool isActive = (active != null && active.id == who.id);
                    if (isActive) ShowMessage("assistant", shown, refMsg.ts);
                    else { SetUnread(who.id, true); Js("showToast", who.name + " 给你发消息了"); }
                    RenderCharacters();
                });
            }
            catch (Exception ex) { Log("gameresult failed: " + ex.Message); }
        }

        // ---- 拍一拍（双击角色头像）----
        public void Pat(string id)
        {
            Character who = null;
            lock (sync)
            {
                foreach (Character c in characters) if (c != null && c.id == id) { who = c; break; }
            }
            if (who == null) who = active;
            if (who == null || IsGroup(who)) return;
            if (!ModelReady()) { Js("showToast", "模型还没准备好，等一下再拍"); return; }
            if (IsBusy(who.id)) { Js("showToast", who.name + " 正在回你，等会儿再拍"); return; }
            SetBusy(who.id, true);
            BumpVer(who.id);
            int version = Ver(who.id);
            if (active != null && active.id == who.id) Js("setTyping", true);
            Js("setBusy", AnyBusy());
            Thread t = new Thread(delegate() { RunPat(who, version); });
            t.IsBackground = true;
            t.Start();
        }

        void RunPat(Character who, int version)
        {
            try
            {
                List<Dictionary<string, object>> msgs = BuildMessages(who, null);
                msgs.Add(Msg("system", "小条刚双击你的头像，拍了拍你（微信那种「拍一拍」）。就这个动作回一句：" +
                    "一两句、口语，可以惊讶、害羞、嫌弃、撒娇，也可以反拍他一下；不要用括号写动作，不要反问，不要解释。"));
                string reply = CallChat(msgs, 110, 0.95);
                if (string.IsNullOrEmpty(reply))
                {
                    Log("pat empty: " + who.id);
                    SetBusy(who.id, false);
                    Ui(delegate { Js("setTyping", false); Js("setBusy", AnyBusy()); });
                    return;
                }
                reply = EmojiGuard(StripStrayLatin(CleanReply(reply)), who);
                if (CountQuestions(reply) >= 1) reply = DropQuestions(reply, 0);
                if (reply.Length > 60) reply = TrimToLength(reply, 60);
                if (string.IsNullOrEmpty(reply)) reply = "……干嘛。";
                Log("pat ok: " + who.id + " -> " + reply);
                ChatMsg a = new ChatMsg(); a.role = "assistant"; a.content = reply; a.ts = Now();
                AppendMsg(who.id, a);
                SetBusy(who.id, false);
                string shown = reply; long ts = a.ts;
                Ui(delegate
                {
                    bool isActive = (active != null && active.id == who.id);
                    if (isActive) { ShowMessage("assistant", shown, ts); Js("setTyping", false); }
                    else { SetUnread(who.id, true); Js("showToast", who.name + " 回你了"); }
                    Js("setBusy", AnyBusy());
                    RenderCharacters();
                });
            }
            catch (Exception ex)
            {
                Log("pat failed: " + ex.Message);
                SetBusy(who.id, false);
                Ui(delegate { Js("setTyping", false); Js("setBusy", AnyBusy()); });
            }
        }

        public void UserSend(string text)
        {
            Character who = active;
            if (who == null || string.IsNullOrEmpty(text)) return;
            string busyName = AnyBusyName();
            if (busyName != null)
            {
                Js("showToast", "等" + busyName + "回完这句再发");
                return;
            }
            if (!ModelReady())
            {
                ChatMsg notice = new ChatMsg();
                notice.role = "assistant";
                notice.content = "（模型还没准备好……先等启动器把模型加载完，或者重新双击 启动本地AI对话.exe。）";
                notice.ts = Now();
                AppendMsg(who.id, notice);
                Ui(delegate
                {
                    if (active != null && active.id == who.id)
                    {
                        ShowMessage("assistant", notice.content, notice.ts);
                        Js("setTyping", false);
                    }
                    Js("setBusy", AnyBusy());
                    RenderCharacters();
                });
                return;
            }
            SetBusy(who.id, true);
            BumpVer(who.id);
            int version = Ver(who.id);
            ChatMsg u = new ChatMsg(); u.role = "user"; u.content = text; u.ts = Now();
            AppendMsg(who.id, u);
            ShowMessage("user", text, u.ts);
            Js("setTyping", true);
            Js("setBusy", true);
            RenderCharacters();
            if (IsGroup(who))
            {
                Thread gt = new Thread(delegate() { RunGroupChat(who, text); });
                gt.IsBackground = true;
                gt.Start();
                return;
            }
            lastUserActivity = Now();
            List<Dictionary<string, object>> msgs = BuildMessages(who, text);
            Thread t = new Thread(delegate() { RunChat(msgs, text, who, version); });
            t.IsBackground = true;
            t.Start();
        }

        // ---- 群聊 ----
        void RunGroupChat(Character g, string userText)
        {
            List<Character> mem = new List<Character>();
            if (g.members != null) foreach (string id in g.members) { Character m = FindCharacter(id); if (m != null && !IsGroup(m)) mem.Add(m); }
            List<string> names = new List<string>();
            foreach (Character m in mem) names.Add(m.name);

            List<Dictionary<string, object>> msgs = BuildGroupMessages(g, mem, userText);
            string reply = null;
            try { reply = CallChat(msgs, 320, 0.85); }
            catch { apiBase = null; reply = null; }

            List<KeyValuePair<string, string>> parts = ParseGroupReply(reply, names);
            List<ChatMsg> added = new List<ChatMsg>();
            if (parts.Count == 0)
            {
                string fallback = CleanReply(reply);
                if (string.IsNullOrEmpty(fallback)) fallback = "（群里没人接话……是不是引擎没起来？）";
                else fallback = LimitEmoji(fallback, 2);
                ChatMsg one = new ChatMsg();
                one.role = "assistant";
                one.who = names.Count > 0 ? names[0] : "群友";
                one.content = fallback;
                one.ts = Now();
                added.Add(one);
            }
            else
            {
                foreach (KeyValuePair<string, string> kv in parts)
                {
                    ChatMsg a = new ChatMsg();
                    a.role = "assistant";
                    a.who = kv.Key;
                    a.content = LimitEmoji(kv.Value, 2);
                    a.ts = Now();
                    added.Add(a);
                }
            }
            foreach (ChatMsg a in added) AppendMsg(g.id, a);
            SetBusy(g.id, false);
            Ui(delegate
            {
                bool isActive = (active != null && active.id == g.id);
                if (isActive)
                {
                    foreach (ChatMsg a in added) ShowMessage("assistant", a.content, a.ts, a.who);
                    Js("setTyping", false);
                }
                else SetUnread(g.id, true);
                Js("setBusy", AnyBusy());
                RenderCharacters();
            });
        }

        static string CleanReply(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (s.Length > 160)
            {
                int cut = 160;
                if (char.IsHighSurrogate(s[cut - 1])) cut--;
                s = s.Substring(0, cut);
            }
            return s.Trim('“', '”', '"', '「', '」', ' ');
        }

        List<Dictionary<string, object>> BuildGroupMessages(Character g, List<Character> mem, string userText)
        {
            List<string> lines = new List<string>();
            List<ChatMsg> hist;
            lock (sync) hist = new List<ChatMsg>(ChatOf(g.id));
            int start = Math.Max(0, hist.Count - 12);
            for (int i = start; i < hist.Count; i++)
            {
                ChatMsg h = hist[i];
                if (h == null) continue;
                // 最后一条往往就是刚发出去的这句，下面会单独拼一次，别重复
                if (i == hist.Count - 1 && h.role == "user" && h.content == userText) continue;
                string speaker = h.role == "user" ? "小条" : (string.IsNullOrEmpty(h.who) ? "群友" : h.who);
                lines.Add(speaker + "：" + h.content);
            }
            return GroupMessages(mem, lines, userText);
        }

        // 提示词 + 历史，独立成静态方法方便测试
        internal static List<Dictionary<string, object>> GroupMessages(List<Character> mem, List<string> historyLines, string userText)
        {
            StringBuilder sys = new StringBuilder();
            sys.Append("你要模拟一个微信群里的对话。群里除了用户「小条」，还有这些角色（性格按《出包王女》原作，不要改）：\n");
            foreach (Character m in mem) sys.Append("- ").Append(m.name).Append("：").Append(ShortPrompt(m.prompt)).Append("\n");
            sys.Append("规则：\n");
            sys.Append("1. 让小条这句话最相关的 2～3 个人各回一句，像真群聊：有先后，可以互相接话、吐槽，不用所有人都说话。\n");
            sys.Append("2. 每行必须是「角色名：内容」，一行一个人，最多 4 行；不要旁白、不要序号、不要括号动作、不要解释。\n");
            sys.Append("3. 每句口语、简短，最多两句；可以带一个微信表情（如 😊 😅），不要连着发。\n");
            sys.Append("4. 不要提 AI，不写露骨内容。\n");
            sys.Append("5. 只输出这几行对话，别的什么都不写。");
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>();
            msgs.Add(Msg("system", sys.ToString()));
            StringBuilder ctx = new StringBuilder();
            if (historyLines != null && historyLines.Count > 0)
            {
                ctx.Append("最近的群聊：\n");
                foreach (string l in historyLines) ctx.Append(l).Append("\n");
            }
            ctx.Append("小条：").Append(userText);
            msgs.Add(Msg("user", ctx.ToString()));
            return msgs;
        }

        // 人设太长，取一句短的塞进群提示词
        internal static string ShortPrompt(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            string s = p.Replace("\r", " ").Replace("\n", " ").Trim();
            int i = s.IndexOf('。');
            if (i > 0 && i < 60) s = s.Substring(i + 1).Trim();
            if (s.Length > 60)
            {
                int cut = 60;
                if (char.IsHighSurrogate(s[cut - 1])) cut--;
                s = s.Substring(0, cut) + "…";
            }
            return s;
        }

        // 把「拉拉：……」这种一行一句拆开，只认群成员的回复
        internal static List<KeyValuePair<string, string>> ParseGroupReply(string raw, List<string> names)
        {
            List<KeyValuePair<string, string>> outp = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(raw) || names == null || names.Count == 0) return outp;
            string[] lines = raw.Replace("\r", "").Split('\n');
            foreach (string lineRaw in lines)
            {
                string line = lineRaw.Trim().TrimStart('-', '*', '·', ' ', '\t');
                if (line.Length == 0) continue;
                int i1 = line.IndexOf('：');
                int i2 = line.IndexOf(':');
                int idx;
                if (i1 >= 0 && (i2 < 0 || i1 < i2)) idx = i1;
                else if (i2 >= 0) idx = i2;
                else continue;
                if (idx <= 0 || idx > 14) continue;
                string who = line.Substring(0, idx).Trim();
                string text = line.Substring(idx + 1).Trim();
                if (text.Length == 0) continue;
                who = who.Trim('@', '【', '】', '[', ']');
                string matched = null;
                foreach (string n in names)
                {
                    if (who == n) { matched = n; break; }
                }
                if (matched == null)
                {
                    foreach (string n in names)
                    {
                        if (who.Contains(n)) { matched = n; break; }
                    }
                }
                if (matched == null) continue;
                outp.Add(new KeyValuePair<string, string>(matched, text));
                if (outp.Count >= 4) break;
            }
            return outp;
        }

        public void CreateGroup(string name, string memberIds)
        {
            List<string> mem = new List<string>();
            if (!string.IsNullOrEmpty(memberIds))
            {
                foreach (string raw in memberIds.Split(','))
                {
                    string id = raw.Trim();
                    if (id.Length == 0) continue;
                    Character m = FindCharacter(id);
                    if (m == null || IsGroup(m) || mem.Contains(id)) continue;
                    mem.Add(id);
                }
            }
            if (mem.Count < 2) { Js("showToast", "至少选 2 个人才能建群"); return; }
            List<string> names = new List<string>();
            foreach (string id in mem) names.Add(FindCharacter(id).name);
            name = name == null ? "" : name.Trim();
            if (name.Length == 0)
            {
                name = string.Join("、", names.ToArray());
                if (name.Length > 16)
                {
                    int cut = 16;
                    if (char.IsHighSurrogate(name[cut - 1])) cut--;
                    name = name.Substring(0, cut) + "…";
                }
            }
            Character g = new Character();
            g.id = "g" + Now().ToString();
            g.kind = "group";
            g.name = name;
            g.members = mem;
            g.avatar = "";
            g.prompt = "";
            g.created = Now();
            characters.Add(g);
            SaveCharacters();
            RenderCharacters();
            SwitchTo(g.id, false);
            Js("showToast", "群聊已创建");
        }

        // 带原因重写一次：current 不为 null 时把原句当 assistant 发回去；
        // accept 判不过（或者模型没给出内容）就保留原句，调用处逻辑不变。
        string RetryWith(List<Dictionary<string, object>> msgs, string current, string hint,
                         int tokens, double temp, Func<string, bool> accept)
        {
            try
            {
                List<Dictionary<string, object>> r = new List<Dictionary<string, object>>(msgs);
                if (current != null) r.Add(Msg("assistant", current));
                r.Add(Msg("system", hint));
                string got = CallChat(r, tokens, temp);
                if (!string.IsNullOrEmpty(got) && (accept == null || accept(got))) return got;
            }
            catch { }
            return current;
        }

        void RunChat(List<Dictionary<string, object>> msgs, string userText, Character who, int version)
        {
            string reply = null;
            bool failed = false;
            try { reply = CallChat(msgs, 240, 0.7); }
            catch { apiBase = null; reply = null; }
            if (string.IsNullOrEmpty(reply)) { failed = true; reply = "（连不上模型……后台的引擎起来了吗？）"; }
            else if (IsRedundantQuestion(userText, reply))
                reply = RetryWith(msgs, null,
                    "小条刚说过「" + userText + "」。不要再问“你那边……吗”这种他刚讲过的事，也别重复确认；直接顺着他的话接一句。",
                    200, 0.6, null);
            // 复读检测：和自己刚说过的话几乎一样，就带着原句重写
            if (!failed)
            {
                List<string> recentOwn = RecentReplies(who, 6);
                if (recentOwn.Count > 0 && IsRepeating(reply, recentOwn, 0.72))
                {
                    reply = RetryWith(msgs, reply,
                        "你刚才已经说过几乎一样的话：" + recentOwn[0] +
                        "\n这次必须换：直接回应小条这句话，给出新的、具体的反应或安排；不要重复上面那句，不要再用「好的，」开头，也不要再问「怎么样」「你觉得如何」。",
                        200, 0.8, null);
                    if (IsRepeating(reply, recentOwn, 0.72))
                        reply = RetryWith(msgs, null,
                            "只回一句：就小条这句话给个明确回应（你的态度、决定，或者接下来要做什么），不要再反问他，也不要重复你之前说过的内容。",
                            120, 0.9, null);
                }
                else if (recentOwn.Count > 0 && SameOpening(reply, recentOwn[0]))
                {
                    // 开头太单调（又是「嗯，…」「好的，…」），换个开头说
                    reply = RetryWith(msgs, reply,
                        "你上一句是「" + recentOwn[0] + "」，这条又用同样的开头，听着很机械。换个开头、换个句式说，意思不变，一两句。",
                        160, 0.85, delegate(string s) { return !SameOpening(s, recentOwn[0]); });
                }
            }
            // 又绕回同一个话题：换一个说
            if (!failed && userText != null)
            {
                int tHits;
                int tDom = DominantTopic(who, 8, out tHits);
                if (tDom > 0 && TopicOf(reply) == tDom && TopicOf(userText) != tDom)
                    reply = RetryWith(msgs, reply,
                        "你这条又把话题带回「" + TopicNames[tDom - 1] + "」了，你们已经连着聊了好几条。换个话题：说点你这边别的事、心情或者别的安排，一两句，不要再提" + TopicNames[tDom - 1] + "。",
                        160, 0.85, delegate(string s) { return TopicOf(s) != tDom; });
            }
            // 连着问两个以上问题，就像在硬找话题，重写成“收住”的版本
            if (!failed && CountQuestions(reply) >= 2)
                reply = RetryWith(msgs, reply,
                    "上面这条连着问了好几个问题，太像在硬聊。改写成一句到两句：最多留一个问题，或者干脆不问，说完就停。",
                    160, 0.6, delegate(string s) { return CountQuestions(s) <= 1; });
            // 小条这句不是问句时，别拿反问收尾（把话丢回给他最像 AI）
            if (!failed && CountQuestions(userText) == 0 && !IsHowToQuestion(userText) && CountQuestions(reply) >= 1)
            {
                int q0 = CountQuestions(reply);
                reply = RetryWith(msgs, reply,
                    "小条这句不是问句。你别反问他、也别把话丢回去：说一句你自己的反应或者你这边的事，说完就停，不要问号。",
                    160, 0.7, delegate(string s) { return CountQuestions(s) < q0; });
                if (CountQuestions(reply) >= 1) reply = DropQuestions(reply, 0);
            }
            // 普通聊天不许长篇大论：太长就压成短句（大事/严肃、或者小条在问“怎么做”的除外）
            bool allowLong = IsSeriousMessage(userText) || IsHowToQuestion(userText);
            if (!failed && !allowLong && reply.Length > 55)
            {
                int len0 = reply.Length;
                reply = RetryWith(msgs, reply,
                    "上面这条太长了，像在写小作文。改写成一句到两句、总共不超过 35 个字，只说最要紧的，说完就停，不要分段。",
                    120, 0.6, delegate(string s) { return s.Length < len0; });
                if (reply.Length > 90) reply = TrimToLength(reply, 90);
            }
            else if (allowLong && reply.Length > 260) reply = TrimToLength(reply, 260);
            string finalText = EmojiGuard(StripStrayLatin(reply), who);
            ChatMsg a = new ChatMsg(); a.role = "assistant"; a.content = finalText; a.ts = Now();
            AppendMsg(who.id, a);
            SetBusy(who.id, false);
            Ui(delegate
            {
                bool isActive = (active != null && active.id == who.id);
                if (isActive)
                {
                    ShowMessage("assistant", finalText, a.ts);
                    Js("setTyping", false);
                }
                else if (!failed)
                {
                    SetUnread(who.id, true);
                    Js("showToast", who.name + " 回你了");
                }
                Js("setBusy", AnyBusy());
                RenderCharacters();
            });
            if (!failed) StartExtract(who, userText, reply);
            ScheduleIdle(who, version);
        }

        static bool IsRedundantQuestion(string user, string reply)
        {
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(reply)) return false;
            bool said = user.Contains("我这边") || user.Contains("我这里") || user.Contains("我这儿");
            bool ask = reply.Contains("你那边") || reply.Contains("你那里") || reply.Contains("你那儿");
            return said && ask;
        }

        void ScheduleIdle(Character who, int version)
        {
            int sec = new Random().Next(idleMin, idleMax + 1);
            Thread t = new Thread(delegate()
            {
                Thread.Sleep(sec * 1000);
                if (version != Ver(who.id) || IsBusy(who.id)) return;
                if (active == null || active.id != who.id) return;
                FollowUp(who, version);
            });
            t.IsBackground = true;
            t.Start();
        }

        void FollowUp(Character who, int version)
        {
            try
            {
                if (active == null || active.id != who.id) return;
                // 一半概率什么都不发：不黏人、不硬找话题
                if (((Now() / 1000) % 2) == 0) return;
                List<Dictionary<string, object>> msgs = BuildMessages(who, null);
                msgs.Add(Msg("system", "小条好几分钟没回你了。你可以发一句很短的收尾（比如「那你先忙」「我先去洗漱了」「…」），也可以轻轻问一句；不要换新话题，不要连问，也不要连着发好几条。"));
                string text = CallChat(msgs, 100, 0.8);
                if (string.IsNullOrEmpty(text)) return;
                if (version != Ver(who.id) || IsBusy(who.id)) return;
                if (active == null || active.id != who.id) return;
                ChatMsg a = new ChatMsg(); a.role = "assistant"; a.content = text; a.ts = Now();
                AppendMsg(who.id, a);
                Ui(delegate
                {
                    if (active != null && active.id == who.id)
                    {
                        ShowMessage("assistant", text, a.ts);
                        Js("showToast", "（等了一会儿没等到你）");
                    }
                    else SetUnread(who.id, true);
                    RenderCharacters();
                });
            }
            catch { }
        }

        // ---- 主动发消息（内置，不依赖外部脚本） ----
        long LastMsgTs(string id)
        {
            List<ChatMsg> hist;
            lock (sync) hist = new List<ChatMsg>(ChatOf(id));
            for (int i = hist.Count - 1; i >= 0; i--) if (hist[i] != null) return hist[i].ts;
            return 0;
        }

        void ProactiveTick()
        {
            if (!ready || characters.Count == 0) return;
            if (AnyBusy()) return;
            long now = Now();
            if (!ProactiveDue(now, lastUserActivity, lastProactive, proactiveIdleMin, proactiveGapMin)) return;
            if (!ModelReady()) return;

            Character who = PickProactiveCharacter();
            if (who == null) return;
            long lastTs = LastMsgTs(who.id);
            if (lastTs > 0 && now - lastTs < proactiveGapMin * 60000L) return;

            lastProactive = now;
            lastProactiveCharId = who.id;
            SetBusy(who.id, true);
            BumpVer(who.id);
            Thread t = new Thread(delegate() { RunProactive(who); });
            t.IsBackground = true;
            t.Start();
        }

        // 该不该主动发消息（时间判断独立出来，方便测试）
        internal static bool ProactiveDue(long now, long lastUserAt, long lastProactiveAt, int idleMin, int gapMin)
        {
            long idleRef = lastUserAt > 0 ? lastUserAt : now;
            if (now - idleRef < idleMin * 60000L) return false;
            if (lastProactiveAt > 0 && now - lastProactiveAt < gapMin * 60000L) return false;
            return true;
        }

        Character PickProactiveCharacter()
        {
            List<Character> pool = new List<Character>();
            foreach (Character c in characters) if (!IsGroup(c)) pool.Add(c);
            if (pool.Count == 0) return null;
            // 一半多一点的概率找当前正在聊的人，这样消息直接弹在眼前
            if (active != null && !IsGroup(active) && (Now() % 100) < 55) return active;
            List<Character> others = new List<Character>();
            foreach (Character c in pool) if (c.id != lastProactiveCharId) others.Add(c);
            if (others.Count == 0) others = pool;
            Random rnd = new Random((int)(Now() & 0x7fffffff));
            return others[rnd.Next(others.Count)];
        }

        void RunProactive(Character who)
        {
            try
            {
                List<Dictionary<string, object>> msgs = BuildMessages(who, null);
                int proHits;
            int proTopic = DominantTopic(who, 8, out proHits);
            if (proTopic > 0)
                msgs.Add(Msg("system", "这次换个话题：最近你们聊的多是「" + TopicNames[proTopic - 1] + "」，这次说点别的（你这边的事、心情、别的安排），别再提它。"));
            msgs.Add(Msg("system", "小条有一阵子没说话了。你主动发一句话找他：一两句，像随手发微信那样，说点自己这边的小事、吐槽或者关心，别重复最近说过的事；最多带一个很轻的问题，也可以完全不问；不要用括号写动作。"));
                string reply = CallChat(msgs, 120, 0.85);
                if (string.IsNullOrEmpty(reply)) reply = "（喂，你在忙吗？怎么突然没动静了。）";
                reply = VaryEmoji(CleanReply(reply), RecentChatEmojis(who), 2);
                ChatMsg a = new ChatMsg();
                a.role = "assistant";
                a.content = reply;
                a.ts = Now();
                AppendMsg(who.id, a);
                SetBusy(who.id, false);
                Ui(delegate
                {
                    bool isActive = (active != null && active.id == who.id);
                    if (isActive)
                    {
                        ShowMessage("assistant", reply, a.ts);
                        Js("showToast", "（TA 主动找你说话了）");
                    }
                    else
                    {
                        SetUnread(who.id, true);
                        Js("showToast", who.name + " 给你发消息了");
                    }
                    Js("setBusy", AnyBusy());
                    RenderCharacters();
                });
            }
            catch
            {
                SetBusy(who.id, false);
                Ui(delegate { Js("setBusy", AnyBusy()); });
            }
        }

        // 一句话里问了几个问题（用来防止硬聊）
        internal static int CountQuestions(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            foreach (char ch in s) if (ch == '？' || ch == '?') n++;
            return n;
        }

        // 这句话是不是“大事/严肃”的：只有这类才允许回复长一点
        internal static bool IsSeriousMessage(string userText)
        {
            if (string.IsNullOrEmpty(userText)) return false;
            if (userText.Length >= 60) return true;
            string[] keys = {
                "难受", "难过", "生病", "发烧", "受伤", "出事", "吵架", "分手", "哭", "对不起", "原谅",
                "害怕", "怎么办", "求助", "帮我", "表白", "去世", "丢了", "失业", "严重", "重要",
                "生气", "讨厌", "搬家", "考试", "担心", "压力"
            };
            foreach (string k in keys) if (userText.Contains(k)) return true;
            return false;
        }

        // 小条只是随口应一声（“不错”“可以”“嗯”），这时不该再反问或提新建议
        internal static bool IsFlatAgreement(string userText)
        {
            if (string.IsNullOrEmpty(userText)) return false;
            string t = userText.Trim();
            if (t.Length > 6) return false;
            string[] words = { "不错", "可以", "挺好", "好的", "好", "嗯", "哦", "行", "对", "是啊", "是的", "成" };
            foreach (string w in words) if (t.Contains(w)) return true;
            return false;
        }

        // 把句子切开（保留标点）
        internal static List<string> SplitSentences(string s)
        {
            List<string> outp = new List<string>();
            if (string.IsNullOrEmpty(s)) return outp;
            StringBuilder cur = new StringBuilder();
            foreach (char ch in s)
            {
                cur.Append(ch);
                if (ch == '。' || ch == '！' || ch == '？' || ch == '…' || ch == '!' || ch == '?')
                {
                    string t = cur.ToString().Trim();
                    if (t.Length > 0) outp.Add(t);
                    cur.Length = 0;
                }
            }
            string tail = cur.ToString().Trim();
            if (tail.Length > 0) outp.Add(tail);
            return outp;
        }

        // 去掉多余的问句，最多留 keep 个
        internal static string DropQuestions(string s, int keep)
        {
            List<string> parts = SplitSentences(s);
            if (parts.Count == 0) return s;
            StringBuilder sb = new StringBuilder();
            int keptQ = 0;
            foreach (string p in parts)
            {
                bool isQ = p.IndexOf('？') >= 0 || p.IndexOf('?') >= 0;
                if (isQ)
                {
                    if (keptQ >= keep) continue;
                    keptQ++;
                }
                sb.Append(p);
            }
            string outp = sb.ToString().Trim();
            return outp.Length >= 3 ? outp : s;
        }

        // 中英混入的单词（比如“西索 indeed，”）清掉
        internal static string StripStrayLatin(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                char ch = s[i];
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
                {
                    int j = i;
                    while (j < s.Length && ((s[j] >= 'a' && s[j] <= 'z') || (s[j] >= 'A' && s[j] <= 'Z'))) j++;
                    string word = s.Substring(i, j - i);
                    bool prevCjk = i > 0 && s[i - 1] > 0x2000;
                    bool nextCjk = j < s.Length && s[j] > 0x2000;
                    bool nextPunct = j < s.Length && (s[j] == '，' || s[j] == '。' || s[j] == '！' || s[j] == '？');
                    string[] keep = { "OK", "ok", "WiFi", "wifi", "TV", "PC", "ID" };
                    bool keepIt = false;
                    foreach (string k in keep) if (word == k) keepIt = true;
                    if (!keepIt && word.Length >= 3 && prevCjk && (nextCjk || nextPunct))
                    {
                        if (j < s.Length && s[j] == ' ') j++;
                        i = j;
                        continue;
                    }
                    sb.Append(word);
                    i = j;
                    continue;
                }
                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        // 开头两个字一样（“嗯，…”“好的，…”连着来）也算单调
        internal static bool SameOpening(string a, string b)
        {
            string x = NormForCompare(a), y = NormForCompare(b);
            if (x.Length < 2 || y.Length < 2) return false;
            return x.Substring(0, 2) == y.Substring(0, 2);
        }

        // 是不是在问“要怎么做”：这类问题回答稍长一点是合理的
        internal static bool IsHowToQuestion(string userText)
        {
            if (string.IsNullOrEmpty(userText)) return false;
            string t = userText.Trim();
            string[] keys = {
                "怎么", "怎样", "如何", "为什么", "为啥", "多少", "多久", "能不能", "可以吗", "要不要",
                "该不该", "吃什么", "做法", "配方", "保养", "护理", "注意什么", "要注意", "步骤", "方法"
            };
            foreach (string k in keys) if (t.Contains(k)) return true;
            // “在吗”这种短招呼不算；带“吗/呢/?”并且有点内容的才算在问事
            if (t.Contains("吗") || t.Contains("呢") || t.EndsWith("?") || t.EndsWith("？"))
                return t.Length >= 6;
            return false;
        }

        // 兜底：太长的按标点截到长度内
        internal static string TrimToLength(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxChars) return s;
            int cut = -1;
            for (int i = 0; i < s.Length && i <= maxChars; i++)
                if (s[i] == '。' || s[i] == '！' || s[i] == '!' || s[i] == '？' || s[i] == '?' || s[i] == '…') cut = i;
            string t;
            if (cut >= 2) t = s.Substring(0, cut + 1);
            else
            {
                int c2 = maxChars;
                if (char.IsHighSurrogate(s[c2 - 1])) c2--;
                t = s.Substring(0, c2) + "…";
            }
            return t.Trim();
        }

        void StartExtract(Character who, string userText, string reply)
        {
            Thread t = new Thread(delegate() { Extract(who, userText, reply); });
            t.IsBackground = true;
            t.Start();
        }

        void Extract(Character who, string userText, string reply)
        {
            try
            {
                string sys = "你是记忆整理器。读下面这轮对话，判断有没有值得长期记住的事，并按重要性分档：\n- 超级重要：小条的生日、姓名/身份、重要约定、关系变化、影响很大的事（生日一律算超级重要）\n- 重要：小条的喜好、习惯、家人朋友、近期重要计划\n- 普通：日常小事、随口提到的事\n- 无：寒暄、没有新信息\n只输出两行，不要解释：\n等级：普通/重要/超级重要/无\n内容：一句话，不超过40字";
                List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>();
                msgs.Add(Msg("system", sys));
                msgs.Add(Msg("user", "小条：" + userText + "\n" + who.name + "：" + reply));
                string res = CallChat(msgs, 80, 0.2);
                if (string.IsNullOrEmpty(res)) return;
                string tier = null, memo = null;
                foreach (string raw in res.Replace("\r", "").Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("等级")) tier = AfterColon(line);
                    else if (line.StartsWith("内容")) memo = AfterColon(line);
                }
                if (string.IsNullOrEmpty(memo)) return;
                string t = "";
                if (tier != null)
                {
                    if (tier.IndexOf("超级") >= 0) t = "super";
                    else if (tier.IndexOf("重要") >= 0) t = "important";
                    else if (tier.IndexOf("普通") >= 0) t = "normal";
                    else if (tier.IndexOf("无") >= 0) return;
                }
                if (t.Length == 0) return;
                MemoryItem m = new MemoryItem();
                m.id = Guid.NewGuid().ToString("N");
                m.tier = t; m.text = memo; m.created = Now();
                m.expires = t == "normal" ? Now() + 7L * 86400000L : (t == "important" ? Now() + 30L * 86400000L : 0);
                List<MemoryItem> cm = LoadJson<List<MemoryItem>>(MemPath(who.id));
                if (cm == null) cm = new List<MemoryItem>();
                bool isActive = (active != null && active.id == who.id);
                if (t == "super")
                {
                    if (!isActive) return;
                    pending[m.id] = m;
                    pendingOwner[m.id] = who.id;
                    Ui(delegate { Js("showConfirm", m.id, memo); });
                }
                else
                {
                    cm.Add(m);
                    Save(MemPath(who.id), cm);
                    if (isActive)
                    {
                        lock (sync) memories = cm;
                        Ui(delegate
                        {
                            if (active != null && active.id == who.id)
                            {
                                PushMemories();
                                Js("showToast", "已记住（" + TierName(t) + "）");
                            }
                        });
                    }
                }
            }
            catch { }
        }

        static string AfterColon(string line)
        {
            int i = line.IndexOf('：');
            if (i < 0) i = line.IndexOf(':');
            if (i < 0) return "";
            return line.Substring(i + 1).Trim();
        }

        public void SuperConfirm(string id, bool ok)
        {
            MemoryItem m;
            if (id == null || !pending.TryGetValue(id, out m)) return;
            pending.Remove(id);
            string owner;
            pendingOwner.TryGetValue(id, out owner);
            pendingOwner.Remove(id);
            if (ok && !string.IsNullOrEmpty(owner))
            {
                m.expires = 0;
                List<MemoryItem> cm = LoadJson<List<MemoryItem>>(MemPath(owner));
                if (cm == null) cm = new List<MemoryItem>();
                cm.Add(m);
                Save(MemPath(owner), cm);
                if (active != null && active.id == owner)
                {
                    lock (sync) memories = cm;
                    PushMemories();
                }
                Js("showToast", "已永久记住");
            }
            else Js("showToast", "已忽略");
        }

        public void DeleteMem(string id)
        {
            if (active == null) return;
            for (int i = 0; i < memories.Count; i++) { if (memories[i].id == id) { memories.RemoveAt(i); break; } }
            SaveMemories();
            PushMemories();
        }

        void PurgeExpired()
        {
            long now = Now();
            bool changed = false;
            for (int i = memories.Count - 1; i >= 0; i--)
            {
                MemoryItem m = memories[i];
                if (m.expires > 0 && m.expires <= now) { memories.RemoveAt(i); changed = true; }
            }
            if (changed) SaveMemories();
        }

        public void PushMemories()
        {
            List<MemoryItem> sorted = new List<MemoryItem>(memories);
            sorted.Sort(delegate(MemoryItem a, MemoryItem b) { return b.created.CompareTo(a.created); });
            Js("renderMemories", ser.Serialize(sorted));
        }

        bool Healthy(string baseUrl)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(baseUrl.TrimEnd('/') + "/models");
                req.Method = "GET";
                req.Timeout = 800;
                req.ReadWriteTimeout = 800;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    return resp.StatusCode == HttpStatusCode.OK;
            }
            catch { return false; }
        }

        string ApiBase
        {
            get
            {
                if (apiBase != null) return apiBase;
                string configured = config.ContainsKey("base_url") ? Convert.ToString(config["base_url"]) : "http://127.0.0.1:11500/v1";
                configured = configured.TrimEnd('/');
                System.Collections.Generic.List<string> cands = new System.Collections.Generic.List<string>();
                cands.Add(configured);
                for (int p = 11500; p <= 11505; p++)
                {
                    string u = "http://127.0.0.1:" + p + "/v1";
                    if (!cands.Contains(u)) cands.Add(u);
                }
                foreach (string u in cands)
                {
                    if (Healthy(u)) { apiBase = u; Log("api base -> " + u); return u; }
                }
                apiBase = configured;
                return configured;
            }
        }

        string Model
        {
            get { return config.ContainsKey("model") ? Convert.ToString(config["model"]) : "qwen2.5:7b"; }
        }

        string CallChat(List<Dictionary<string, object>> msgs, int maxTokens, double temperature)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["model"] = Model;
            payload["messages"] = msgs;
            payload["temperature"] = temperature;
            payload["max_tokens"] = maxTokens;
            payload["stream"] = false;
            string json = ser.Serialize(payload);
            byte[] data = Encoding.UTF8.GetBytes(json);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ApiBase + "/chat/completions");
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Timeout = 120000;
            req.ReadWriteTimeout = 120000;
            req.ContentLength = data.Length;
            using (Stream s = req.GetRequestStream()) { s.Write(data, 0, data.Length); }
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string body = sr.ReadToEnd();
                Dictionary<string, object> obj = (Dictionary<string, object>)ser.DeserializeObject(body);
                IList choices = (IList)obj["choices"];
                Dictionary<string, object> first = (Dictionary<string, object>)choices[0];
                Dictionary<string, object> msg = (Dictionary<string, object>)first["message"];
                return Convert.ToString(msg["content"]);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                string exe = Path.GetFileName(Application.ExecutablePath);
                RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION");
                if (k != null) { k.SetValue(exe, 11001, RegistryValueKind.DWord); k.Close(); }
            }
            catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }
}
