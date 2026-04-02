#if UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;

namespace UnityEli.Editor
{
    /// <summary>
    /// Flashes the Unity Editor's taskbar button on Windows to notify the user
    /// that a background task (e.g. Claude response) has completed.
    /// Guarded by UNITY_EDITOR_WIN — this file compiles to nothing on macOS/Linux.
    /// </summary>
    internal static class TaskbarFlash
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        // Flash both the taskbar button and the window caption
        private const uint FLASHW_ALL = 3;
        // Flash until the window comes to the foreground
        private const uint FLASHW_TIMERNOFG = 12;

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static IntPtr _unityHwnd;

        /// <summary>
        /// Captures the Unity Editor's window handle. Call once while the editor is focused
        /// (e.g. on window enable or user interaction).
        /// </summary>
        public static void CaptureWindowHandle()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
                _unityHwnd = hwnd;
        }

        /// <summary>
        /// Flashes the taskbar button if the Unity Editor is not the foreground window.
        /// Does nothing if the editor is already focused (no point flashing).
        /// </summary>
        public static void FlashIfNotFocused()
        {
            if (_unityHwnd == IntPtr.Zero)
                return;

            // Don't flash if Unity is already the foreground window
            if (GetForegroundWindow() == _unityHwnd)
                return;

            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)),
                hwnd = _unityHwnd,
                dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                uCount = 3,
                dwTimeout = 0, // use default cursor blink rate
            };
            FlashWindowEx(ref info);
        }
    }
}
#endif
