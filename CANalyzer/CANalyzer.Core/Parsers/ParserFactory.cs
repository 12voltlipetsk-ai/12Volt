using System;

namespace CANalyzer.Core.Parsers
{
    public class ParserFactory
    {
        public ILogParser CreateParser(string filePath)
        {
            var format = DetectFormat(filePath);
            return CreateParser(format);
        }
        
        public ILogParser CreateParser(LogFormat format)
        {
            return new AdvancedLogParser();
        }
        
        public LogFormat DetectFormat(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return LogFormat.Unknown;
            
            string extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            
            return extension switch
            {
                ".csv" => LogFormat.CSV,
                ".asc" => LogFormat.ASC,
                ".trc" => LogFormat.TRC,
                ".mf4" => LogFormat.MF4,
                ".pcap" => LogFormat.PCAP,
                ".log" => LogFormat.Generic,
                ".txt" => LogFormat.Text,
                _ => LogFormat.Unknown
            };
        }
    }
    
    public enum LogFormat
    {
        Unknown,
        CSV,
        ASC,
        TRC,
        MF4,
        PCAP,
        Generic,
        Text
    }
}