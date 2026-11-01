using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[ComVisible(true)]
public class FakeBridge
{
    public static List<string> Calls = new List<string>();
    public void SwitchChat(string id) { Calls.Add("SwitchChat:" + id); }
    public void Send(string t) { Calls.Add("Send:" + t); }
    public void GameChat(string id, string t, string g) { Calls.Add("GameChat:" + id + ":" + t + ":" + g); }
    public void PinChat(string id, bool p) { Calls.Add("PinChat:" + id + ":" + p); }
    public void LikeMoment(string id) { Calls.Add("Like:" + id); }
    public void CommentMoment(string id, string t) { Calls.Add("Comment:" + id); }
    public void PublishMoment(string t) { Calls.Add("Publish:" + t); }
    public void CreateGroup(string n, string ids) { Calls.Add("Group:" + n); }
    public void DeleteCharacter(string id) { Calls.Add("Delete:" + id); }
    public void OpenMoments() { Calls.Add("OpenMoments"); }
    public void CloseMoments() { Calls.Add("CloseMoments"); }
    public void AddCharacter(string name, string prompt, string avatar) { Calls.Add("AddChar:" + name); }
}

public class UiHost : Form
{
    WebBrowser wb = new WebBrowser();
    string outPath;
    string mode;

    static string contacts =
        "[{\"id\":\"lala\",\"name\":\"拉拉\",\"avatar\":\"lala\",\"py\":\"L\",\"builtin\":true,\"last\":\"好啊好啊！\",\"time\":\"21:20\",\"active\":false,\"unread\":false,\"group\":false}," +
        "{\"id\":\"yui\",\"name\":\"古手川唯\",\"avatar\":\"yui\",\"py\":\"G\",\"builtin\":true,\"pinned\":true,\"last\":\"太不像话了\",\"time\":\"20:58\",\"active\":false,\"unread\":true,\"group\":false}," +
        "{\"id\":\"g1\",\"name\":\"彩南三人组\",\"avatar\":\"\",\"py\":\"C\",\"builtin\":false,\"last\":\"小暗：行吧，吃拉面。\",\"time\":\"21:21\",\"active\":true,\"unread\":false,\"group\":true}," +
        "{\"id\":\"rito\",\"name\":\"结城梨斗\",\"avatar\":\"rito\",\"py\":\"J\",\"builtin\":true,\"last\":\"我回来了\",\"time\":\"19:40\",\"active\":false,\"unread\":false,\"group\":false}," +
        "{\"id\":\"u9\",\"name\":\"阿测\",\"avatar\":\"\",\"py\":\"A\",\"builtin\":false,\"last\":\"\",\"time\":\"\",\"active\":false,\"unread\":false,\"group\":false}," +
        "{\"id\":\"nana\",\"name\":\"娜娜\",\"avatar\":\"nana\",\"py\":\"N\",\"builtin\":true,\"last\":\"\",\"time\":\"\",\"active\":false,\"unread\":false,\"group\":false}]";

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

    static void EnableIe11(string exeName)
    {
        try
        {
            RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION");
            if (k != null) { k.SetValue(exeName, 11001, RegistryValueKind.DWord); k.Close(); }
        }
        catch { }
    }

    public UiHost(string htmlPath, string png, string m)
    {
        outPath = png; mode = m;
        Text = "微信界面预览";
        ClientSize = new Size(916, 659);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ShowInTaskbar = false;

        wb.Dock = DockStyle.Fill;
        wb.ScriptErrorsSuppressed = true;
        wb.ObjectForScripting = new FakeBridge();
        Controls.Add(wb);

        Load += delegate
        {
            wb.DocumentCompleted += delegate { RunMode(); };
            wb.Navigate(htmlPath);
        };
    }

