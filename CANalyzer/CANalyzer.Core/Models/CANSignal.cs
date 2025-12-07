using System;

namespace CANalyzer.Core.Models
{
    public class CANSignal
    {
        public uint MessageId { get; set; }
        public string Name { get; set; } = "Signal";
        public int StartBit { get; set; }
        public int Length { get; set; }
        public SignalByteOrder ByteOrder { get; set; }
        public double Factor { get; set; } = 1.0;
        public double Offset { get; set; } = 0.0;
        public double MinPhysical { get; set; }
        public double MaxPhysical { get; set; }
        public string Unit { get; set; } = "";
        public SignalType Type { get; set; } = SignalType.Unknown;
        
        public double GetPhysicalValue(byte[] messageData)
        {
            try
            {
                double rawValue = ExtractRawValue(messageData);
                return (rawValue * Factor) + Offset;
            }
            catch
            {
                return 0;
            }
        }
        
        private double ExtractRawValue(byte[] data)
        {
            if (data == null || data.Length == 0 || StartBit < 0 || Length <= 0)
                return 0;
                
            ulong result = 0;
            int bitPosition = StartBit;
            
            for (int i = 0; i < Length; i++)
            {
                int byteIndex = bitPosition / 8;
                int bitIndex = bitPosition % 8;
                
                if (byteIndex < data.Length)
                {
                    if ((data[byteIndex] & (1 << bitIndex)) != 0)
                    {
                        if (ByteOrder == SignalByteOrder.Intel)
                            result |= (ulong)1 << i;
                        else
                            result |= (ulong)1 << (Length - i - 1);
                    }
                }
                
                bitPosition++;
            }
            
            return result;
        }
    }
    
    public enum SignalByteOrder
    {
        Intel,
        Motorola
    }
    
    public enum SignalType
    {
        Unknown,
        Constant,
        Cyclic,
        Event,
        State,
        Sensor,
        Counter
    }
}