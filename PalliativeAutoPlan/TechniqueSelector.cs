using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Small modal dialog, shown before the run, that lets the user pick the beam technique
    /// (VMAT arc vs static posterior pair). Built in code — no XAML — so it stays a single file
    /// with no extra build wiring. Dark theme to match <see cref="LogWindow"/>.
    /// </summary>
    public static class TechniqueSelector
    {
        // Dark palette shared with LogWindow.
        private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

        /// <summary>
        /// Shows the dialog and returns the chosen technique, or null if the user cancelled or
        /// closed the window (in which case the caller should abort the run).
        /// </summary>
        public static PlanTechnique? Show()
        {
            var window = new Window
            {
                Title = "PalliativeAutoPlan — technique",
                Width = 440,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Panel,
            };

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock
            {
                Text = "Beam technique:",
                Foreground = Text,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12),
            });

            var staticRadio = new RadioButton
            {
                Content = "Static posterior pair (open fields, hinge-angle search)",
                Foreground = Text,
                GroupName = "Technique",
                IsChecked = true,   // default — the active workstream
                Margin = new Thickness(0, 0, 0, 8),
            };
            var apPaRadio = new RadioButton
            {
                Content = "AP/PA parallel pair (front + back, weight-split search)",
                Foreground = Text,
                GroupName = "Technique",
                Margin = new Thickness(0, 0, 0, 8),
            };
            var vmatRadio = new RadioButton
            {
                Content = "VMAT arc (single full arc, inverse optimized)",
                Foreground = Text,
                GroupName = "Technique",
                Margin = new Thickness(0, 0, 0, 8),
            };
            var vmat2Radio = new RadioButton
            {
                Content = "2-field VMAT (dual arc: 181->179 CW coll 30 + 179->181 CCW coll 330)",
                Foreground = Text,
                GroupName = "Technique",
                Margin = new Thickness(0, 0, 0, 14),
            };
            root.Children.Add(staticRadio);
            root.Children.Add(apPaRadio);
            root.Children.Add(vmatRadio);
            root.Children.Add(vmat2Radio);

            root.Children.Add(new TextBlock
            {
                Text = "Treatment machine:",
                Foreground = Text,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
            });
            var sbh2Radio = new RadioButton
            {
                Content = RunConfig.SBH_2,
                Foreground = Text,
                GroupName = "Machine",
                IsChecked = true,   // default — the site-verified machine id
                Margin = new Thickness(0, 0, 0, 8),
            };
            var sbh3Radio = new RadioButton
            {
                Content = RunConfig.SBH3_2021,
                Foreground = Text,
                GroupName = "Machine",
            };
            root.Children.Add(sbh2Radio);
            root.Children.Add(sbh3Radio);

            var structuresGeneratedCheck = new CheckBox
            {
                Content = "Structures already generated (reuse existing plan; skip segmentation + CTV/PTV)",
                Foreground = Text,
                Margin = new Thickness(0, 14, 0, 0),   // unchecked by default = segment as usual
            };
            root.Children.Add(structuresGeneratedCheck);

            var fixationCheck = new CheckBox
            {
                Content = "Patient has fixation gear (adjust couch + segment fixation)",
                Foreground = Text,
                Margin = new Thickness(0, 8, 0, 0),   // unchecked by default = no fixation handling
            };
            root.Children.Add(fixationCheck);

            var fastCheck = new CheckBox
            {
                Content = "Fast optimization (Moderate ASC, fewer cycles, no intermediate dose)",
                Foreground = Text,
                Margin = new Thickness(0, 8, 0, 0),   // unchecked by default = higher-quality run
            };
            root.Children.Add(fastCheck);

            var okButton = new Button { Content = "OK", Width = 90, Height = 26, IsDefault = true, Margin = new Thickness(0, 16, 8, 0) };
            var cancelButton = new Button { Content = "Cancel", Width = 90, Height = 26, IsCancel = true, Margin = new Thickness(0, 16, 0, 0) };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            root.Children.Add(buttons);

            PlanTechnique? result = null;
            okButton.Click += (s, e) =>
            {
                if (staticRadio.IsChecked == true) result = PlanTechnique.StaticPair;
                else if (apPaRadio.IsChecked == true) result = PlanTechnique.ApPaPair;
                else if (vmatRadio.IsChecked == true) result = PlanTechnique.Vmat;
                else result = PlanTechnique.VmatDualArc;

                // Apply the machine + speed + fixation choices for this run.
                RunConfig.MachineId = sbh3Radio.IsChecked == true ? RunConfig.SBH3_2021 : RunConfig.SBH_2;
                RunConfig.Fast = fastCheck.IsChecked == true;
                RunConfig.Fixation = fixationCheck.IsChecked == true;
                RunConfig.StructuresAlreadyGenerated = structuresGeneratedCheck.IsChecked == true;

                window.DialogResult = true;   // closes the modal window
            };

            window.Content = root;
            window.ShowDialog();
            return result;   // null if Cancel / X / Esc (DialogResult never set true)
        }
    }
}
