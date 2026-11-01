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
    }

    public class MainForm : Form
    {
        WebBrowser browser;
        Bridge bridge;
        readonly string dataDir;
        Dictionary<string, object> config = new Dictionary<string, object>();
        List<MemoryItem> memories = new List<MemoryItem>();
        List<ChatMsg> history = new List<ChatMsg>();
        Dictionary<string, MemoryItem> pending = new Dictionary<string, MemoryItem>();
        JavaScriptSerializer ser = new JavaScriptSerializer();
        bool ready = false;
        bool busy = false;
        int msgVersion = 0;
        int idleMin = 180;
        int idleMax = 300;
        string autoSend = null;
        long lastMsgTs = 0;

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
                }
            }
            dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            Text = "微信";
            ClientSize = new Size(920, 760);
            MinimumSize = new Size(680, 540);
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
            LoadData();
            PurgeExpired();
            string htmlPath = Path.Combine(dataDir, "wechat.html");
            browser.DocumentCompleted += OnDoc;
            if (File.Exists(htmlPath))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "rito_wechat_ui.html");
                try { File.Copy(htmlPath, tmp, true); } catch { }
                browser.Navigate(tmp);
            }
            else
                browser.DocumentText = "<html><body style='font-family:Microsoft YaHei'>missing wechat.html</body></html>";
        }

        void OnDoc(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (ready) return;
            try { if (browser.Document.GetElementById("chat") == null) return; } catch { return; }
            ready = true;
            Js("clearChat");
            foreach (ChatMsg m in history) ShowMessage(m.role == "user" ? "user" : "assistant", m.content, m.ts);
            PushMemories();
            Js("setStatus", "在线");
            if (!string.IsNullOrEmpty(autoSend)) Js("setInput", autoSend);


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
            DateTime t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ts).ToLocalTime();
            if (t.Date == DateTime.Now.Date) return t.ToString("HH:mm");
            return t.ToString("M月d日 HH:mm");
        }

        void ShowMessage(string role, string content, long ts)
        {
            if (lastMsgTs == 0 || ts - lastMsgTs > 300000L) Js("addTime", ClockText(ts));
            lastMsgTs = ts;
            Js("addMessage", role, content);
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

        void LoadData()
        {
            try
            {
                string mp = Path.Combine(dataDir, "wechat_memory.json");
                if (File.Exists(mp))
                {
                    List<MemoryItem> m = ser.Deserialize<List<MemoryItem>>(File.ReadAllText(mp, Encoding.UTF8));
                    if (m != null) memories = m;
                }
            }
            catch { memories = new List<MemoryItem>(); }
            try
            {
                string hp = Path.Combine(dataDir, "wechat_history.json");
                if (File.Exists(hp))
                {
                    List<ChatMsg> h = ser.Deserialize<List<ChatMsg>>(File.ReadAllText(hp, Encoding.UTF8));
                    if (h != null) history = h;
                }
                else ImportOldChat();
            }
            catch { history = new List<ChatMsg>(); }
        }

        void ImportOldChat()
        {
            try
            {
                string op = Path.Combine(dataDir, "conversations.json");
                if (!File.Exists(op)) return;
                object raw = ser.DeserializeObject(File.ReadAllText(op, Encoding.UTF8));
                IList convs = raw as IList;
                if (convs == null || convs.Count == 0) return;
                Dictionary<string, object> last = null;
                long best = -1;
                foreach (object o in convs)
                {
                    Dictionary<string, object> c = o as Dictionary<string, object>;
                    if (c == null) continue;
                    long up = 0;
                    if (c.ContainsKey("updated")) long.TryParse(Convert.ToString(c["updated"]), out up);
                    if (up >= best) { best = up; last = c; }
                }
                if (last == null) return;
                IList ms = last["messages"] as IList;
                if (ms == null) return;
                foreach (object o in ms)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    string role = Convert.ToString(d["role"]);
                    string content = Convert.ToString(d["content"]);
                    if (string.IsNullOrEmpty(content)) continue;
                    if (role != "user" && role != "assistant") continue;
                    ChatMsg m = new ChatMsg();
                    m.role = role; m.content = content;
                    long ts = 0; long.TryParse(Convert.ToString(d["ts"]), out ts);
                    m.ts = ts;
                    history.Add(m);
                }
                SaveHistory();
            }
            catch { }
        }

        void SaveHistory()
        {
            try
            {
                if (history.Count > 300) history.RemoveRange(0, history.Count - 300);
                File.WriteAllText(Path.Combine(dataDir, "wechat_history.json"), ser.Serialize(history), new UTF8Encoding(false));
            }
            catch { }
        }

        void SaveMemories()
        {
            try
            {
                File.WriteAllText(Path.Combine(dataDir, "wechat_memory.json"), ser.Serialize(memories), new UTF8Encoding(false));
            }
            catch { }
        }

        void PurgeExpired()
        {
            long now = Now();
            int before = memories.Count;
            for (int i = memories.Count - 1; i >= 0; i--)
            {
                MemoryItem m = memories[i];
                if (m.expires > 0 && m.expires <= now) memories.RemoveAt(i);
            }
            if (memories.Count != before) SaveMemories();
        }

        static long Now()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        public void PushMemories()
        {
            List<MemoryItem> sorted = new List<MemoryItem>(memories);
            sorted.Sort(delegate(MemoryItem a, MemoryItem b) { return b.created.CompareTo(a.created); });
            Js("renderMemories", ser.Serialize(sorted));
        }

        string Persona
        {
            get
            {
                if (config.ContainsKey("system_prompt")) return Convert.ToString(config["system_prompt"]);
                return "你是结城梨斗。";
            }
        }

        string BaseUrl
        {
            get
            {
                string b = config.ContainsKey("base_url") ? Convert.ToString(config["base_url"]) : "http://127.0.0.1:11500/v1";
                return b.TrimEnd('/');
            }
        }

        string Model
        {
            get { return config.ContainsKey("model") ? Convert.ToString(config["model"]) : "qwen2.5:7b"; }
        }

        string BuildMemoryBlock()
        {
            if (memories.Count == 0) return "";
            List<MemoryItem> list = new List<MemoryItem>(memories);
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
                if (sb.Length > 2200) break;
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

        List<Dictionary<string, object>> BuildMessages(string userText)
        {
            List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>();
            msgs.Add(Msg("system", Persona));
            string mem = BuildMemoryBlock();
            if (mem.Length > 0) msgs.Add(Msg("system", mem));
            msgs.Add(Msg("system", TimeText()));
            int start = Math.Max(0, history.Count - 16);
            for (int i = start; i < history.Count; i++)
                msgs.Add(Msg(history[i].role, history[i].content));
            if (userText != null) msgs.Add(Msg("user", userText));
            return msgs;
        }

        static Dictionary<string, object> Msg(string role, string content)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["role"] = role; d["content"] = content;
            return d;
        }

        public void UserSend(string text)
        {
            if (busy || string.IsNullOrEmpty(text)) return;
            busy = true;
            msgVersion++;
            List<Dictionary<string, object>> msgs = BuildMessages(text);
            ChatMsg u = new ChatMsg();
            u.role = "user"; u.content = text; u.ts = Now();
            history.Add(u);
            SaveHistory();
            ShowMessage("user", text, u.ts);
            Js("setTyping", true);
            Js("setBusy", true);
            Thread t = new Thread(delegate() { RunChat(msgs, text); });
            t.IsBackground = true;
            t.Start();
        }

        void RunChat(List<Dictionary<string, object>> msgs, string userText)
        {
            string reply = null;
            try { reply = CallChat(msgs, 512, 0.7); }
            catch { reply = null; }
            if (string.IsNullOrEmpty(reply))
                reply = "（连不上模型……后台的引擎起来了吗？）";
            ChatMsg a = new ChatMsg();
            a.role = "assistant"; a.content = reply; a.ts = Now();
            history.Add(a);
            SaveHistory();
            Ui(delegate
            {
                ShowMessage("assistant", reply, a.ts);
                Js("setTyping", false);
                Js("setBusy", false);
                busy = false;
            });
            if (!reply.StartsWith("（连不上")) StartExtract(userText, reply);
            ScheduleIdle(msgVersion);
        }

        void ScheduleIdle(int version)
        {
            int sec = new Random().Next(idleMin, idleMax + 1);
            Thread t = new Thread(delegate()
            {
                Thread.Sleep(sec * 1000);
                if (version != msgVersion || busy) return;
                FollowUp(version);
            });
            t.IsBackground = true;
            t.Start();
        }

        void FollowUp(int version)
        {
            try
            {
                List<Dictionary<string, object>> msgs = BuildMessages(null);
                msgs.Add(Msg("system", "小条已经好几分钟没回你了。你有点不满，主动发一句催他或者抱怨，一两句就够，别太长，别一直追问。"));
                string text = CallChat(msgs, 120, 0.8);
                if (string.IsNullOrEmpty(text)) return;
                if (version != msgVersion || busy) return;
                ChatMsg a = new ChatMsg();
                a.role = "assistant"; a.content = text; a.ts = Now();
                history.Add(a);
                SaveHistory();
                Ui(delegate
                {
                    ShowMessage("assistant", text, a.ts);
                    Js("showToast", "（等了一会儿没等到你）");
                });
            }
            catch { }
        }

        void StartExtract(string userText, string reply)
        {
            Thread t = new Thread(delegate() { Extract(userText, reply); });
            t.IsBackground = true;
            t.Start();
        }

        void Extract(string userText, string reply)
        {
            try
            {
                string sys = "你是记忆整理器。读下面这轮对话，判断有没有值得长期记住的事：小条的喜好、约定、重要经历、关系变化。\n只输出两行，不要解释：\n等级：普通/重要/超级重要/无\n内容：一句话，不超过40字\n如果只是闲聊或没有新信息，等级写“无”。";
                List<Dictionary<string, object>> msgs = new List<Dictionary<string, object>>();
                msgs.Add(Msg("system", sys));
                msgs.Add(Msg("user", "小条：" + userText + "\n梨斗：" + reply));
                string res = CallChat(msgs, 80, 0.2);
                if (string.IsNullOrEmpty(res)) return;
                string tier = null, memo = null;
                string[] lines = res.Replace("\r", "").Split('\n');
                foreach (string raw in lines)
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
                if (t == "super")
                {
                    pending[m.id] = m;
                    Ui(delegate { Js("showConfirm", m.id, memo); });
                }
                else
                {
                    memories.Add(m);
                    SaveMemories();
                    Ui(delegate
                    {
                        PushMemories();
                        Js("showToast", "已记住（" + TierName(t) + "）");
                    });
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
            if (ok)
            {
                m.expires = 0;
                memories.Add(m);
                SaveMemories();
                PushMemories();
                Js("showToast", "已永久记住");
            }
            else
            {
                Js("showToast", "已忽略");
            }
        }

        public void DeleteMem(string id)
        {
            for (int i = 0; i < memories.Count; i++)
            {
                if (memories[i].id == id) { memories.RemoveAt(i); break; }
            }
            SaveMemories();
            PushMemories();
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
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(BaseUrl + "/chat/completions");
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Timeout = 300000;
            req.ReadWriteTimeout = 300000;
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
