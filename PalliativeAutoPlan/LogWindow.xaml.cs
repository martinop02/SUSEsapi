using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// A lightweight, console-like log window that replaces the (buggy in Eclipse) Win32 console.
    ///
    /// The whole workflow runs on the ESAPI main/UI thread (ESAPI objects have thread affinity and
    /// cannot be touched from a background thread). To keep the window live without slowing the run,
    /// it does NOT repaint per message: coarse <see cref="Log"/> messages flush + repaint
    /// immediately, while high-volume output captured from Console is batched and flushed at most
    /// every <see cref="ThrottleMs"/> ms. Repaints use a single dispatcher pump (a "DoEvents"), so
    /// the cost is a handful of repaints per second regardless of how chatty the work is.
    ///
    /// During genuinely blocking ESAPI calls (OptimizeVMAT, CalculateDose, the segmentation-server
    /// wait) the thread can't pump, so the window holds the last message until the call returns —
    /// same behaviour the console had.
    /// </summary>
    public partial class LogWindow : Window
    {
        private const double ThrottleMs = 150;

        private readonly StringBuilder _pending = new StringBuilder();
        private DateTime _lastFlush = DateTime.MinValue;
        private Action<Action<string>> _work;
        private bool _done;

        public LogWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the window and runs <paramref name="work"/> on the UI thread once the window has
        /// rendered, passing it a log callback. Returns when the user closes the window.
        /// </summary>
        public static void RunWithLog(Action<Action<string>> work)
        {
            var win = new LogWindow { _work = work };
            win.ShowDialog();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (_done) return;   // OnContentRendered can fire again; only run once
            _done = true;

            // Capture Console output (e.g. the per-slice NIfTI progress) into the window too.
            TextWriter previousOut = Console.Out;
            Console.SetOut(new WindowWriter(this));
            try
            {
                _work(Log);
            }
            catch (Exception ex)
            {
                Log("FATAL ERROR: " + ex.Message);
                Log(ex.StackTrace ?? string.Empty);
            }
            finally
            {
                Console.SetOut(previousOut);
                Flush();
                MarkDone();
            }
        }

        /// <summary>Coarse progress message: appended and shown immediately.</summary>
        public void Log(string message)
        {
            Append(message + Environment.NewLine, force: true);
        }

        // Verbose, possibly high-frequency text (from Console): batched.
        internal void AppendQuiet(string text)
        {
            Append(text, force: false);
        }

        private void Append(string text, bool force)
        {
            _pending.Append(text);
            DateTime now = DateTime.UtcNow;
            if (force || (now - _lastFlush).TotalMilliseconds >= ThrottleMs)
            {
                Flush();
                _lastFlush = now;
            }
        }

        private void Flush()
        {
            if (_pending.Length > 0)
            {
                LogBox.AppendText(_pending.ToString());   // append delta only — no full re-render
                _pending.Clear();
                LogBox.ScrollToEnd();
            }
            DoEvents();
        }

        // Processes pending UI work (layout, render, input) once, then returns. This is what makes
        // the window update mid-run while the work occupies the UI thread.
        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private void MarkDone()
        {
            TitleText.Text = "PalliativeAutoPlan — finished";
            CloseButton.IsEnabled = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>Funnels Console output into the window buffer (batched, no per-write repaint).</summary>
    internal sealed class WindowWriter : TextWriter
    {
        private readonly LogWindow _window;
        public WindowWriter(LogWindow window) { _window = window; }
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) { _window.AppendQuiet(value.ToString()); }
        public override void Write(string value) { _window.AppendQuiet(value); }
        public override void WriteLine(string value) { _window.AppendQuiet(value + Environment.NewLine); }
    }
}
