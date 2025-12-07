// C:\Users\rveremeev\Documents\CANalyzer\CANalyzer.Core\Analyzers\StatisticalAnalyzer.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CANalyzer.Core.Models;

namespace CANalyzer.Core.Analyzers
{
    public class StatisticalAnalyzer
    {
        private readonly ConcurrentDictionary<uint, MessageStatistics> _statistics = new();

        public void CalculateStatistics(List<CANMessage> messages)
        {
            if (messages == null || messages.Count == 0) return;

            // Группируем сообщения по ID
            var groupedMessages = messages.GroupBy(m => m.Id);

            // Параллельно обрабатываем каждую группу
            Parallel.ForEach(groupedMessages, group =>
            {
                var stats = CalculateMessageStatistics(group.ToList());
                _statistics[group.Key] = stats;
            });
        }

        private MessageStatistics CalculateMessageStatistics(List<CANMessage> messages)
        {
            if (messages == null || messages.Count == 0)
                return new MessageStatistics();

            var stats = new MessageStatistics
            {
                MessageId = messages.First().Id,
                Count = messages.Count,
                FirstSeen = messages.Min(m => m.Timestamp),
                LastSeen = messages.Max(m => m.Timestamp),
                DataLengths = messages.Select(m => m.Data.Length).Distinct().ToHashSet(),
                Samples = messages.Take(200).Select(m => m.Data).ToList() // Берем первые 200 образцов
            };

            // Рассчитываем энтропию байтов
            CalculateByteEntropy(stats);

            // Рассчитываем изменчивость битов
            CalculateBitChangeRate(stats);

            return stats;
        }

        private void CalculateByteEntropy(MessageStatistics stats)
        {
            if (stats.Samples.Count == 0) return;
            int byteCount = stats.Samples[0].Length;
            stats.ByteEntropy = new double[byteCount];

            for (int bytePos = 0; bytePos < byteCount; bytePos++)
            {
                var byteValues = stats.Samples
                    .Where(s => s.Length > bytePos)
                    .Select(s => s[bytePos])
                    .ToList();

                if (byteValues.Count == 0) continue;

                var valueCounts = new Dictionary<byte, int>();
                foreach (var value in byteValues)
                {
                    if (!valueCounts.ContainsKey(value))
                        valueCounts[value] = 0;
                    valueCounts[value]++;
                }

                double entropy = 0.0;
                double total = byteValues.Count;
                foreach (var count in valueCounts.Values)
                {
                    double probability = count / total;
                    entropy -= probability * Math.Log(probability, 2);
                }

                stats.ByteEntropy[bytePos] = entropy;
            }
        }

        private void CalculateBitChangeRate(MessageStatistics stats)
        {
            if (stats.Samples.Count < 2) return;

            int maxBits = stats.Samples[0].Length * 8;
            stats.BitChangeRate = new double[Math.Max(maxBits, 512)]; // Максимум 512 бит

            for (int bit = 0; bit < maxBits; bit++)
            {
                int changes = 0;
                int bytePos = bit / 8;
                int bitPos = bit % 8;

                for (int i = 1; i < stats.Samples.Count; i++)
                {
                    if (bytePos >= stats.Samples[i - 1].Length ||
                        bytePos >= stats.Samples[i].Length)
                        continue;

                    bool prevBit = (stats.Samples[i - 1][bytePos] & (1 << bitPos)) != 0;
                    bool currBit = (stats.Samples[i][bytePos] & (1 << bitPos)) != 0;

                    if (prevBit != currBit)
                        changes++;
                }

                stats.BitChangeRate[bit] = (double)changes / (stats.Samples.Count - 1);
            }
        }

        public Dictionary<uint, MessageStatistics> GetStatistics()
        {
            return _statistics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }
}