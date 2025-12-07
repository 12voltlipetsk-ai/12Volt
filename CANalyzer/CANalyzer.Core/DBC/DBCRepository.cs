using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CANalyzer.Core.DBC
{
    public interface IDBCRepository
    {
        Task LoadAllAsync();
        Task LoadFromUrlAsync(string url);
        void LoadFromFile(string filePath);
        List<DBCDatabase> GetAllDatabases();
        List<DBCMessage> FindMessages(uint id);
        List<DBCSignal> FindSignalByName(string name);
        void SaveCache(string cachePath);
        void LoadCache(string cachePath);
    }

    public class DBCRepository : IDBCRepository
    {
        private readonly ConcurrentDictionary<string, DBCDatabase> _databases = new();
        private readonly HttpClient _httpClient;
        private readonly object _lock = new();
        
        // Известные репозитории DBC
        private readonly Dictionary<string, string[]> _knownRepositories = new()
        {
            ["commaai"] = new[]
            {
                "https://raw.githubusercontent.com/commaai/opendbc/master/",
                "https://raw.githubusercontent.com/commaai/opendbc/master/toyota_prius_2017_pt_generated.dbc",
                "https://raw.githubusercontent.com/commaai/opendbc/master/honda_civic_touring_2016_can_generated.dbc",
                "https://raw.githubusercontent.com/commaai/opendbc/master/hyundai_kia_generated.dbc",
                "https://raw.githubusercontent.com/commaai/opendbc/master/ford_f150_2020_generated.dbc"
            },
            ["byd"] = new[]
            {
                "https://raw.githubusercontent.com/BYDcar/opendbc-byd/master/",
                "https://raw.githubusercontent.com/BYDcar/opendbc-byd/master/BYD_Atto3.dbc",
                "https://raw.githubusercontent.com/BYDcar/opendbc-byd/master/BYD_Han.dbc"
            },
            ["opendbc_community"] = new[]
            {
                "https://raw.githubusercontent.com/opendbc/dbc/master/",
                "https://raw.githubusercontent.com/opendbc/dbc/master/body.dbc",
                "https://raw.githubusercontent.com/opendbc/dbc/master/chassis.dbc",
                "https://raw.githubusercontent.com/opendbc/dbc/master/powertrain.dbc"
            }
        };

        public DBCRepository()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
                DefaultRequestHeaders = { { "User-Agent", "CANalyzer/1.0" } }
            };
        }

        public async Task LoadAllAsync()
        {
            var tasks = new List<Task>();
            
            foreach (var repo in _knownRepositories.Values)
            {
                foreach (var url in repo)
                {
                    if (url.EndsWith(".dbc"))
                    {
                        tasks.Add(LoadDbcFileAsync(url));
                    }
                    else if (url.EndsWith("/"))
                    {
                        tasks.Add(LoadRepositoryAsync(url));
                    }
                }
            }
            
            await Task.WhenAll(tasks);
            Console.WriteLine($"Loaded {_databases.Count} DBC databases");
        }

        private async Task LoadRepositoryAsync(string baseUrl)
        {
            try
            {
                // Попробуем получить index или найти .dbc файлы
                var content = await _httpClient.GetStringAsync(baseUrl);
                
                // Ищем ссылки на .dbc файлы
                var dbcFiles = FindDbcLinks(content, baseUrl);
                
                foreach (var dbcFile in dbcFiles.Take(10)) // Ограничим для скорости
                {
                    await LoadDbcFileAsync(dbcFile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load repository {baseUrl}: {ex.Message}");
            }
        }

        private List<string> FindDbcLinks(string htmlContent, string baseUrl)
        {
            var links = new List<string>();
            var pattern = @"href=""([^""]+\.dbc)""";
            var matches = Regex.Matches(htmlContent, pattern);
            
            foreach (Match match in matches)
            {
                if (match.Success && match.Groups.Count > 1)
                {
                    var relativePath = match.Groups[1].Value;
                    var fullUrl = new Uri(new Uri(baseUrl), relativePath).ToString();
                    links.Add(fullUrl);
                }
            }
            
            return links.Distinct().ToList();
        }

        public async Task LoadDbcFileAsync(string url)
        {
            try
            {
                var dbcContent = await _httpClient.GetStringAsync(url);
                var database = ParseDbcContent(dbcContent, GetFileNameFromUrl(url));
                
                if (database != null && database.Messages.Count > 0)
                {
                    lock (_lock)
                    {
                        if (!_databases.ContainsKey(database.Name))
                        {
                            _databases[database.Name] = database;
                            Console.WriteLine($"Loaded DBC: {database.Name} ({database.Messages.Count} messages)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load DBC from {url}: {ex.Message}");
            }
        }

        public void LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"DBC file not found: {filePath}");

            try
            {
                var dbcContent = File.ReadAllText(filePath, Encoding.UTF8);
                var database = ParseDbcContent(dbcContent, Path.GetFileNameWithoutExtension(filePath));
                
                if (database != null)
                {
                    lock (_lock)
                    {
                        _databases[database.Name] = database;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse DBC file {filePath}: {ex.Message}", ex);
            }
        }

        private DBCDatabase ParseDbcContent(string content, string name)
        {
            var database = new DBCDatabase
            {
                Name = name,
                Source = name,
                LoadTime = DateTime.UtcNow
            };

            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            DBCMessage currentMessage = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
                    continue;

                if (trimmed.StartsWith("BO_"))
                {
                    currentMessage = ParseMessageLine(trimmed);
                    if (currentMessage != null)
                    {
                        database.Messages[currentMessage.Id] = currentMessage;
                    }
                }
                else if (trimmed.StartsWith("SG_") && currentMessage != null)
                {
                    var signal = ParseSignalLine(trimmed);
                    if (signal != null)
                    {
                        currentMessage.Signals.Add(signal);
                    }
                }
                else if (trimmed.StartsWith("CM_ BO_") || trimmed.StartsWith("CM_ SG_"))
                {
                    ParseCommentLine(trimmed, database, currentMessage);
                }
                else if (trimmed.StartsWith("VAL_"))
                {
                    ParseValueTable(trimmed, database, currentMessage);
                }
            }

            return database.Messages.Count > 0 ? database : null;
        }

        private DBCMessage ParseMessageLine(string line)
        {
            // Формат: BO_ ID Name: DLC Transmitter
            var match = Regex.Match(line, @"BO_\s+(\d+)\s+([\w_]+)\s*:\s*(\d+)\s+([\w_]+)");
            
            if (!match.Success)
                return null;

            return new DBCMessage
            {
                Id = uint.Parse(match.Groups[1].Value),
                Name = match.Groups[2].Value,
                DLC = int.Parse(match.Groups[3].Value),
                Transmitter = match.Groups[4].Value,
                Signals = new List<DBCSignal>()
            };
        }

        private DBCSignal ParseSignalLine(string line)
        {
            // Формат: SG_ SignalName : StartBit|Length@ByteOrderSign (Factor,Offset) [Min|Max] "Unit" Receiver
            var pattern = @"SG_\s+([\w_]+)\s+:\s+(\d+)\|(\d+)@([01])([+\-])\s+\(([^,]+),([^)]+)\)\s+\[([^|]+)\|([^\]]+)\]\s+""([^""]*)""\s+([\w_]+)";
            var match = Regex.Match(line, pattern);
            
            if (!match.Success)
                return null;

            return new DBCSignal
            {
                Name = match.Groups[1].Value,
                StartBit = int.Parse(match.Groups[2].Value),
                Length = int.Parse(match.Groups[3].Value),
                IsLittleEndian = match.Groups[4].Value == "1",
                IsSigned = match.Groups[5].Value == "-",
                Factor = double.Parse(match.Groups[6].Value, System.Globalization.CultureInfo.InvariantCulture),
                Offset = double.Parse(match.Groups[7].Value, System.Globalization.CultureInfo.InvariantCulture),
                Minimum = double.Parse(match.Groups[8].Value, System.Globalization.CultureInfo.InvariantCulture),
                Maximum = double.Parse(match.Groups[9].Value, System.Globalization.CultureInfo.InvariantCulture),
                Unit = match.Groups[10].Value,
                Receiver = match.Groups[11].Value
            };
        }

        private void ParseCommentLine(string line, DBCDatabase database, DBCMessage currentMessage)
        {
            // Формат: CM_ BO_ ID "Comment";
            // или: CM_ SG_ ID SignalName "Comment";
            var match = Regex.Match(line, @"CM_\s+(BO_|SG_)\s+(\d+)(?:\s+([\w_]+))?\s+""([^""]*)"";");
            
            if (match.Success)
            {
                var type = match.Groups[1].Value;
                var id = uint.Parse(match.Groups[2].Value);
                var comment = match.Groups[4].Value;

                if (type == "BO_" && database.Messages.ContainsKey(id))
                {
                    database.Messages[id].Comment = comment;
                }
                else if (type == "SG_")
                {
                    var signalName = match.Groups[3].Value;
                    if (database.Messages.TryGetValue(id, out var message))
                    {
                        var signal = message.Signals.FirstOrDefault(s => s.Name == signalName);
                        if (signal != null)
                        {
                            signal.Comment = comment;
                        }
                    }
                }
            }
        }

        private void ParseValueTable(string line, DBCDatabase database, DBCMessage currentMessage)
        {
            // Формат: VAL_ ID SignalName Value1 "Description1" Value2 "Description2" ... ;
            var match = Regex.Match(line, @"VAL_\s+(\d+)\s+([\w_]+)\s+(.+);");
            
            if (match.Success)
            {
                var id = uint.Parse(match.Groups[1].Value);
                var signalName = match.Groups[2].Value;
                var valuesStr = match.Groups[3].Value;

                if (database.Messages.TryGetValue(id, out var message))
                {
                    var signal = message.Signals.FirstOrDefault(s => s.Name == signalName);
                    if (signal != null)
                    {
                        signal.ValueTable = ParseValueTableEntries(valuesStr);
                    }
                }
            }
        }

        private Dictionary<int, string> ParseValueTableEntries(string valuesStr)
        {
            var table = new Dictionary<int, string>();
            var pairs = valuesStr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            
            for (int i = 0; i < pairs.Length; i += 2)
            {
                if (i + 1 < pairs.Length)
                {
                    var value = int.Parse(pairs[i]);
                    var description = pairs[i + 1].Trim('"');
                    table[value] = description;
                }
            }
            
            return table;
        }

        private string GetFileNameFromUrl(string url)
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);
            return Path.GetFileNameWithoutExtension(fileName) ?? "unknown";
        }

        public List<DBCDatabase> GetAllDatabases()
        {
            return _databases.Values.ToList();
        }

        public List<DBCMessage> FindMessages(uint id)
        {
            return _databases.Values
                .SelectMany(db => db.Messages.Values)
                .Where(msg => msg.Id == id)
                .ToList();
        }

        public List<DBCSignal> FindSignalByName(string name)
        {
            return _databases.Values
                .SelectMany(db => db.Messages.Values)
                .SelectMany(msg => msg.Signals)
                .Where(sig => sig.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public async Task LoadFromUrlAsync(string url)
        {
            await LoadDbcFileAsync(url);
        }

        public void SaveCache(string cachePath)
        {
            var cacheData = new CacheData
            {
                Databases = _databases.Values.ToList(),
                CacheTime = DateTime.UtcNow
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(cacheData);
            File.WriteAllText(cachePath, json);
        }

        public void LoadCache(string cachePath)
        {
            if (!File.Exists(cachePath))
                return;

            var json = File.ReadAllText(cachePath);
            var cacheData = System.Text.Json.JsonSerializer.Deserialize<CacheData>(json);
            
            if (cacheData != null)
            {
                lock (_lock)
                {
                    foreach (var db in cacheData.Databases)
                    {
                        _databases[db.Name] = db;
                    }
                }
            }
        }

        private class CacheData
        {
            public List<DBCDatabase> Databases { get; set; } = new();
            public DateTime CacheTime { get; set; }
        }
    }

    // ПЕРВОНАЧАЛЬНЫЕ ОПРЕДЕЛЕНИЯ КЛАССОВ УЖЕ СУЩЕСТВУЮТ ВЫШЕ В ФАЙЛЕ
    // Эти повторные определения были удалены для устранения ошибок CS0101
    // Если при компиляции возникнут ошибки о missing types, создайте отдельные файлы:
    // 1. DBCDatabase.cs
    // 2. DBCMessage.cs  
    // 3. DBCSignal.cs
    // И скопируйте туда соответствующие классы из этого файла
}