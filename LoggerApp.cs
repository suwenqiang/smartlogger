using System;
using System.Diagnostics;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Text;

namespace SimpleLoggerApp
{
    public partial class MainForm : Form
    {
        private Process adbProcess;
        private Button btnStart;
        private Button btnStop;
        private TextBox txtLog;
        private bool isLogging = false;
        private string logFileName;
        private Thread logThread;

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // 创建按钮
            btnStart = new Button() { Text = "开始记录", Location = new System.Drawing.Point(10, 10), Size = new System.Drawing.Size(80, 30) };
            btnStop = new Button() { Text = "停止记录", Location = new System.Drawing.Point(100, 10), Size = new System.Drawing.Size(80, 30), Enabled = false };
            
            // 创建文本框用于显示日志
            txtLog = new TextBox() { 
                Multiline = true, 
                Size = new System.Drawing.Size(600, 400), 
                Location = new System.Drawing.Point(10, 50), 
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                WordWrap = true
            };

            // 按钮事件
            btnStart.Click += (sender, e) => StartLogging();
            btnStop.Click += (sender, e) => StopLogging();

            // 添加到窗体
            this.Controls.AddRange(new Control[] { btnStart, btnStop, txtLog });
            this.Text = "ADB 日志记录器";
            this.Size = new System.Drawing.Size(640, 500);
        }

        private void StartLogging()
        {
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            isLogging = true;

            // 生成日志文件名
            logFileName = string.Format("UnitAging_test_{0:yyyyMMdd}_{0:HHmm}.log", DateTime.Now);
            
            // 写入开始信息到文件 - 使用 \r\n 确保正确换行
            File.AppendAllText(logFileName, "=======================================\r\n");
            File.AppendAllText(logFileName, string.Format("UnitAging Test Started at {0}\r\n", DateTime.Now));
            File.AppendAllText(logFileName, "=======================================\r\n");

            UpdateLogText(string.Format("[{0}] 开始记录日志到文件: {1}\r\n", DateTime.Now, logFileName));

            // 在新线程中执行日志记录，避免阻塞UI
            logThread = new Thread(new ThreadStart(LoggingWorker));
            logThread.IsBackground = true;
            logThread.Start();
        }

        private void LoggingWorker()
        {
            try
            {
                UpdateLogText(string.Format("[{0}] 等待设备连接...\r\n", DateTime.Now));
                
                // 等待设备
                using (Process waitProcess = new Process())
                {
                    waitProcess.StartInfo.FileName = "adb";
                    waitProcess.StartInfo.Arguments = "wait-for-device";
                    waitProcess.StartInfo.UseShellExecute = false;
                    waitProcess.StartInfo.CreateNoWindow = true;
                    waitProcess.StartInfo.RedirectStandardOutput = true;
                    waitProcess.StartInfo.RedirectStandardError = true;
                    waitProcess.Start();
                    waitProcess.WaitForExit();
                }

                UpdateLogText(string.Format("[{0}] 设备已连接\r\n", DateTime.Now));

                // 清空日志缓存
                using (Process clearProcess = new Process())
                {
                    clearProcess.StartInfo.FileName = "adb";
                    clearProcess.StartInfo.Arguments = "logcat -c";
                    clearProcess.StartInfo.UseShellExecute = false;
                    clearProcess.StartInfo.CreateNoWindow = true;
                    clearProcess.Start();
                    clearProcess.WaitForExit();
                }

                UpdateLogText(string.Format("[{0}] 开始记录logcat\r\n", DateTime.Now));

                // 开始记录logcat
                adbProcess = new Process();
                adbProcess.StartInfo.FileName = "adb";
                adbProcess.StartInfo.Arguments = "logcat -v time";
                adbProcess.StartInfo.UseShellExecute = false;
                adbProcess.StartInfo.RedirectStandardOutput = true;
                adbProcess.StartInfo.RedirectStandardError = true;
                adbProcess.StartInfo.CreateNoWindow = true;
                adbProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                adbProcess.StartInfo.StandardErrorEncoding = Encoding.UTF8;

                // 实时读取输出
                adbProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data) && isLogging)
                    {
                        string logEntry = e.Data + "\r\n"; // 使用 \r\n 确保正确换行
                        UpdateLogText(logEntry);
                        try
                        {
                            File.AppendAllText(logFileName, logEntry);
                        }
                        catch (Exception ex)
                        {
                            UpdateLogText(string.Format("[{0}] 文件写入错误: {1}\r\n", DateTime.Now, ex.Message));
                        }
                    }
                };

                adbProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data) && isLogging)
                    {
                        string errorEntry = string.Format("[ERROR] {0}\r\n", e.Data); // 使用 \r\n 确保正确换行
                        UpdateLogText(errorEntry);
                        try
                        {
                            File.AppendAllText(logFileName, errorEntry);
                        }
                        catch (Exception ex)
                        {
                            UpdateLogText(string.Format("[{0}] 文件写入错误: {1}\r\n", DateTime.Now, ex.Message));
                        }
                    }
                };

                adbProcess.Start();
                adbProcess.BeginOutputReadLine();
                adbProcess.BeginErrorReadLine();
                
                // 等待进程结束
                adbProcess.WaitForExit();
                
                UpdateLogText(string.Format("[{0}] Logcat进程结束\r\n", DateTime.Now));
            }
            catch (Exception ex)
            {
                UpdateLogText(string.Format("[{0}] 错误: {1}\r\n", DateTime.Now, ex.Message));
            }
        }

        private void UpdateLogText(string text)
        {
            if (this.InvokeRequired)
            {
                if (!this.IsDisposed)
                {
                    this.BeginInvoke(new Action<string>(UpdateLogText), text);
                }
                return;
            }
            
            if (!this.IsDisposed && txtLog != null && !txtLog.IsDisposed)
            {
                txtLog.AppendText(text);
                // 自动滚动到最后
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
        }

        private void StopLogging()
        {
            isLogging = false;
            btnStart.Enabled = true;
            btnStop.Enabled = false;

            if (adbProcess != null && !adbProcess.HasExited)
            {
                try
                {
                    adbProcess.Kill();
                    adbProcess = null;
                }
                catch (Exception ex)
                {
                    UpdateLogText(string.Format("[{0}] 停止进程时出错: {1}\r\n", DateTime.Now, ex.Message));
                }
            }
            
            UpdateLogText(string.Format("[{0}] 日志记录已停止\r\n", DateTime.Now));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopLogging();
            
            // 等待日志线程结束
            if (logThread != null && logThread.IsAlive)
            {
                logThread.Join(2000); // 等待最多2秒
            }
            
            base.OnFormClosing(e);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}