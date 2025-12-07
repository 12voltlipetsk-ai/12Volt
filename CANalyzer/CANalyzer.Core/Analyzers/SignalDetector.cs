using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CANalyzer.Core.Models;

namespace CANalyzer.Core.Analyzers
{
    public interface ISignalDetector
    {
        Dictionary<uint, List<CANSignal>> DetectAllSignals(Dictionary<uint, MessageStatistics> statistics);
        List<CANSignal> DetectSignals(MessageStatistics stats);
        DetectionReport GetDetectionReport();
    }

    public class SignalDetector : ISignalDetector
    {
        private readonly DetectionConfig _config;
        private DetectionReport _report;

        public SignalDetector(DetectionConfig config = null)
        {
            _config = config ?? new DetectionConfig();
            _report = new DetectionReport();
        }

        public Dictionary<uint, List<CANSignal>> DetectAllSignals(Dictionary<uint, MessageStatistics> statistics)
        {
            _report = new DetectionReport();
            var allSignals = new ConcurrentDictionary<uint, List<CANSignal>>();
            
            if (statistics == null || statistics.Count == 0)
                return allSignals.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            Parallel.ForEach(statistics, options, kvp =>
            {
                var signals = DetectSignalsOptimized(kvp.Value);
                if (signals.Count > 0)
                {
                    allSignals[kvp.Key] = signals;
                }
                
                lock (_report)
                {
                    _report.TotalMessagesAnalyzed++;
                    _report.TotalSignalsDetected += signals.Count;
                }
            });

            return allSignals.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public List<CANSignal> DetectSignals(MessageStatistics stats)
        {
            return DetectSignalsOptimized(stats);
        }

        private List<CANSignal> DetectSignalsOptimized(MessageStatistics stats)
        {
            var signals = new List<CANSignal>();
            
            if (stats == null || stats.Samples.Count < _config.MinSamples)
                return signals;

            int byteCount = stats.Samples[0].Length;
            int bitCount = byteCount * 8;

            // Шаг 1: Анализ изменчивости битов
            var bitVariability = AnalyzeBitVariability(stats, bitCount);
            
            // Шаг 2: Поиск кластеров изменяющихся битов (потенциальные сигналы)
            var signalCandidates = FindSignalCandidates(bitVariability, bitCount);
            
            // Шаг 3: Проверка каждого кандидата
            foreach (var candidate in signalCandidates)
            {
                var values = ExtractSignalValues(stats.Samples, candidate.StartBit, candidate.Length, candidate.IsIntel);
                
                if (IsValidSignal(values, candidate.Length))
                {
                    var signal = CreateSignal(stats, candidate, values);
                    signals.Add(signal);
                }
            }
            
            // Шаг 4: Удаление перекрывающихся сигналов
            signals = RemoveOverlappingSignals(signals);
            
            return signals;
        }

        private List<BitVariability> AnalyzeBitVariability(MessageStatistics stats, int bitCount)
        {
            var variability = new List<BitVariability>();
            
            // Используем предвычисленную BitChangeRate если есть
            if (stats.BitChangeRate != null && stats.BitChangeRate.Length >= bitCount)
            {
                for (int i = 0; i < bitCount; i++)
                {
                    variability.Add(new BitVariability
                    {
                        BitIndex = i,
                        ChangeRate = stats.BitChangeRate[i],
                        IsChanging = stats.BitChangeRate[i] > _config.MinBitChangeRate
                    });
                }
            }
            else
            {
                // Вычисляем изменчивость вручную
                variability = CalculateBitVariabilityManual(stats, bitCount);
            }
            
            return variability;
        }

        private List<BitVariability> CalculateBitVariabilityManual(MessageStatistics stats, int bitCount)
        {
            var variability = new List<BitVariability>();
            var changeCounts = new int[bitCount];
            
            if (stats.Samples.Count < 2)
                return variability;

            // Подсчитываем изменения между последовательными samples
            for (int sampleIdx = 1; sampleIdx < stats.Samples.Count; sampleIdx++)
            {
                var prev = stats.Samples[sampleIdx - 1];
                var curr = stats.Samples[sampleIdx];
                
                for (int bit = 0; bit < bitCount; bit++)
                {
                    int byteIdx = bit / 8;
                    int bitIdx = bit % 8;
                    
                    if (byteIdx < prev.Length && byteIdx < curr.Length)
                    {
                        bool prevBit = (prev[byteIdx] & (1 << bitIdx)) != 0;
                        bool currBit = (curr[byteIdx] & (1 << bitIdx)) != 0;
                        
                        if (prevBit != currBit)
                            changeCounts[bit]++;
                    }
                }
            }
            
            // Конвертируем в изменчивость
            for (int i = 0; i < bitCount; i++)
            {
                double changeRate = (double)changeCounts[i] / (stats.Samples.Count - 1);
                variability.Add(new BitVariability
                {
                    BitIndex = i,
                    ChangeRate = changeRate,
                    IsChanging = changeRate > _config.MinBitChangeRate
                });
            }
            
            return variability;
        }

        private List<SignalCandidate> FindSignalCandidates(List<BitVariability> variability, int bitCount)
        {
            var candidates = new List<SignalCandidate>();
            var visited = new bool[bitCount];
            
            for (int i = 0; i < bitCount; i++)
            {
                if (!visited[i] && variability[i].IsChanging)
                {
                    // Находим непрерывный кластер изменяющихся битов
                    int clusterStart = i;
                    int clusterLength = 1;
                    
                    while (i + clusterLength < bitCount && 
                           variability[i + clusterLength].IsChanging &&
                           !visited[i + clusterLength])
                    {
                        clusterLength++;
                    }
                    
                    // Создаем кандидаты разной длины из этого кластера
                    var clusterCandidates = CreateCandidatesFromCluster(clusterStart, clusterLength);
                    candidates.AddRange(clusterCandidates);
                    
                    // Помечаем биты как обработанные
                    for (int j = clusterStart; j < clusterStart + clusterLength; j++)
                    {
                        visited[j] = true;
                    }
                }
            }
            
            return candidates;
        }

        private List<SignalCandidate> CreateCandidatesFromCluster(int startBit, int clusterLength)
        {
            var candidates = new List<SignalCandidate>();
            
            // Пробуем разные длины сигналов
            var possibleLengths = new[] { 1, 2, 4, 8, 10, 12, 16, 20, 24, 32 };
            
            foreach (var length in possibleLengths)
            {
                if (length <= clusterLength)
                {
                    // Пробуем оба порядка байтов
                    candidates.Add(new SignalCandidate
                    {
                        StartBit = startBit,
                        Length = length,
                        IsIntel = true
                    });
                    
                    candidates.Add(new SignalCandidate
                    {
                        StartBit = startBit,
                        Length = length,
                        IsIntel = false
                    });
                }
            }
            
            // Также пробуем сигнал на весь кластер
            if (clusterLength <= 32)
            {
                candidates.Add(new SignalCandidate
                {
                    StartBit = startBit,
                    Length = clusterLength,
                    IsIntel = true
                });
            }
            
            return candidates;
        }

        private List<double> ExtractSignalValues(List<byte[]> samples, int startBit, int length, bool isIntel)
        {
            var values = new List<double>();
            
            foreach (var sample in samples)
            {
                try
                {
                    ulong value = 0;
                    
                    if (isIntel)
                    {
                        // Little Endian (Intel)
                        for (int i = 0; i < length; i++)
                        {
                            int currentBit = startBit + i;
                            int byteIndex = currentBit / 8;
                            int bitIndex = currentBit % 8;
                            
                            if (byteIndex < sample.Length)
                            {
                                if ((sample[byteIndex] & (1 << bitIndex)) != 0)
                                {
                                    value |= (1UL << i);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Big Endian (Motorola)
                        for (int i = 0; i < length; i++)
                        {
                            int currentBit = startBit + i;
                            int byteIndex = currentBit / 8;
                            int bitIndex = 7 - (currentBit % 8);
                            
                            if (byteIndex < sample.Length)
                            {
                                if ((sample[byteIndex] & (1 << bitIndex)) != 0)
                                {
                                    value |= (1UL << (length - 1 - i));
                                }
                            }
                        }
                    }
                    
                    values.Add(value);
                }
                catch
                {
                    values.Add(0);
                }
            }
            
            return values;
        }

        private bool IsValidSignal(List<double> values, int bitLength)
        {
            if (values.Count < _config.MinSamples)
                return false;
            
            // Фильтруем по уникальным значениям
            var uniqueValues = values.Distinct().Count();
            var uniqueRatio = (double)uniqueValues / values.Count;
            
            if (uniqueRatio < _config.MinUniqueRatio || uniqueRatio > _config.MaxUniqueRatio)
                return false;
            
            // Фильтруем по диапазону значений
            var min = values.Min();
            var max = values.Max();
            var range = max - min;
            
            if (range < _config.MinValueRange)
                return false;
            
            // Проверяем максимальное значение для заданной длины
            double maxPossible = Math.Pow(2, bitLength) - 1;
            if (max > maxPossible * 1.1) // 10% допуск
                return false;
            
            return true;
        }

        private CANSignal CreateSignal(MessageStatistics stats, SignalCandidate candidate, List<double> values)
        {
            var min = values.Min();
            var max = values.Max();
            var avg = values.Average();
            
            // Автоматически определяем множитель
            double factor = 1.0;
            double offset = 0;
            
            if (max - min > 1000)
            {
                factor = 0.1;
            }
            else if (max - min > 100)
            {
                factor = 1.0;
            }
            else if (max - min > 10)
            {
                factor = 0.1;
            }
            else
            {
                factor = 0.01;
            }
            
            var signal = new CANSignal
            {
                MessageId = stats.MessageId,
                Name = GenerateSignalName(stats.MessageId, candidate.StartBit, candidate.Length, values),
                StartBit = candidate.StartBit,
                Length = candidate.Length,
                ByteOrder = candidate.IsIntel ? SignalByteOrder.Intel : SignalByteOrder.Motorola,
                Factor = factor,
                Offset = offset,
                MinPhysical = min * factor + offset,
                MaxPhysical = max * factor + offset,
                Unit = DetermineUnit(values, candidate.Length),
                Type = ClassifySignal(values, candidate.Length),
                Description = GenerateSignalDescription(values, candidate)
            };
            
            return signal;
        }

        private string GenerateSignalName(uint messageId, int startBit, int length, List<double> values)
        {
            var classification = ClassifySignal(values, length);
            
            return classification switch
            {
                SignalType.Counter => $"Counter_{messageId:X}_{startBit}_{length}",
                SignalType.State => $"State_{messageId:X}_{startBit}_{length}",
                SignalType.Sensor => $"Sensor_{messageId:X}_{startBit}_{length}",
                SignalType.Boolean => $"Flag_{messageId:X}_{startBit}",
                _ => $"Signal_{messageId:X}_{startBit}_{length}"
            };
        }

        private string DetermineUnit(List<double> values, int length)
        {
            var classification = ClassifySignal(values, length);
            var range = values.Max() - values.Min();
            
            return classification switch
            {
                SignalType.Counter => "count",
                SignalType.State => "state",
                SignalType.Sensor when range > 100 => "rpm",
                SignalType.Sensor when range > 10 => "km/h",
                SignalType.Sensor => "°C",
                SignalType.Boolean => "bool",
                _ => ""
            };
        }

        private SignalType ClassifySignal(List<double> values, int length)
        {
            if (length == 1)
                return SignalType.Boolean;
            
            var uniqueValues = values.Distinct().Count();
            
            if (uniqueValues == 1)
                return SignalType.Constant;
            
            if (uniqueValues <= 4)
                return SignalType.State;
            
            // Проверяем на счетчик
            if (IsCounter(values))
                return SignalType.Counter;
            
            // Проверяем на датчик
            if (IsSensor(values))
                return SignalType.Sensor;
            
            return SignalType.Unknown;
        }

        private bool IsCounter(List<double> values)
        {
            int sequentialCount = 0;
            int resetCount = 0;
            
            for (int i = 1; i < values.Count; i++)
            {
                double diff = values[i] - values[i - 1];
                
                // Инкремент на 1
                if (Math.Abs(diff - 1) < 0.5)
                {
                    sequentialCount++;
                }
                // Сброс (большое отрицательное изменение)
                else if (diff < -100)
                {
                    resetCount++;
                }
            }
            
            double sequentialRatio = (double)sequentialCount / (values.Count - 1);
            double resetRatio = (double)resetCount / (values.Count - 1);
            
            return sequentialRatio > 0.3 || resetRatio > 0.1;
        }

        private bool IsSensor(List<double> values)
        {
            // Датчики обычно имеют плавные изменения
            double totalChange = 0;
            for (int i = 1; i < values.Count; i++)
            {
                totalChange += Math.Abs(values[i] - values[i - 1]);
            }
            
            double avgChange = totalChange / (values.Count - 1);
            double range = values.Max() - values.Min();
            
            // Среднее изменение относительно диапазона
            return avgChange < range * 0.1;
        }

        private string GenerateSignalDescription(List<double> values, SignalCandidate candidate)
        {
            var classification = ClassifySignal(values, candidate.Length);
            var uniqueCount = values.Distinct().Count();
            var range = values.Max() - values.Min();
            
            return $"Detected {classification} signal. {uniqueCount} unique values, range: {range:F2}. Byte order: {(candidate.IsIntel ? "Intel" : "Motorola")}";
        }

        private List<CANSignal> RemoveOverlappingSignals(List<CANSignal> signals)
        {
            var filtered = new List<CANSignal>();
            
            // Сортируем по начальному биту
            var sorted = signals.OrderBy(s => s.StartBit).ToList();
            
            for (int i = 0; i < sorted.Count; i++)
            {
                var current = sorted[i];
                bool isOverlapping = false;
                
                // Проверяем перекрытие с уже добавленными сигналами
                for (int j = 0; j < filtered.Count; j++)
                {
                    var existing = filtered[j];
                    
                    // Проверяем перекрытие
                    if (current.StartBit < existing.StartBit + existing.Length &&
                        current.StartBit + current.Length > existing.StartBit)
                    {
                        // Выбираем лучший сигнал (с большим количеством уникальных значений)
                        isOverlapping = true;
                        break;
                    }
                }
                
                if (!isOverlapping)
                {
                    filtered.Add(current);
                }
            }
            
            return filtered;
        }

        public DetectionReport GetDetectionReport()
        {
            return _report;
        }

        // Вспомогательные классы
        private class BitVariability
        {
            public int BitIndex { get; set; }
            public double ChangeRate { get; set; }
            public bool IsChanging { get; set; }
        }

        private class SignalCandidate
        {
            public int StartBit { get; set; }
            public int Length { get; set; }
            public bool IsIntel { get; set; }
        }

        public class DetectionConfig
        {
            public int MinSamples { get; set; } = 10;
            public double MinBitChangeRate { get; set; } = 0.05; // 5%
            public double MinUniqueRatio { get; set; } = 0.1; // 10%
            public double MaxUniqueRatio { get; set; } = 0.95; // 95%
            public double MinValueRange { get; set; } = 1.0;
            public bool DetectBothByteOrders { get; set; } = true;
        }

        public class DetectionReport
        {
            public int TotalMessagesAnalyzed { get; set; }
            public int TotalSignalsDetected { get; set; }
            public Dictionary<SignalType, int> SignalsByType { get; set; } = new();
            public TimeSpan ProcessingTime { get; set; }
            public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;
        }
    }
}