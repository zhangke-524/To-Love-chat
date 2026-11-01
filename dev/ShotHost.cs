using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class ShotHost
{
    [STAThread]
    static void Main(string[] args)
    {
        string page = args[0];
        string shot = args[1];
        int waitMs = args.Length > 2 ? int.Parse(args[2]) : 2000;
        try
        {
            Application.EnableVisualStyles();
            Form f = new Form();
            f.Text = "预览";
            f.ClientSize = new Size(760, 720);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(60, 40);
            f.BackColor = Color.White;
            WebBrowser wb = new WebBrowser();
            wb.Dock = DockStyle.Fill;
            wb.ScriptErrorsSuppressed = true;
            f.Controls.Add(wb);
            wb.Navigate(page);
            Timer t = new Timer();
            t.Interval = waitMs;
            t.Tick += delegate
            {
                t.Stop();
                try
                {
                    Bitmap bmp = new Bitmap(f.Width, f.Height);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        PrintWindow(f.Handle, hdc, 2);
                        g.ReleaseHdc(hdc);
                    }
                    bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                    bmp.Dispose();
                    File.WriteAllText(shot + ".ok.txt", "ok");
                }
                catch (Exception ex) { File.WriteAllText(shot + ".err.txt", ex.ToString()); }
                f.Close();
            };
            t.Start();
            Application.Run(f);
        }
        catch (Exception ex)
        {
            File.WriteAllText(shot + ".fatal.txt", ex.ToString());
            Environment.Exit(1);
        }
    }

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
}
