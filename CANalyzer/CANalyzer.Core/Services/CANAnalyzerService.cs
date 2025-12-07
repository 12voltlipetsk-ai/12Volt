// CANalyzer.Core\Services\CANAnalyzerService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CANalyzer.Core.Analyzers;
using CANalyzer.Core.DBC;
using CANalyzer.Core.Models;
using CANalyzer.Core.Parsers;
using CANalyzer.Correlation.Analyzers;

namespace CANalyzer.Core.Services
{
    /// <summary>
    /// Основной сервис анализа CAN логов. Координирует все компоненты анализа.
    /// </summary>
    public interface ICANAnalyzerService
    {
        Task InitializeAsync(string cachePath = null);
        Task<AnalysisResult> AnalyzeLogAsync(string filePath, AnalysisMode mode = AnalysisMode.Standard);
        void SaveGeneratedDBC(string filePath);
        void ExportResults(string directoryPath);
        
        // Свойства для доступа к промежуточным данным
        IReadOnlyList<CANMessage> LoadedMessages { get; }
        IReadOnlyDictionary<uint, MessageStatistics> Statistics { get; }
        IReadOnlyDictionary<uint, List<CANSignal>> DetectedSignals { get; }
    }

    public class CANAnalyzerService : ICANAnalyzerService
    {
        private readonly ILogParser _logParser;
        private readonly StatisticalAnalyzer _statisticalAnalyzer;
        private readonly ISignalDetector _signalDetector;
        private readonly IDBCRepository _dbcRepository;
        private readonly IDBCGenerator _dbcGenerator;
        private readonly DBCMatcher _dbcMatcher;
        private readonly CorrelationAnalyzer _correlationAnalyzer;
        
        private List<CANMessage> _loadedMessages;
        private Dictionary<uint, MessageStatistics> _statistics;
        private Dictionary<uint, List<CANSignal>> _detectedSignals;
        
        public IReadOnlyList<CANMessage> LoadedMessages => _loadedMessages;
        public IReadOnlyDictionary<uint, MessageStatistics> Statistics => _statistics;
        public IReadOnlyDictionary<uint, List<CANSignal>> DetectedSignals => _detectedSignals;
        
        public CANAnalyzerService(
            ILogParser logParser = null,
            StatisticalAnalyzer statisticalAnalyzer = null,
            ISignalDetector signalDetector = null,
            IDBCRepository dbcRepository = null,
            IDBCGenerator dbcGenerator = null,
            DBCMatcher dbcMatcher = null,
            CorrelationAnalyzer correlationAnalyzer = null)
        {
            _logParser = logParser ?? new LogParser();
            _statisticalAnalyzer = statisticalAnalyzer ?? new StatisticalAnalyzer();
            _signalDetector = signalDetector ?? new SignalDetector();
            _dbcRepository = dbcRepository ?? new DBCRepository();
            _dbcGenerator = dbcGenerator ?? new DBCGenerator();
            _dbcMatcher = dbcMatcher ?? new DBCMatcher();
            _correlationAnalyzer = correlationAnalyzer ?? new CorrelationAnalyzer();
            
            _loadedMessages = new List<CANMessage>();
            _statistics = new Dictionary<uint, MessageStatistics>();
            _detectedSignals = new Dictionary<uint, List<CANSignal>>();
        }
        
        public async Task InitializeAsync(string cachePath = null)
        {
            // Загружаем известные DBC
            await _dbcRepository.LoadAllAsync();
            
            if (!string.IsNullOrEmpty(cachePath) && File.Exists(cachePath))
            {
                _dbcRepository.LoadCache(cachePath);
            }
        }
        
