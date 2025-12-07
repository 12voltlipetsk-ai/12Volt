using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CANalyzer.Core.DBC;
using CANalyzer.Core.Models;
using CANalyzer.ML.NeuralNetworks;

namespace CANalyzer.Core.Analyzers
{
    public class DBCMatcher
    {
        private readonly DBCRepository _dbcRepository;
        private readonly SignalClassifier _classifier;
        
        public DBCMatcher()
        {
            _dbcRepository = new DBCRepository();
            _classifier = new SignalClassifier();
        }
        
        public async Task InitializeAsync()
        {
            // Загружаем DBC из известных источников
            await _dbcRepository.LoadFromCommaAIAsync();
            
            // Тренируем классификатор
            _classifier.TrainWithSyntheticData();
        }
        
        public MatchResult MatchMessage(CANMessage message, List<CANMessage> historicalData = null)
        {
            var result = new MatchResult
            {
                MessageId = message.Id,
                RawMessage = message
            };
            
            // Прямое сопоставление с DBC
            var dbcMatches = _dbcRepository.FindMessages(message.Id);
            if (dbcMatches.Any())
            {
                result.Category = MatchCategory.Defined;
                result.MatchedDBC = dbcMatches.First();
                result.Confidence = 1.0f;
                result.Comment = $"Matched with DBC: {result.MatchedDBC.Name}";
                return result;
            }
            
            // ML классификация
            if (historicalData != null && historicalData.Count > 10)
            {
                var feature = _classifier.ExtractFeatures(message, historicalData);
                if (feature != null)
                {
                    var (predictedName, confidence) = _classifier.PredictSignal(feature);
                    if (confidence > 0.5)
                    {
                        result.Category = MatchCategory.Try;
                        result.SuggestedName = predictedName;
                        result.Confidence = confidence;
                        result.Comment = $"ML prediction: {predictedName} (confidence: {confidence:P0})";
                        return result;
                    }
                }
            }
            
            // Эвристический анализ
            result.Category = MatchCategory.Undefined;
            result.SuggestedName = $"Unknown_{message.Id:X}";
            result.Confidence = 0.3f;
            result.Comment = AnalyzeHeuristically(message, historicalData);
            
            return result;
        }
        
        private string AnalyzeHeuristically(CANMessage message, List<CANMessage> historicalData)
        {
            var data = message.Data;
            
            if (data.Length == 0) return "Empty message";
            
            // Проверяем на ASCII данные
            int asciiCount = data.Count(b => b >= 32 && b <= 126);
            if ((float)asciiCount / data.Length > 0.8)
            {
                var asciiStr = System.Text.Encoding.ASCII.GetString(data.Where(b => b >= 32 && b <= 126).ToArray());
                return $"Contains ASCII: {asciiStr}";
            }
            
            // Проверяем на флаги
            int flagCount = data.Count(b => b == 0x00 || b == 0xFF || b == 0x55 || b == 0xAA);
            if ((float)flagCount / data.Length > 0.7)
            {
                return "Likely status flags or patterns";
            }
            
            // Проверяем частоту
            if (historicalData != null)
            {
                var sameIdMessages = historicalData.Where(m => m.Id == message.Id).ToList();
                if (sameIdMessages.Count > 5)
                {
                    var timestamps = sameIdMessages.Select(m => m.Timestamp).ToList();
                    double avgInterval = CalculateAverageInterval(timestamps);
                    
                    if (avgInterval > 0)
                    {
                        double freq = 1000000.0 / avgInterval; // мкс -> Гц
                        return $"Frequency: {freq:F2} Hz";
                    }
                }
            }
            
            return "No specific patterns detected";
        }
        
        private double CalculateAverageInterval(List<ulong> timestamps)
        {
            if (timestamps.Count < 2) return 0;
            
            double totalInterval = 0;
            for (int i = 1; i < timestamps.Count; i++)
            {
                totalInterval += timestamps[i] - timestamps[i - 1];
            }
            
            return totalInterval / (timestamps.Count - 1);
        }
        
        public class MatchResult
        {
            public uint MessageId { get; set; }
            public CANMessage RawMessage { get; set; }
            public MatchCategory Category { get; set; }
            public DBCMessage MatchedDBC { get; set; }
            public string SuggestedName { get; set; }
            public float Confidence { get; set; }
            public string Comment { get; set; }
        }
    }
}