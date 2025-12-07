namespace CANalyzer.Core.Models
{
    public enum AnalysisMode
    {
        Basic,
        Standard,
        Advanced,
        Full
    }
    
    public enum FileFormat
    {
        CSV,
        ASC,
        MF4,
        PCAP,
        TRC
    }
    
    public enum MatchCategory
    {
        Defined,    // Полное совпадение с DBC
        Undefined,  // Неизвестный сигнал
        Try         // Возможное совпадение
    }
}