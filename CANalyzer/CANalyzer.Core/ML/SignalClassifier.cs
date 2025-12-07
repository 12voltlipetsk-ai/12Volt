using System;
using System.Collections.Generic;
using System.Linq;
using CANalyzer.Core.Models;

namespace CANalyzer.Core.ML
{
    public class SignalFeature
    {
        public uint MessageId { get; set; }
        
        public float Byte0_Mean { get; set; }
        public float Byte0_Std { get; set; }
        public float Byte0_Entropy { get; set; }
        
        public float Byte1_Mean { get; set; }
        public float Byte1_Std { get; set; }
        public float Byte1_Entropy { get; set; }
        
        public float Byte2_Mean { get; set; }
        public float Byte2_Std { get; set; }
        public float Byte2_Entropy { get; set; }
        
        public float Byte3_Mean { get; set; }
        public float Byte3_Std { get; set; }
        public float Byte3_Entropy { get; set; }
        
        public float Byte4_Mean { get; set; }
        public float Byte4_Std { get; set; }
        public float Byte4_Entropy { get; set; }
        
        public float Byte5_Mean { get; set; }
        public float Byte5_Std { get; set; }
        public float Byte5_Entropy { get; set; }
        
        public float Byte6_Mean { get; set; }
        public float Byte6_Std { get; set; }
        public float Byte6_Entropy { get; set; }
        
        public float Byte7_Mean { get; set; }
        public float Byte7_Std { get; set; }
        public float Byte7_Entropy { get; set; }
        
        public float CrossByteCorrelation { get; set; }
        public float BitChangeFrequency { get; set; }
        public float PatternPeriodicity { get; set; }
        
        public string KnownSignalName { get; set; } = string.Empty;
    }
    
    public class SignalPrediction
    {
        public string PredictedSignalName { get; set; } = string.Empty;
        public float Confidence { get; set; }
    }
    
    public class SignalClassifier
    {
        public void Train(List<SignalFeature> trainingData)
        {
            // Упрощенная реализация для Core проекта
            // В реальном проекте здесь будет ML.NET логика
        }
        
        public SignalFeature? ExtractFeatures(CANMessage message, List<CANMessage> historicalData) // Измените сигнатуру на nullable
        {
            if (historicalData == null || historicalData.Count == 0)
                return null;
            
            var features = new SignalFeature
            {
                MessageId = message.Id
            };
            
            // Анализируем статистику по каждому байту
            for (int byteIndex = 0; byteIndex < Math.Min(8, message.Data.Length); byteIndex++)
            {
                var byteValues = historicalData
                    .Where(m => m.Id == message.Id && m.Data.Length > byteIndex)
                    .Select(m => (float)m.Data[byteIndex])
                    .ToList();
                
                if (byteValues.Count > 0)
                {
                    float mean = byteValues.Average();
                    float sumOfSquares = byteValues.Sum(v => (v - mean) * (v - mean));
                    float std = (float)Math.Sqrt(sumOfSquares / byteValues.Count);
                    float entropy = CalculateEntropy(byteValues);
                    
                    // Устанавливаем значения в зависимости от индекса байта
                    SetByteFeature(features, byteIndex, mean, std, entropy);
                }
            }
            
            features.CrossByteCorrelation = CalculateCrossByteCorrelation(historicalData, message.Id);
            features.BitChangeFrequency = CalculateBitChangeFrequency(historicalData, message.Id);
            features.PatternPeriodicity = CalculatePatternPeriodicity(historicalData, message.Id);
            
            return features;
        }
        
        public (string SignalName, float Confidence) PredictSignal(SignalFeature features)
        {
            if (features == null)
                return ("Unknown", 0);
            
            // Упрощенная логика предсказания
            // В реальном проекте здесь будет ML модель
            
            if (features.BitChangeFrequency < 0.1f)
                return ("Constant_Signal", 0.7f);
            else if (features.BitChangeFrequency > 0.8f)
                return ("Dynamic_Signal", 0.6f);
            else
                return ("Medium_Variance_Signal", 0.5f);
        }
        
        private float CalculateEntropy(List<float> values)
        {
            if (values.Count == 0) return 0;
            
            // Упрощенный расчет энтропии
            int[] histogram = new int[16];
            float min = values.Min();
            float max = values.Max();
            float range = max - min;
            
            if (range == 0) return 0;
            
            foreach (var value in values)
            {
                int bin = Math.Min(15, (int)((value - min) / range * 16));
                histogram[bin]++;
            }
            
            float entropy = 0;
            float total = values.Count;
            
            foreach (var count in histogram)
            {
                if (count > 0)
                {
                    float probability = count / total;
                    entropy -= probability * (float)Math.Log(probability, 2);
                }
            }
            
            return entropy;
        }
        
