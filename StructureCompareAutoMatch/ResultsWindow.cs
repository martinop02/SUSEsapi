using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace StructureCompareAutoMatch
{
    /// <summary>
    /// Read-only summary of the automatic match, with a Save button. Code-only WPF (dark theme) to
    /// match the rest of the repo. All matching is done before this opens; the only interaction is
    /// choosing where to save the CSV (the user's desired location).
    /// </summary>
    public static class ResultsWindow
    {
        private static readonly Brush Panel = Frozen(0x1E, 0x1E, 0x1E);
        private static readonly Brush Bar = Frozen(0x2D, 0x2D, 0x30);
        private static readonly Brush Text = Frozen(0xDC, 0xDC, 0xDC);

        public static void Show(string patientId, IReadOnlyList<StructureSetInfo> sets,
                                IReadOnlyList<MatchGroup> groups)
        {
            int matchedStructures = groups.Sum(g => g.Members.Count);
            int totalStructures = sets.Sum(s => s.Structures.Count);

            var window = new Window
            {
                Title = $"StructureCompare — automatic match ({patientId})",
                Width = 860,
                Height = 620,
                Background = Panel,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };

            var root = new DockPanel();

            var header = new Border { Background = Bar, Padding = new Thickness(12, 8, 12, 8) };
            header.Child = new TextBlock
            {
                Foreground = Frozen(0xEE, 0xEE, 0xEE),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Text = $"{groups.Count} organ(s) matched across {sets.Count} structure set(s) — " +
                       $"{matchedStructures} of {totalStructures} structures. Unmatched structures ignored.",
                TextWrapping = TextWrapping.Wrap,
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var footer = new Border { Background = Bar, Padding = new Thickness(12, 8, 12, 8) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var saveButton = new Button { Content = "Save CSV…", Width = 110, Height = 26, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var closeButton = new Button { Content = "Close", Width = 90, Height = 26 };
            saveButton.Click += (s, e) => Save(window, patientId, groups);
            closeButton.Click += (s, e) => window.Close();
            buttons.Children.Add(saveButton);
            buttons.Children.Add(closeButton);
            footer.Child = buttons;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var box = new TextBox
            {
                Text = BuildReport(groups),
                IsReadOnly = true,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap,
                Background = Panel,
                Foreground = Text,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(10),
            };
            root.Children.Add(box);

            window.Content = root;
            window.ShowDialog();
        }

        private static string BuildReport(IReadOnlyList<MatchGroup> groups)
        {
            if (groups.Count == 0)
                return "No organs matched across two or more structure sets.";

            var sb = new StringBuilder();
            int n = 0;
            foreach (MatchGroup g in groups)
            {
                n++;
                sb.AppendLine($"[{n}] {g.Key}   ({g.SetCount} sets)");
                foreach (MatchMember m in g.Members)
                    sb.AppendLine($"       {m.SetId}  :  {m.StructureId}{(m.IsEmpty ? "   (empty)" : "")}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static void Save(Window owner, string patientId, IReadOnlyList<MatchGroup> groups)
        {
            if (groups.Count == 0)
            {
                MessageBox.Show("There is nothing to save — no organs matched.",
                    "Nothing to save", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save matched structures",
                Filter = "Semicolon-separated CSV (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"structure_matches_{Sanitize(patientId)}.csv",
                AddExtension = true,
                DefaultExt = ".csv",
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(owner) != true) return;

            try
            {
                int rows = AutoMatcher.WriteCsv(dialog.FileName, groups);
                MessageBox.Show($"Saved {rows} row(s) across {groups.Count} matched organ(s).\n\n{dialog.FileName}",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save the CSV:\n\n" + ex.Message,
                    "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string Sanitize(string s)
        {
            var chars = (s ?? "").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            return new string(chars);
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
