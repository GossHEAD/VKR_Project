using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using VRK_WPF.MVVM.Model;
using VRK_WPF.MVVM.Services;
using Path = System.Windows.Shapes.Path;

namespace VRK_WPF.MVVM.View.UserPages
{
    public sealed partial class AnalyticsPage : Page, IDisposable
    {
        private readonly LogManager _logManager;
        private readonly Random _random = new Random();
        private List<LogEntry> _logs = new();
        private bool _disposed = false;
        private string _currentLogFilePath = string.Empty;
        
        public AnalyticsPage()
        {
            InitializeComponent();
            _logManager = new LogManager(Dispatcher);
            
            Loaded += AnalyticsPage_Loaded;
            SizeChanged += AnalyticsPage_SizeChanged;
            Unloaded += AnalyticsPage_Unloaded;
        }
        
        private void AnalyticsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }
        
        private void AnalyticsPage_Loaded(object sender, RoutedEventArgs e)
        {
            GenerateSampleLogData();
            DrawCharts();
        }
        
        private void AnalyticsPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawCharts();
        }
        
        private void GenerateSampleLogData()
        {
            _logs.Clear();
            string[] levels = ["INFO", "WARNING", "ERROR", "DEBUG"];
            string[] nodes = ["Node1", "Node2", "Node3", "Node4", "Node5"];
            string[] messages =
            [
                "System started successfully",
                "Connected to node",
                "Error loading file",
                "Node disconnected",
                "Data chunk replicated successfully",
                "Node configuration changed",
                "Warning: high network load",
                "Database access error"
            ];
            
            DateTime startDate = DateTime.Now.AddDays(-14);
            for (int i = 0; i < 200; i++)
            {
                _logs.Add(new LogEntry
                {
                    Timestamp = startDate.AddHours(_random.Next(1, 24 * 14)),
                    Level = levels[_random.Next(levels.Length)],
                    NodeId = nodes[_random.Next(nodes.Length)],
                    Message = messages[_random.Next(messages.Length)]
                });
            }
            
            _logs = _logs.OrderBy(l => l.Timestamp).ToList();
        }
        
        private async void BrowseLogFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Выберите файл журнала",
                Filter = "Текстовые файлы (*.txt)|*.txt|Файлы журнала (*.log)|*.log|Все файлы (*.*)|*.*",
                CheckFileExists = true
            };
            
            if (openFileDialog.ShowDialog() == true)
            {
                _currentLogFilePath = openFileDialog.FileName;
                SelectedFilePathText.Text = _currentLogFilePath;
                
                await LoadLogFile(_currentLogFilePath);
            }
        }
        
        private async Task LoadLogFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;
            
            _logs.Clear();
            
            try
            {
                await _logManager.SwitchLogFileAsync(filePath);
                
                if (_logManager.Logs.Count == 0)
                {
                    await _logManager.LoadLogsManuallyAsync(filePath);
                }
                
                _logs = _logManager.Logs.ToList();
                
                DrawCharts();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки файла журнала: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                
                GenerateSampleLogData();
                DrawCharts();
            }
        }
        
        private void DrawCharts()
        {
            LogEventChart.Children.Clear();
            EventTypeChart.Children.Clear();
            NodeActivityChart.Children.Clear();
            
            DrawLogEventChart();
            DrawEventTypeChart();
            DrawNodeActivityChart();
        }
        
        private void DrawLogEventChart()
        {
            LogEventChart.Children.Clear();
            
            if (_logs.Count == 0)
                return;
            
            var groupedLogs = _logs
                .GroupBy(l => l.Timestamp.Date)
                .OrderBy(g => g.Key)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToList();
            
            double width = LogEventChart.ActualWidth > 0 ? LogEventChart.ActualWidth : 300;
            double height = LogEventChart.ActualHeight > 0 ? LogEventChart.ActualHeight : 200;
            double padding = 30;
            
            int maxCount = groupedLogs.Max(g => g.Count);
            
            // Draw axes
            Line xAxis = new Line
            {
                X1 = padding,
                Y1 = height - padding,
                X2 = width - padding,
                Y2 = height - padding,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            
            Line yAxis = new Line
            {
                X1 = padding,
                Y1 = padding,
                X2 = padding,
                Y2 = height - padding,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            
            LogEventChart.Children.Add(xAxis);
            LogEventChart.Children.Add(yAxis);
            
            TextBlock yLabel = new TextBlock
            {
                Text = "Event Count",
                RenderTransform = new RotateTransform(-90),
                FontSize = 10
            };
            Canvas.SetLeft(yLabel, 5);
            Canvas.SetTop(yLabel, height / 2);
            LogEventChart.Children.Add(yLabel);
            
            TextBlock xLabel = new TextBlock
            {
                Text = "Date",
                FontSize = 10
            };
            Canvas.SetLeft(xLabel, width / 2);
            Canvas.SetTop(xLabel, height - 15);
            LogEventChart.Children.Add(xLabel);
            
            Polyline polyline = new Polyline
            {
                Stroke = Brushes.Blue,
                StrokeThickness = 2
            };
            
            var points = new PointCollection();
            
            double xInterval = (width - 2 * padding) / (groupedLogs.Count - 1 > 0 ? groupedLogs.Count - 1 : 1);
            
            for (int i = 0; i < groupedLogs.Count; i++)
            {
                double x = padding + i * xInterval;
                double y = height - padding - (groupedLogs[i].Count * (height - 2 * padding) / maxCount);
                
                points.Add(new Point(x, y));
                
                Ellipse dataPoint = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = Brushes.Blue
                };
                Canvas.SetLeft(dataPoint, x - 3);
                Canvas.SetTop(dataPoint, y - 3);
                LogEventChart.Children.Add(dataPoint);
                
                if (i % 3 == 0 || i == groupedLogs.Count - 1)
                {
                    TextBlock dateLabel = new TextBlock
                    {
                        Text = groupedLogs[i].Date.ToString("MM/dd"),
                        FontSize = 8
                    };
                    Canvas.SetLeft(dateLabel, x - 10);
                    Canvas.SetTop(dateLabel, height - padding + 5);
                    LogEventChart.Children.Add(dateLabel);
                }
            }
            
            polyline.Points = points;
            LogEventChart.Children.Add(polyline);
        }
        
        private void DrawEventTypeChart()
        {
            EventTypeChart.Children.Clear();
            
            if (_logs.Count == 0)
                return;
            
            var groupedLogs = _logs
                .GroupBy(l => l.Level)
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();
            
            double width = EventTypeChart.ActualWidth > 0 ? EventTypeChart.ActualWidth : 300;
            double height = EventTypeChart.ActualHeight > 0 ? EventTypeChart.ActualHeight : 200;
            double padding = 30;
            
            int totalCount = _logs.Count;
            
            Dictionary<string, Brush> levelColors = new Dictionary<string, Brush>
            {
                { "INFO", Brushes.Green },
                { "WARNING", Brushes.Orange },
                { "ERROR", Brushes.Red },
                { "DEBUG", Brushes.Blue },
                { "TRACE", Brushes.Gray }
            };
            
            
            double centerX = width / 2;
            double centerY = height / 2;
            double radius = Math.Min(width, height) / 2 - padding;
            
            double startAngle = 0;
            
            
            TextBlock title = new TextBlock
            {
                Text = "Event Type Distribution",
                FontSize = 12,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(title, centerX - 60);
            Canvas.SetTop(title, 10);
            EventTypeChart.Children.Add(title);
            
            foreach (var item in groupedLogs)
            {
                double percentage = (double)item.Count / totalCount;
                double sweepAngle = percentage * 360;
                
                PathFigure figure = new PathFigure
                {
                    StartPoint = new Point(centerX, centerY)
                };

                double startRad = startAngle * Math.PI / 180;
                double endRad = (startAngle + sweepAngle) * Math.PI / 180;
                
                Point startPoint = new Point(
                    centerX + Math.Cos(startRad) * radius,
                    centerY + Math.Sin(startRad) * radius);
                
                Point endPoint = new Point(
                    centerX + Math.Cos(endRad) * radius,
                    centerY + Math.Sin(endRad) * radius);
                
                figure.Segments.Add(new LineSegment(startPoint, true));
                figure.Segments.Add(new ArcSegment(
                    endPoint,
                    new Size(radius, radius),
                    0,
                    sweepAngle > 180,
                    SweepDirection.Clockwise,
                    true));
                figure.Segments.Add(new LineSegment(new Point(centerX, centerY), true));
                
                PathGeometry geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                
                Path path = new Path
                {
                    Data = geometry,
                    Fill = levelColors.TryGetValue(item.Level, out var color) ? color : Brushes.Gray,
                    Stroke = Brushes.White,
                    StrokeThickness = 1
                };

                EventTypeChart.Children.Add(path);
                
                double middleAngle = startAngle + (sweepAngle / 2);
                double middleRad = middleAngle * Math.PI / 180;
                
                double labelX = centerX + Math.Cos(middleRad) * (radius * 0.7);
                double labelY = centerY + Math.Sin(middleRad) * (radius * 0.7);
                
                TextBlock label = new TextBlock
                {
                    Text = item.Level.Replace("_", " "),
                    FontSize = 9,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center
                };
                
                Canvas.SetLeft(label, labelX - 20);
                Canvas.SetTop(label, labelY - 7);
                EventTypeChart.Children.Add(label);
                
                
                double percLabelX = centerX + Math.Cos(middleRad) * (radius + 15);
                double percLabelY = centerY + Math.Sin(middleRad) * (radius + 15);
                
                TextBlock percLabel = new TextBlock
                {
                    Text = $"{percentage:P1}",
                    FontSize = 9
                };
                
                Canvas.SetLeft(percLabel, percLabelX - 15);
                Canvas.SetTop(percLabel, percLabelY - 7);
                EventTypeChart.Children.Add(percLabel);
                
                startAngle += sweepAngle;
            }
        }
        
        private void DrawNodeActivityChart()
        {
            NodeActivityChart.Children.Clear();
            
            if (_logs.Count == 0)
                return;
            
            
            var groupedLogs = _logs
                .GroupBy(l => l.NodeId)
                .Select(g => new { NodeId = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();
            
            
            double width = NodeActivityChart.ActualWidth > 0 ? NodeActivityChart.ActualWidth : 300;
            double height = NodeActivityChart.ActualHeight > 0 ? NodeActivityChart.ActualHeight : 200;
            double padding = 40;
            
            
            int maxCount = groupedLogs.Max(g => g.Count);
            
            
            Line xAxis = new Line
            {
                X1 = padding,
                Y1 = height - padding,
                X2 = width - padding,
                Y2 = height - padding,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            
            Line yAxis = new Line
            {
                X1 = padding,
                Y1 = padding,
                X2 = padding,
                Y2 = height - padding,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            
            NodeActivityChart.Children.Add(xAxis);
            NodeActivityChart.Children.Add(yAxis);
            
            
            TextBlock yLabel = new TextBlock
            {
                Text = "Event Count",
                RenderTransform = new RotateTransform(-90),
                FontSize = 10
            };
            Canvas.SetLeft(yLabel, 5);
            Canvas.SetTop(yLabel, height / 2);
            NodeActivityChart.Children.Add(yLabel);
            
            TextBlock xLabel = new TextBlock
            {
                Text = "Nodes",
                FontSize = 10
            };
            Canvas.SetLeft(xLabel, width / 2);
            Canvas.SetTop(xLabel, height - 15);
            NodeActivityChart.Children.Add(xLabel);
            
            
            int numBars = groupedLogs.Count;
            double totalBarWidth = width - 2 * padding;
            double barWidth = totalBarWidth / (numBars * 2); 
            
            
            for (int i = 0; i < numBars; i++)
            {
                var item = groupedLogs[i];
                double barHeight = (item.Count * (height - 2 * padding) / maxCount);
                
                
                double x = padding + i * (barWidth * 2) + barWidth / 2;
                double y = height - padding - barHeight;
                
                
                Rectangle bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = new SolidColorBrush(Color.FromRgb((byte)(50 + i * 40), (byte)(100 + i * 20), 220))
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
                NodeActivityChart.Children.Add(bar);
                
                
                TextBlock nodeLabel = new TextBlock
                {
                    Text = item.NodeId,
                    FontSize = 9,
                    TextAlignment = TextAlignment.Center,
                    Width = barWidth * 2
                };
                Canvas.SetLeft(nodeLabel, x - barWidth / 2);
                Canvas.SetTop(nodeLabel, height - padding + 5);
                NodeActivityChart.Children.Add(nodeLabel);
                
                
                TextBlock countLabel = new TextBlock
                {
                    Text = item.Count.ToString(),
                    FontSize = 9,
                    TextAlignment = TextAlignment.Center,
                    Width = barWidth
                };
                Canvas.SetLeft(countLabel, x);
                Canvas.SetTop(countLabel, y - 15);
                NodeActivityChart.Children.Add(countLabel);
            }
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _logManager.StopMonitoring();
                    _logManager.Dispose();
                
                    _logs.Clear();
                    _logs = null;
                
                    LogEventChart?.Children.Clear();
                    EventTypeChart?.Children.Clear();
                    NodeActivityChart?.Children.Clear();
                
                    Loaded -= AnalyticsPage_Loaded;
                    SizeChanged -= AnalyticsPage_SizeChanged;
                    Unloaded -= AnalyticsPage_Unloaded;
                }
                _disposed = true;
            }
        }
    }
}