        public async Task<AnalysisResult> AnalyzeLogAsync(string filePath, AnalysisMode mode = AnalysisMode.Standard)
        {
            var result = new AnalysisResult
            {
                FilePath = filePath,
                StartTime = DateTime.UtcNow,
                AnalysisMode = mode
            };
            
            try
            {
                // Шаг 1: Загрузка лога
                result.Phase = "Loading log file";
                _loadedMessages = _logParser.Parse(filePath);
                result.TotalMessages = _loadedMessages.Count;
                
                if (_loadedMessages.Count == 0)
                {
                    result.Error = "No messages found in log file";
                    return result;
                }
                
                // Шаг 2: Статистический анализ
                result.Phase = "Statistical analysis";
                _statisticalAnalyzer.CalculateStatistics(_loadedMessages);
                _statistics = _statisticalAnalyzer.GetStatistics();
                result.UniqueMessageIds = _statistics.Count;
                
                // Шаг 3: Детекция сигналов
                result.Phase = "Signal detection";
                _detectedSignals = _signalDetector.DetectAllSignals(_statistics);
                result.DetectedSignalsCount = _detectedSignals.Sum(kvp => kvp.Value.Count);
                
                // Шаг 4: Сопоставление с известными DBC
                result.Phase = "DBC matching";
                var matches = await MatchWithDBCAsync();
                result.DBCMatches = matches;
                
                // Шаг 5: Генерация DBC файла
                result.Phase = "DBC generation";
                _dbcGenerator.AddDetectedSignals(_detectedSignals);
                var generatedDBC = _dbcGenerator.Generate();
                result.GeneratedDBC = generatedDBC;
                
                // Шаг 6: Дополнительный анализ (если режим Advanced или Full)
                if (mode >= AnalysisMode.Advanced)
                {
                    result.Phase = "Advanced analysis";
                    await PerformAdvancedAnalysis(result);
                }
                
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.Success = false;
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
            }
            
            return result;
        }
        
        private async Task<List<DBCMatch>> MatchWithDBCAsync()
        {
            var matches = new List<DBCMatch>();
            var tasks = new List<Task>();
            
            foreach (var kvp in _detectedSignals)
            {
                var dbcMessages = _dbcRepository.FindMessages(kvp.Key);
                foreach (var dbcMessage in dbcMessages)
                {
                    matches.Add(new DBCMatch
                    {
                        MessageId = kvp.Key,
                        DetectedSignals = kvp.Value,
                        DBCMessage = dbcMessage,
                        MatchConfidence = CalculateMatchConfidence(kvp.Value, dbcMessage)
                    });
                }
            }
            
            return matches;
        }
        
        private double CalculateMatchConfidence(List<CANSignal> detectedSignals, DBCMessage dbcMessage)
        {
            if (detectedSignals.Count == 0 || dbcMessage.Signals.Count == 0)
                return 0;
            
            int matchingSignals = 0;
            double totalSimilarity = 0;
            
            foreach (var detected in detectedSignals)
            {
                foreach (var dbcSignal in dbcMessage.Signals)
                {
                    // Вычисляем сходство по нескольким параметрам
                    double similarity = CalculateSignalSimilarity(detected, dbcSignal);
                    if (similarity > 0.6) // Порог сходства 60%
                    {
                        matchingSignals++;
                        totalSimilarity += similarity;
                        break;
                    }
                }
            }
            
            if (matchingSignals == 0)
                return 0;
            
            double positionScore = (double)matchingSignals / Math.Max(detectedSignals.Count, dbcMessage.Signals.Count);
            double similarityScore = totalSimilarity / matchingSignals;
            double countScore = 1.0 - Math.Abs(detectedSignals.Count - dbcMessage.Signals.Count) / 
                (double)Math.Max(detectedSignals.Count, dbcMessage.Signals.Count);
            
            return (positionScore * 0.4 + similarityScore * 0.4 + countScore * 0.2);
        }
        
        private double CalculateSignalSimilarity(CANSignal detected, DBCSignal dbcSignal)
        {
            double similarity = 0;
            
            // Сходство по длине (вес 40%)
            double lengthDiff = Math.Abs(detected.Length - dbcSignal.Length);
            double lengthSimilarity = 1.0 - (lengthDiff / Math.Max(detected.Length, dbcSignal.Length));
            similarity += lengthSimilarity * 0.4;
            
            // Сходство по позиции (вес 40%)
            double positionDiff = Math.Abs(detected.StartBit - dbcSignal.StartBit);
            double positionSimilarity = 1.0 - (positionDiff / 64.0); // Максимум 64 бита
            similarity += positionSimilarity * 0.4;
            
            // Сходство по диапазону значений (вес 20%)
            double detectedRange = detected.MaxPhysical - detected.MinPhysical;
            double dbcRange = dbcSignal.Maximum - dbcSignal.Minimum;
            if (dbcRange > 0 && detectedRange > 0)
            {
                double rangeSimilarity = 1.0 - Math.Abs(detectedRange - dbcRange) / Math.Max(detectedRange, dbcRange);
                similarity += rangeSimilarity * 0.2;
            }
            else
            {
                similarity += 0.2; // Если диапазон не определен, даем базовое сходство
            }
            
            return Math.Clamp(similarity, 0, 1);
        }
        
