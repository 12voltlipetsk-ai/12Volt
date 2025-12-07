using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CANalyzer.Core.Analyzers;
using CANalyzer.Core.DBC;
using CANalyzer.Core.Models;
using CANalyzer.Core.Parsers;
using CANalyzer.ML.NeuralNetworks;

namespace CANalyzer.WPF.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly LogParser _logParser;
        private readonly StatisticalAnalyzer _statisticalAnalyzer;
        private readonly SignalDetector _signalDetector;
        private readonly DBCMatcher _dbcMatcher;
        private readonly CorrelationAnalyzer _correlationAnalyzer;
        
        public ObservableCollection<CANMessage> RawMessages { get; } = new();
        public ObservableCollection<DetectedSignalViewModel> DetectedSignals { get; } = new();
        public ObservableCollection<DBCMatchViewModel> DBCMatches { get; } = new();
        public ObservableCollection<CorrelationViewModel> Correlations { get; } = new();
        
        public MainViewModel()
        {
            _logParser = new LogParser();
            _statisticalAnalyzer = new StatisticalAnalyzer();
            _signalDetector = new SignalDetector();
            _dbcMatcher = new DBCMatcher();
            _correlationAnalyzer = new CorrelationAnalyzer();
        }
        
        public async Task InitializeAsync()
        {
            await _dbcMatcher.InitializeAsync();
        }
        
        public async Task LoadLogFileAsync(string filePath)
        {
            try
            {
                var messages = _logParser.Parse(filePath);
                RawMessages.Clear();
                
                foreach (var msg in messages.Take(1000)) // Ограничиваем для UI
                {
                    RawMessages.Add(msg);
                }
                
                // Анализ статистики
                _statisticalAnalyzer.CalculateStatistics(messages);
                var stats = _statisticalAnalyzer.GetStatistics();
                
                // Детекция сигналов
                DetectedSignals.Clear();
                foreach (var stat in stats.Values)
                {
                    var detectionResult = _signalDetector.DetectSignals(stat);
                    foreach (var signal in detectionResult.DetectedSignals)
                    {
                        DetectedSignals.Add(new DetectedSignalViewModel(signal));
                    }
                }
                
                // Сопоставление с DBC
                DBCMatches.Clear();
                foreach (var msg in messages.Take(100))
                {
                    var matchResult = await Task.Run(() => 
                        _dbcMatcher.MatchMessage(msg, messages));
                    DBCMatches.Add(new DBCMatchViewModel(matchResult));
                }
                
                // Анализ корреляций
                Correlations.Clear();
                var correlations = _correlationAnalyzer.AnalyzeCorrelations(stats);
                foreach (var corr in correlations)
                {
                    Correlations.Add(new CorrelationViewModel(corr.Key, corr.Value));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    // ViewModel классы для привязки данных
    public class DetectedSignalViewModel
    {
        public string Name { get; }
        public string MessageId { get; }
        public int StartBit { get; }
        public int Length { get; }
        public string Type { get; }
        
        public DetectedSignalViewModel(CANSignal signal)
        {
            Name = signal.Name;
            MessageId = $"0x{signal.MessageId:X}";
            StartBit = signal.StartBit;
            Length = signal.Length;
            Type = signal.Type.ToString();
        }
    }
    
    public class DBCMatchViewModel
    {
        public string MessageId { get; }
        public string Category { get; }
        public string SignalName { get; }
        public float Confidence { get; }
        public string Comment { get; }
        
        public DBCMatchViewModel(DBCMatcher.MatchResult result)
        {
            MessageId = $"0x{result.MessageId:X}";
            Category = result.Category.ToString();
            SignalName = result.SuggestedName;
            Confidence = result.Confidence;
            Comment = result.Comment;
        }
    }
    
    public class CorrelationViewModel
    {
        public string Signal1 { get; }
        public string Signal2 { get; }
        public double Correlation { get; }
        
        public CorrelationViewModel((uint, uint) ids, double correlation)
        {
            Signal1 = $"0x{ids.Item1:X}";
            Signal2 = $"0x{ids.Item2:X}";
            Correlation = correlation;
        }
    }
    
    public class ObservableObject : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, 
                new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}