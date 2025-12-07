using System;
using System.Collections.Generic;
using System.Linq;

namespace CANalyzer.ML.NeuralNetworks
{
    public class SignalFeature
    {
        public string KnownSignalName { get; set; } = string.Empty;
        public float Byte0_Mean { get; set; }
        public float Byte0_Std { get; set; }
        public float Byte0_Entropy { get; set; }
        public float ValueRange { get; set; }
        public float ValueStdDev { get; set; }
        public int UniqueValues { get; set; }
    }
    
    public class SignalClassifier
    {
        private readonly Dictionary<string, List<SignalFeature>> _trainingData = new();
        private readonly Random _random = new();
        
        public void Train(List<SignalFeature> features)
        {
            foreach (var feature in features)
            {
                if (!_trainingData.ContainsKey(feature.KnownSignalName))
                {
                    _trainingData[feature.KnownSignalName] = new List<SignalFeature>();
                }
                _trainingData[feature.KnownSignalName].Add(feature);
            }
        }
        
        public (string predictedName, float confidence) PredictSignal(SignalFeature feature)
        {
            if (_trainingData.Count == 0)
                return ("Unknown", 0.1f);
            
            string bestMatch = "Unknown";
            float bestScore = float.MaxValue;
            
            foreach (var category in _trainingData)
            {
                foreach (var trainingFeature in category.Value)
                {
                    float distance = CalculateDistance(feature, trainingFeature);
                    if (distance < bestScore)
                    {
                        bestScore = distance;
                        bestMatch = category.Key;
                    }
                }
            }
            
            // Преобразуем расстояние в уверенность (0-1)
            float confidence = 1.0f / (1.0f + bestScore);
            return (bestMatch, confidence);
        }
        
        private float CalculateDistance(SignalFeature f1, SignalFeature f2)
        {
            // Евклидово расстояние по нормализованным признакам
            float sum = 0;
            
            sum += (f1.Byte0_Mean - f2.Byte0_Mean) * (f1.Byte0_Mean - f2.Byte0_Mean);
            sum += (f1.Byte0_Std - f2.Byte0_Std) * (f1.Byte0_Std - f2.Byte0_Std);
            sum += (f1.Byte0_Entropy - f2.Byte0_Entropy) * (f1.Byte0_Entropy - f2.Byte0_Entropy);
            sum += (f1.ValueRange - f2.ValueRange) * (f1.ValueRange - f2.ValueRange);
            sum += (f1.ValueStdDev - f2.ValueStdDev) * (f1.ValueStdDev - f2.ValueStdDev);
            
            return (float)Math.Sqrt(sum);
        }
        
        public SignalFeature ExtractFeatures(CANalyzer.Core.Models.CANMessage message, 
            List<CANalyzer.Core.Models.CANMessage> historicalData)
        {
            var relevantMessages = historicalData
                .Where(m => m.Id == message.Id)
                .Take(50) // Ограничиваем для производительности
                .ToList();
            
            if (relevantMessages.Count == 0)
                return null;
            
            // Извлекаем значения первого байта для анализа
            var byte0Values = relevantMessages
                .Where(m => m.Data.Length > 0)
                .Select(m => (float)m.Data[0])
                .ToList();
            
            if (byte0Values.Count == 0)
                return null;
            
            var feature = new SignalFeature
            {
                Byte0_Mean = byte0Values.Average(),
                Byte0_Std = CalculateStdDev(byte0Values),
                Byte0_Entropy = CalculateEntropy(byte0Values),
                ValueRange = byte0Values.Max() - byte0Values.Min(),
                ValueStdDev = CalculateStdDev(byte0Values),
                UniqueValues = byte0Values.Distinct().Count()
            };
            
            return feature;
        }
        
        private float CalculateStdDev(List<float> values)
        {
            if (values.Count < 2) return 0;
            
            var mean = values.Average();
            var sumSquares = values.Sum(v => (v - mean) * (v - mean));
            return (float)Math.Sqrt(sumSquares / values.Count);
        }
        
        private float CalculateEntropy(List<float> values)
        {
            if (values.Count == 0) return 0;
            
            var valueCounts = new Dictionary<float, int>();
            foreach (var value in values)
            {
                if (!valueCounts.ContainsKey(value))
                    valueCounts[value] = 0;
                valueCounts[value]++;
            }
            
            float entropy = 0;
            float total = values.Count;
            
            foreach (var count in valueCounts.Values)
            {
                float probability = count / total;
                entropy -= probability * (float)Math.Log(probability, 2);
            }
            
            return entropy;
        }
        
        public void TrainWithSyntheticData()
        {
            // Генерация синтетических данных для тренировки
            var trainingFeatures = new List<SignalFeature>();
            
            // Примеры известных типов сигналов
            var signalTypes = new Dictionary<string, Action<SignalFeature>>
            {
                ["EngineRPM"] = f => {
                    f.Byte0_Mean = 100;
                    f.Byte0_Std = 30;
                    f.ValueRange = 150;
                },
                ["VehicleSpeed"] = f => {
                    f.Byte0_Mean = 80;
                    f.Byte0_Std = 20;
                    f.ValueRange = 120;
                },
                ["Temperature"] = f => {
                    f.Byte0_Mean = 90;
                    f.Byte0_Std = 5;
                    f.ValueRange = 40;
                },
                ["Pressure"] = f => {
                    f.Byte0_Mean = 50;
                    f.Byte0_Std = 15;
                    f.ValueRange = 100;
                }
            };
            
            foreach (var kvp in signalTypes)
            {
                for (int i = 0; i < 10; i++) // По 10 примеров каждого типа
                {
                    var feature = new SignalFeature { KnownSignalName = kvp.Key };
                    kvp.Value(feature);
                    
                    // Добавляем немного шума
                    feature.Byte0_Mean += (float)(_random.NextDouble() * 10 - 5);
                    feature.Byte0_Std += (float)(_random.NextDouble() * 2 - 1);
                    
                    trainingFeatures.Add(feature);
                }
            }
            
            Train(trainingFeatures);
        }
    }
}