        private async Task PerformAdvancedAnalysis(AnalysisResult result)
        {
            // Анализ корреляций между сообщениями
            var correlations = _correlationAnalyzer.AnalyzeCorrelations(_statistics);
            result.Correlations = correlations;
            
            // Анализ частот
            result.MessageFrequencies = _statistics
                .Select(kvp => new MessageFrequency
                {
                    MessageId = kvp.Key,
                    Frequency = kvp.Value.Frequency,
                    Count = kvp.Value.Count
                })
                .OrderByDescending(mf => mf.Frequency)
                .ToList();
            
            // Поиск паттернов
            result.Patterns = FindPatterns();
            
            // Анализ сетевой активности
            result.NetworkActivity = AnalyzeNetworkActivity();
        }
        
        private List<Pattern> FindPatterns()
        {
            var patterns = new List<Pattern>();
            
            // Ищем циклические сообщения
            foreach (var kvp in _statistics)
            {
                if (kvp.Value.Frequency > 10) // Частота > 10 Гц
                {
                    patterns.Add(new Pattern
                    {
                        Type = PatternType.CyclicMessage,
                        MessageId = kvp.Key,
                        Frequency = kvp.Value.Frequency,
                        Confidence = 0.8,
                        Description = $"Cyclic message with {kvp.Value.Frequency:F1} Hz frequency"
                    });
                }
                
                // Ищем диагностические сообщения (редкие, но длинные)
                if (kvp.Value.Frequency < 1 && kvp.Value.DataLengths.Any(l => l >= 4))
                {
                    patterns.Add(new Pattern
                    {
                        Type = PatternType.Diagnostic,
                        MessageId = kvp.Key,
                        Frequency = kvp.Value.Frequency,
                        Confidence = 0.6,
                        Description = "Possible diagnostic message (low frequency, multi-byte)"
                    });
                }
            }
            
            return patterns;
        }
        
        private NetworkActivity AnalyzeNetworkActivity()
        {
            if (_loadedMessages.Count == 0)
                return new NetworkActivity();
                
            var firstTime = _loadedMessages.Min(m => m.Timestamp);
            var lastTime = _loadedMessages.Max(m => m.Timestamp);
            var duration = (lastTime - firstTime) / 1000000.0; // микросекунды -> секунды
            
            if (duration <= 0)
                return new NetworkActivity();
                
            return new NetworkActivity
            {
                TotalMessages = _loadedMessages.Count,
                DurationSeconds = duration,
                MessagesPerSecond = _loadedMessages.Count / duration,
                BusLoadPercentage = CalculateBusLoad(),
                PeakLoadTime = FindPeakLoad(),
                BusUtilization = CalculateBusUtilization()
            };
        }
        
        private double CalculateBusLoad()
        {
            // Упрощенный расчет загрузки шины
            // В реальности нужно учитывать битрейт, размер сообщений и т.д.
            double estimatedBitsPerSecond = _loadedMessages.Sum(m => 
                (m.Data.Length * 8 + 50) * // Данные + заголовок CAN
                (_statistics.TryGetValue(m.Id, out var stats) ? stats.Frequency : 1));
                
            double maxBitsPerSecond = 500000; // 500 kbps для CAN
            return Math.Min(estimatedBitsPerSecond / maxBitsPerSecond * 100, 100);
        }
        
        private TimeSpan FindPeakLoad()
        {
            // Простой алгоритм поиска пиковой нагрузки
            if (_loadedMessages.Count < 100)
                return TimeSpan.Zero;
                
            // Группируем сообщения по временным окнам
            var windowSize = 1000000; // 1 секунда в микросекундах
            var windowedCounts = new Dictionary<long, int>();
            
            foreach (var msg in _loadedMessages)
            {
                var window = msg.Timestamp / windowSize;
                if (!windowedCounts.ContainsKey(window))
                    windowedCounts[window] = 0;
                windowedCounts[window]++;
            }
            
            var peakWindow = windowedCounts.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
            return TimeSpan.FromMilliseconds(peakWindow.Key);
        }
        
        private double CalculateBusUtilization()
        {
            // Расчет утилизации шины
            var uniqueIds = _statistics.Count;
            var totalFrequency = _statistics.Sum(kvp => kvp.Value.Frequency);
            
            return Math.Min(uniqueIds * totalFrequency / 1000.0, 1.0);
        }
        
        public void SaveGeneratedDBC(string filePath)
        {
            _dbcGenerator.SaveToFile(filePath);
        }
        
        public void ExportResults(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);
            
            // Сохраняем DBC
            var dbcPath = Path.Combine(directoryPath, $"generated_{DateTime.Now:yyyyMMdd_HHmmss}.dbc");
            SaveGeneratedDBC(dbcPath);
            
