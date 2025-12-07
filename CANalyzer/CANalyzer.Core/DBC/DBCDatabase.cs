using System;
using System.Collections.Generic;
using System.Linq;
using CANalyzer.Core.Models;

namespace CANalyzer.Core.DBC
{
    public class DBCDatabase
    {
        public string Name { get; set; } = "Unknown";
        public Dictionary<uint, DBCMessage> Messages { get; } = new();
        
        public void AddMessage(DBCMessage message)
        {
            Messages[message.ID] = message;
        }
        
        public bool TryGetMessage(uint id, out DBCMessage message)
        {
            return Messages.TryGetValue(id, out message);
        }
    }
    
    public class DBCMessage
    {
        public uint ID { get; set; }
        public string Name { get; set; } = "Unknown";
        public int DLC { get; set; }
        public string Transmitter { get; set; } = "ECU";
        public List<DBCSignal> Signals { get; } = new();
    }
    
    public class DBCSignal
    {
        public string Name { get; set; } = string.Empty;
        public int StartBit { get; set; }
        public int Length { get; set; }
        public bool IsSigned { get; set; }
        public bool IsBigEndian { get; set; }
        public double Factor { get; set; } = 1.0;
        public double Offset { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string Receiver { get; set; } = "ECU";
    }
}