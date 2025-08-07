using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SCLOCUA
{
    internal class AntiAFK
    {
        private readonly System.Threading.Timer _timer;
        private bool _isRunning = false;
        private const int MovementDelta = 1;
        private bool moveRight = true;
        private CancellationTokenSource _cancellationTokenSource;
        private DateTime _lastInputTime;
        private int AfkThreshold;  // Випадковий поріг AFK в мілісекундах
        private readonly Random _random = new Random();

        public AntiAFK()
        {
            _timer = new System.Threading.Timer(TimerCallback, null, Timeout.Infinite, Timeout.Infinite);
            _cancellationTokenSource = new CancellationTokenSource();
            _lastInputTime = DateTime.Now;

            // Встановлюємо випадковий поріг для AFK
            SetRandomAfkThreshold();

            // Слухаємо події миші та клавіатури
            StartUserActivityListener();
        }

        public void ToggleAntiAFK(ToolStripStatusLabel statusLabel)
        {
            if (_isRunning)
            {
                // Вимикаємо таймер та скасування
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose(); // Очищаємо ресурси токену
                _cancellationTokenSource = new CancellationTokenSource(); // Перезавантажуємо токен для майбутнього використання

                statusLabel.Text = "Anti-AFK вимкнено";
                _isRunning = false;
            }
            else
            {
                // Ініціалізуємо скасування
                _cancellationTokenSource = new CancellationTokenSource();
                var token = _cancellationTokenSource.Token;

                // Запускаємо таймер з можливістю зупинки
                _timer.Change(0, 1000);  // Перевіряємо кожну секунду
                statusLabel.Text = "Anti-AFK увімкнено";
                _isRunning = true;
            }
        }

        private void TimerCallback(object state)
        {
            // Перевірка часу останньої активності
            if ((DateTime.Now - _lastInputTime).TotalMilliseconds < AfkThreshold)
            {
                return;
            }

            // Рух курсора, якщо користувач неактивний
            int delta = moveRight ? MovementDelta : -MovementDelta;
            moveRight = !moveRight;

            var inp = new INPUT
            {
                type = InputType.INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = delta,
                        dy = delta,
                        dwFlags = MouseEventFlags.MOUSEEVENTF_MOVE
                    }
                }
            };

            try
            {
                // Викликаємо SendInput для переміщення мишки
                SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during SendInput: {ex.Message}");
            }

            // Після руху мишки оновлюємо поріг AFK
            SetRandomAfkThreshold();
        }

        private void SetRandomAfkThreshold()
        {
            // Генерація випадкового значення від 1 до 60 секунд
            AfkThreshold = _random.Next(1000, 60000);  // Від 1 секунди до 60 секунд (в мілісекундах)
        }

        // Ловимо активність миші або клавіатури
        private void StartUserActivityListener()
        {
            HookManager.MouseMove += (sender, e) => _lastInputTime = DateTime.Now;
            HookManager.KeyDown += (sender, e) => _lastInputTime = DateTime.Now;
        }

        #region PInvoke

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public InputType type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public MouseEventFlags dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private enum InputType : uint
        {
            INPUT_MOUSE = 0
        }

        [Flags]
        private enum MouseEventFlags : uint
        {
            MOUSEEVENTF_MOVE = 0x0001
        }

        #endregion
    }

    // Слухач подій користувача (миша та клавіатура)
    public static class HookManager
    {
        public static event MouseEventHandler MouseMove = delegate { };
        public static event KeyEventHandler KeyDown = delegate { };

        private static LowLevelKeyboardProc _keyboardProc = HookCallback;
        private static IntPtr _keyboardHookID = IntPtr.Zero;

        private static LowLevelMouseProc _mouseProc = MouseCallback;
        private static IntPtr _mouseHookID = IntPtr.Zero;

        static HookManager()
        {
            _keyboardHookID = SetHook(_keyboardProc, WH_KEYBOARD_LL);
            _mouseHookID = SetHook(_mouseProc, WH_MOUSE_LL);
        }

        public static void Unhook()
        {
            UnhookWindowsHookEx(_keyboardHookID);
            UnhookWindowsHookEx(_mouseHookID);
        }

        private static IntPtr SetHook(Delegate proc, int hookType)
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(hookType, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                KeyDown?.Invoke(null, new KeyEventArgs(Keys.None));
            }
            return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
        }

        private static IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MouseMove?.Invoke(null, new MouseEventArgs(MouseButtons.None, 0, 0, 0, 0));
            }
            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
    }
}
