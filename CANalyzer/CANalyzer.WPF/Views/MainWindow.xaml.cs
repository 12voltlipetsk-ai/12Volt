using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using CANalyzer.Core.Models;
using CANalyzer.Core.Services;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Wpf;

namespace CANalyzer.WPF.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ICANAnalyzerService _analyzerService;
        private AnalysisResult _currentAnalysisResult;
        
        // ViewModel коллекции для DataGrid
        public List<MessageViewModel> RawMessages { get; set; } = new();
        public List<SignalViewModel> DetectedSignals { get; set; } = new();
        public List<DBCMatchViewModel> DBCMatches { get; set; } = new();
        public List<CorrelationViewModel> Correlations { get; set; } = new();
        public List<PatternViewModel> Patterns { get; set; } = new();
        
        // Графики
        public PlotModel FrequencyChart { get; set; }
        public PlotModel SignalDistributionChart { get; set; }
        public PlotModel NetworkLoadChart { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            
            // Инициализируем сервис анализа
            _analyzerService = new CANAnalyzerService();
            
            // Инициализируем графики
            InitializeCharts();
            
            // Привязка данных
            DataContext = this;
            
            // Инициализация при загрузке окна
            Loaded += async (s, e) => 
            {
                try
                {
                    StatusText.Text = "Инициализация...";
                    await _analyzerService.InitializeAsync("dbc_cache.json");
                    StatusText.Text = "Готово. Загружены DBC из comma.ai и других источников";
                    UpdateFooter("Инициализация завершена");
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Ошибка инициализации";
                    MessageBox.Show($"Ошибка при инициализации: {ex.Message}", 
                                  "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
        }

        private void InitializeCharts()
        {
            // График частот сообщений
            FrequencyChart = new PlotModel { Title = "Частота сообщений" };
            FrequencyChart.Series.Add(new ColumnSeries { Title = "Частота (Гц)" });
            
            // График распределения сигналов
            SignalDistributionChart = new PlotModel { Title = "Распределение сигналов по типам" };
            SignalDistributionChart.Series.Add(new PieSeries { InnerDiameter = 0.4 });
            
            // График загрузки сети
            NetworkLoadChart = new PlotModel { Title = "Загрузка сети" };
            NetworkLoadChart.Series.Add(new LineSeries { Title = "Загрузка (%)" });
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CAN Log Files (*.csv;*.asc;*.trc)|*.csv;*.asc;*.trc|CSV files (*.csv)|*.csv|ASC files (*.asc)|*.asc|TRC files (*.trc)|*.trc|All files (*.*)|*.*",
                Title = "Открыть CAN лог файл"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Показываем прогресс
                    StatusText.Text = "Загрузка файла...";
                    ProgressBar.Visibility = Visibility.Visible;
                    ProgressBar.Value = 10;
                    
                    // Анализируем файл
                    _currentAnalysisResult = await _analyzerService.AnalyzeLogAsync(
                        dialog.FileName, 
                        AnalysisMode.Advanced
                    );
                    
                    ProgressBar.Value = 70;
                    
                    if (!_currentAnalysisResult.Success)
                    {
                        MessageBox.Show(_currentAnalysisResult.Error, "Ошибка",
                                       MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    // Обновляем интерфейс
                    await UpdateUIAsync(_currentAnalysisResult);
                    
                    ProgressBar.Value = 100;
                    StatusText.Text = $"Анализ завершен: {_currentAnalysisResult.TotalMessages} сообщений";
                    
                    MessageBox.Show($"Успешно проанализировано {_currentAnalysisResult.TotalMessages} сообщений\n" +
                                  $"Обнаружено {_currentAnalysisResult.DetectedSignalsCount} сигналов\n" +
                                  $"Найдено {_currentAnalysisResult.DBCMatches.Count} совпадений с DBC",
                                  "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при анализе файла: {ex.Message}",
                                   "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText.Text = "Ошибка";
                }
                finally
                {
                    ProgressBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async Task UpdateUIAsync(AnalysisResult result)
        {
            // Обновляем панель информации
            DataInfoText.Text = $"Файл: {Path.GetFileName(result.FilePath)}\n" +
                              $"Сообщений: {result.TotalMessages}\n" +
                              $"Уникальных ID: {result.UniqueMessageIds}\n" +
                              $"Сигналов: {result.DetectedSignalsCount}\n" +
                              $"Совпадений с DBC: {result.DBCMatches.Count}\n" +
                              $"Время анализа: {result.Duration.TotalSeconds:F1} сек";
            
            InfoPanel.Visibility = Visibility.Visible;
            
            // Заполняем таблицы
            await FillRawMessagesTableAsync();
            await FillDetectedSignalsTableAsync();
            await FillDBCMatchesTableAsync(result);
            await FillCorrelationsTableAsync(result);
            await FillPatternsTableAsync(result);
            
            // Обновляем графики
            UpdateCharts(result);
            
            // Обновляем статистику в подвале
            UpdateFooter($"Анализ завершен за {result.Duration.TotalSeconds:F1} сек");
        }

        private async Task FillRawMessagesTableAsync()
        {
            RawMessages.Clear();
            
            var messages = _analyzerService.LoadedMessages;
            if (messages == null) return;
            
            // Берем первые 500 сообщений для отображения
            var displayMessages = messages.Take(500).ToList();
            
            await Task.Run(() =>
            {
                int index = 0;
                foreach (var msg in displayMessages)
                {
                    RawMessages.Add(new MessageViewModel
                    {
                        Index = ++index,
                        Timestamp = msg.Timestamp / 1000000.0, // микросекунды -> миллисекунды
                        Id = $"0x{msg.Id:X}",
                        DLC = msg.Data.Length,
                        Data = BitConverter.ToString(msg.Data).Replace("-", " "),
                        IsExtended = msg.IsExtended,
                        IsCANFD = msg.IsCANFD,
                        Direction = msg.Direction.ToString()
                    });
                }
            });
            
            // Обновляем DataGrid
            RawMessagesGrid.ItemsSource = null;
            RawMessagesGrid.ItemsSource = RawMessages;
        }

        private async Task FillDetectedSignalsTableAsync()
        {
            DetectedSignals.Clear();
            
            var signalsByMessage = _analyzerService.DetectedSignals;
            if (signalsByMessage == null) return;
            
            await Task.Run(() =>
            {
                foreach (var kvp in signalsByMessage)
                {
                    foreach (var signal in kvp.Value)
                    {
                        DetectedSignals.Add(new SignalViewModel
                        {
                            MessageId = $"0x{signal.MessageId:X}",
                            Name = signal.Name,
                            StartBit = signal.StartBit,
                            Length = signal.Length,
                            ByteOrder = signal.ByteOrder.ToString(),
                            MinValue = signal.MinPhysical.ToString("F2"),
                            MaxValue = signal.MaxPhysical.ToString("F2"),
                            Type = signal.Type.ToString(),
                            Unit = signal.Unit
                        });
                    }
                }
            });
            
            // Сортируем по ID сообщения
            DetectedSignals.Sort((a, b) => string.Compare(a.MessageId, b.MessageId));
            
            // Обновляем DataGrid
            DetectedSignalsGrid.ItemsSource = null;
            DetectedSignalsGrid.ItemsSource = DetectedSignals;
        }

        private async Task FillDBCMatchesTableAsync(AnalysisResult result)
        {
            DBCMatches.Clear();
            
            if (result.DBCMatches == null) return;
            
            await Task.Run(() =>
            {
                foreach (var match in result.DBCMatches)
                {
                    DBCMatches.Add(new DBCMatchViewModel
                    {
                        MessageId = $"0x{match.MessageId:X}",
                        DBCName = match.DBCMessage?.Name ?? "Неизвестно",
                        SignalCount = match.DetectedSignals?.Count ?? 0,
                        DBCSignalCount = match.DBCMessage?.Signals?.Count ?? 0,
                        Confidence = $"{match.MatchConfidence:P1}",
                        Status = GetMatchStatus(match.MatchConfidence)
                    });
                }
            });
            
            // Сортируем по уверенности совпадения
            DBCMatches.Sort((a, b) => 
            {
                double aConfidence = double.Parse(a.Confidence.TrimEnd('%')) / 100;
                double bConfidence = double.Parse(b.Confidence.TrimEnd('%')) / 100;
                return bConfidence.CompareTo(aConfidence);
            });
            
            // Обновляем DataGrid
            DBCMatchesGrid.ItemsSource = null;
            DBCMatchesGrid.ItemsSource = DBCMatches;
        }

        private string GetMatchStatus(double confidence)
        {
            return confidence switch
            {
                >= 0.8 => "Высокое",
                >= 0.6 => "Среднее",
                >= 0.4 => "Низкое",
                _ => "Слабое"
            };
        }

        private async Task FillCorrelationsTableAsync(AnalysisResult result)
        {
            Correlations.Clear();
            
            if (result.Correlations == null) return;
            
            await Task.Run(() =>
            {
                foreach (var correlation in result.Correlations)
                {
                    if (correlation.Value > 0.7) // Показываем только сильные корреляции
                    {
                        Correlations.Add(new CorrelationViewModel
                        {
                            Signal1 = $"0x{correlation.Key.Item1:X}",
                            Signal2 = $"0x{correlation.Key.Item2:X}",
                            Correlation = correlation.Value.ToString("F3"),
                            Strength = GetCorrelationStrength(correlation.Value)
                        });
                    }
                }
            });
            
            // Сортируем по силе корреляции
            Correlations.Sort((a, b) => 
                double.Parse(b.Correlation).CompareTo(double.Parse(a.Correlation)));
            
            // Обновляем DataGrid
            CorrelationsGrid.ItemsSource = null;
            CorrelationsGrid.ItemsSource = Correlations;
        }

        private string GetCorrelationStrength(double value)
        {
            return Math.Abs(value) switch
            {
                >= 0.9 => "Очень сильная",
                >= 0.7 => "Сильная",
                >= 0.5 => "Умеренная",
                >= 0.3 => "Слабая",
                _ => "Очень слабая"
            };
        }

        private async Task FillPatternsTableAsync(AnalysisResult result)
        {
            Patterns.Clear();
            
            if (result.Patterns == null) return;
            
            await Task.Run(() =>
            {
                foreach (var pattern in result.Patterns)
                {
                    Patterns.Add(new PatternViewModel
                    {
                        Type = GetPatternTypeName(pattern.Type),
                        MessageId = $"0x{pattern.MessageId:X}",
                        Frequency = pattern.Frequency.ToString("F1") + " Гц",
                        Confidence = $"{pattern.Confidence:P0}",
                        Description = pattern.Description
                    });
                }
            });
            
            // Обновляем DataGrid
            PatternsGrid.ItemsSource = null;
            PatternsGrid.ItemsSource = Patterns;
        }

        private string GetPatternTypeName(PatternType type)
        {
            return type switch
            {
                PatternType.CyclicMessage => "Циклическое",
                PatternType.EventTriggered => "Событийное",
                PatternType.Diagnostic => "Диагностическое",
                PatternType.ErrorFrame => "Ошибка",
                PatternType.NetworkManagement => "Управление сетью",
                _ => "Неизвестно"
            };
        }

        private void UpdateCharts(AnalysisResult result)
        {
            // График частот
            UpdateFrequencyChart(result);
            
            // График распределения сигналов
            UpdateSignalDistributionChart();
            
            // График загрузки сети
            UpdateNetworkLoadChart(result);
        }

        private void UpdateFrequencyChart(AnalysisResult result)
        {
            var columnSeries = FrequencyChart.Series[0] as ColumnSeries;
            columnSeries?.Items.Clear();
            
            if (result.MessageFrequencies != null)
            {
                // Берем топ-10 самых частых сообщений
                var topFrequencies = result.MessageFrequencies
                    .Take(10)
                    .ToList();
                
                for (int i = 0; i < topFrequencies.Count; i++)
                {
                    columnSeries?.Items.Add(new ColumnItem(topFrequencies[i].Frequency)
                    {
                        Color = OxyColor.FromRgb(
                            (byte)(100 + i * 15), 
                            (byte)(150 + i * 10), 
                            (byte)(200 + i * 5)
                        )
                    });
                }
            }
            
            FrequencyChart.InvalidatePlot(true);
        }

        private void UpdateSignalDistributionChart()
        {
            var pieSeries = SignalDistributionChart.Series[0] as PieSeries;
            pieSeries?.Slices.Clear();
            
            if (DetectedSignals.Count > 0)
            {
                var typeGroups = DetectedSignals
                    .GroupBy(s => s.Type)
                    .Select(g => new { Type = g.Key, Count = g.Count() })
                    .ToList();
                
                var colors = new[]
                {
                    OxyColors.Blue,
                    OxyColors.Green,
                    OxyColors.Red,
                    OxyColors.Orange,
                    OxyColors.Purple,
                    OxyColors.Teal
                };
                
                for (int i = 0; i < typeGroups.Count && i < colors.Length; i++)
                {
                    pieSeries?.Slices.Add(new PieSlice(
                        $"{typeGroups[i].Type} ({typeGroups[i].Count})",
                        typeGroups[i].Count)
                    {
                        Fill = colors[i]
                    });
                }
            }
            
            SignalDistributionChart.InvalidatePlot(true);
        }

        private void UpdateNetworkLoadChart(AnalysisResult result)
        {
            var lineSeries = NetworkLoadChart.Series[0] as LineSeries;
            lineSeries?.Points.Clear();
            
            if (result.NetworkActivity != null)
            {
                // Создаем простой график загрузки
                for (int i = 0; i < 10; i++)
                {
                    double load = result.NetworkActivity.BusLoadPercentage * 
                                 (0.8 + 0.2 * Math.Sin(i * Math.PI / 5)); // Имитация изменения
                    
                    lineSeries?.Points.Add(new DataPoint(i, load));
                }
            }
            
            NetworkLoadChart.InvalidatePlot(true);
        }

        private void UpdateFooter(string message)
        {
            FooterText.Text = $"{DateTime.Now:HH:mm:ss} - {message}";
        }

        private async void AnalyzeWithDBCButton_Click(object sender, RoutedEventArgs e)
        {
            if (_analyzerService.LoadedMessages.Count == 0)
            {
                MessageBox.Show("Сначала загрузите CAN лог файл", "Информация",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            try
            {
                StatusText.Text = "Анализ с DBC и ML...";
                ProgressBar.Visibility = Visibility.Visible;
                
                // Детальный анализ с ML
                var result = await _analyzerService.AnalyzeLogAsync(
                    _currentAnalysisResult?.FilePath ?? "current",
                    AnalysisMode.Full
                );
                
                await UpdateUIAsync(result);
                
                StatusText.Text = "Анализ с DBC/ML завершен";
                MessageBox.Show($"ML анализ завершен:\n" +
                              $"Найдено {result.Patterns?.Count ?? 0} паттернов\n" +
                              $"Обнаружено {result.Correlations?.Count ?? 0} корреляций",
                              "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при анализе с DBC/ML: {ex.Message}",
                              "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка анализа";
            }
            finally
            {
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void GenerateDBCButton_Click(object sender, RoutedEventArgs e)
        {
            if (_analyzerService.DetectedSignals.Count == 0)
            {
                MessageBox.Show("Сначала выполните анализ файла", "Информация",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var saveDialog = new SaveFileDialog
            {
                Filter = "DBC files (*.dbc)|*.dbc|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"generated_{DateTime.Now:yyyyMMdd_HHmmss}.dbc",
                Title = "Сохранить DBC файл"
            };
            
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _analyzerService.SaveGeneratedDBC(saveDialog.FileName);
                    
                    MessageBox.Show($"DBC файл успешно сохранен:\n{saveDialog.FileName}",
                                  "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    UpdateFooter($"DBC сохранен: {Path.GetFileName(saveDialog.FileName)}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при сохранении DBC: {ex.Message}",
                                  "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportResultsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_analyzerService.LoadedMessages.Count == 0)
            {
                MessageBox.Show("Сначала загрузите и проанализируйте файл", "Информация",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Выберите папку для экспорта результатов",
                ShowNewFolderButton = true
            };
            
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    StatusText.Text = "Экспорт результатов...";
                    ProgressBar.Visibility = Visibility.Visible;
                    
                    _analyzerService.ExportResults(folderDialog.SelectedPath);
                    
                    MessageBox.Show($"Результаты экспортированы в папку:\n{folderDialog.SelectedPath}",
                                  "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    UpdateFooter($"Экспорт завершен: {folderDialog.SelectedPath}");
                    StatusText.Text = "Экспорт завершен";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при экспорте: {ex.Message}",
                                  "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText.Text = "Ошибка экспорта";
                }
                finally
                {
                    ProgressBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void CreateTestDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Генерация тестовых данных...";
                
                // Создаем тестовые CAN сообщения
                var testMessages = GenerateTestData();
                
                // Сохраняем во временный файл
                var tempFile = Path.GetTempFileName() + ".csv";
                SaveTestDataToCSV(testMessages, tempFile);
                
                // Анализируем тестовые данные
                _currentAnalysisResult = await _analyzerService.AnalyzeLogAsync(tempFile);
                
                await UpdateUIAsync(_currentAnalysisResult);
                
                StatusText.Text = "Тестовые данные созданы и проанализированы";
                MessageBox.Show($"Создано и проанализировано {testMessages.Count} тестовых сообщений",
                              "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Удаляем временный файл
                File.Delete(tempFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при создании тестовых данных: {ex.Message}",
                              "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка";
            }
        }

        private List<CANMessage> GenerateTestData()
        {
            var messages = new List<CANMessage>();
            var random = new Random();
            
            // Создаем различные типы тестовых сообщений
            for (int i = 0; i < 1000; i++)
            {
                var data = new byte[8];
                random.NextBytes(data);
                
                // Имитация разных типов сообщений
                uint id = (uint)(i % 20 + 0x100); // 20 разных ID
                
                // Для некоторых ID делаем циклические данные
                if (id == 0x100) data[0] = (byte)(i % 256); // Счетчик
                if (id == 0x101) data[0] = (byte)(Math.Sin(i * 0.1) * 50 + 100); // Синусоида
                if (id == 0x102) data[0] = (byte)(random.Next(0, 2)); // Флаг
                
                messages.Add(new CANMessage(id, data, (ulong)(i * 10000))); // 10 мс интервал
            }
            
            return messages;
        }

        private void SaveTestDataToCSV(List<CANMessage> messages, string filePath)
        {
            using var writer = new StreamWriter(filePath);
            writer.WriteLine("Timestamp,ID,Data");
            
            foreach (var msg in messages)
            {
                writer.WriteLine($"{msg.Timestamp},{msg.Id:X},{BitConverter.ToString(msg.Data).Replace("-", "")}");
            }
        }

        private void ClearResultsButton_Click(object sender, RoutedEventArgs e)
        {
            // Очищаем все таблицы
            RawMessages.Clear();
            DetectedSignals.Clear();
            DBCMatches.Clear();
            Correlations.Clear();
            Patterns.Clear();
            
            // Очищаем DataGrid
            RawMessagesGrid.ItemsSource = null;
            DetectedSignalsGrid.ItemsSource = null;
            DBCMatchesGrid.ItemsSource = null;
            CorrelationsGrid.ItemsSource = null;
            PatternsGrid.ItemsSource = null;
            
            // Скрываем панель информации
            InfoPanel.Visibility = Visibility.Collapsed;
            
            // Очищаем графики
            ClearCharts();
            
            StatusText.Text = "Готово";
            UpdateFooter("Результаты очищены");
        }

        private void ClearCharts()
        {
            var columnSeries = FrequencyChart.Series[0] as ColumnSeries;
            columnSeries?.Items.Clear();
            
            var pieSeries = SignalDistributionChart.Series[0] as PieSeries;
            pieSeries?.Slices.Clear();
            
            var lineSeries = NetworkLoadChart.Series[0] as LineSeries;
            lineSeries?.Points.Clear();
            
            FrequencyChart.InvalidatePlot(true);
            SignalDistributionChart.InvalidatePlot(true);
            NetworkLoadChart.InvalidatePlot(true);
        }
    }

    // ViewModel классы для привязки данных
    public class MessageViewModel
    {
        public int Index { get; set; }
        public double Timestamp { get; set; }
        public string Id { get; set; } = string.Empty;
        public int DLC { get; set; }
        public string Data { get; set; } = string.Empty;
        public bool IsExtended { get; set; }
        public bool IsCANFD { get; set; }
        public string Direction { get; set; } = string.Empty;
    }

    public class SignalViewModel
    {
        public string MessageId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int StartBit { get; set; }
        public int Length { get; set; }
        public string ByteOrder { get; set; } = string.Empty;
        public string MinValue { get; set; } = string.Empty;
        public string MaxValue { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
    }

    public class DBCMatchViewModel
    {
        public string MessageId { get; set; } = string.Empty;
        public string DBCName { get; set; } = string.Empty;
        public int SignalCount { get; set; }
        public int DBCSignalCount { get; set; }
        public string Confidence { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class CorrelationViewModel
    {
        public string Signal1 { get; set; } = string.Empty;
        public string Signal2 { get; set; } = string.Empty;
        public string Correlation { get; set; } = string.Empty;
        public string Strength { get; set; } = string.Empty;
    }

    public class PatternViewModel
    {
        public string Type { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string Frequency { get; set; } = string.Empty;
        public string Confidence { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}