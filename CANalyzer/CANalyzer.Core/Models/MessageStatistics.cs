using System;
using System.Collections.Generic;

namespace CANalyzer.Core.Models
{
    public class MessageStatistics
    {
        public uint MessageId { get; set; }
        public long Count { get; set; }
        public ulong FirstSeen { get; set; }
        public ulong LastSeen { get; set; }
        public HashSet<int> DataLengths { get; set; } = new();
        public List<byte[]> Samples { get; set; } = new();
        
        // Статистические метрики
        public double[] ByteEntropy { get; set; } = new double[64];
        public double[] BitChangeRate { get; set; } = new double[512];
        public double[] BitStability { get; set; } = new double[512];
        
        // Метрики производительности
        public double Frequency => 
            Count > 1 ? (Count - 1) / ((LastSeen - FirstSeen) / 1000000.0) : 0;
        
        // Флаги
        public bool IsCANFD { get; set; }
        public double CANFDRate { get; set; }
        
        // Добавьте метод для получения статистики по битам
        public string GetBitStatistics()
        {
            int changingBits = 0;
            int stableBits = 0;
            
            for (int i = 0; i < BitChangeRate.Length; i++)
            {
                if (BitChangeRate[i] > 0.1) changingBits++;
                else if (BitChangeRate[i] < 0.01) stableBits++;
            }
            
            return $"Changing bits: {changingBits}, Stable bits: {stableBits}";
        }
    }
}