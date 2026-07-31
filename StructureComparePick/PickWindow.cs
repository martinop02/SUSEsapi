using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StructureComparePick
{
    /// <summary>The two sets the user chose to compare.</summary>
    public sealed class PickResult
    {
        public StructureSetInfo GroundTruth { get; set; }
        public StructureSetInfo Compare { get; set; }
    }

    /// <summary>
    /// A very simple modal dialog: two drop-downs listing the patient's structure sets — pick the
    /// ground-truth (reference) set and the set to compare against it. Returns null if cancelled.
    /// </summary>
    public static class PickWindow
    {
        private static readonly Brush Panel = Frozen(0x1E, 0x1E, 0x1E);
        private static readonly Brush Text = Frozen(0xEE, 0xEE, 0xEE);

        public static PickResult Show(IReadOnlyList<StructureSetInfo> sets)
        {
            var window = new Window
            {
                Title = "StructureCompare — pick two sets",
                Width = 520,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Panel,
            };

            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(Label("Ground truth (reference) set:"));
            ComboBox gtCombo = MakeCombo(sets);
            root.Children.Add(gtCombo);

            root.Children.Add(Label("Compare against:"));
            ComboBox cmpCombo = MakeCombo(sets);
            root.Children.Add(cmpCombo);

            // Sensible defaults: first two distinct sets.
            gtCombo.SelectedIndex = 0;
            cmpCombo.SelectedIndex = sets.Count > 1 ? 1 : 0;

            var okButton = new Button { Content = "Compare", Width = 100, Height = 26, IsDefault = true, Margin = new Thickness(0, 16, 8, 0) };
            var cancelButton = new Button { Content = "Cancel", Width = 90, Height = 26, IsCancel = true, Margin = new Thickness(0, 16, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            root.Children.Add(buttons);

            PickResult result = null;
            okButton.Click += (s, e) =>
            {
                var gt = gtCombo.SelectedItem as StructureSetInfo;
                var cmp = cmpCombo.SelectedItem as StructureSetInfo;
                if (gt == null || cmp == null) return;
                if (ReferenceEquals(gt, cmp))
                {
                    MessageBox.Show("Pick two different structure sets.", "StructureComparePick",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = new PickResult { GroundTruth = gt, Compare = cmp };
                window.DialogResult = true;
            };

            window.Content = root;
            window.ShowDialog();
            return result;
        }

        private static ComboBox MakeCombo(IReadOnlyList<StructureSetInfo> sets)
        {
            var c = new ComboBox
            {
                Margin = new Thickness(0, 4, 0, 12),
                Padding = new Thickness(6, 3, 6, 3),
                DisplayMemberPath = "Display",
            };
            foreach (StructureSetInfo s in sets) c.Items.Add(s);
            return c;
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, Foreground = Text, FontWeight = FontWeights.Bold };
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
