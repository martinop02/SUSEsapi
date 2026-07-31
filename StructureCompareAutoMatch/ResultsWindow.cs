using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace StructureCompareAutoMatch
{
    /// <summary>
    /// Read-only summary of the automatic match + metrics, with a Save button. Code-only WPF (dark
    /// theme). All matching and metric computation is done before this opens; the only interaction
    /// is choosing where to save the CSV.
    /// </summary>
    public static class ResultsWindow
    {
        private static readonly Brush Panel = Frozen(0x1E, 0x1E, 0x1E);
        private static readonly Brush Bar = Frozen(0x2D, 0x2D, 0x30);
        private static readonly Brush Text = Frozen(0xDC, 0xDC, 0xDC);

        public static void Show(string patientId, IReadOnlyList<StructureSetInfo> sets,
                                IReadOnlyList<MatchGroup> groups, IReadOnlyList<ComparisonRow> rows)
        {
            int computed = rows.Count(r => r.Metrics != null && r.Metrics.Valid);

            var window = new Window
            {
                Title = $"StructureCompare — automatic match + metrics ({patientId})",
                Width = 1000,
                Height = 680,
                Background = Panel,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };

            var root = new DockPanel();

            // ---- header (summary + ground-truth line/warning) ----
            List<string> groundTruthSets = sets.Where(s => !AutoMatcher.IsAuto(s.Id)).Select(s => s.Id).ToList();
            int aiSets = sets.Count - groundTruthSets.Count;

            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Foreground = Frozen(0xEE, 0xEE, 0xEE),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Text = $"{groups.Count} organ(s) matched across {sets.Count} structure set(s); " +
                       $"metrics computed for {computed} of {rows.Count} comparison(s).",
                TextWrapping = TextWrapping.Wrap,
            });

            string gtLine = groundTruthSets.Count == 1
                ? $"Ground truth: '{groundTruthSets[0]}'   ·   AI set(s): {aiSets}"
                : $"⚠ Expected exactly one ground-truth set (no 'auto' in the name) but found " +
                  $"{groundTruthSets.Count}: {(groundTruthSets.Count == 0 ? "(none)" : string.Join(", ", groundTruthSets))}. " +
                  "Rename the sets so exactly one lacks 'auto'.";
            headerStack.Children.Add(new TextBlock
            {
                Foreground = groundTruthSets.Count == 1 ? Frozen(0xC8, 0xC8, 0xC8) : Frozen(0xE0, 0xC0, 0x4F),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
                Text = gtLine,
                TextWrapping = TextWrapping.Wrap,
            });

            var header = new Border { Background = Bar, Padding = new Thickness(12, 8, 12, 8), Child = headerStack };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ---- footer (buttons) ----
            var footer = new Border { Background = Bar, Padding = new Thickness(12, 8, 12, 8) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var saveButton = new Button { Content = "Save CSV…", Width = 110, Height = 26, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var closeButton = new Button { Content = "Close", Width = 90, Height = 26 };
            saveButton.Click += (s, e) => Save(window, patientId, rows);
            closeButton.Click += (s, e) => window.Close();
            buttons.Children.Add(saveButton);
            buttons.Children.Add(closeButton);
            footer.Child = buttons;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ---- body ----
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

        private static string BuildReport(IReadOnlyList<ComparisonRow> rows)
        {
            if (rows.Count == 0)
                return "No organs matched across two or more structure sets.";

            var sb = new StringBuilder();
            foreach (IGrouping<int, ComparisonRow> g in rows.GroupBy(r => r.Group))
            {
                ComparisonRow first = g.First();
                sb.AppendLine($"[{first.Group}] {first.MatchKey}");
                foreach (ComparisonRow r in g)
                {
                    string gt = string.IsNullOrEmpty(r.GtStructureId) ? "(no GT)" : $"{r.GtSetId}:{r.GtStructureId}";
                    sb.AppendLine($"     GT {gt}   vs   AI {r.AiSetId}:{r.AiStructureId}");
                    MetricResult m = r.Metrics;
                    if (m != null && m.Valid)
                    {
                        sb.AppendLine(
                            $"        DICE {F(m.Dice)}  Jaccard {F(m.Jaccard)}  " +
                            $"HD {F(m.HausdorffMm)}mm  HD95 {F(m.Hd95Mm)}mm  ASSD {F(m.AssdMm)}mm");
                        sb.AppendLine(
                            $"        Vol GT/AI {F(m.VolumeGtCc)}/{F(m.VolumeAiCc)} cc  " +
                            $"(diff {F(m.VolumeDiffCc)} cc)  COM diff {F(m.ComDiffMm)} mm");
                    }
                    else
                    {
                        sb.AppendLine($"        — {r.Note}");
                    }
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static void Save(Window owner, string patientId, IReadOnlyList<ComparisonRow> rows)
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
                FileName = $"structure_metrics_{Sanitize(patientId)}.csv",
                AddExtension = true,
                DefaultExt = ".csv",
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(owner) != true) return;

            try
            {
                int n = Comparison.WriteCsv(dialog.FileName, rows);
                MessageBox.Show($"Saved {n} comparison row(s).\n\n{dialog.FileName}",
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
