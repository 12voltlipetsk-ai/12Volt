using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CANalyzer.Core.Models;

namespace CANalyzer.Core.Parsers
{
    public class AdvancedLogParser : ILogParser
    {
        private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
        
        public List<CANMessage> Parse(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            return extension switch
            {
                ".csv" => ParseCSV(filePath),
                ".asc" => ParseASC(filePath),
                ".log" => ParseGenericLog(filePath),
                ".trc" => ParseTRC(filePath),
                ".txt" => ParseTextLog(filePath),
                _ => ParseAutodetect(filePath)
            };
        }

        #region CSV Парсер
        private List<CANMessage> ParseCSV(string filePath)
        {
            var messages = new List<CANMessage>();
            
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length == 0) return messages;

                char separator = DetectCSVSeparator(lines[0]);
                CSVFormat format = DetectCSVFormat(lines[0], separator);
                
                for (int i = format.HasHeader ? 1 : 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    try
                    {
                        CANMessage? message = ParseCSVLine(line, separator, format);
                        if (message != null)
                            messages.Add(message);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new LogParserException($"Error parsing CSV: {ex.Message}", ex);
            }

            return messages;
        }

        private char DetectCSVSeparator(string line)
        {
            int commaCount = line.Count(c => c == ',');
            int semicolonCount = line.Count(c => c == ';');
            int tabCount = line.Count(c => c == '\t');
            int spaceCount = line.Count(c => c == ' ');

            if (tabCount > commaCount && tabCount > semicolonCount) return '\t';
            if (semicolonCount > commaCount && semicolonCount > spaceCount) return ';';
            if (spaceCount > commaCount * 2 && spaceCount > 5) return ' ';
            return ',';
        }

        private CSVFormat DetectCSVFormat(string headerLine, char separator)
        {
            var format = new CSVFormat();
            string[] headers = headerLine.Split(separator);
            
            for (int i = 0; i < headers.Length; i++)
            {
                string header = headers[i].Trim().ToLowerInvariant();
                
                if (header.Contains("time") || header.Contains("timestamp"))
                    format.TimeIndex = i;
                else if (header.Contains("id") || header.Contains("canid") || header.Contains("identifier"))
                    format.IdIndex = i;
                else if (header.Contains("data") || header.Contains("bytes") || header.Contains("payload"))
                    format.DataIndex = i;
                else if (header.Contains("dlc"))
                    format.DLCIndex = i;
                else if (header.Contains("ext") || header.Contains("extended"))
                    format.ExtendedIndex = i;
                else if (header.Contains("fd") || header.Contains("canfd"))
                    format.FDIndex = i;
                else if (header.Contains("dir") || header.Contains("direction") || header.Contains("tx") || header.Contains("rx"))
                    format.DirectionIndex = i;
                else if (header.Contains("channel"))
                    format.ChannelIndex = i;
            }

            format.HasHeader = format.TimeIndex >= 0 || format.IdIndex >= 0 || format.DataIndex >= 0;
            return format;
        }

