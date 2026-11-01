using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class ProgressForm : Form
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    System.Windows.Forms.Timer timer;
    DateTime start;
    ProgressBar bar;
    Label status, pct;
    Process engine;
    volatile bool engineReady = false;
    volatile bool engineFailed = false;
    string dir;
    int port = 11500;

    public ProgressForm()
    {
        dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
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
        title.SetBounds(28, 22, 420, 34);
        Controls.Add(title);

        status = new Label();
        status.Text = "正在检查模型状态…";
        status.Font = new Font("Microsoft YaHei", 10);
        status.ForeColor = Color.FromArgb(90, 90, 90);
        status.SetBounds(28, 66, 420, 22);
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

        Label sub = new Label();
        sub.Text = "首次加载约 2～3 分钟，之后模型会保持运行，再开很快";
        sub.Font = new Font("Microsoft YaHei", 9);
        sub.ForeColor = Color.FromArgb(150, 150, 150);
        sub.SetBounds(28, 126, 430, 20);
        Controls.Add(sub);

        start = DateTime.Now;
        Thread th = new Thread(Work);
        th.IsBackground = true;
        th.Start();
        timer = new System.Windows.Forms.Timer();
        timer.Interval = 400;
        timer.Tick += Tick;
        timer.Start();
    }

    bool Healthy(int p)
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + p + "/health");
            req.Timeout = 700; req.ReadWriteTimeout = 700;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                return resp.StatusCode == HttpStatusCode.OK;
        }
        catch { return false; }
    }

    void Work()
    {
        foreach (Process ep in Process.GetProcessesByName("本地AI对话"))
        {
            if (ep.MainWindowTitle != null && ep.MainWindowTitle.Length > 0)
            {
                SetForegroundWindow(ep.MainWindowHandle);
                Environment.Exit(0);
            }
        }
        for (int p = 11500; p <= 11505; p++)
        {
            if (Healthy(p)) { port = p; engineReady = true; OpenApp(); return; }
        }
        try
        {
            string engDir = Path.Combine(dir, @"ollama\lib\ollama");
            string model = Path.Combine(dir, @"models\qwen2.5-7b\qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf");
            ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(engDir, "llama-server.exe"));
            psi.WorkingDirectory = engDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.EnvironmentVariables["GGML_VULKAN"] = "1";
            psi.EnvironmentVariables["GGML_BACKEND_PATH"] = Path.Combine(engDir, "vulkan");
            psi.Arguments = "-m \"" + model + "\" --host 127.0.0.1 --port 11500 -c 4096 -t 8 -ngl 99 --no-webui";
            engine = Process.Start(psi);
        }
        catch (Exception ex)
        {
            engineFailed = true;
            BeginInvoke((Action)delegate { status.Text = "启动失败：" + ex.Message; });
            return;
        }
        DateTime deadline = DateTime.Now.AddMinutes(8);
        while (DateTime.Now < deadline)
        {
            if (Healthy(11500)) { port = 11500; engineReady = true; OpenApp(); return; }
            if (engine != null && engine.HasExited) { engineFailed = true; return; }
            Thread.Sleep(2000);
        }
        engineFailed = true;
    }

    void OpenApp()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(dir, @"app\本地AI对话.exe"));
            psi.WorkingDirectory = Path.Combine(dir, "app");
            Process.Start(psi);
        }
        catch { }
    }

    void Tick(object s, EventArgs e)
    {
        if (engineFailed)
        {
            status.Text = "模型启动失败，请改用原启动器 启动本地AI对话.exe";
            pct.Text = "";
            return;
        }
        bool app = false;
        foreach (Process p in Process.GetProcessesByName("本地AI对话"))
        {
            if (p.MainWindowTitle != null && p.MainWindowTitle.Length > 0) { app = true; break; }
        }
        if (app)
        {
            bar.Value = 100; pct.Text = "100%";
            status.Text = "已就绪";
            timer.Stop();
            System.Windows.Forms.Timer close = new System.Windows.Forms.Timer();
            close.Interval = 400;
            close.Tick += delegate { close.Stop(); Close(); };
            close.Start();
            return;
        }
        double sec = (DateTime.Now - start).TotalSeconds;
        int val = engineReady ? 92 : (int)Math.Min(88, 3 + sec / 180.0 * 85);
        if (val > bar.Value) bar.Value = val;
        pct.Text = bar.Value + "%";
        if (engineReady) status.Text = "模型已就绪，正在打开界面…";
        else if (sec > 120) status.Text = "正在加载模型，首次启动稍慢…";
        else status.Text = "正在加载本地模型，请稍候…";
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ProgressForm());
    }
}
