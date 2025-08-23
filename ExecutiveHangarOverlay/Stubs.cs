#if !FEATURE_OVERLAY
// Minimal stubs so Program.cs compiles without removing features.
// Keep API surface tiny; replace with real implementations later.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

#nullable enable

namespace ExecutiveHangarOverlay
{
    // Provides async "start time" value
    public static class StartTimeProvider
    {
        public static Task<long> ResolveAsync(CancellationToken ct = default)
        {
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return Task.FromResult(ms);
        }
    }

    public class HangarOverlayForm : Form
    {
        private double _scale = 1.0;
        private double _opacity = 0.92;

        public HangarOverlayForm(long startMs)
        {
            Text = "EX-Hangar Overlay (stub)";
            ShowInTaskbar = false;
            TopMost = true;
            Opacity = _opacity;
        }

        public void ToggleClickThrough()
        {
            int exStyle = (int)GetWindowLong(Handle, -20);
            const int WS_EX_TRANSPARENT = 0x20;
            bool clickThrough = (exStyle & WS_EX_TRANSPARENT) == 0;
            if (clickThrough) SetWindowLong(Handle, -20, exStyle | WS_EX_TRANSPARENT);
            else SetWindowLong(Handle, -20, exStyle & ~WS_EX_TRANSPARENT);
        }

        public void BeginTemporaryDragMode() { }
        public void SetStartNow() { }
        public void PromptManualStart() { }
        public Task ForceSyncAsync() => Task.CompletedTask;
        public Task ClearOverrideAndSyncAsync() => Task.CompletedTask;

        public void ScaleDown()  { _scale = Math.Max(0.5, _scale - 0.05); }
        public void ScaleUp()    { _scale = Math.Min(2.0, _scale + 0.05); }
        public void ScaleReset() { _scale = 1.0; }
        public void OpacityDown()  { _opacity = Math.Max(0.2, _opacity - 0.05); Opacity = _opacity; }
        public void OpacityUp()    { _opacity = Math.Min(1.0, _opacity + 0.05); Opacity = _opacity; }
        public void OpacityReset() { _opacity = 0.92; Opacity = _opacity; }

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }

    public sealed class HotkeyMessageFilter : IDisposable
    {
        public event Action? OnToggleOverlay;
        public event Action? OnToggleClickThrough;
        public event Action? OnBeginTempDrag;
        public event Action? OnSetStartNow;
        public event Action? OnPromptManualStart;
        public event Func<Task>? OnForceSync;
        public event Func<Task>? OnClearOverrideAndSync;
        public event Action? OnScaleDown;
        public event Action? OnScaleUp;
        public event Action? OnScaleReset;
        public event Action? OnOpacityDown;
        public event Action? OnOpacityUp;
        public event Action? OnOpacityReset;

        private readonly IntPtr _handle;
        public HotkeyMessageFilter(IntPtr handle) { _handle = handle; }

        public void TriggerToggleOverlay() => OnToggleOverlay?.Invoke();

        public void Dispose() { }
    }
}
#endif
