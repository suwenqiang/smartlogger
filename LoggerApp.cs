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
        
        // 循环缓冲区相关变量 - 仅用于界面显示
        private Queue<string> displayBuffer;
        private const int MAX_DISPLAY_LINES = 1000;
        
        // UI 更新优化
        private System.Windows.Forms.Timer uiUpdateTimer;
        private volatile bool uiUpdateNeeded = false;
        private readonly object displayLock = new object();

        public MainForm()
        {
            // 在初始化窗体前检查ADB环境变量
            if (!CheckAdbEnvironment())
            {
                // 如果ADB不存在，显示错误并退出
                MessageBox.Show(
                    "没有找到系统中的ADB环境变量。\n\n请确保已安装Android SDK Platform-Tools并将其添加到系统PATH环境变量中。",
                    "ADB环境变量错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Environment.Exit(1);
            }
            
            InitializeComponent();
            
            // 初始化UI更新定时器
            uiUpdateTimer = new System.Windows.Forms.Timer();
            uiUpdateTimer.Interval = 100; // 每100毫秒更新一次UI
            uiUpdateTimer.Tick += (s, e) => 
            {
                if (uiUpdateNeeded)
                {
                    UpdateLogDisplay();
                    uiUpdateNeeded = false;
                }
            };
            uiUpdateTimer.Start();
        }

        private bool CheckAdbEnvironment()
        {
            try
            {
                // 尝试执行 adb version 命令
                Process process = new Process();
                process.StartInfo.FileName = "adb";
                process.StartInfo.Arguments = "version";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                
                process.Start();
                process.WaitForExit(2000); // 等待最多2秒
                
                // 如果进程成功启动并退出，说明adb存在
                return process.ExitCode == 0;
            }
            catch (Exception)
            {
                // 如果出现异常（通常是文件未找到），说明adb不存在
                return false;
            }
        }

        private void InitializeComponent()
        {
            // 初始化循环缓冲区 - 仅用于界面显示
            displayBuffer = new Queue<string>(MAX_DISPLAY_LINES);
            
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
            
            // 清空显示缓冲区
            lock (displayLock)
            {
                displayBuffer.Clear();
            }

            // 生成日志文件名
            logFileName = string.Format("UnitAging_test_{0:yyyyMMdd}_{0:HHmm}.log", DateTime.Now);
            
            // 写入开始信息到文件 - 使用 \r\n 确保正确换行
            File.AppendAllText(logFileName, "=======================================\r\n");
            File.AppendAllText(logFileName, string.Format("UnitAging Test Started at {0}\r\n", DateTime.Now));
            File.AppendAllText(logFileName, "=======================================\r\n");

            // 添加初始消息到显示缓冲区
            AddToDisplayBuffer(string.Format("[{0}] 开始记录日志到文件: {1}", DateTime.Now, logFileName));
            AddToDisplayBuffer(string.Format("[{0}] 等待设备连接...", DateTime.Now));
            
            // 在新线程中执行日志记录，避免阻塞UI
            logThread = new Thread(new ThreadStart(LoggingWorker));
            logThread.IsBackground = true;
            logThread.Start();
        }

        private void LoggingWorker()
        {
            while (isLogging)
            {
                try
                {
                    if (!isLogging) break;
                    
                    // 等待设备
                    AddToDisplayBuffer(string.Format("[{0}] 等待设备连接...", DateTime.Now));
                    using (Process waitProcess = new Process())
                    {
                        waitProcess.StartInfo.FileName = "adb";
                        waitProcess.StartInfo.Arguments = "wait-for-device";
                        waitProcess.StartInfo.UseShellExecute = false;
                        waitProcess.StartInfo.CreateNoWindow = true;
                        waitProcess.StartInfo.RedirectStandardOutput = true;
                        waitProcess.StartInfo.RedirectStandardError = true;
                        waitProcess.Start();
                        
                        // 使用带超时的等待，避免永久阻塞
                        if (!waitProcess.WaitForExit(30000)) // 30秒超时
                        {
                            AddToDisplayBuffer(string.Format("[{0}] 等待设备超时，重新尝试...", DateTime.Now));
                            waitProcess.Kill();
                            continue;
                        }
                    }

                    if (!isLogging) break;
                    AddToDisplayBuffer(string.Format("[{0}] 设备已连接", DateTime.Now));

                    // 清空日志缓存
                    using (Process clearProcess = new Process())
                    {
                        clearProcess.StartInfo.FileName = "adb";
                        clearProcess.StartInfo.Arguments = "logcat -c";
                        clearProcess.StartInfo.UseShellExecute = false;
                        clearProcess.StartInfo.CreateNoWindow = true;
                        clearProcess.Start();
                        clearProcess.WaitForExit(5000); // 5秒超时
                    }

                    // 修改提示信息
                    AddToDisplayBuffer(string.Format("[{0}] 开始记录logcat,本窗口只显示1000行，完整log保存在{1}文件中", DateTime.Now, logFileName));

                    // 开始记录logcat - 保留 -v time 参数
                    using (Process currentAdbProcess = new Process())
                    {
                        adbProcess = currentAdbProcess; // 保持引用以便必要时终止
                        currentAdbProcess.StartInfo.FileName = "adb";
                        currentAdbProcess.StartInfo.Arguments = "logcat -v time";
                        currentAdbProcess.StartInfo.UseShellExecute = false;
                        currentAdbProcess.StartInfo.RedirectStandardOutput = true;
                        currentAdbProcess.StartInfo.RedirectStandardError = true;
                        currentAdbProcess.StartInfo.CreateNoWindow = true;
                        currentAdbProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                        currentAdbProcess.StartInfo.StandardErrorEncoding = Encoding.UTF8;

                        // 使用ManualResetEvent来监控进程状态
                        using (var processExitEvent = new ManualResetEvent(false))
                        {
                            currentAdbProcess.EnableRaisingEvents = true;
                            currentAdbProcess.Exited += (s, e) => processExitEvent.Set();

                            // 实时读取输出
                            currentAdbProcess.OutputDataReceived += (sender, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data) && isLogging)
                                {
                                    // 在每一行前面添加PC系统当前时间
                                    string logEntry = string.Format("[PC:{0}] {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), e.Data);
                                    
                                    // 添加到显示缓冲区
                                    AddToDisplayBuffer(logEntry);
                                    
                                    // 直接写入文件 - 不经过缓冲区
                                    try
                                    {
                                        File.AppendAllText(logFileName, logEntry + "\r\n");
                                    }
                                    catch (Exception ex)
                                    {
                                        AddToDisplayBuffer(string.Format("[{0}] 文件写入错误: {1}", DateTime.Now, ex.Message));
                                    }
                                }
                            };

                            currentAdbProcess.ErrorDataReceived += (sender, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data) && isLogging)
                                {
                                    // 在每一行前面添加PC系统当前时间
                                    string errorEntry = string.Format("[PC:{0}] [ERROR] {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), e.Data);
                                    
                                    // 添加到显示缓冲区
                                    AddToDisplayBuffer(errorEntry);
                                    
                                    // 直接写入文件 - 不经过缓冲区
                                    try
                                    {
                                        File.AppendAllText(logFileName, errorEntry + "\r\n");
                                    }
                                    catch (Exception ex)
                                    {
                                        AddToDisplayBuffer(string.Format("[{0}] 文件写入错误: {1}", DateTime.Now, ex.Message));
                                    }
                                }
                            };

                            currentAdbProcess.Start();
                            currentAdbProcess.BeginOutputReadLine();
                            currentAdbProcess.BeginErrorReadLine();
                            
                            // 使用带超时的等待，而不是无限期等待
                            while (isLogging && !processExitEvent.WaitOne(1000))
                            {
                                // 每秒检查一次是否应该停止
                            }
                            
                            // 如果仍在记录状态但进程已退出，说明是意外退出
                            if (isLogging && currentAdbProcess.HasExited)
                            {
                                AddToDisplayBuffer(string.Format("[{0}] ADB进程意外退出，退出代码: {1}", DateTime.Now, currentAdbProcess.ExitCode));
                            }
                        }
                    }
                    
                    // 如果仍在记录状态，说明进程意外退出（如设备断开）
                    // 将循环回去重新等待设备连接
                    if (isLogging)
                    {
                        AddToDisplayBuffer(string.Format("[{0}] 设备连接断开，等待重新连接...", DateTime.Now));
                    }
                }
                catch (Exception ex)
                {
                    if (isLogging)
                    {
                        AddToDisplayBuffer(string.Format("[{0}] 错误: {1}", DateTime.Now, ex.Message));
                        AddToDisplayBuffer(string.Format("[{0}] 5秒后尝试重新连接...", DateTime.Now));
                        Thread.Sleep(5000); // 重试前等待
                    }
                }
            }
            
            AddToDisplayBuffer(string.Format("[{0}] 日志记录已停止", DateTime.Now));
        }

        private void AddToDisplayBuffer(string logLine)
        {
            // 只对显示使用循环缓冲区
            lock (displayLock)
            {
                // 如果缓冲区已满，移除最旧的一行
                if (displayBuffer.Count >= MAX_DISPLAY_LINES)
                {
                    displayBuffer.Dequeue();
                }
                
                // 添加新行
                displayBuffer.Enqueue(logLine);
            }
            
            // 标记需要UI更新，由定时器负责实际更新
            uiUpdateNeeded = true;
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
                
                // 添加缓冲区中的所有行
                lock (displayLock)
                {
                    foreach (string line in displayBuffer)
                    {
                        displayText.AppendLine(line);
                    }
                }
                
                // 保存当前滚动位置
                int firstCharIndex = txtLog.GetCharIndexFromPosition(new System.Drawing.Point(0, 0));
                int selectionStart = txtLog.SelectionStart;
                bool isAtBottom = (txtLog.GetCharIndexFromPosition(new System.Drawing.Point(0, txtLog.ClientSize.Height - 1)) >= txtLog.TextLength - 10);
                
                // 更新文本框
                txtLog.Text = displayText.ToString();
                
                // 恢复滚动位置或滚动到底部
                if (isAtBottom)
                {
                    txtLog.SelectionStart = txtLog.TextLength;
                    txtLog.ScrollToCaret();
                }
                else
                {
                    txtLog.SelectionStart = Math.Min(selectionStart, txtLog.TextLength);
                    try
                    {
                        txtLog.ScrollToCaret();
                    }
                    catch
                    {
                        // 忽略滚动异常
                    }
                }
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
                    AddToDisplayBuffer(string.Format("[{0}] 停止进程时出错: {1}", DateTime.Now, ex.Message));
                }
            }
            
            AddToDisplayBuffer(string.Format("[{0}] 日志记录已停止", DateTime.Now));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopLogging();
            
            // 停止UI更新定时器
            if (uiUpdateTimer != null)
            {
                uiUpdateTimer.Stop();
                uiUpdateTimer.Dispose();
            }
            
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