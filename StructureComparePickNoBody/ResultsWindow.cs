using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace StructureComparePickNoBody
{
    /// <summary>
    /// Read-only summary of the two-set comparison, with a Save button. Code-only WPF (dark theme).
    /// All metrics are computed before this opens; the only interaction is choosing where to save.
    /// </summary>
    public static class ResultsWindow
    {
        private static readonly Brush Panel = Frozen(0x1E, 0x1E, 0x1E);
        private static readonly Brush Bar = Frozen(0x2D, 0x2D, 0x30);
        private static readonly Brush Text = Frozen(0xDC, 0xDC, 0xDC);

        public static void Show(string patientId, StructureSetInfo gtSet, StructureSetInfo cmpSet,
                                IReadOnlyList<PairRow> rows)
        {
            int computed = rows.Count(r => r.Metrics != null && r.Metrics.Valid);

            var window = new Window
            {
                Title = $"StructureCompare — {gtSet.Id} vs {cmpSet.Id} ({patientId})",
                Width = 1000,
                Height = 660,
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
                TextWrapping = TextWrapping.Wrap,
                Text = $"Ground truth '{gtSet.Id}'  vs  compare '{cmpSet.Id}' — " +
                       $"{rows.Count} organ(s) matched, metrics computed for {computed}.",
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var footer = new Border { Background = Bar, Padding = new Thickness(12, 8, 12, 8) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var saveButton = new Button { Content = "Save CSV…", Width = 110, Height = 26, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var closeButton = new Button { Content = "Close", Width = 90, Height = 26 };
            saveButton.Click += (s, e) => Save(window, patientId, gtSet, cmpSet, rows);
            closeButton.Click += (s, e) => window.Close();
            buttons.Children.Add(saveButton);
            buttons.Children.Add(closeButton);
            footer.Child = buttons;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var box = new TextBox
            {
                Text = BuildReport(rows),
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

        private static string BuildReport(IReadOnlyList<PairRow> rows)
        {
            if (rows.Count == 0)
                return "No organs matched by name between the two sets.";

            var sb = new StringBuilder();
            foreach (PairRow r in rows)
            {
                sb.AppendLine($"{r.MatchKey}     GT '{r.GtStructureId}'  vs  '{r.CmpStructureId}'");
                MetricResult m = r.Metrics;
                if (m != null && m.Valid)
                {
                    sb.AppendLine(
                        $"        DICE {F(m.Dice)}  Jaccard {F(m.Jaccard)}  " +
                        $"HD {F(m.HausdorffMm)}mm  HD95 {F(m.Hd95Mm)}mm  ASSD {F(m.AssdMm)}mm");
                    sb.AppendLine(
                        $"        Vol GT/Cmp {F(m.VolumeGtCc)}/{F(m.VolumeAiCc)} cc  " +
                        $"(diff {F(m.VolumeDiffCc)} cc)  COM diff {F(m.ComDiffMm)} mm");
                }
                else
                {
                    sb.AppendLine($"        — {r.Note}");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static void Save(Window owner, string patientId, StructureSetInfo gtSet,
                                 StructureSetInfo cmpSet, IReadOnlyList<PairRow> rows)
        {
            if (rows.Count == 0)
            {
                MessageBox.Show("There is nothing to save — no organs matched.",
                    "Nothing to save", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save comparison metrics",
                Filter = "Semicolon-separated CSV (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"structure_metrics_{Sanitize(patientId)}_{Sanitize(gtSet.Id)}_vs_{Sanitize(cmpSet.Id)}.csv",
                AddExtension = true,
                DefaultExt = ".csv",
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(owner) != true) return;

            try
            {
                int n = PairCompare.WriteCsv(dialog.FileName, gtSet, cmpSet, rows);
                MessageBox.Show($"Saved {n} row(s).\n\n{dialog.FileName}",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save the CSV:\n\n" + ex.Message,
                    "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string F(double v)
        {
            return double.IsNaN(v) ? "n/a" : v.ToString("0.###", CultureInfo.InvariantCulture);
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