        private CANMessage? ParseCSVLine(string line, char separator, CSVFormat format)
        {
            string[] parts = line.Split(separator);
            if (parts.Length < 2) return null;

            ulong timestamp = 0;
            uint id = 0;
            byte[] data = Array.Empty<byte>();
            int dlc = 0;
            bool isExtended = false;
            bool isCANFD = false;
            Direction direction = Direction.Received;
            int channel = 1;

            // Парсим время
            if (format.TimeIndex >= 0 && format.TimeIndex < parts.Length)
                timestamp = ParseTime(parts[format.TimeIndex]);

            // Парсим ID
            if (format.IdIndex >= 0 && format.IdIndex < parts.Length)
                id = ParseId(parts[format.IdIndex]);

            // Парсим данные
            if (format.DataIndex >= 0 && format.DataIndex < parts.Length)
            {
                data = ParseData(parts[format.DataIndex], out dlc);
                isCANFD = dlc > 8;
            }

            // Парсим DLC
            if (format.DLCIndex >= 0 && format.DLCIndex < parts.Length)
                dlc = ParseDLC(parts[format.DLCIndex]);

            // Парсим Extended flag
            if (format.ExtendedIndex >= 0 && format.ExtendedIndex < parts.Length)
                isExtended = ParseExtended(parts[format.ExtendedIndex]);

            // Парсим CAN FD flag
            if (format.FDIndex >= 0 && format.FDIndex < parts.Length)
                isCANFD = ParseCANFD(parts[format.FDIndex]);

            // Парсим направление
            if (format.DirectionIndex >= 0 && format.DirectionIndex < parts.Length)
                direction = ParseDirection(parts[format.DirectionIndex]);

            // Парсим канал
            if (format.ChannelIndex >= 0 && format.ChannelIndex < parts.Length)
                channel = ParseChannel(parts[format.ChannelIndex]);

            // Если время не указано, используем индекс строки
            if (timestamp == 0)
                timestamp = (ulong)(DateTime.Now.Ticks / 10);

            var message = new CANMessage(id, data, timestamp)
            {
                IsExtended = isExtended,
                IsCANFD = isCANFD,
                Direction = direction,
                Channel = channel,
                DLC = dlc
            };

            return message;
        }
        #endregion

        #region ASC Парсер
        private List<CANMessage> ParseASC(string filePath)
        {
            var messages = new List<CANMessage>();
            
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    
                    if (trimmed.StartsWith("//") || trimmed.StartsWith(";") || 
                        trimmed.StartsWith("date") || trimmed.StartsWith("base"))
                        continue;
                    
                    CANMessage? message = ParseASCLine(trimmed);
                    if (message != null)
                        messages.Add(message);
                }
            }
            catch (Exception ex)
            {
                throw new LogParserException($"Error parsing ASC: {ex.Message}", ex);
            }

