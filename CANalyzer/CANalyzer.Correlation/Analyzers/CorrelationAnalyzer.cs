using System.Collections.Generic;
using System.Linq;
using CANalyzer.Core.Models;

namespace CANalyzer.Correlation.Analyzers
{
    public class CorrelationAnalyzer
    {
        public Dictionary<(uint, uint), double> AnalyzeCorrelations(Dictionary<uint, MessageStatistics> statistics)
        {
            var correlations = new Dictionary<(uint, uint), double>();
            var messageIds = statistics.Keys.ToList();
            
            for (int i = 0; i < messageIds.Count; i++)
            {
                for (int j = i + 1; j < messageIds.Count; j++)
                {
                    var id1 = messageIds[i];
                    var id2 = messageIds[j];
                    
                    var freq1 = statistics[id1].Frequency;
                    var freq2 = statistics[id2].Frequency;
                    
                    if (freq1 > 0 && freq2 > 0)
                    {
                        double correlation = CalculateFrequencyCorrelation(freq1, freq2);
                        correlations[(id1, id2)] = correlation;
                    }
                }
            }
            
            return correlations;
        }
        
        private double CalculateFrequencyCorrelation(double freq1, double freq2)
        {
            // Простая корреляция на основе частот
            double ratio = freq1 / freq2;
            
            if (ratio > 0.9 && ratio < 1.1)
                return 1.0;
            else if (ratio > 0.45 && ratio < 0.55)
                return 0.5;
            else if (ratio > 0.9 || ratio < 1.1)
                return 0.2;
            else
                return 0.0;
        }
    }
}