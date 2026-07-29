using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SetupNoteParser
{
    /// <summary>
    /// Minimal, code-only (no XAML) log window for the setup note parser.
    ///
    /// Usage:
    ///   LogWindow.Run("Field inspector", log => { log("..."); ... });
    ///
    /// The work runs synchronously on the ESAPI main thread (as ESAPI requires for model access);
    /// lines are collected and shown in a scrollable, read-only window at the end.
    /// </summary>
    public static class LogWindow
    {
        public static void Run(string title, Action<Action<string>> body)
        {
            var sb = new StringBuilder();
            Action<string> log = line =>
            {
                sb.AppendLine(line ?? string.Empty);
                System.Diagnostics.Trace.WriteLine(line);
            };

            try
            {
                body(log);
            }
            catch (Exception ex)
            {
                log("");
                log("UNHANDLED ERROR:");
                log(ex.ToString());
            }

            Show(title, sb.ToString());
        }

        private static void Show(string title, string text)
        {
            var box = new TextBox
            {
                Text = text,
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

            var window = new Window
            {
                Title = title,
                Width = 820,
                Height = 560,
                Content = box,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            window.ShowDialog();
        }
    }
}
