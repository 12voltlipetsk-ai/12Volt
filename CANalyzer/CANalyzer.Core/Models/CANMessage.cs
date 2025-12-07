using System;

namespace CANalyzer.Core.Models
{
    public class CANMessage
    {
        public ulong Timestamp { get; set; }          // Время в микросекундах
        public uint Id { get; set; }                  // CAN идентификатор
        public byte[] Data { get; set; } = Array.Empty<byte>(); // Данные сообщения
        public bool IsExtended { get; set; }          // Расширенный идентификатор (29-bit)
        public bool IsCANFD { get; set; }             // CAN FD сообщение
        public bool IsRemoteFrame { get; set; }       // Удаленный фрейм
        public bool IsErrorFrame { get; set; }        // Фрейм ошибки
        public int Channel { get; set; } = 1;         // Канал CAN
        public int DLC { get; set; }                  // Data Length Code
        public Direction Direction { get; set; } = Direction.Received; // Направление
        
        // CAN FD специфичные поля
        public CANFDBitRate BitRate { get; set; } = CANFDBitRate.Normal;
        public bool BRS { get; set; }                // Bit Rate Switch
        public bool ESI { get; set; }                // Error State Indicator
        
        public CANMessage(uint id, byte[] data, ulong timestamp)
        {
            Id = id;
            Data = data ?? Array.Empty<byte>();
            Timestamp = timestamp;
            DLC = Data.Length;
            IsCANFD = Data.Length > 8; // CAN FD может иметь до 64 байт
        }
        
        public override string ToString()
        {
            string type = IsCANFD ? "FD" : "CAN";
            string extended = IsExtended ? "EXT" : "STD";
            string dir = Direction == Direction.Received ? "Rx" : "Tx";
            string dataStr = Data.Length > 0 ? BitConverter.ToString(Data).Replace("-", " ") : "";
            return $"[{Timestamp,10}] {dir} {type} {extended} 0x{Id:X8} [{DLC}] {dataStr}";
        }
    }
    
    public enum Direction
    {
        Received,
        Transmitted
    }
    
    public enum CANFDBitRate
    {
        Normal,     // Стандартная скорость
        Fast,       // Быстрая скорость
        Mixed       // Смешанный режим (BRS)
    }
}