            // Сохраняем отчет в JSON
            var reportPath = Path.Combine(directoryPath, "analysis_report.json");
            SaveReportJSON(reportPath);
            
            // Сохраняем статистику в CSV
            var statsPath = Path.Combine(directoryPath, "statistics.csv");
            SaveStatisticsCSV(statsPath);
            
            // Сохраняем обнаруженные сигналы
            var signalsPath = Path.Combine(directoryPath, "detected_signals.csv");
            SaveDetectedSignalsCSV(signalsPath);
        }
        
        private void SaveReportJSON(string filePath)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    AnalysisTime = DateTime.UtcNow,
                    TotalMessages = _loadedMessages.Count,
                    UniqueMessages = _statistics.Count,
                    DetectedSignals = _detectedSignals.Sum(kvp => kvp.Value.Count),
                    MessageFrequencies = _statistics.Select(kvp => new
                    {
                        MessageId = $"0x{kvp.Key:X}",
                        kvp.Value.Count,
                        Frequency = kvp.Value.Frequency
                    }),
                    GeneratedFiles = new[] { "generated.dbc", "statistics.csv", "detected_signals.csv" }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving JSON report: {ex.Message}");
            }
        }
        
        private void SaveStatisticsCSV(string filePath)
        {
            using var writer = new StreamWriter(filePath);
            writer.WriteLine("MessageID,Count,Frequency(Hz),MinDataLen,MaxDataLen,SignalCount,Type");
            
            foreach (var kvp in _statistics)
            {
                var signals = _detectedSignals.GetValueOrDefault(kvp.Key, new List<CANSignal>());
                var type = kvp.Value.Frequency > 10 ? "Cyclic" : "Event";
                
                writer.WriteLine($"{kvp.Key:X},{kvp.Value.Count},{kvp.Value.Frequency:F2}," +
                               $"{kvp.Value.DataLengths.Min()},{kvp.Value.DataLengths.Max()}," +
                               $"{signals.Count},{type}");
            }
        }
        
        private void SaveDetectedSignalsCSV(string filePath)
        {
            using var writer = new StreamWriter(filePath);
            writer.WriteLine("MessageID,SignalName,StartBit,Length,ByteOrder,MinValue,MaxValue,Type,Unit");
            
            foreach (var kvp in _detectedSignals)
            {
                foreach (var signal in kvp.Value)
                {
                    writer.WriteLine($"{kvp.Key:X},{signal.Name},{signal.StartBit},{signal.Length}," +
                                   $"{signal.ByteOrder},{signal.MinPhysical:F2},{signal.MaxPhysical:F2}," +
                                   $"{signal.Type},{signal.Unit}");
                }
            }
        }
    }
    
    // Модели результатов (лучше вынести в отдельный файл Models/AnalysisResult.cs)
    public class AnalysisResult
    {
        public string FilePath { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public AnalysisMode AnalysisMode { get; set; }
        public string Phase { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        
        public int TotalMessages { get; set; }
        public int UniqueMessageIds { get; set; }
        public int DetectedSignalsCount { get; set; }
        public List<DBCMatch> DBCMatches { get; set; } = new();
        public string GeneratedDBC { get; set; }
        public Dictionary<(uint, uint), double> Correlations { get; set; } = new();
        public List<MessageFrequency> MessageFrequencies { get; set; } = new();
        public List<Pattern> Patterns { get; set; } = new();
        public NetworkActivity NetworkActivity { get; set; } = new();
    }
    
    public class DBCMatch
    {
        public uint MessageId { get; set; }
        public List<CANSignal> DetectedSignals { get; set; }
        public DBCMessage DBCMessage { get; set; }
        public double MatchConfidence { get; set; }
        public string Comment { get; set; }
    }
    
    public class MessageFrequency
    {
        public uint MessageId { get; set; }
        public long Count { get; set; }
        public double Frequency { get; set; }
        public string Type { get; set; }
    }
    
    public class Pattern
    {
        public PatternType Type { get; set; }
        public uint MessageId { get; set; }
        public double Frequency { get; set; }
        public double Confidence { get; set; }
        public string Description { get; set; }
    }
    
    public class NetworkActivity
    {
        public int TotalMessages { get; set; }
        public double DurationSeconds { get; set; }
        public double MessagesPerSecond { get; set; }
        public double BusLoadPercentage { get; set; }
        public TimeSpan PeakLoadTime { get; set; }
        public double BusUtilization { get; set; }
    }
    
    public enum PatternType
    {
        CyclicMessage,
        EventTriggered,
        Diagnostic,
        ErrorFrame,
        NetworkManagement
    }
}