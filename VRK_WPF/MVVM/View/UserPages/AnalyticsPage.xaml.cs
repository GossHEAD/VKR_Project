using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VRK_WPF.MVVM.Model;
using VRK_WPF.MVVM.Services;

namespace VRK_WPF.MVVM.View.UserPages
{
    public partial class AnalyticsPage : Page
    {
        private readonly LogManager _logManager;
        private readonly Random _random = new Random();
        
        // Sample log data for visualization
        private List<LogEntry> _logs = new List<LogEntry>();
        
        public AnalyticsPage()
        {
            InitializeComponent();
            _logManager = new LogManager(Dispatcher);
            
            // Initialize with current date
            FromDatePicker.SelectedDate = DateTime.Now.AddDays(-7);
            
            Loaded += AnalyticsPage_Loaded;
        }
        
        private async void AnalyticsPage_Loaded(object sender, RoutedEventArgs e)
        {
            await _logManager.StartMonitoringAsync();
            
            if (_logManager.Logs.Count > 0)
            {
                _logs = _logManager.Logs.ToList();
            }
            else
            {
                // If no real logs are available, generate sample data for UI visualization
                GenerateSampleLogData();
            }
            
            DrawCharts();
        }
        
        private void GenerateSampleLogData()
        {
            _logs.Clear();
            string[] levels = { "INFO", "WARNING", "ERROR", "DEBUG" };
            string[] nodes = { "Node1", "Node2", "Node3", "Node4", "Node5" };
            string[] messages = { 
                "Система запущена успешно",
                "Подключение к узлу установлено",
                "Ошибка при загрузке файла",
                "Отключение узла",
                "Фрагмент данных реплицирован успешно",
                "Изменена конфигурация узла",
                "Предупреждение: высокая загрузка сети",
                "Ошибка доступа к базе данных" 
            };
            
            // Generate 200 sample log entries
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
            
            // Sort by timestamp
            _logs = _logs.OrderBy(l => l.Timestamp).ToList();
        }
        
        private void DrawCharts()
        {
            DrawLogEventChart();
            DrawEventTypeChart();
            DrawNodeActivityChart();
            DrawErrorAnalysisChart();
        }
        