    void RunMode()
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            Thread.Sleep(800);
            try
            {
                BeginInvoke((Action)delegate
                {
                    try { Script(); } catch (Exception ex) { Log("script: " + ex.Message); }
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Thread.Sleep(1400);
                        try { BeginInvoke((Action)delegate { Shot(); }); } catch { }
                    });
                });
            }
            catch (Exception ex) { Log("run: " + ex.Message); }
        });
    }

    void Script()
    {
        if (mode == "chat") { wb.Document.InvokeScript("renderCharacters", new object[] { contacts }); return; }
        if (mode == "pat")
        {
            wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
            wb.Document.InvokeScript("setActive", new object[] { "拉拉", "lala", "lala" });
            wb.Document.InvokeScript("addMessage", new object[] { "assistant", "今天在实验室试新齿轮，结果又炸了……" });
            wb.Document.InvokeScript("addMessage", new object[] { "user", "你可真行" });
            wb.Document.InvokeScript("patPat", new object[] { "lala", "拉拉" });
            wb.Document.InvokeScript("addMessage", new object[] { "assistant", "诶诶诶？你拍我干嘛，吓我一跳。" });
            return;
        }
        if (mode == "games")
        {
            wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
            wb.Document.InvokeScript("openGames");
            wb.Document.InvokeScript("startGame", new object[] { "yui", "me" });
            wb.Document.InvokeScript("gCell", new object[] { 7 * 15 + 7 });
            return;
        }
        if (mode == "gamesay")
        {
            wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
            wb.Document.InvokeScript("openGames");
            wb.Document.InvokeScript("startGame", new object[] { "yui", "opp" });
            wb.Document.InvokeScript("gShowBub", new object[] { "gBubMe", "这步棋你走得挺快啊" });
            wb.Document.InvokeScript("gShowBub", new object[] { "gBubOpp", "那是当然，你可别输得太难看" });
            return;
        }
        if (mode == "gamexq")
        {
            wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
            wb.Document.InvokeScript("openGames");
            wb.Document.InvokeScript("pickGame", new object[] { "chess" });
            wb.Document.InvokeScript("startGame", new object[] { "rito", "me" });
            return;
        }
        if (mode == "gamefl")
        {
            wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
            wb.Document.InvokeScript("openGames");
            wb.Document.InvokeScript("pickGame", new object[] { "flight" });
            wb.Document.InvokeScript("startGame", new object[] { "lala", "me" });
            return;
        }
        if (mode == "gamego")
        {
            wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
            wb.Document.InvokeScript("openGames");
            wb.Document.InvokeScript("pickGame", new object[] { "go" });
            wb.Document.InvokeScript("startGame", new object[] { "rito", "me" });
            wb.Document.InvokeScript("gCell", new object[] { 40 });
            wb.Document.InvokeScript("aiTurn");
            wb.Document.InvokeScript("gCell", new object[] { 30 });
            wb.Document.InvokeScript("aiTurn");
            return;
        }
        if (mode == "gamewin" || mode == "gamelose" || mode == "gamedraw")
        {
            wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
            wb.Document.InvokeScript("openGames");
            wb.Document.InvokeScript("pickGame", new object[] { "gomoku" });
            wb.Document.InvokeScript("startGame", new object[] { "yui", "me" });
            wb.Document.InvokeScript("showResult", new object[] { mode == "gamewin" ? "win" : (mode == "gamelose" ? "lose" : "draw") });
            return;
        }
        if (mode == "check")
        {
            wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
            wb.Document.InvokeScript("openGames");
            wb.Document.InvokeScript("pickGame", new object[] { "chess" });
            wb.Document.InvokeScript("startGame", new object[] { "rito", "me" });
            wb.Document.InvokeScript("gDemo", new object[] { "check" });
            return;
        }
        if (mode == "again")
        {
            wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
            wb.Document.InvokeScript("openGames");
            wb.Document.InvokeScript("pickGame", new object[] { "chess" });
            wb.Document.InvokeScript("startGame", new object[] { "rito", "me" });
            wb.Document.InvokeScript("gDemo", new object[] { "over" });
            return;
        }
        if (mode == "dice")
        {
            wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
            wb.Document.InvokeScript("openGames");
            wb.Document.InvokeScript("pickGame", new object[] { "flight" });
            wb.Document.InvokeScript("startGame", new object[] { "lala", "me" });
            wb.Document.InvokeScript("gDemo", new object[] { "dice" });
            return;
        }        if (mode == "invite")
        {
            wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
            wb.Document.InvokeScript("showInvite", new object[] { "yui", "gomoku", "这局算你运气好……要不再来一局？" });
            return;
        }
        wb.Document.InvokeScript("renderCharacters", new object[] { contacts });
    }

    void Log(string s)
    {
        try { File.AppendAllText(outPath + ".err.txt", s + "\r\n", Encoding.UTF8); } catch { }
    }

    void Shot()
    {
        try
        {
            Bitmap bmp = new Bitmap(Width, Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(Handle, hdc, 0);
                g.ReleaseHdc(hdc);
            }
            bmp.Save(outPath, ImageFormat.Png);
            bmp.Dispose();
        }
        catch (Exception ex) { Log("shot: " + ex.Message); }
        Close();
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length < 3) return;
        EnableIe11(Path.GetFileName(Application.ExecutablePath));
        Application.EnableVisualStyles();
        Application.Run(new UiHost(args[0], args[1], args[2]));
    }
}
