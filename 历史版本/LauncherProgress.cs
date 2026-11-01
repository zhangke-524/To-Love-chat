using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class ProgressForm : Form
{
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

    System.Windows.Forms.Timer timer;
    DateTime start;
    ProgressBar bar;
    Label status, pct, sub;
    Process launcher;
    volatile bool engineReady = false;
    string dir;
    bool demo = false;

    public ProgressForm(string[] args)
    {
        dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        demo = Array.IndexOf(args, "--demo") >= 0;
        Text = "启动本地AI";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(480, 200);
        BackColor = Color.White;
        TopMost = true;
        Paint += delegate(object s, PaintEventArgs e) { e.Graphics.DrawRectangle(Pens.LightGray, 0, 0, Width - 1, Height - 1); };

        Label title = new Label();
        title.Text = "正在启动本地AI";
        title.Font = new Font("Microsoft YaHei", 16, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(30, 30, 30);
        title.SetBounds(28, 22, 400, 34);
        Controls.Add(title);

        status = new Label();
        status.Text = "正在加载本地模型，请稍候…";
        status.Font = new Font("Microsoft YaHei", 10);
        status.ForeColor = Color.FromArgb(90, 90, 90);
        status.SetBounds(28, 66, 410, 22);
        Controls.Add(status);

        bar = new ProgressBar();
        bar.Minimum = 0; bar.Maximum = 100; bar.Value = 2;
        bar.SetBounds(28, 96, 380, 14);
        Controls.Add(bar);

        pct = new Label();
        pct.Text = "2%";
        pct.Font = new Font("Microsoft YaHei", 9);
        pct.ForeColor = Color.FromArgb(120, 120, 120);
        pct.SetBounds(416, 92, 44, 20);
        Controls.Add(pct);

        sub = new Label();
        sub.Text = "首次启动约 2～3 分钟，加载完会自动打开";
        sub.Font = new Font("Microsoft YaHei", 9);
        sub.ForeColor = Color.FromArgb(150, 150, 150);
        sub.SetBounds(28, 126, 410, 20);
        Controls.Add(sub);

        start = DateTime.Now;
        if (!demo)
        {
            foreach (Process ep in Process.GetProcessesByName("本地AI对话"))
            {
                if (ep.MainWindowTitle != null && ep.MainWindowTitle.Length > 0)
                {
                    SetForegroundWindow(ep.MainWindowHandle);
                    Environment.Exit(0);
                }
            }
            bool engineUp = QuickHealth();
            if (engineUp)
            {
                try
                {
                    ProcessStartInfo api = new ProcessStartInfo(System.IO.Path.Combine(dir, @"app\本地AI对话.exe"));
                    api.WorkingDirectory = System.IO.Path.Combine(dir, "app");
                    Process.Start(api);
                }
                catch { StartLauncher(); }
            }
            else StartLauncher();
        }
        timer = new System.Windows.Forms.Timer();
        timer.Interval = 400;
        timer.Tick += Tick;
        timer.Start();
        if (!demo)
        {
            Thread th = new Thread(HealthLoop);
            th.IsBackground = true;
            th.Start();
        }
    }

    void StartLauncher()
    {
        try
        {
            string path = System.IO.Path.Combine(dir, "启动器_原版.exe");
            ProcessStartInfo psi = new ProcessStartInfo(path);
            psi.WorkingDirectory = dir;
            psi.UseShellExecute = true;
            launcher = Process.Start(psi);
        }
        catch (Exception ex)
        {
            status.Text = "启动失败：" + ex.Message;
        }
    }

    bool QuickHealth()
    {
        for (int p = 11500; p <= 11501; p++)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + p + "/health");
                req.Timeout = 600; req.ReadWriteTimeout = 600;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode == HttpStatusCode.OK) return true;
                }
            }
            catch { }
        }
        return false;
    }

    void HealthLoop()
    {
        while (true)
        {
            for (int p = 11500; p <= 11505; p++)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + p + "/health");
                    req.Timeout = 700; req.ReadWriteTimeout = 700;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        if (resp.StatusCode == HttpStatusCode.OK) { engineReady = true; return; }
                    }
                }
                catch { }
            }
            Thread.Sleep(1500);
        }
    }

    void Tick(object s, EventArgs e)
    {
        if (demo)
        {
            int v = Math.Min(65, bar.Value + 2);
            bar.Value = v; pct.Text = v + "%";
            if (v >= 65) status.Text = "（演示）这里实时显示模型加载进度";
            return;
        }
        if (launcher != null && launcher.MainWindowHandle != IntPtr.Zero)
            ShowWindow(launcher.MainWindowHandle, 0);

        bool app = false;
        foreach (Process p in Process.GetProcessesByName("本地AI对话"))
        {
            if (p.MainWindowTitle != null && p.MainWindowTitle.Length > 0) { app = true; break; }
        }
        if (app)
        {
            bar.Value = 100; pct.Text = "100%";
            status.Text = "已就绪，正在打开…";
            timer.Stop();
            System.Windows.Forms.Timer close = new System.Windows.Forms.Timer(); close.Interval = 500;
            close.Tick += delegate { close.Stop(); Close(); };
            close.Start();
            return;
        }
        double sec = (DateTime.Now - start).TotalSeconds;
        int val = engineReady ? 92 : (int)Math.Min(88, 3 + sec / 180.0 * 85);
        if (val > bar.Value) bar.Value = val;
        pct.Text = bar.Value + "%";
        if (engineReady) status.Text = "模型已就绪，正在打开界面…";
        else if (sec > 180) status.Text = "还在加载模型，首次启动可能慢一点…";
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ProgressForm(args));
    }
}
