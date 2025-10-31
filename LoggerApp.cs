using System;
using System.Diagnostics;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Text;
using System.Collections.Generic;

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
        
        // 循环缓冲区相关变量
        private Queue<string> logBuffer;
        private const int MAX_LINES = 1000;
        private bool isInitializing = true;
        private List<string> initialMessages;

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // 初始化循环缓冲区
            logBuffer = new Queue<string>(MAX_LINES);
            initialMessages = new List<string>();
            
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
            
            // 清空缓冲区
            logBuffer.Clear();
            initialMessages.Clear();
            isInitializing = true;

            // 生成日志文件名
            logFileName = string.Format("UnitAging_test_{0:yyyyMMdd}_{0:HHmm}.log", DateTime.Now);
            
            // 写入开始信息到文件 - 使用 \r\n 确保正确换行
            File.AppendAllText(logFileName, "=======================================\r\n");
            File.AppendAllText(logFileName, string.Format("UnitAging Test Started at {0}\r\n", DateTime.Now));
            File.AppendAllText(logFileName, "=======================================\r\n");

            // 添加初始消息到缓冲区
            AddLogToBuffer(string.Format("[{0}] 开始记录日志到文件: {1}", DateTime.Now, logFileName));
            AddLogToBuffer(string.Format("[{0}] 等待设备连接...", DateTime.Now));
            
            // 在新线程中执行日志记录，避免阻塞UI
            logThread = new Thread(new ThreadStart(LoggingWorker));
            logThread.IsBackground = true;
            logThread.Start();
        }

        private void LoggingWorker()
        {
            try
            {
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

                AddLogToBuffer(string.Format("[{0}] 设备已连接", DateTime.Now));

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

                // 修改提示信息
                AddLogToBuffer(string.Format("[{0}] 开始记录logcat,本窗口只显示1000行，完整log保存在{1}文件中", DateTime.Now, logFileName));
                
                // 初始化阶段结束
                isInitializing = false;
                
                // 保存初始消息
                foreach (string message in initialMessages)
                {
                    logBuffer.Enqueue(message);
                }
                initialMessages.Clear();
                
                // 更新显示
                UpdateLogDisplay();

                // 开始记录logcat - 保留 -v time 参数
                adbProcess = new Process();
                adbProcess.StartInfo.FileName = "adb";
                adbProcess.StartInfo.Arguments = "logcat -v time"; // 保留 -v time 参数
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
                        // 在每一行前面添加PC系统当前时间
                        string logEntry = string.Format("[PC:{0}] {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), e.Data);
                        AddLogToBuffer(logEntry);
                        try
                        {
                            File.AppendAllText(logFileName, logEntry + "\r\n");
                        }
                        catch (Exception ex)
                        {
                            AddLogToBuffer(string.Format("[{0}] 文件写入错误: {1}", DateTime.Now, ex.Message));
                        }
                    }
                };

                adbProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data) && isLogging)
                    {
                        // 在每一行前面添加PC系统当前时间
                        string errorEntry = string.Format("[PC:{0}] [ERROR] {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), e.Data);
                        AddLogToBuffer(errorEntry);
                        try
                        {
                            File.AppendAllText(logFileName, errorEntry + "\r\n");
                        }
                        catch (Exception ex)
                        {
                            AddLogToBuffer(string.Format("[{0}] 文件写入错误: {1}", DateTime.Now, ex.Message));
                        }
                    }
                };

                adbProcess.Start();
                adbProcess.BeginOutputReadLine();
                adbProcess.BeginErrorReadLine();
                
                // 等待进程结束
                adbProcess.WaitForExit();
                
                AddLogToBuffer(string.Format("[{0}] Logcat进程结束", DateTime.Now));
            }
            catch (Exception ex)
            {
                AddLogToBuffer(string.Format("[{0}] 错误: {1}", DateTime.Now, ex.Message));
            }
        }

        private void AddLogToBuffer(string logLine)
        {
            if (isInitializing)
            {
                // 在初始化阶段，保存初始消息
                initialMessages.Add(logLine);
            }
            else
            {
                // 正常阶段，使用循环缓冲区
                lock (logBuffer)
                {
                    // 如果缓冲区已满，移除最旧的一行
                    if (logBuffer.Count >= MAX_LINES)
                    {
                        logBuffer.Dequeue();
                    }
                    
                    // 添加新行
                    logBuffer.Enqueue(logLine);
                }
                
                // 更新显示
                UpdateLogDisplay();
            }
        }

        private void UpdateLogDisplay()
        {
            if (this.InvokeRequired)
            {
                if (!this.IsDisposed)
                {
                    this.BeginInvoke(new Action(UpdateLogDisplay));
                }
                return;
            }
            
            if (!this.IsDisposed && txtLog != null && !txtLog.IsDisposed)
            {
                // 构建显示文本
                StringBuilder displayText = new StringBuilder();
                
                // 添加初始化消息（如果有）
                if (isInitializing)
                {
                    foreach (string message in initialMessages)
                    {
                        displayText.AppendLine(message);
                    }
                }
                else
                {
                    // 添加缓冲区中的所有行
                    lock (logBuffer)
                    {
                        foreach (string line in logBuffer)
                        {
                            displayText.AppendLine(line);
                        }
                    }
                }
                
                // 更新文本框
                txtLog.Text = displayText.ToString();
                
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
                    AddLogToBuffer(string.Format("[{0}] 停止进程时出错: {1}", DateTime.Now, ex.Message));
                }
            }
            
            AddLogToBuffer(string.Format("[{0}] 日志记录已停止", DateTime.Now));
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