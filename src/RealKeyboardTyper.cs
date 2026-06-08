using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RealKeyboardTyper
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly TextBox contentBox;
        private readonly NumericUpDown intervalBox;
        private readonly NumericUpDown delayBox;
        private readonly Button runButton;
        private readonly Button cancelButton;
        private readonly Label statusLabel;
        private readonly Label detectedLabel;
        private readonly CheckBox hideWindowBox;
        private readonly CheckBox tabToSpacesBox;
        private readonly ComboBox formatModeBox;
        private readonly ComboBox inputModeBox;
        private readonly System.Windows.Forms.Timer waitClickTimer;

        private CancellationTokenSource cancellation;
        private bool waitingForClick;
        private bool previousMouseDown;
        private uint ownProcessId;

        public MainForm()
        {
            Text = "真实键盘逐字输入器";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(800, 560);
            Size = new Size(940, 680);
            Font = new Font("Microsoft YaHei UI", 9F);
            KeyPreview = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(14),
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = new Label
            {
                AutoSize = true,
                Text = "粘贴要输入的文字或代码。点击“运行”后，再点击目标输入框，程序会通过 Windows SendInput 逐字发送按键。",
                Margin = new Padding(0, 0, 0, 10),
            };

            contentBox = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 10F),
            };
            contentBox.TextChanged += ContentBoxOnTextChanged;

            var options = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 12, 0, 4),
                WrapContents = true,
            };

            options.Controls.Add(new Label { AutoSize = true, Text = "每字间隔(ms)", Margin = new Padding(0, 7, 8, 0) });
            intervalBox = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 2000,
                Value = 35,
                Width = 82,
                Margin = new Padding(0, 3, 20, 0),
            };
            options.Controls.Add(intervalBox);

            options.Controls.Add(new Label { AutoSize = true, Text = "点击后延迟(ms)", Margin = new Padding(0, 7, 8, 0) });
            delayBox = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 10000,
                Value = 500,
                Increment = 100,
                Width = 88,
                Margin = new Padding(0, 3, 20, 0),
            };
            options.Controls.Add(delayBox);

            options.Controls.Add(new Label { AutoSize = true, Text = "格式", Margin = new Padding(0, 7, 8, 0) });
            formatModeBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 265,
                Margin = new Padding(0, 3, 20, 0),
            };
            formatModeBox.Items.Add("自动识别文字/代码");
            formatModeBox.Items.Add("原样输入（保留粘贴格式）");
            formatModeBox.Items.Add("代码模式（统一换行并保留缩进）");
            formatModeBox.SelectedIndex = 0;
            options.Controls.Add(formatModeBox);

            options.Controls.Add(new Label { AutoSize = true, Text = "按键方式", Margin = new Padding(0, 7, 8, 0) });
            inputModeBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 260,
                Margin = new Padding(0, 3, 20, 0),
            };
            inputModeBox.Items.Add("Unicode逐字（中文推荐）");
            inputModeBox.Items.Add("智能模式（自动选择）");
            inputModeBox.Items.Add("英文物理按键（仅ASCII）");
            inputModeBox.Items.Add("兼容粘贴（备用）");
            inputModeBox.SelectedIndex = 0;
            options.Controls.Add(inputModeBox);

            hideWindowBox = new CheckBox
            {
                AutoSize = true,
                Checked = true,
                Text = "运行时最小化窗口",
                Margin = new Padding(0, 6, 20, 0),
            };
            options.Controls.Add(hideWindowBox);

            tabToSpacesBox = new CheckBox
            {
                AutoSize = true,
                Checked = false,
                Text = "Tab 转 4 空格",
                Margin = new Padding(0, 6, 0, 0),
            };
            options.Controls.Add(tabToSpacesBox);

            detectedLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Text = "检测：普通文本",
                Margin = new Padding(0, 4, 0, 0),
                Height = 26,
            };

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 6, 0, 8),
            };

            runButton = new Button
            {
                Text = "运行",
                Width = 120,
                Height = 34,
                Margin = new Padding(0, 0, 8, 0),
            };
            runButton.Click += BeginWaitingForTarget;

            cancelButton = new Button
            {
                Text = "取消",
                Width = 120,
                Height = 34,
                Enabled = false,
                Margin = new Padding(0, 0, 8, 0),
            };
            cancelButton.Click += delegate { CancelCurrentJob("已取消。"); };

            buttons.Controls.Add(runButton);
            buttons.Controls.Add(cancelButton);

            statusLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Text = "状态：待机。Esc 可取消；原样/代码模式会保留换行、空格、缩进、符号和中文。",
                Padding = new Padding(0, 6, 0, 0),
                Height = 38,
            };

            root.Controls.Add(title, 0, 0);
            root.Controls.Add(contentBox, 0, 1);
            root.Controls.Add(options, 0, 2);
            root.Controls.Add(detectedLabel, 0, 3);
            root.Controls.Add(buttons, 0, 4);
            root.Controls.Add(statusLabel, 0, 5);
            Controls.Add(root);

            waitClickTimer = new System.Windows.Forms.Timer { Interval = 30 };
            waitClickTimer.Tick += WaitClickTimerOnTick;

            KeyDown += MainFormOnKeyDown;
            FormClosing += MainFormOnFormClosing;
        }

        private void BeginWaitingForTarget(object sender, EventArgs e)
        {
            if (waitingForClick || cancellation != null)
            {
                return;
            }

            if (string.IsNullOrEmpty(contentBox.Text))
            {
                MessageBox.Show(this, "请先填写或粘贴要输入的内容。", "没有内容", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            cancellation = new CancellationTokenSource();
            waitingForClick = true;
            previousMouseDown = IsLeftMouseDown();
            ownProcessId = NativeMethods.GetCurrentProcessId();

            SetControlsRunning(false);

            if (hideWindowBox.Checked)
            {
                WindowState = FormWindowState.Minimized;
            }

            statusLabel.Text = "状态：等待你点击目标输入框，按 Esc 可取消。";
            waitClickTimer.Start();
        }

        private void WaitClickTimerOnTick(object sender, EventArgs e)
        {
            if (!waitingForClick || cancellation == null)
            {
                return;
            }

            if (IsEscapeDown() || cancellation.IsCancellationRequested)
            {
                FinishJob("已取消。");
                return;
            }

            bool mouseDown = IsLeftMouseDown();
            bool releasedAfterClick = previousMouseDown && !mouseDown;
            previousMouseDown = mouseDown;

            if (!releasedAfterClick)
            {
                return;
            }

            IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return;
            }

            uint targetProcessId;
            NativeMethods.GetWindowThreadProcessId(foregroundWindow, out targetProcessId);
            if (targetProcessId == ownProcessId)
            {
                statusLabel.Text = "状态：请点击其他程序里的目标输入框。";
                return;
            }

            waitingForClick = false;
            waitClickTimer.Stop();

            string text = contentBox.Text;
            int interval = Decimal.ToInt32(intervalBox.Value);
            int clickDelay = Decimal.ToInt32(delayBox.Value);
            FormatMode formatMode = GetFormatMode();
            InputMode inputMode = GetInputMode();
            bool tabToSpaces = tabToSpacesBox.Checked;
            CancellationToken token = cancellation.Token;

            statusLabel.Text = "状态：已检测到目标，准备输入...";
            Task.Run(delegate { TypeText(text, interval, clickDelay, formatMode, inputMode, tabToSpaces, token); }, token);
        }

        private void TypeText(string text, int interval, int clickDelay, FormatMode formatMode, InputMode inputMode, bool tabToSpaces, CancellationToken token)
        {
            try
            {
                if (clickDelay > 0)
                {
                    SleepWithCancel(clickDelay, token);
                }

                TypingProfile profile = BuildProfile(text, formatMode, tabToSpaces);
                InputMode resolvedMode = ResolveInputMode(profile.Text, inputMode);

                if (resolvedMode == InputMode.Paste)
                {
                    PostStatus("状态：使用兼容粘贴模式输入，正在保留完整格式...");
                    PasteWithClipboard(profile.Text, token);
                    PostFinish("输入完成。");
                    return;
                }

                int count = CountTypingUnits(profile.Text);
                int index = 0;

                for (int i = 0; i < profile.Text.Length; i++)
                {
                    ThrowIfCanceled(token);
                    char ch = profile.Text[i];

                    if (ch == '\r')
                    {
                        if (i + 1 < profile.Text.Length && profile.Text[i + 1] == '\n')
                        {
                            i++;
                        }

                        SendVirtualKey(Keys.Return);
                    }
                    else if (ch == '\n')
                    {
                        SendVirtualKey(Keys.Return);
                    }
                    else if (ch == '\t')
                    {
                        SendVirtualKey(Keys.Tab);
                    }
                    else if (char.IsHighSurrogate(ch) && i + 1 < profile.Text.Length && char.IsLowSurrogate(profile.Text[i + 1]))
                    {
                        SendUnicodeChar(ch);
                        i++;
                        SendUnicodeChar(profile.Text[i]);
                    }
                    else
                    {
                        SendCharacter(ch, resolvedMode);
                    }

                    index++;
                    if (index % 10 == 0 || index == count)
                    {
                        PostStatus("状态：正在输入 " + index + "/" + count + "（" + profile.Name + "，" + GetInputModeName(resolvedMode) + "），按 Esc 可取消。");
                    }

                    if (interval > 0)
                    {
                        SleepWithCancel(interval, token);
                    }
                }

                PostFinish("输入完成。");
            }
            catch (OperationCanceledException)
            {
                PostFinish("已取消。");
            }
            catch (Exception ex)
            {
                PostFinish("发生错误：" + ex.Message);
            }
        }

        private void ContentBoxOnTextChanged(object sender, EventArgs e)
        {
            CodeLanguage language = DetectLanguage(contentBox.Text);
            string chineseHint = ContainsNonAscii(contentBox.Text) ? "；包含中文/非ASCII字符，建议使用 Unicode逐字" : "";
            detectedLabel.Text = "检测：" + language.DisplayName + chineseHint;
        }

        private FormatMode GetFormatMode()
        {
            if (formatModeBox.SelectedIndex == 1)
            {
                return FormatMode.Original;
            }

            if (formatModeBox.SelectedIndex == 2)
            {
                return FormatMode.Code;
            }

            return FormatMode.Auto;
        }

        private InputMode GetInputMode()
        {
            if (inputModeBox.SelectedIndex == 1)
            {
                return InputMode.Smart;
            }

            if (inputModeBox.SelectedIndex == 2)
            {
                return InputMode.PhysicalAscii;
            }

            if (inputModeBox.SelectedIndex == 3)
            {
                return InputMode.Paste;
            }

            return InputMode.Unicode;
        }

        private static TypingProfile BuildProfile(string text, FormatMode mode, bool tabToSpaces)
        {
            CodeLanguage language = DetectLanguage(text);
            bool useCodeMode = mode == FormatMode.Code || (mode == FormatMode.Auto && language.Kind != CodeLanguageKind.PlainText);
            string prepared = text;

            if (useCodeMode)
            {
                prepared = prepared.Replace("\r\n", "\n").Replace("\r", "\n");
            }

            if (tabToSpaces)
            {
                prepared = prepared.Replace("\t", "    ");
            }

            string name = useCodeMode ? language.DisplayName + "代码模式" : "原样格式";
            return new TypingProfile(prepared, name);
        }

        private static InputMode ResolveInputMode(string text, InputMode requestedMode)
        {
            if (requestedMode == InputMode.Smart)
            {
                return ContainsNonAscii(text) ? InputMode.Unicode : InputMode.PhysicalAscii;
            }

            return requestedMode;
        }

        private static string GetInputModeName(InputMode mode)
        {
            if (mode == InputMode.PhysicalAscii)
            {
                return "英文物理按键";
            }

            if (mode == InputMode.Paste)
            {
                return "兼容粘贴";
            }

            if (mode == InputMode.Smart)
            {
                return "智能模式";
            }

            return "Unicode逐字";
        }

        private static bool ContainsNonAscii(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] > 127)
                {
                    return true;
                }
            }

            return false;
        }

        private static CodeLanguage DetectLanguage(string text)
        {
            string sample = text.Trim();
            if (sample.Length == 0)
            {
                return CodeLanguage.Plain;
            }

            int js = 0;
            int python = 0;
            int html = 0;
            int css = 0;
            int json = 0;
            int sql = 0;
            int csharp = 0;
            int java = 0;

            AddIf(ref js, sample.IndexOf("function ", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref js, sample.IndexOf("const ", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref js, sample.IndexOf("let ", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref js, sample.IndexOf("=>", StringComparison.Ordinal) >= 0);
            AddIf(ref js, sample.IndexOf("console.log", StringComparison.OrdinalIgnoreCase) >= 0);

            AddIf(ref python, sample.IndexOf("def ", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref python, sample.IndexOf("import ", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref python, sample.IndexOf("print(", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref python, sample.IndexOf("if __name__", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref python, sample.IndexOf("    ", StringComparison.Ordinal) >= 0 && sample.IndexOf(":", StringComparison.Ordinal) >= 0);

            AddIf(ref html, sample.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref html, sample.IndexOf("</", StringComparison.Ordinal) >= 0);
            AddIf(ref html, sample.IndexOf("<div", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref html, sample.IndexOf("<script", StringComparison.OrdinalIgnoreCase) >= 0);

            AddIf(ref css, sample.IndexOf("{", StringComparison.Ordinal) >= 0 && sample.IndexOf("}", StringComparison.Ordinal) >= 0);
            AddIf(ref css, sample.IndexOf(":", StringComparison.Ordinal) >= 0 && sample.IndexOf(";", StringComparison.Ordinal) >= 0);
            AddIf(ref css, sample.IndexOf("@media", StringComparison.OrdinalIgnoreCase) >= 0);

            AddIf(ref json, StartsAndEnds(sample, "{", "}"));
            AddIf(ref json, StartsAndEnds(sample, "[", "]"));
            AddIf(ref json, sample.IndexOf("\":", StringComparison.Ordinal) >= 0);

            AddIf(ref sql, sample.IndexOf("select ", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref sql, sample.IndexOf(" from ", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref sql, sample.IndexOf(" where ", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref sql, sample.IndexOf("insert into ", StringComparison.OrdinalIgnoreCase) >= 0);

            AddIf(ref csharp, sample.IndexOf("using System", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref csharp, sample.IndexOf("namespace ", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref csharp, sample.IndexOf("public class ", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref csharp, sample.IndexOf("private ", StringComparison.OrdinalIgnoreCase) >= 0);

            AddIf(ref java, sample.IndexOf("public static void main", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref java, sample.IndexOf("system.out", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref java, sample.IndexOf("public class ", StringComparison.OrdinalIgnoreCase) >= 0);
            AddIf(ref java, sample.IndexOf("import java.", StringComparison.OrdinalIgnoreCase) >= 0);

            CodeLanguage best = CodeLanguage.Plain;
            PickBest(ref best, CodeLanguageKind.JavaScript, "JavaScript/TypeScript", js);
            PickBest(ref best, CodeLanguageKind.Python, "Python", python);
            PickBest(ref best, CodeLanguageKind.Html, "HTML/XML", html);
            PickBest(ref best, CodeLanguageKind.Css, "CSS", css);
            PickBest(ref best, CodeLanguageKind.Json, "JSON", json);
            PickBest(ref best, CodeLanguageKind.Sql, "SQL", sql);
            PickBest(ref best, CodeLanguageKind.CSharp, "C#", csharp);
            PickBest(ref best, CodeLanguageKind.Java, "Java", java);

            if (best.Score >= 2)
            {
                return best;
            }

            if ((sample.IndexOf("{", StringComparison.Ordinal) >= 0 && sample.IndexOf("}", StringComparison.Ordinal) >= 0) ||
                (sample.IndexOf("\n", StringComparison.Ordinal) >= 0 && sample.IndexOf("    ", StringComparison.Ordinal) >= 0))
            {
                return new CodeLanguage(CodeLanguageKind.Code, "代码", 2);
            }

            return CodeLanguage.Plain;
        }

        private static void AddIf(ref int score, bool condition)
        {
            if (condition)
            {
                score++;
            }
        }

        private static bool StartsAndEnds(string text, string start, string end)
        {
            return text.StartsWith(start, StringComparison.Ordinal) && text.EndsWith(end, StringComparison.Ordinal);
        }

        private static void PickBest(ref CodeLanguage best, CodeLanguageKind kind, string displayName, int score)
        {
            if (score > best.Score)
            {
                best = new CodeLanguage(kind, displayName, score);
            }
        }

        private static int CountTypingUnits(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                else if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                }

                count++;
            }

            return count;
        }

        private void PasteWithClipboard(string text, CancellationToken token)
        {
            IDataObject originalData = null;
            bool hasOriginalData = false;

            try
            {
                RunOnUiThread(delegate
                {
                    originalData = Clipboard.GetDataObject();
                    hasOriginalData = originalData != null;
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                }, token);

                SleepWithCancel(120, token);
                SendModifiedKey(Keys.V, Keys.ControlKey);
                SleepWithCancel(250, token);
            }
            finally
            {
                if (hasOriginalData)
                {
                    try
                    {
                        RunOnUiThread(delegate { Clipboard.SetDataObject(originalData, true); }, CancellationToken.None);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void RunOnUiThread(Action action, CancellationToken token)
        {
            if (IsDisposed)
            {
                return;
            }

            Exception error = null;
            using (ManualResetEventSlim completed = new ManualResetEventSlim(false))
            {
                BeginInvoke(new Action(delegate
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        completed.Set();
                    }
                }));

                while (!completed.Wait(50))
                {
                    if (token.CanBeCanceled)
                    {
                        ThrowIfCanceled(token);
                    }
                }
            }

            if (error != null)
            {
                throw new InvalidOperationException("剪贴板操作失败：" + error.Message, error);
            }
        }

        private static void SleepWithCancel(int milliseconds, CancellationToken token)
        {
            int elapsed = 0;
            while (elapsed < milliseconds)
            {
                ThrowIfCanceled(token);
                int step = Math.Min(50, milliseconds - elapsed);
                Thread.Sleep(step);
                elapsed += step;
            }
        }

        private static void ThrowIfCanceled(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsEscapeDown())
            {
                throw new OperationCanceledException(token);
            }
        }

        private void MainFormOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                CancelCurrentJob("已取消。");
            }
        }

        private void MainFormOnFormClosing(object sender, FormClosingEventArgs e)
        {
            CancelCurrentJob("已取消。");
        }

        private void CancelCurrentJob(string message)
        {
            if (cancellation == null)
            {
                return;
            }

            cancellation.Cancel();
            if (waitingForClick)
            {
                FinishJob(message);
            }
        }

        private void PostStatus(string text)
        {
            if (IsDisposed)
            {
                return;
            }

            BeginInvoke(new Action(delegate { statusLabel.Text = text; }));
        }

        private void PostFinish(string text)
        {
            if (IsDisposed)
            {
                return;
            }

            BeginInvoke(new Action(delegate { FinishJob(text); }));
        }

        private void FinishJob(string message)
        {
            waitClickTimer.Stop();
            waitingForClick = false;

            if (cancellation != null)
            {
                cancellation.Dispose();
                cancellation = null;
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            SetControlsRunning(true);
            statusLabel.Text = "状态：" + message;
        }

        private void SetControlsRunning(bool ready)
        {
            runButton.Enabled = ready;
            cancelButton.Enabled = !ready;
            contentBox.Enabled = ready;
            intervalBox.Enabled = ready;
            delayBox.Enabled = ready;
            hideWindowBox.Enabled = ready;
            tabToSpacesBox.Enabled = ready;
            formatModeBox.Enabled = ready;
            inputModeBox.Enabled = ready;
        }

        private static void SendCharacter(char ch, InputMode mode)
        {
            if (mode == InputMode.PhysicalAscii)
            {
                if (TrySendPhysicalChar(ch))
                {
                    return;
                }
            }

            SendUnicodeChar(ch);
        }

        private static bool TrySendPhysicalChar(char ch)
        {
            if (ch > 127)
            {
                return false;
            }

            short scan = NativeMethods.VkKeyScanEx(ch, NativeMethods.GetKeyboardLayout(0));
            if (scan == -1)
            {
                return false;
            }

            byte virtualKey = (byte)(scan & 0xff);
            byte shiftState = (byte)((scan >> 8) & 0xff);
            Keys[] modifiers = new Keys[3];
            int modifierCount = 0;

            if ((shiftState & 1) != 0)
            {
                modifiers[modifierCount++] = Keys.ShiftKey;
            }

            if ((shiftState & 2) != 0)
            {
                modifiers[modifierCount++] = Keys.ControlKey;
            }

            if ((shiftState & 4) != 0)
            {
                modifiers[modifierCount++] = Keys.Menu;
            }

            for (int i = 0; i < modifierCount; i++)
            {
                SendKey(modifiers[i], false);
            }

            SendVirtualKey((Keys)virtualKey);

            for (int i = modifierCount - 1; i >= 0; i--)
            {
                SendKey(modifiers[i], true);
            }

            return true;
        }

        private static void SendModifiedKey(Keys key, params Keys[] modifiers)
        {
            for (int i = 0; i < modifiers.Length; i++)
            {
                SendKey(modifiers[i], false);
            }

            SendVirtualKey(key);

            for (int i = modifiers.Length - 1; i >= 0; i--)
            {
                SendKey(modifiers[i], true);
            }
        }

        private static void SendVirtualKey(Keys key)
        {
            SendKey(key, false);
            SendKey(key, true);
        }

        private static void SendKey(Keys key, bool keyUp)
        {
            NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[1];
            inputs[0].type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = (ushort)key;
            inputs[0].u.ki.wScan = 0;
            inputs[0].u.ki.dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0;

            SendInputOrThrow(inputs);
        }

        private static void SendUnicodeChar(char ch)
        {
            NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[2];

            inputs[0].type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = 0;
            inputs[0].u.ki.wScan = (ushort)ch;
            inputs[0].u.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE;

            inputs[1].type = NativeMethods.INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = 0;
            inputs[1].u.ki.wScan = (ushort)ch;
            inputs[1].u.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP;

            SendInputOrThrow(inputs);
        }

        private static void SendInputOrThrow(NativeMethods.INPUT[] inputs)
        {
            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
            if (sent != inputs.Length)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private static bool IsLeftMouseDown()
        {
            return (NativeMethods.GetAsyncKeyState((int)Keys.LButton) & 0x8000) != 0;
        }

        private static bool IsEscapeDown()
        {
            return (NativeMethods.GetAsyncKeyState((int)Keys.Escape) & 0x8000) != 0;
        }
    }

    internal enum FormatMode
    {
        Auto,
        Original,
        Code,
    }

    internal enum InputMode
    {
        Unicode,
        Smart,
        PhysicalAscii,
        Paste,
    }

    internal enum CodeLanguageKind
    {
        PlainText,
        Code,
        JavaScript,
        Python,
        Html,
        Css,
        Json,
        Sql,
        CSharp,
        Java,
    }

    internal sealed class TypingProfile
    {
        internal TypingProfile(string text, string name)
        {
            Text = text;
            Name = name;
        }

        internal readonly string Text;
        internal readonly string Name;
    }

    internal struct CodeLanguage
    {
        internal static readonly CodeLanguage Plain = new CodeLanguage(CodeLanguageKind.PlainText, "普通文本", 0);

        internal CodeLanguage(CodeLanguageKind kind, string displayName, int score)
        {
            Kind = kind;
            DisplayName = displayName;
            Score = score;
        }

        internal readonly CodeLanguageKind Kind;
        internal readonly string DisplayName;
        internal readonly int Score;
    }

    internal static class NativeMethods
    {
        internal const uint INPUT_KEYBOARD = 1;
        internal const uint KEYEVENTF_KEYUP = 0x0002;
        internal const uint KEYEVENTF_UNICODE = 0x0004;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        internal static extern short VkKeyScanEx(char ch, IntPtr dwhkl);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentProcessId();

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            internal uint type;
            internal INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct INPUTUNION
        {
            [FieldOffset(0)]
            internal MOUSEINPUT mi;

            [FieldOffset(0)]
            internal KEYBDINPUT ki;

            [FieldOffset(0)]
            internal HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            internal ushort wVk;
            internal ushort wScan;
            internal uint dwFlags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            internal int dx;
            internal int dy;
            internal uint mouseData;
            internal uint dwFlags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HARDWAREINPUT
        {
            internal uint uMsg;
            internal ushort wParamL;
            internal ushort wParamH;
        }
    }
}
