using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CouchFixationTest
{
    /// <summary>
    /// Minimal, code-only (no XAML) log window for the couch-fixation test harness.
    ///
    /// Usage:
    ///   LogWindow.Run("Couch fixation test", log => { log("..."); ... });
    ///
    /// Mirrors PalliativeAutoPlan's LogWindow pattern: the window is shown first and the work runs
    /// in <see cref="OnContentRendered"/> on the ESAPI UI thread (ESAPI objects have thread
    /// affinity, so the work must stay on this thread). Each message pumps the dispatcher once
    /// (a "DoEvents") so the window paints live instead of staying a blank white square until the
    /// work finishes.
    /// </summary>
    public sealed class LogWindow : Window
    {
        private readonly TextBox _box;
        private readonly Button _closeButton;
        private Action<Action<string>> _work;
        private bool _started;

        public LogWindow()
        {
            Width = 820;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _box = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8)
            };
            Grid.SetRow(_box, 0);
            grid.Children.Add(_box);

            _closeButton = new Button
            {
                Content = "Close",
                IsEnabled = false,
                Width = 90,
                Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _closeButton.Click += (s, e) => Close();
            Grid.SetRow(_closeButton, 1);
            grid.Children.Add(_closeButton);

            Content = grid;
        }

        public static void Run(string title, Action<Action<string>> work)
        {
            var win = new LogWindow { Title = title, _work = work };
            win.ShowDialog();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (_started) return;   // OnContentRendered can fire more than once; run the work once
            _started = true;

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
                Title += " — finished";
                _closeButton.IsEnabled = true;
            }
        }

        private void Log(string message)
        {
            _box.AppendText((message ?? string.Empty) + Environment.NewLine);
            _box.ScrollToEnd();
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
    }
}
