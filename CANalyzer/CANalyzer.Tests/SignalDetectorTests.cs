using System;
using System.Collections.Generic;
using System.Linq;
using CANalyzer.Core.Models;

namespace CANalyzer.Core.Analyzers
{
    public class SignalDetector
    {
        public List<CANSignal> FindSignals(CANalyzer.Core.Models.MessageStatistics stats)
        {
            var signals = new List<CANSignal>();

            if (stats.Samples.Count < 2) return signals;

            int maxBits = stats.Samples[0].Length * 8;

            for (int startBit = 0; startBit < maxBits; startBit++)
            {
                for (int length = 1; length <= Math.Min(32, maxBits - startBit); length++)
                {
                    var signalValues = ExtractSignalValues(stats.Samples, startBit, length);

                    if (IsValidSignal(signalValues))
                    {
                        var signal = new CANSignal
                        {
                            MessageId = stats.MessageId,
                            Name = $"Signal_{stats.MessageId:X}_{startBit}_{length}",
                            StartBit = startBit,
                            Length = length,
                            ByteOrder = SignalByteOrder.Intel,
                            MinPhysical = signalValues.Min(),
                            MaxPhysical = signalValues.Max()
                        };

                        signal.Type = ClassifySignal(signalValues);
                        signals.Add(signal);
                    }
                }
            }

            return signals;
        }

        private List<double> ExtractSignalValues(List<byte[]> samples, int startBit, int length)
        {
            var values = new List<double>();

            foreach (var sample in samples)
            {
                try
                {
                    ulong value = 0;
                    int bitPosition = startBit;

                    for (int i = 0; i < length; i++)
                    {
                        int byteIndex = bitPosition / 8;
                        int bitIndex = bitPosition % 8;

                        if (byteIndex < sample.Length)
                        {
                            if ((sample[byteIndex] & (1 << bitIndex)) != 0)
                            {
                                value |= (ulong)1 << i;
                            }
                        }

                        bitPosition++;
                    }

                    values.Add((double)value);
                }
                catch
                {
                    values.Add(0);
                }
            }

            return values;
        }

        private bool IsValidSignal(List<double> values)
        {
            if (values.Count < 5) return false;

            double min = values.Min();
            double max = values.Max();

            if (max - min < 1) return false;

            double uniqueValues = values.Distinct().Count();
            double uniqueRatio = uniqueValues / values.Count;

            return uniqueRatio > 0.1 && uniqueRatio < 0.9;
        }

        private SignalType ClassifySignal(List<double> values)
        {
            if (values.Distinct().Count() == 1)
                return SignalType.Constant;

            double max = values.Max();
            double min = values.Min();

            if (max - min < 10)
                return SignalType.State;

            bool hasSequentialPattern = CheckSequentialPattern(values);

            if (hasSequentialPattern)
                return SignalType.Counter;

            return SignalType.Sensor;
        }

        private bool CheckSequentialPattern(List<double> values)
        {
            int sequentialCount = 0;

            for (int i = 1; i < values.Count; i++)
            {
                if (Math.Abs(values[i] - values[i - 1] - 1) < 0.1)
                    sequentialCount++;
            }

            return (double)sequentialCount / (values.Count - 1) > 0.7;
        }
    }
}