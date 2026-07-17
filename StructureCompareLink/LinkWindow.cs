using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace StructureCompareLink
{
    /// <summary>
    /// The interactive linking window. Each structure set in the series is a column of structure
    /// "nodes"; the user connects a structure in one set to the same structure in another by
    /// dragging a line between them. Same-named structures are linked automatically on open.
    /// Linked structures are then exported as a semicolon-delimited CSV, grouped so that the same
    /// anatomical structure across methods (AI / manual / other) shares a group number.
    ///
    /// Built in code (no XAML) to match the repo's <see cref="TechniqueSelector"/> style and keep
    /// the build wiring to a single compile unit. The window works entirely against the
    /// <see cref="SeriesData"/> snapshot and never touches ESAPI, so it is safe on the UI thread.
    /// </summary>
    public sealed class LinkWindow
    {
        // ---- Layout constants (canvas coordinates, device-independent pixels) --------------------
        private const double LeftMargin = 20;
        private const double TopMargin = 16;
        private const double ColumnWidth = 190;
        private const double ColumnGap = 90;
        private const double HeaderHeight = 52;
        private const double NodeHeight = 24;
        private const double NodeGap = 6;
        private const double BottomPad = 24;

        // ---- Dark palette, matching LogWindow / TechniqueSelector --------------------------------
        private static readonly Brush PanelBrush = Frozen(0x1E, 0x1E, 0x1E);
        private static readonly Brush BarBrush = Frozen(0x2D, 0x2D, 0x30);
        private static readonly Brush TextBrush = Frozen(0xEE, 0xEE, 0xEE);
        private static readonly Brush DimTextBrush = Frozen(0x9A, 0x9A, 0x9A);
        private static readonly Brush NodeBrush = Frozen(0x33, 0x33, 0x38);
        private static readonly Brush NodeBorderBrush = Frozen(0x55, 0x55, 0x5A);
        private static readonly Brush EmptyNodeBrush = Frozen(0x2A, 0x2A, 0x2A);

        private static readonly Brush AutoLinkBrush = Frozen(0x4F, 0x9D, 0xD6);   // blue
        private static readonly Brush ManualLinkBrush = Frozen(0xE0, 0x8A, 0x3C); // orange
        private static readonly Brush SelectedLinkBrush = Frozen(0xE0, 0x4F, 0x4F); // red

        // ---- Model -------------------------------------------------------------------------------
        private readonly SeriesData _data;
        private readonly LinkGraph _graph = new LinkGraph();
        private readonly List<LinkNode> _nodes = new List<LinkNode>();
        private readonly Dictionary<LinkNode, NodeView> _views = new Dictionary<LinkNode, NodeView>();

        private Window _window;
        private Canvas _canvas;
        private TextBlock _statusText;
        private TextBlock _countsText;
        private CheckBox _includeUnlinkedCheck;

        // Drag-to-link state.
        private NodeView _dragSource;
        private Line _dragPreview;

        // Current selection (a drawn link the user clicked).
        private LinkEdge _selectedEdge;

        private LinkWindow(SeriesData data)
        {
            _data = data;
            foreach (StructureSetInfo set in data.Sets)
                foreach (StructureInfo s in set.Structures)
                {
                    var node = new LinkNode(set, s);
                    _nodes.Add(node);
                }
        }

        /// <summary>Builds and shows the window modally for the given series snapshot.</summary>
        public static void Show(SeriesData data)
        {
            new LinkWindow(data).Run();
        }

        private void Run()
        {
            _window = new Window
            {
                Title = $"StructureCompare — link structures  ({_data.PatientId}, series {_data.SeriesId})",
                Width = 1100,
                Height = 720,
                Background = PanelBrush,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            _window.Content = BuildContent();
            _window.KeyDown += OnKeyDown;

            // Auto-link same-named structures up front, then draw everything.
            _graph.AutoLinkByName(_nodes);
            RedrawEdges();
            UpdateStatus();

            _window.ShowDialog();
        }

        // ---- UI construction ---------------------------------------------------------------------

        private UIElement BuildContent()
        {
            var root = new DockPanel();

            root.Children.Add(BuildToolbar());
            DockPanel.SetDock((UIElement)root.Children[root.Children.Count - 1], Dock.Top);

            root.Children.Add(BuildStatusBar());
            DockPanel.SetDock((UIElement)root.Children[root.Children.Count - 1], Dock.Bottom);

            root.Children.Add(BuildScrollableCanvas());
            return root;
        }

        private UIElement BuildToolbar()
        {
            var bar = new WrapPanel { Background = BarBrush, Orientation = Orientation.Horizontal };
            bar.Children.Add(new TextBlock
            {
                Text = "Drag from a structure in one column to the matching structure in another to link them.",
                Foreground = TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 8, 16, 8),
                FontWeight = FontWeights.Bold,
            });

            bar.Children.Add(MakeButton("Auto-link by name", OnAutoLink));
            bar.Children.Add(MakeButton("Remove selected link", OnRemoveSelected));
            bar.Children.Add(MakeButton("Clear all links", OnClearAll));
            bar.Children.Add(MakeButton("Save CSV…", OnSaveCsv));
            bar.Children.Add(MakeButton("Close", (s, e) => _window.Close()));

            _includeUnlinkedCheck = new CheckBox
            {
                Content = "Include unlinked structures in CSV",
                Foreground = TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 8, 8, 8),
                IsChecked = false,
            };
            bar.Children.Add(_includeUnlinkedCheck);

            // Colour legend.
            bar.Children.Add(MakeLegend("Auto-linked", AutoLinkBrush));
            bar.Children.Add(MakeLegend("Manual link", ManualLinkBrush));
            bar.Children.Add(MakeLegend("Selected", SelectedLinkBrush));

            return bar;
        }

        private UIElement BuildStatusBar()
        {
            var panel = new DockPanel { Background = BarBrush, LastChildFill = true };

            // Counts sit on the right (persistent summary); action messages fill the left.
            _countsText = new TextBlock
            {
                Foreground = DimTextBrush,
                FontSize = 12,
                Margin = new Thickness(16, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            DockPanel.SetDock(_countsText, Dock.Right);
            panel.Children.Add(_countsText);

            _statusText = new TextBlock
            {
                Foreground = DimTextBrush,
                FontSize = 12,
                Margin = new Thickness(12, 6, 8, 6),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            panel.Children.Add(_statusText);

            return panel;
        }

        private UIElement BuildScrollableCanvas()
        {
            _canvas = new Canvas { Background = PanelBrush };
            // Clicking empty canvas clears the current selection.
            _canvas.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == _canvas) SelectEdge(null); };
            _canvas.MouseMove += OnCanvasMouseMove;
            _canvas.MouseLeftButtonUp += OnCanvasMouseUp;

            LayoutColumns();

            return new ScrollViewer
            {
                Content = _canvas,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = PanelBrush,
            };
        }

        // Places every set as a column of nodes and records each node's canvas anchor points.
        private void LayoutColumns()
        {
            int maxRows = 0;

            for (int col = 0; col < _data.Sets.Count; col++)
            {
                StructureSetInfo set = _data.Sets[col];
                double x = LeftMargin + col * (ColumnWidth + ColumnGap);

                // Column header: set id + image, and structure count.
                var header = new StackPanel { Width = ColumnWidth };
                header.Children.Add(new TextBlock
                {
                    Text = set.Id,
                    Foreground = TextBrush,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = set.Id,
                });
                header.Children.Add(new TextBlock
                {
                    Text = $"image {set.ImageId} · {set.Structures.Count} structures",
                    Foreground = DimTextBrush,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                Canvas.SetLeft(header, x);
                Canvas.SetTop(header, TopMargin);
                _canvas.Children.Add(header);

                for (int row = 0; row < set.Structures.Count; row++)
                {
                    StructureInfo s = set.Structures[row];
                    LinkNode node = _nodes.First(n => n.Set == set && n.Structure == s);

                    double y = TopMargin + HeaderHeight + row * (NodeHeight + NodeGap);
                    var view = new NodeView(node, x, y, ColumnWidth, NodeHeight);
                    _views[node] = view;

                    Border border = BuildNodeBorder(view);
                    Canvas.SetLeft(border, x);
                    Canvas.SetTop(border, y);
                    _canvas.Children.Add(border);
                    view.Border = border;
                }

                maxRows = Math.Max(maxRows, set.Structures.Count);
            }

            _canvas.Width = LeftMargin + _data.Sets.Count * (ColumnWidth + ColumnGap);
            _canvas.Height = TopMargin + HeaderHeight + maxRows * (NodeHeight + NodeGap) + BottomPad;
        }

        private Border BuildNodeBorder(NodeView view)
        {
            StructureInfo s = view.Node.Structure;
            string volText = double.IsNaN(s.VolumeCc) ? "" : $"  ({s.VolumeCc:0.#} cc)";

            var label = new TextBlock
            {
                Text = s.Id,
                Foreground = s.IsEmpty ? DimTextBrush : TextBrush,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var border = new Border
            {
                Width = view.Width,
                Height = view.Height,
                Background = s.IsEmpty ? EmptyNodeBrush : NodeBrush,
                BorderBrush = NodeBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = label,
                Cursor = Cursors.Hand,
                Tag = view,
                ToolTip = $"{s.Id}\nType: {s.DicomType}{(s.IsEmpty ? "\n(empty)" : volText)}",
            };

            border.MouseLeftButtonDown += OnNodeMouseDown;
            return border;
        }

        private Button MakeButton(string text, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = text,
                Height = 26,
                MinWidth = 90,
                Margin = new Thickness(4, 8, 4, 8),
                Padding = new Thickness(10, 0, 10, 0),
            };
            b.Click += onClick;
            return b;
        }

        private static UIElement MakeLegend(string text, Brush brush)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 8, 4, 8),
            };
            panel.Children.Add(new Rectangle
            {
                Width = 18,
                Height = 4,
                Fill = brush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            });
            panel.Children.Add(new TextBlock { Text = text, Foreground = DimTextBrush, FontSize = 11 });
            return panel;
        }

        // ---- Drag-to-link ------------------------------------------------------------------------

        private void OnNodeMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is Border border) || !(border.Tag is NodeView view)) return;

            _dragSource = view;
            Point start = view.RightAnchor;

            _dragPreview = new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = start.X,
                Y2 = start.Y,
                Stroke = ManualLinkBrush,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
            };
            _canvas.Children.Add(_dragPreview);
            _canvas.CaptureMouse();
            e.Handled = true;
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragSource == null || _dragPreview == null) return;
            Point p = e.GetPosition(_canvas);
            _dragPreview.X2 = p.X;
            _dragPreview.Y2 = p.Y;
        }

        private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragSource == null) return;

            NodeView target = HitTestNode(e.GetPosition(_canvas));
            NodeView source = _dragSource;

            // Tear down the drag first, so a failed link leaves no dangling preview.
            _canvas.ReleaseMouseCapture();
            if (_dragPreview != null) _canvas.Children.Remove(_dragPreview);
            _dragPreview = null;
            _dragSource = null;

            if (target == null || target == source) return;
            if (target.Node.Set == source.Node.Set)
            {
                SetStatus("Can't link two structures in the same set.");
                return;
            }

            LinkEdge existing = _graph.Find(source.Node, target.Node);
            if (existing != null)
            {
                SetStatus($"Already linked: '{source.Node.Structure.Id}' ↔ '{target.Node.Structure.Id}'.");
                SelectEdge(existing);
                return;
            }

            LinkEdge edge = _graph.Add(source.Node, target.Node, manual: true);
            RedrawEdges();
            SelectEdge(edge);
            SetStatus($"Linked '{source.Node.Structure.Id}' ({source.Node.Set.Id}) ↔ " +
                      $"'{target.Node.Structure.Id}' ({target.Node.Set.Id}).");
            UpdateStatus();
        }

        // Walks up the visual tree from the point to find the owning node border.
        private NodeView HitTestNode(Point p)
        {
            HitTestResult hit = VisualTreeHelper.HitTest(_canvas, p);
            DependencyObject o = hit?.VisualHit;
            while (o != null)
            {
                if (o is Border b && b.Tag is NodeView view) return view;
                o = VisualTreeHelper.GetParent(o);
            }
            return null;
        }

        // ---- Edge drawing & selection ------------------------------------------------------------

        // Rebuilds every edge line from the graph (called after any add/remove/clear).
        private void RedrawEdges()
        {
            // Remove existing edge lines (keep nodes, headers, preview).
            List<Line> old = _canvas.Children.OfType<Line>()
                .Where(l => l.Tag is LinkEdge)
                .ToList();
            foreach (Line l in old) _canvas.Children.Remove(l);

            foreach (LinkEdge edge in _graph.Edges)
            {
                if (!_views.TryGetValue(edge.A, out NodeView va) ||
                    !_views.TryGetValue(edge.B, out NodeView vb))
                    continue;

                // Connect the sides that face each other so the line stays clear of the boxes.
                bool aLeftOfB = va.X < vb.X;
                Point pa = aLeftOfB ? va.RightAnchor : va.LeftAnchor;
                Point pb = aLeftOfB ? vb.LeftAnchor : vb.RightAnchor;

                var line = new Line
                {
                    X1 = pa.X,
                    Y1 = pa.Y,
                    X2 = pb.X,
                    Y2 = pb.Y,
                    Stroke = edge.Manual ? ManualLinkBrush : AutoLinkBrush,
                    StrokeThickness = 2.5,
                    Cursor = Cursors.Hand,
                    Tag = edge,
                };
                line.MouseLeftButtonDown += (s, e) => { SelectEdge(edge); e.Handled = true; };
                line.MouseRightButtonDown += (s, e) => { RemoveEdge(edge); e.Handled = true; };

                // Draw edges under the nodes so the node boxes stay readable.
                _canvas.Children.Insert(0, line);
            }

            // Re-apply selection highlight if the selected edge still exists.
            if (_selectedEdge != null && !_graph.Edges.Contains(_selectedEdge))
                _selectedEdge = null;
            ApplyEdgeStyles();
        }

        private void SelectEdge(LinkEdge edge)
        {
            _selectedEdge = edge;
            ApplyEdgeStyles();
            if (edge != null)
                SetStatus($"Selected link '{edge.A.Structure.Id}' ↔ '{edge.B.Structure.Id}'  " +
                          "— press Delete or click 'Remove selected link' to remove it.");
        }

        private void ApplyEdgeStyles()
        {
            foreach (Line line in _canvas.Children.OfType<Line>().Where(l => l.Tag is LinkEdge))
            {
                var edge = (LinkEdge)line.Tag;
                bool selected = edge == _selectedEdge;
                line.Stroke = selected ? SelectedLinkBrush
                                       : (edge.Manual ? ManualLinkBrush : AutoLinkBrush);
                line.StrokeThickness = selected ? 4.0 : 2.5;
            }
        }

        private void RemoveEdge(LinkEdge edge)
        {
            if (edge == null) return;
            if (edge == _selectedEdge) _selectedEdge = null;
            _graph.Remove(edge);
            RedrawEdges();
            SetStatus($"Removed link '{edge.A.Structure.Id}' ↔ '{edge.B.Structure.Id}'.");
            UpdateStatus();
        }

        // ---- Toolbar / keyboard actions ----------------------------------------------------------

        private void OnAutoLink(object sender, RoutedEventArgs e)
        {
            int added = _graph.AutoLinkByName(_nodes);
            RedrawEdges();
            UpdateStatus();
            SetStatus(added > 0
                ? $"Auto-linked {added} new same-name pair(s)."
                : "No new same-name links found.");
        }

        private void OnRemoveSelected(object sender, RoutedEventArgs e)
        {
            if (_selectedEdge == null) { SetStatus("No link selected. Click a link line first."); return; }
            RemoveEdge(_selectedEdge);
        }

        private void OnClearAll(object sender, RoutedEventArgs e)
        {
            if (_graph.Edges.Count == 0) { SetStatus("There are no links to clear."); return; }
            MessageBoxResult r = MessageBox.Show(
                "Remove ALL links (auto and manual)?", "Clear all links",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            _graph.Clear();
            _selectedEdge = null;
            RedrawEdges();
            UpdateStatus();
            SetStatus("Cleared all links.");
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Delete || e.Key == Key.Back) && _selectedEdge != null)
            {
                RemoveEdge(_selectedEdge);
                e.Handled = true;
            }
        }

        private void OnSaveCsv(object sender, RoutedEventArgs e)
        {
            bool includeUnlinked = _includeUnlinkedCheck.IsChecked == true;
            List<List<LinkNode>> groups = _graph.BuildGroups(_nodes, includeUnlinked);

            if (groups.Count == 0)
            {
                MessageBox.Show(
                    "There is nothing to export. Link some structures first, or tick " +
                    "'Include unlinked structures in CSV'.",
                    "Nothing to save", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save structure links",
                Filter = "Semicolon-separated CSV (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"structure_links_{Sanitize(_data.PatientId)}_{Sanitize(_data.SeriesId)}.csv",
                AddExtension = true,
                DefaultExt = ".csv",
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(_window) != true) return;

            try
            {
                int rows = _graph.WriteCsv(dialog.FileName, _nodes, includeUnlinked);
                SetStatus($"Saved {rows} row(s) in {groups.Count} group(s) to {dialog.FileName}");
                MessageBox.Show(
                    $"Saved {rows} row(s) across {groups.Count} group(s).\n\n{dialog.FileName}",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save the CSV:\n\n" + ex.Message,
                    "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---- Status ------------------------------------------------------------------------------

        private void UpdateStatus()
        {
            int manual = _graph.Edges.Count(e => e.Manual);
            int auto = _graph.Edges.Count - manual;
            int correspondences = _graph.BuildGroups(_nodes, includeUnlinked: false).Count;
            if (_countsText != null)
                _countsText.Text = $"{_data.Sets.Count} set(s) · {_nodes.Count} structures · " +
                                   $"{_graph.Edges.Count} link(s) ({auto} auto, {manual} manual) · " +
                                   $"{correspondences} linked group(s)";
        }

        private void SetStatus(string text)
        {
            if (_statusText != null) _statusText.Text = text;
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

    /// <summary>
    /// Screen placement of one node: its canvas rectangle plus the left/right edge midpoints used
    /// as line anchors. Immutable except for the <see cref="Border"/> reference set after creation.
    /// </summary>
    internal sealed class NodeView
    {
        public LinkNode Node { get; }
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public Border Border { get; set; }

        public NodeView(LinkNode node, double x, double y, double width, double height)
        {
            Node = node;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public Point LeftAnchor { get { return new Point(X, Y + Height / 2); } }
        public Point RightAnchor { get { return new Point(X + Width, Y + Height / 2); } }
    }
}
