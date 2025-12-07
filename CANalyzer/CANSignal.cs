using System;
using System.Collections.Generic;

namespace CANalyzer.Core.Models
{
    public class CANSignal
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public uint MessageId { get; set; }
        public string Name { get; set; } = $"Signal_{Guid.NewGuid():N}";
        
        // Позиция в сообщении
        public int StartBit { get; set; }
        public int Length { get; set; }
        public SignalByteOrder ByteOrder { get; set; } = SignalByteOrder.Motorola;
        
        // Преобразование
        public double Factor { get; set; } = 1.0;
        public double Offset { get; set; } = 0.0;
        public double MinRaw { get; set; }
        public double MaxRaw { get; set; }
        public double MinPhysical { get; set; }
        public double MaxPhysical { get; set; }
        
        // Метаданные
        public string Unit { get; set; } = "";
        public string Description { get; set; } = "";
        public SignalType Type { get; set; } = SignalType.Unknown;
        
        // Статистика
        public SignalStatistics Statistics { get; set; } = new();
        
        public CANSignal(uint messageId, int startBit, int length)
        {
            MessageId = messageId;
            StartBit = startBit;
            Length = length;
        }
        
        public double GetPhysicalValue(byte[] messageData)
        {
            double raw = ExtractRawValue(messageData);
            return (raw * Factor) + Offset;
        }
        
        public double ExtractRawValue(byte[] messageData)
        {
            if (messageData == null || messageData.Length * 8 <= StartBit)
                return 0;
            
            ulong result = 0;
            int totalBits = Math.Min(Length, messageData.Length * 8 - StartBit);
            
            for (int i = 0; i < totalBits; i++)
            {
                int currentBit = StartBit + i;
                int byteIndex = currentBit / 8;
                int bitIndex = currentBit % 8;
                
                if (byteIndex < messageData.Length)
                {
                    bool bitValue = (messageData[byteIndex] & (1 << bitIndex)) != 0;
                    if (bitValue)
                    {
                        if (ByteOrder == SignalByteOrder.Motorola)
                            result |= (ulong)1 << (totalBits - i - 1);
                        else
                            result |= (ulong)1 << i;
                    }
                }
            }
            
            return result;
        }
    }
    
    public enum SignalByteOrder
    {
        Motorola,  // Big-endian
        Intel      // Little-endian
    }
    
    public enum SignalType
    {
        Unknown,
        Cyclic,           // Циклический (скорость, обороты)
        Sensor,           // Сенсорные данные
        Actuator,         // Приводы
        Switch,           // Переключатели
        Button,           // Кнопки
        Status,           // Статусные биты
        Diagnostic,       // Диагностика
        Counter,          // Счетчики
        Checksum,         // Контрольные суммы
        Padding,          // Заполнители
        Configuration     // Конфигурационные
    }
}