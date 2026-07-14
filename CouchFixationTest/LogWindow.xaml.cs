using System;
using System.Windows;
using System.Windows.Threading;

namespace CouchFixationTest
{
    /// <summary>
    /// Log window for the couch-fixation test harness. Same proven pattern as PalliativeAutoPlan's
    /// LogWindow: the window is shown first and the work runs in <see cref="OnContentRendered"/> on
    /// the ESAPI UI thread (ESAPI objects have thread affinity). Each message pumps the dispatcher
    /// once (a "DoEvents") so the window paints live instead of freezing.
    /// </summary>
    public partial class LogWindow : Window
    {
        private Action<Action<string>> _work;
        private string _title = "Couch fixation test";
        private bool _done;

        public LogWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the window and runs <paramref name="work"/> on the UI thread once the window has
        /// rendered, passing it a log callback. Returns when the user closes the window.
        /// </summary>
        public static void Run(string title, Action<Action<string>> work)
        {
            var win = new LogWindow { _work = work, _title = title };
            win.Title = title;
            win.TitleText.Text = title + " — running...";
            win.ShowDialog();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (_done) return;   // OnContentRendered can fire again; only run once
            _done = true;

            try
            {
                _work(Log);
            }
            catch (Exception ex)
            {
                Log("");
                Log("UNHANDLED ERROR:");
                Log(ex.ToString());
            }
            finally
            {
                TitleText.Text = _title + " — finished";
                CloseButton.IsEnabled = true;
            }
        }

        public void Log(string message)
        {
            LogBox.AppendText((message ?? string.Empty) + Environment.NewLine);
            LogBox.ScrollToEnd();
            DoEvents();
        }

        // Pump the dispatcher once so the window repaints while the work occupies the UI thread.
        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
