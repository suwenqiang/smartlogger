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
        
        // 设备状态监控
        private System.Windows.Forms.Timer deviceCheckTimer;
        private DateTime lastLogTime = DateTime.MinValue;
        private const int DEVICE_CHECK_INTERVAL = 2000; // 2秒检查一次
        private volatile bool isAdbProcessRunning = false;

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
            
            // 初始化设备检查定时器
            deviceCheckTimer = new System.Windows.Forms.Timer();
            deviceCheckTimer.Interval = DEVICE_CHECK_INTERVAL;
            deviceCheckTimer.Tick += CheckDeviceStatus;
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
            lastLogTime = DateTime.Now;
            
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
            
            // 启动设备检查定时器
            deviceCheckTimer.Start();
            
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
                        if (!waitProcess.WaitForExit(10000)) // 10秒超时
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
                        isAdbProcessRunning = true;
                        
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
                            currentAdbProcess.Exited += (s, e) => 
                            {
                                processExitEvent.Set();
                                isAdbProcessRunning = false;
                            };

                            // 实时读取输出
                            currentAdbProcess.OutputDataReceived += (sender, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data) && isLogging)
                                {
                                    // 更新最后日志时间
                                    lastLogTime = DateTime.Now;
                                    
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
                                    // 更新最后日志时间
                                    lastLogTime = DateTime.Now;
                                    
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
                            while (isLogging && !processExitEvent.WaitOne(500)) // 500毫秒检查一次
                            {
                                // 每500毫秒检查一次是否应该停止
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
                        AddToDisplayBuffer(string.Format("[{0}] 2秒后尝试重新连接...", DateTime.Now));
                        Thread.Sleep(2000); // 重试前等待
                    }
                }
            }
            
            AddToDisplayBuffer(string.Format("[{0}] 日志记录已停止", DateTime.Now));
        }

        private void CheckDeviceStatus(object sender, EventArgs e)
        {
            if (!isLogging) return;
            
            // 检查最后日志时间，如果超过10秒没有新日志，认为设备可能已断开
            if ((DateTime.Now - lastLogTime).TotalSeconds > 10)
            {
                // 检查设备是否真的断开
                bool deviceConnected = CheckDeviceConnected();
                if (!deviceConnected && isAdbProcessRunning)
                {
                    AddToDisplayBuffer(string.Format("[{0}] 检测到设备断开，正在重新连接...", DateTime.Now));
                    try
                    {
                        // 安全地检查并终止ADB进程
                        if (adbProcess != null && !adbProcess.HasExited)
                        {
                            adbProcess.Kill();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // 忽略"没有与此对象关联的进程"异常
                        isAdbProcessRunning = false;
                    }
                    catch (Exception ex)
                    {
                        AddToDisplayBuffer(string.Format("[{0}] 终止ADB进程时出错: {1}", DateTime.Now, ex.Message));
                    }
                }
            }
        }

        private bool CheckDeviceConnected()
        {
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = "adb";
                    process.StartInfo.Arguments = "devices";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.Start();
                    
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000);
                    
                    // 检查输出中是否包含设备（排除头行）
                    string[] lines = output.Split('\n');
                    for (int i = 1; i < lines.Length; i++)
                    {
                        if (lines[i].Trim().EndsWith("device"))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
            catch
            {
                return false;
            }
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
        try
        {
            // 保存当前滚动位置
            int firstVisibleLine = GetFirstVisibleLine();
            int selectionStart = txtLog.SelectionStart;
            bool wasAtBottom = IsScrolledToBottom();
            
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
            
            // 更新文本框
            txtLog.Text = displayText.ToString();
            
            // 恢复滚动位置
            if (wasAtBottom || displayBuffer.Count <= 1)
            {
                // 如果之前是在底部或者这是第一次更新，滚动到底部
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
            else
            {
                // 否则恢复之前的滚动位置
                SetFirstVisibleLine(firstVisibleLine);
                txtLog.SelectionStart = Math.Min(selectionStart, txtLog.TextLength);
            }
        }
        catch (Exception)
        {
            // 忽略UI更新异常，避免程序崩溃
#if DEBUG
            System.Diagnostics.Debug.WriteLine("UI更新异常");
#endif
        }
    }
}

// 获取第一个可见行的索引
private int GetFirstVisibleLine()
{
    const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    return SendMessage(txtLog.Handle, EM_GETFIRSTVISIBLELINE, 0, 0);
}

// 设置第一个可见行
private void SetFirstVisibleLine(int line)
{
    const int EM_LINESCROLL = 0x00B6;
    SendMessage(txtLog.Handle, EM_LINESCROLL, 0, line);
}

// 导入Windows API
[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern int SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);

private bool IsScrolledToBottom()
{
    // 检查是否滚动到底部
    int visibleLines = txtLog.ClientSize.Height / txtLog.Font.Height;
    int firstVisibleLine = GetFirstVisibleLine();
    int totalLines = txtLog.Lines.Length;
    
    return firstVisibleLine + visibleLines >= totalLines - 1;
}
        private void StopLogging()
        {
            isLogging = false;
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            
            // 停止设备检查定时器
            deviceCheckTimer.Stop();

            try
            {
                if (adbProcess != null && !adbProcess.HasExited)
                {
                    adbProcess.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // 忽略"没有与此对象关联的进程"异常
            }
            catch (Exception ex)
            {
                AddToDisplayBuffer(string.Format("[{0}] 停止进程时出错: {1}", DateTime.Now, ex.Message));
            }
            finally
            {
                adbProcess = null;
                isAdbProcessRunning = false;
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
            
            // 停止设备检查定时器
            if (deviceCheckTimer != null)
            {
                deviceCheckTimer.Stop();
                deviceCheckTimer.Dispose();
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