        private void SetByteFeature(SignalFeature features, int byteIndex, float mean, float std, float entropy)
        {
            // Упрощенная установка свойств
            switch (byteIndex)
            {
                case 0:
                    features.Byte0_Mean = mean;
                    features.Byte0_Std = std;
                    features.Byte0_Entropy = entropy;
                    break;
                case 1:
                    features.Byte1_Mean = mean;
                    features.Byte1_Std = std;
                    features.Byte1_Entropy = entropy;
                    break;
                case 2:
                    features.Byte2_Mean = mean;
                    features.Byte2_Std = std;
                    features.Byte2_Entropy = entropy;
                    break;
                case 3:
                    features.Byte3_Mean = mean;
                    features.Byte3_Std = std;
                    features.Byte3_Entropy = entropy;
                    break;
                case 4:
                    features.Byte4_Mean = mean;
                    features.Byte4_Std = std;
                    features.Byte4_Entropy = entropy;
                    break;
                case 5:
                    features.Byte5_Mean = mean;
                    features.Byte5_Std = std;
                    features.Byte5_Entropy = entropy;
                    break;
                case 6:
                    features.Byte6_Mean = mean;
                    features.Byte6_Std = std;
                    features.Byte6_Entropy = entropy;
                    break;
                case 7:
                    features.Byte7_Mean = mean;
                    features.Byte7_Std = std;
                    features.Byte7_Entropy = entropy;
                    break;
            }
        }
        
        private float CalculateCrossByteCorrelation(List<CANMessage> messages, uint messageId)
        {
            var relevantMessages = messages.Where(m => m.Id == messageId).ToList();
            if (relevantMessages.Count < 2) return 0;
            
            var byte1Values = relevantMessages
                .Where(m => m.Data.Length > 0)
                .Select(m => (float)m.Data[0])
                .ToList();
                
            var byte2Values = relevantMessages
                .Where(m => m.Data.Length > 1)
                .Select(m => (float)m.Data[1])
                .ToList();
            
            if (byte1Values.Count != byte2Values.Count || byte1Values.Count < 2)
                return 0;
            
            float mean1 = byte1Values.Average();
            float mean2 = byte2Values.Average();
            
            float covariance = 0;
            float variance1 = 0;
            float variance2 = 0;
            
            for (int i = 0; i < byte1Values.Count; i++)
            {
                float diff1 = byte1Values[i] - mean1;
                float diff2 = byte2Values[i] - mean2;
                
                covariance += diff1 * diff2;
                variance1 += diff1 * diff1;
                variance2 += diff2 * diff2;
            }
            
            if (variance1 == 0 || variance2 == 0)
                return 0;
                
            return covariance / (float)(Math.Sqrt(variance1) * Math.Sqrt(variance2));
        }
        
        private float CalculateBitChangeFrequency(List<CANMessage> messages, uint messageId)
        {
            var relevantMessages = messages
                .Where(m => m.Id == messageId)
                .OrderBy(m => m.Timestamp)
                .ToList();
                
            if (relevantMessages.Count < 2) return 0;
            
            int totalBits = 0;
            int changedBits = 0;
            
            for (int i = 1; i < relevantMessages.Count; i++)
            {
                var prev = relevantMessages[i - 1];
                var curr = relevantMessages[i];
                
                int minLength = Math.Min(prev.Data.Length, curr.Data.Length);
                totalBits += minLength * 8;
                
                for (int byteIdx = 0; byteIdx < minLength; byteIdx++)
                {
                    byte prevByte = prev.Data[byteIdx];
                    byte currByte = curr.Data[byteIdx];
                    
                    byte changed = (byte)(prevByte ^ currByte);
                    
                    while (changed != 0)
                    {
                        changedBits += changed & 1;
                        changed >>= 1;
                    }
                }
            }
            
            return totalBits > 0 ? (float)changedBits / totalBits : 0;
        }
        
        private float CalculatePatternPeriodicity(List<CANMessage> messages, uint messageId)
        {
            var relevantMessages = messages
                .Where(m => m.Id == messageId)
                .OrderBy(m => m.Timestamp)
                .ToList();
                
            if (relevantMessages.Count < 10) return 0;
            
            var firstByteValues = relevantMessages
                .Where(m => m.Data.Length > 0)
                .Select(m => (int)m.Data[0])
                .ToList();
                
            int maxPatternLength = Math.Min(5, firstByteValues.Count / 2);
            float bestPeriodicity = 0;
            
            for (int patternLength = 1; patternLength <= maxPatternLength; patternLength++)
            {
                int matches = 0;
                int comparisons = 0;
                
                for (int i = 0; i < firstByteValues.Count - patternLength; i++)
                {
                    comparisons++;
                    if (firstByteValues[i] == firstByteValues[i + patternLength])
                    {
                        matches++;
                    }
                }
                
                float periodicity = comparisons > 0 ? (float)matches / comparisons : 0;
                if (periodicity > bestPeriodicity)
                {
                    bestPeriodicity = periodicity;
                }
            }
            
            return bestPeriodicity;
        }
    }
}