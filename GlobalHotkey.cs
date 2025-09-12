// GlobalHotkey.cs
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExecutiveHangarOverlay
{
    /// <summary>
    /// Global hotkeys via WM_HOTKEY:
    ///  - F6            -> toggle overlay visibility
    ///  - Shift+F8      -> toggle click-through
    ///  - Ctrl+F8       -> begin temporary drag (disable click-through until MouseUp)
    ///  - Ctrl+Shift+F7 -> set start NOW (local override)
    ///  - Shift+F7      -> prompt manual start (ms)
    ///  - F9            -> force sync from URL
    ///  - Shift+F9      -> clear override + force sync
    ///  - Ctrl + '-'    -> scale down
    ///  - Ctrl + '='    -> scale up (max 100%)
    ///  - Ctrl + '0'    -> scale reset (100%)
    ///  - Ctrl+Alt+'-'  -> opacity down
    ///  - Ctrl+Alt+'='  -> opacity up
    ///  - Ctrl+Alt+'0'  -> opacity reset (0.92)
    /// </summary>
    internal sealed class HotkeyMessageFilter : IMessageFilter, IDisposable
    {
        private readonly IntPtr _windowHandle;

        private const int WM_HOTKEY = 0x0312;

        private const int ID_TOGGLE_OVERLAY = 0xB001;
        private const int ID_TOGGLE_CLICKTHR = 0xB002;
        private const int ID_TEMP_DRAG = 0xB003;
        private const int ID_SET_START_NOW = 0xB004;
        private const int ID_PROMPT_MANUAL = 0xB005;
        private const int ID_FORCE_SYNC = 0xB006;
        private const int ID_CLEAR_AND_SYNC = 0xB007;

        private const int ID_SCALE_DOWN = 0xB010;
        private const int ID_SCALE_UP = 0xB011;
        private const int ID_SCALE_RESET = 0xB012;

        private const int ID_OPACITY_DOWN = 0xB020;
        private const int ID_OPACITY_UP = 0xB021;
        private const int ID_OPACITY_RESET = 0xB022;

        private const uint MOD_NONE = 0x0000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_SHIFT = 0x0004;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, Keys vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event Action OnToggleOverlay;
        public event Action OnToggleClickThrough;
        public event Action OnBeginTempDrag;

        public event Action OnSetStartNow;
        public event Action OnPromptManualStart;
        public event Action OnForceSync;
        public event Action OnClearOverrideAndSync;

        public event Action OnScaleDown;
        public event Action OnScaleUp;
        public event Action OnScaleReset;

        public event Action OnOpacityDown;
        public event Action OnOpacityUp;
        public event Action OnOpacityReset;

        public HotkeyMessageFilter(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;

            // Core overlay controls
            RegisterHotKey(_windowHandle, ID_TOGGLE_OVERLAY, MOD_NONE, Keys.F6);               // F6
            RegisterHotKey(_windowHandle, ID_TOGGLE_CLICKTHR, MOD_SHIFT, Keys.F8);             // Shift+F8
            RegisterHotKey(_windowHandle, ID_TEMP_DRAG, MOD_CONTROL, Keys.F8);                 // Ctrl+F8

            // Timing controls
            RegisterHotKey(_windowHandle, ID_SET_START_NOW, MOD_CONTROL | MOD_SHIFT, Keys.F7); // Ctrl+Shift+F7
            RegisterHotKey(_windowHandle, ID_PROMPT_MANUAL, MOD_SHIFT, Keys.F7);               // Shift+F7
            RegisterHotKey(_windowHandle, ID_FORCE_SYNC, MOD_NONE, Keys.F9);                   // F9
            RegisterHotKey(_windowHandle, ID_CLEAR_AND_SYNC, MOD_SHIFT, Keys.F9);              // Shift+F9

            // Scale: Ctrl + -, Ctrl + =, Ctrl + 0
            RegisterHotKey(_windowHandle, ID_SCALE_DOWN, MOD_CONTROL, Keys.OemMinus);
            RegisterHotKey(_windowHandle, ID_SCALE_UP, MOD_CONTROL, Keys.Oemplus);
            RegisterHotKey(_windowHandle, ID_SCALE_RESET, MOD_CONTROL, Keys.D0);

            // Opacity: Ctrl+Alt + -, =, 0
            RegisterHotKey(_windowHandle, ID_OPACITY_DOWN, MOD_CONTROL | MOD_ALT, Keys.OemMinus);
            RegisterHotKey(_windowHandle, ID_OPACITY_UP, MOD_CONTROL | MOD_ALT, Keys.Oemplus);
            RegisterHotKey(_windowHandle, ID_OPACITY_RESET, MOD_CONTROL | MOD_ALT, Keys.D0);

            Application.AddMessageFilter(this);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_HOTKEY) return false;
            int id = m.WParam.ToInt32();

            if (id == ID_TOGGLE_OVERLAY) { OnToggleOverlay?.Invoke(); return true; }
            if (id == ID_TOGGLE_CLICKTHR) { OnToggleClickThrough?.Invoke(); return true; }
            if (id == ID_TEMP_DRAG) { OnBeginTempDrag?.Invoke(); return true; }

            if (id == ID_SET_START_NOW) { OnSetStartNow?.Invoke(); return true; }
            if (id == ID_PROMPT_MANUAL) { OnPromptManualStart?.Invoke(); return true; }
            if (id == ID_FORCE_SYNC) { OnForceSync?.Invoke(); return true; }
            if (id == ID_CLEAR_AND_SYNC) { OnClearOverrideAndSync?.Invoke(); return true; }

            if (id == ID_SCALE_DOWN) { OnScaleDown?.Invoke(); return true; }
            if (id == ID_SCALE_UP) { OnScaleUp?.Invoke(); return true; }
            if (id == ID_SCALE_RESET) { OnScaleReset?.Invoke(); return true; }

            if (id == ID_OPACITY_DOWN) { OnOpacityDown?.Invoke(); return true; }
            if (id == ID_OPACITY_UP) { OnOpacityUp?.Invoke(); return true; }
            if (id == ID_OPACITY_RESET) { OnOpacityReset?.Invoke(); return true; }

            return false;
        }

        public void Dispose()
        {
            Application.RemoveMessageFilter(this);

            UnregisterHotKey(_windowHandle, ID_TOGGLE_OVERLAY);
            UnregisterHotKey(_windowHandle, ID_TOGGLE_CLICKTHR);
            UnregisterHotKey(_windowHandle, ID_TEMP_DRAG);

            UnregisterHotKey(_windowHandle, ID_SET_START_NOW);
            UnregisterHotKey(_windowHandle, ID_PROMPT_MANUAL);
            UnregisterHotKey(_windowHandle, ID_FORCE_SYNC);
            UnregisterHotKey(_windowHandle, ID_CLEAR_AND_SYNC);

            UnregisterHotKey(_windowHandle, ID_SCALE_DOWN);
            UnregisterHotKey(_windowHandle, ID_SCALE_UP);
            UnregisterHotKey(_windowHandle, ID_SCALE_RESET);

            UnregisterHotKey(_windowHandle, ID_OPACITY_DOWN);
            UnregisterHotKey(_windowHandle, ID_OPACITY_UP);
            UnregisterHotKey(_windowHandle, ID_OPACITY_RESET);
        }
    }
}
