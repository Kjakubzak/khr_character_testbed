using System;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
#endif

namespace Samples.Shared
{
    /// <summary>
    /// Runtime OS file drag-and-drop for the Unity window — <b>Windows only</b>. Unity has no built-in runtime file
    /// drop, so this subclasses the window's message procedure (P/Invoke <c>WM_DROPFILES</c>) and raises
    /// <see cref="FilesDropped"/> on the main thread with the dropped absolute paths. On non-Windows platforms it
    /// compiles to an inert component (the event simply never fires), so callers can add it unconditionally.
    ///
    /// The hook is installed in <c>OnEnable</c> and the original window procedure is restored in <c>OnDisable</c>
    /// (which Unity also runs on destroy and on play-mode exit), so it never leaves a dangling <c>WndProc</c>. In the
    /// editor the active window is the whole editor, so while playing a drop anywhere on it is accepted — acceptable
    /// for a sample; in a standalone build it is the game window.
    /// </summary>
    public class WindowsFileDrop : MonoBehaviour
    {
#pragma warning disable 0067 // FilesDropped is only raised on Windows; inert (never invoked) on other platforms.
        /// <summary>Raised on the main thread with the absolute paths of the files dropped onto the window.</summary>
        public event Action<string[]> FilesDropped;
#pragma warning restore 0067

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int GWLP_WNDPROC = -4;
        private const uint WM_DROPFILES = 0x0233;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        // 64-bit only (Unity editor + standalone are x64); on 32-bit SetWindowLongPtr isn't exported.
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr_Set(IntPtr hWnd, int nIndex, WndProcDelegate newProc);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr_Restore(IntPtr hWnd, int nIndex, IntPtr oldProc);
        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern IntPtr CallWindowProc(IntPtr prevProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("shell32.dll")] private static extern void DragAcceptFiles(IntPtr hWnd, bool accept);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);
        [DllImport("shell32.dll")] private static extern void DragFinish(IntPtr hDrop);

        private IntPtr _hwnd;
        private IntPtr _oldProc;
        private WndProcDelegate _newProc; // held so the GC can't collect the native thunk while it's installed
        private readonly Queue<string[]> _pending = new Queue<string[]>();
        private readonly object _lock = new object();
        private bool _installed;
        // Process-global: only ONE instance may subclass the window at a time. Legacy GWLP_WNDPROC subclassing has no
        // safe nested/out-of-order teardown, so a second hook (additive load, double-add) could later restore a stale
        // procedure or leave a freed thunk installed → crash. We simply refuse to install a second hook.
        private static bool _anyInstalled;

        private void OnEnable()
        {
            if (_installed || _anyInstalled) return; // never double-hook the window
            _hwnd = GetActiveWindow();
            if (_hwnd == IntPtr.Zero) return;
            _newProc = HookProc;
            _oldProc = SetWindowLongPtr_Set(_hwnd, GWLP_WNDPROC, _newProc);
            if (_oldProc == IntPtr.Zero) { _newProc = null; return; } // failed to install — leave the window untouched
            DragAcceptFiles(_hwnd, true);
            _installed = true;
            _anyInstalled = true;
        }

        private void OnDisable()
        {
            if (!_installed) return;
            DragAcceptFiles(_hwnd, false);
            SetWindowLongPtr_Restore(_hwnd, GWLP_WNDPROC, _oldProc); // restore the original procedure — never leave a dangling hook
            _installed = false;
            _anyInstalled = false;
            _oldProc = IntPtr.Zero;
            _newProc = null;
        }

        // Runs on the main thread (Unity pumps window messages there). We still enqueue and drain in Update to keep
        // any UnityEngine calls out of the raw message path.
        private IntPtr HookProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg != WM_DROPFILES)
                return CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);

            try
            {
                uint count = DragQueryFile(wParam, 0xFFFFFFFF, null, 0);
                var paths = new List<string>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    uint len = DragQueryFile(wParam, i, null, 0); // required length, excluding the null terminator
                    var sb = new StringBuilder((int)len + 1);
                    if (DragQueryFile(wParam, i, sb, len + 1) > 0) paths.Add(sb.ToString());
                }
                if (paths.Count > 0)
                    lock (_lock) _pending.Enqueue(paths.ToArray());
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { DragFinish(wParam); }
            return IntPtr.Zero;
        }

        private void Update()
        {
            string[] batch = null;
            lock (_lock) { if (_pending.Count > 0) batch = _pending.Dequeue(); }
            if (batch != null) FilesDropped?.Invoke(batch);
        }
#endif
    }
}