        private void DrawLogEventChart()
        {
            LogEventChart.Children.Clear();
            
            if (_logs.Count == 0)
                return;
            
            // Group logs by day
            var groupedLogs = _logs
                .GroupBy(l => l.Timestamp.Date)
                .OrderBy(g => g.Key)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToList();
            
            // Define chart dimensions
            double width = LogEventChart.ActualWidth > 0 ? LogEventChart.ActualWidth : 300;
            double height = LogEventChart.ActualHeight > 0 ? LogEventChart.ActualHeight : 200;
            double padding = 30;
            
            // Find max count for scaling
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
            
            // Add axes labels
            TextBlock yLabel = new TextBlock
            {
                Text = "Количество событий",
                RenderTransform = new RotateTransform(-90),
                FontSize = 10
            };
            Canvas.SetLeft(yLabel, 5);
            Canvas.SetTop(yLabel, height / 2);
            LogEventChart.Children.Add(yLabel);
            
            TextBlock xLabel = new TextBlock
            {
                Text = "Дата",
                FontSize = 10
            };
            Canvas.SetLeft(xLabel, width / 2);
            Canvas.SetTop(xLabel, height - 15);
            LogEventChart.Children.Add(xLabel);
            
            // Draw data points and connecting lines
            Polyline polyline = new Polyline
            {
                Stroke = Brushes.Blue,
                StrokeThickness = 2
            };
            
            PointCollection points = new PointCollection();
            
            // Calculate the spacing between points
            double xInterval = (width - 2 * padding) / (groupedLogs.Count - 1 > 0 ? groupedLogs.Count - 1 : 1);
            
            for (int i = 0; i < groupedLogs.Count; i++)
            {
                double x = padding + i * xInterval;
                double y = height - padding - (groupedLogs[i].Count * (height - 2 * padding) / maxCount);
                
                points.Add(new Point(x, y));
                
                // Add data point
                Ellipse dataPoint = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = Brushes.Blue
                };
                Canvas.SetLeft(dataPoint, x - 3);
                Canvas.SetTop(dataPoint, y - 3);
                LogEventChart.Children.Add(dataPoint);
                
                // Add date label for some points
                if (i % 3 == 0 || i == groupedLogs.Count - 1)
                {
                    TextBlock dateLabel = new TextBlock
                    {
                        Text = groupedLogs[i].Date.ToString("dd.MM"),
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
            
            // Group logs by level
            var groupedLogs = _logs
                .GroupBy(l => l.Level)
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();
            
            // Define chart dimensions
            double width = EventTypeChart.ActualWidth > 0 ? EventTypeChart.ActualWidth : 300;
            double height = EventTypeChart.ActualHeight > 0 ? EventTypeChart.ActualHeight : 200;
            double padding = 30;
            
            // Find total count for percentage calculation
            int totalCount = _logs.Count;
            
            // Define colors for each level
            Dictionary<string, Brush> levelColors = new Dictionary<string, Brush>
            {
                { "INFO", Brushes.Green },
                { "WARNING", Brushes.Orange },
                { "ERROR", Brushes.Red },
                { "DEBUG", Brushes.Blue },
                { "TRACE", Brushes.Gray }
            };
            
            // Draw pie chart
            double centerX = width / 2;
            double centerY = height / 2;
            double radius = Math.Min(width, height) / 2 - padding;
            
            double startAngle = 0;
            
            // Add title
            TextBlock title = new TextBlock
            {
                Text = "Распределение по типам",
                FontSize = 12,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(title, centerX - 60);
            Canvas.SetTop(title, 10);
            EventTypeChart.Children.Add(title);
            
            // Draw segments and labels
            for (int i = 0; i < groupedLogs.Count; i++)
            {
                var item = groupedLogs[i];
                double percentage = (double)item.Count / totalCount;
                double sweepAngle = percentage * 360;
                
                // Draw pie segment
                PathFigure figure = new PathFigure();
                figure.StartPoint = new Point(centerX, centerY);
                
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
                
                Path path = new Path();
                path.Data = geometry;
                path.Fill = levelColors.ContainsKey(item.Level) ? levelColors[item.Level] : Brushes.Gray;
                path.Stroke = Brushes.White;
                path.StrokeThickness = 1;
                
                EventTypeChart.Children.Add(path);
                
                // Add label
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
                
                // Add percentage label outside the pie
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
            
            // Group logs by node
            var groupedLogs = _logs
                .GroupBy(l => l.NodeId)
                .Select(g => new { NodeId = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();
            
            // Define chart dimensions
            double width = NodeActivityChart.ActualWidth > 0 ? NodeActivityChart.ActualWidth : 300;
            double height = NodeActivityChart.ActualHeight > 0 ? NodeActivityChart.ActualHeight : 200;
            double padding = 40;
            
            // Find max count for scaling
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
            
            NodeActivityChart.Children.Add(xAxis);
            NodeActivityChart.Children.Add(yAxis);
            
            // Add axes labels
            TextBlock yLabel = new TextBlock
            {
                Text = "Количество событий",
                RenderTransform = new RotateTransform(-90),
                FontSize = 10
            };
            Canvas.SetLeft(yLabel, 5);
            Canvas.SetTop(yLabel, height / 2);
            NodeActivityChart.Children.Add(yLabel);
            
            TextBlock xLabel = new TextBlock
            {
                Text = "Узлы",
                FontSize = 10
            };
            Canvas.SetLeft(xLabel, width / 2);
            Canvas.SetTop(xLabel, height - 15);
            NodeActivityChart.Children.Add(xLabel);
            
            // Calculate bar width and spacing
            int numBars = groupedLogs.Count;
            double totalBarWidth = width - 2 * padding;
            double barWidth = totalBarWidth / (numBars * 2); // Allow space between bars
            
            // Draw bars
            for (int i = 0; i < numBars; i++)
            {
                var item = groupedLogs[i];
                double barHeight = (item.Count * (height - 2 * padding) / maxCount);
                
                // Calculate position
                double x = padding + i * (barWidth * 2) + barWidth / 2;
                double y = height - padding - barHeight;
                
                // Draw bar
                Rectangle bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = new SolidColorBrush(Color.FromRgb((byte)(50 + i * 40), (byte)(100 + i * 20), 220))
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
                NodeActivityChart.Children.Add(bar);
                
                // Add node label
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
                
                // Add count label
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
        
        private void DrawErrorAnalysisChart()
        {
            ErrorAnalysisChart.Children.Clear();
            
            if (_logs.Count == 0)
                return;
            
            // Filter and group error logs by message type
            var errorLogs = _logs
                .Where(l => l.Level.Contains("ERR") || l.Level.Contains("FAIL"))
                .GroupBy(l => GetErrorCategory(l.Message))
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(5)  // Top 5 error categories
                .ToList();
            
            // Define chart dimensions
            double width = ErrorAnalysisChart.ActualWidth > 0 ? ErrorAnalysisChart.ActualWidth : 300;
            double height = ErrorAnalysisChart.ActualHeight > 0 ? ErrorAnalysisChart.ActualHeight : 200;
            double padding = 40;
            
            // Add title and info text if no errors
            if (errorLogs.Count == 0)
            {
                TextBlock noErrorsText = new TextBlock
                {
                    Text = "Нет ошибок для анализа",
                    FontSize = 12,
                    TextAlignment = TextAlignment.Center,
                    Width = width
                };
                Canvas.SetTop(noErrorsText, height / 2 - 10);
                ErrorAnalysisChart.Children.Add(noErrorsText);
                return;
            }
            
            // Find max count for scaling
            int maxCount = errorLogs.Max(g => g.Count);
            
            // Draw horizontal bar chart
            
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
            
            ErrorAnalysisChart.Children.Add(xAxis);
            ErrorAnalysisChart.Children.Add(yAxis);
            
            // Add axes labels
            TextBlock xLabel = new TextBlock
            {
                Text = "Количество",
                FontSize = 10
            };
            Canvas.SetLeft(xLabel, width / 2);
            Canvas.SetTop(xLabel, height - 15);
            ErrorAnalysisChart.Children.Add(xLabel);
            
            // Calculate bar height and spacing
            int numBars = errorLogs.Count;
            double totalBarHeight = height - 2 * padding;
            double barHeight = totalBarHeight / (numBars * 1.5); // Allow space between bars
            
            // Draw bars
            for (int i = 0; i < numBars; i++)
            {
                var item = errorLogs[i];
                double barWidth = (item.Count * (width - 2 * padding) / maxCount);
                
                // Calculate position
                double x = padding;
                double y = padding + i * (barHeight * 1.5);
                
                // Draw bar
                Rectangle bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = Brushes.Red
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
                ErrorAnalysisChart.Children.Add(bar);
                
                // Add category label
                TextBlock categoryLabel = new TextBlock
                {
                    Text = TruncateText(item.Category, 20),
                    FontSize = 9,
                    TextWrapping = TextWrapping.Wrap,
                    Width = 80
                };
                Canvas.SetLeft(categoryLabel, padding - 85);
                Canvas.SetTop(categoryLabel, y);
                ErrorAnalysisChart.Children.Add(categoryLabel);
                
                // Add count label
                TextBlock countLabel = new TextBlock
                {
                    Text = item.Count.ToString(),
                    FontSize = 9
                };
                Canvas.SetLeft(countLabel, x + barWidth + 5);
                Canvas.SetTop(countLabel, y + barHeight / 2 - 5);
                ErrorAnalysisChart.Children.Add(countLabel);
            }
        }
        
        private string GetErrorCategory(string message)
        {
            // Simple categorization of error messages - in a real app, this would be more sophisticated
            if (message.Contains("база") || message.Contains("БД") || message.Contains("database"))
                return "Ошибка базы данных";
            else if (message.Contains("сеть") || message.Contains("соединение") || message.Contains("connect"))
                return "Ошибка сети";
            else if (message.Contains("файл") || message.Contains("file"))
                return "Ошибка файловой системы";
            else if (message.Contains("память") || message.Contains("memory"))
                return "Ошибка памяти";
            else if (message.Contains("доступ") || message.Contains("permission") || message.Contains("access"))
                return "Ошибка доступа";
            else
                return "Прочие ошибки";
        }
        
        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;
                
            return text.Substring(0, maxLength - 3) + "...";
        }
        
        private void ApplyFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            // Get filter values
            string selectedLevel = (LogLevelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Все";
            DateTime? fromDate = FromDatePicker.SelectedDate;
            string searchText = SearchTextBox.Text;
            
            // Apply filters to logs
            var filteredLogs = _logs;
            
            // Filter by level
            if (selectedLevel != "Все")
            {
                string levelFilter = selectedLevel switch
                {
                    "Ошибка" => "ERROR",
                    "Предупреждение" => "WARN",
                    "Информация" => "INFO",
                    "Отладка" => "DEBUG",
                    _ => ""
                };
                
                if (!string.IsNullOrEmpty(levelFilter))
                {
                    filteredLogs = filteredLogs.Where(l => l.Level.Contains(levelFilter)).ToList();
                }
            }
            
            // Filter by date
            if (fromDate.HasValue)
            {
                filteredLogs = filteredLogs.Where(l => l.Timestamp >= fromDate.Value).ToList();
            }
            
            // Filter by search text
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filteredLogs = filteredLogs.Where(l => 
                    l.Message.Contains(searchText, StringComparison.OrdinalIgnoreCase) || 
                    l.NodeId.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            
            // Update charts with filtered data
            _logs = filteredLogs;
            DrawCharts();
        }
    }
}