using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using OmniConnect.Desktop.ViewModels;

namespace OmniConnect.Desktop.Views;

public partial class NetworkGraphView : UserControl
{
    public NetworkGraphView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is NetworkGraphViewModel vm)
        {
            vm.Devices.CollectionChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(DrawMesh);
            Avalonia.Threading.Dispatcher.UIThread.Post(DrawMesh);
        }
    }

    private void DrawMesh()
    {
        try
        {
            if (GraphCanvas == null) return;
            GraphCanvas.Children.Clear();
            if (DataContext is not NetworkGraphViewModel vm) return;
            try { vm.ComputeGraphLayout(); } catch { }
            var list = vm.Devices.ToList();
            if (list == null || list.Count == 0) return;

            var edges = vm.ComputeEdges();
            foreach (var edge in edges)
            {
                var a = list[edge.FromIndex];
                var b = list[edge.ToIndex];
                GraphCanvas.Children.Add(new Line
                {
                    StartPoint = new Point(a.X, a.Y),
                    EndPoint = new Point(b.X, b.Y),
                    Stroke = new SolidColorBrush(Color.Parse(edge.Color)),
                    StrokeThickness = 2, Opacity = 0.5
                });
            }

            foreach (var d in list)
            {
                // Outer ring (like mobile)
                var ring = new Ellipse
                {
                    Width = 44, Height = 44,
                    Fill = new SolidColorBrush(Color.Parse(d.TransportColor ?? "#10b981")),
                    Opacity = 0.2
                };
                Canvas.SetLeft(ring, d.X - 22); Canvas.SetTop(ring, d.Y - 22);
                GraphCanvas.Children.Add(ring);

                // Main circle
                var circle = new Ellipse
                {
                    Width = 36, Height = 36,
                    Fill = new SolidColorBrush(Color.Parse("#13161d")),
                    Stroke = new SolidColorBrush(Color.Parse(d.TransportColor ?? "#10b981")),
                    StrokeThickness = 2.5
                };
                Canvas.SetLeft(circle, d.X - 18); Canvas.SetTop(circle, d.Y - 18);
                GraphCanvas.Children.Add(circle);

                // Device initial inside circle
                var txt = new TextBlock
                {
                    Text = d.Initials,
                    FontSize = 14, FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#f1f5f9")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                Canvas.SetLeft(txt, d.X - 8); Canvas.SetTop(txt, d.Y - 10);
                GraphCanvas.Children.Add(txt);

                // Name label below (like mobile)
                var nameLabel = new TextBlock
                {
                    Text = d.DeviceName,
                    FontSize = 11, FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#f1f5f9")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    Width = 120, TextWrapping = Avalonia.Media.TextWrapping.Wrap
                };
                Canvas.SetLeft(nameLabel, d.X - 60); Canvas.SetTop(nameLabel, d.Y + 24);
                GraphCanvas.Children.Add(nameLabel);

                // Transport badge (like mobile legend)
                var badge = new Border
                {
                    CornerRadius = new Avalonia.CornerRadius(4),
                    Padding = new Avalonia.Thickness(6, 2),
                    Background = new SolidColorBrush(Color.Parse("#232a45")),
                    Child = new TextBlock
                    {
                        Text = d.TransportLabel,
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.Parse(d.TransportColor ?? "#10b981")),
                        FontWeight = FontWeight.SemiBold
                    }
                };
                Canvas.SetLeft(badge, d.X - 20); Canvas.SetTop(badge, d.Y + 42);
                GraphCanvas.Children.Add(badge);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("DrawMesh: " + ex); }
    }
}