            return messages;
        }

        private CANMessage? ParseASCLine(string line)
        {
            // Регулярное выражение для ASC
            string pattern = @"^(\d+\.\d+)\s+(\d+)\s+([0-9A-Fa-f]+)\s+(Rx|Tx)\s+(?:(\w+)\s+)?(?:(\w+)\s+)?(?:([Ee])\s+)?(?:(\w+)\s+)?d\s+(\d+)\s+((?:[0-9A-Fa-f]{2}\s*)+)$";
            Match match = Regex.Match(line, pattern);
            
            if (!match.Success) return null;

            try
            {
                // Парсим время
                double timeSeconds = double.Parse(match.Groups[1].Value, InvariantCulture);
                ulong timestamp = (ulong)(timeSeconds * 1000000);
                
                // Парсим канал
                int channel = int.Parse(match.Groups[2].Value);
                
                // Парсим ID
                uint id = ParseId(match.Groups[3].Value);
                
                // Парсим направление
                Direction direction = match.Groups[4].Value.Equals("Tx", StringComparison.OrdinalIgnoreCase) 
                    ? Direction.Transmitted 
                    : Direction.Received;
                
                // Определяем тип сообщения
                bool isCANFD = false;
                bool isExtended = false;
                
                for (int i = 5; i <= 8; i++)
                {
                    if (match.Groups[i].Success)
                    {
                        string value = match.Groups[i].Value.ToUpperInvariant();
                        if (value.Contains("CANFD") || value.Contains("FD"))
                            isCANFD = true;
                        if (value == "E")
                            isExtended = true;
                    }
                }
                
                // Парсим DLC
                int dlc = int.Parse(match.Groups[9].Value);
                
                // Парсим данные
                byte[] data = ParseData(match.Groups[10].Value, out _);
                
                // Ограничиваем данные по DLC
                if (data.Length > dlc)
                    Array.Resize(ref data, dlc);
                
                var message = new CANMessage(id, data, timestamp)
                {
                    Channel = channel,
                    Direction = direction,
                    IsExtended = isExtended,
                    IsCANFD = isCANFD,
                    DLC = dlc
                };
                
                return message;
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region TRC Парсер
        private List<CANMessage> ParseTRC(string filePath)
        {
            var messages = new List<CANMessage>();
            
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    
                    if (trimmed.StartsWith(";$")) continue;
                    
                    CANMessage? message = ParseTRCLine(trimmed);
                    if (message != null)
                        messages.Add(message);
                }
            }
            catch (Exception ex)
            {
                throw new LogParserException($"Error parsing TRC: {ex.Message}", ex);
            }

            return messages;
        }

        private CANMessage? ParseTRCLine(string line)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7) return null;

            try
            {
                double timeSeconds = double.Parse(parts[0], InvariantCulture);
                ulong timestamp = (ulong)(timeSeconds * 1000000);
                
                int channel = int.Parse(parts[1]);
                uint id = ParseId(parts[2]);
                
                Direction direction = parts[3].Equals("Tx", StringComparison.OrdinalIgnoreCase) 
                    ? Direction.Transmitted 
                    : Direction.Received;
                
                bool isExtended = id > 0x7FF;
                bool isCANFD = false;
                
                for (int i = 4; i < parts.Length; i++)
                {
                    if (parts[i].Equals("CANFD", StringComparison.OrdinalIgnoreCase))
                    {
                        isCANFD = true;
                        break;
                    }
                }
                
                int dlcIndex = -1;
                for (int i = 4; i < parts.Length; i++)
                {
                    if (parts[i].Equals("d", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                    {
                        dlcIndex = i + 1;
                        break;
                    }
                }
                
                if (dlcIndex == -1) return null;
                
                int dlc = int.Parse(parts[dlcIndex]);
                
                var data = new List<byte>();
                for (int i = dlcIndex + 1; i < parts.Length && data.Count < dlc; i++)
                {
                    if (parts[i].Length == 2 && 
                        byte.TryParse(parts[i], NumberStyles.HexNumber, InvariantCulture, out byte b))
                    {
                        data.Add(b);
                    }
                }
                
                if (data.Count == 0) return null;
                
                var message = new CANMessage(id, data.ToArray(), timestamp)
                {
                    Channel = channel,
                    Direction = direction,
                    IsExtended = isExtended,
                    IsCANFD = isCANFD,
                    DLC = dlc
                };
                
                return message;
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region Утилитарные методы
        private ulong ParseTime(string timeStr)
        {
            timeStr = timeStr.Trim();
            
            if (ulong.TryParse(timeStr, out ulong result))
                return result;
            
            if (double.TryParse(timeStr, NumberStyles.Any, InvariantCulture, out double doubleResult))
                return (ulong)(doubleResult * 1000000);
            
            return 0;
        }

        private uint ParseId(string idStr)
        {
            idStr = idStr.Trim().ToUpperInvariant();
            
            idStr = idStr.Replace("0X", "").Replace("X", "").Replace("CAN", "").Replace("ID:", "");
            
            idStr = new string(idStr.Where(c => char.IsDigit(c) || (c >= 'A' && c <= 'F')).ToArray());
            
            if (uint.TryParse(idStr, NumberStyles.HexNumber, InvariantCulture, out uint result))
                return result;
            
            return 0;
        }

        private byte[] ParseData(string dataStr, out int dlc)
        {
            dlc = 0;
            dataStr = dataStr.Trim().ToUpperInvariant();
            
            dataStr = new string(dataStr.Where(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')).ToArray());
            
            if (string.IsNullOrEmpty(dataStr) || dataStr.Length % 2 != 0)
                return Array.Empty<byte>();
            
            dlc = dataStr.Length / 2;
            byte[] data = new byte[dlc];
            
            for (int i = 0; i < dlc; i++)
            {
                string byteStr = dataStr.Substring(i * 2, 2);
                data[i] = byte.Parse(byteStr, NumberStyles.HexNumber, InvariantCulture);
            }
            
            return data;
        }

        private int ParseDLC(string dlcStr)
        {
            dlcStr = dlcStr.Trim();
            return int.TryParse(dlcStr, out int result) ? Math.Clamp(result, 0, 64) : 0;
        }

        private bool ParseExtended(string extendedStr)
        {
            extendedStr = extendedStr.Trim().ToUpperInvariant();
            return extendedStr == "1" || extendedStr == "TRUE" || extendedStr == "YES" || extendedStr == "EXTENDED";
        }

        private bool ParseCANFD(string fdStr)
        {
            fdStr = fdStr.Trim().ToUpperInvariant();
            return fdStr == "1" || fdStr == "TRUE" || fdStr == "YES" || fdStr == "FD" || fdStr.Contains("CANFD");
        }

        private Direction ParseDirection(string dirStr)
        {
            dirStr = dirStr.Trim().ToUpperInvariant();
            return dirStr == "TX" || dirStr == "TRANSMIT" || dirStr == "1" 
                ? Direction.Transmitted 
                : Direction.Received;
        }

        private int ParseChannel(string channelStr)
        {
            channelStr = channelStr.Trim();
            return int.TryParse(channelStr, out int result) ? Math.Max(1, result) : 1;
        }
        #endregion

        #region Вспомогательные методы
        private List<CANMessage> ParseGenericLog(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length == 0) return new List<CANMessage>();

            string firstLine = lines[0];
            
            if (firstLine.Contains(";") || firstLine.Contains("date"))
                return ParseTRC(filePath);
            else if (firstLine.Contains("base") || firstLine.Contains("//"))
                return ParseASC(filePath);
            else if (firstLine.Contains(",") || firstLine.Contains("\t"))
                return ParseCSV(filePath);
            else
                return ParseTextLog(filePath);
        }

        private List<CANMessage> ParseTextLog(string filePath)
        {
            var messages = new List<CANMessage>();
            
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                
                foreach (string line in lines)
                {
                    CANMessage? message = ParseGenericLine(line);
                    if (message != null)
                        messages.Add(message);
                }
            }
            catch (Exception ex)
            {
                throw new LogParserException($"Error parsing text log: {ex.Message}", ex);
            }

            return messages;
        }

        private CANMessage? ParseGenericLine(string line)
        {
            CANMessage? message;
            
            message = ParseASCLine(line);
            if (message != null) return message;
            
            message = ParseTRCLine(line);
            if (message != null) return message;
            
            char[] separators = { ',', ';', '\t' };
            foreach (char separator in separators)
            {
                if (line.Contains(separator))
                {
                    var format = new CSVFormat
                    {
                        TimeIndex = 0,
                        IdIndex = 1,
                        DataIndex = 2,
                        HasHeader = false
                    };
                    
                    message = ParseCSVLine(line, separator, format);
                    if (message != null) return message;
                }
            }
            
            return null;
        }

        private List<CANMessage> ParseAutodetect(string filePath)
        {
            List<CANMessage> messages;
            
            try { messages = ParseASC(filePath); if (messages.Count > 0) return messages; } catch { }
            try { messages = ParseTRC(filePath); if (messages.Count > 0) return messages; } catch { }
            try { messages = ParseCSV(filePath); if (messages.Count > 0) return messages; } catch { }
            try { messages = ParseTextLog(filePath); if (messages.Count > 0) return messages; } catch { }
            
            return new List<CANMessage>();
        }
        #endregion

        #region Вспомогательные классы
        private class CSVFormat
        {
            public bool HasHeader { get; set; }
            public int TimeIndex { get; set; } = -1;
            public int IdIndex { get; set; } = -1;
            public int DataIndex { get; set; } = -1;
            public int DLCIndex { get; set; } = -1;
            public int ExtendedIndex { get; set; } = -1;
            public int FDIndex { get; set; } = -1;
            public int DirectionIndex { get; set; } = -1;
            public int ChannelIndex { get; set; } = -1;
        }
        #endregion
    }

    public interface ILogParser
    {
        List<CANMessage> Parse(string filePath);
    }

    public class LogParserException : Exception
    {
        public LogParserException(string message) : base(message) { }
        public LogParserException(string message, Exception innerException) : base(message, innerException) { }
    }
}