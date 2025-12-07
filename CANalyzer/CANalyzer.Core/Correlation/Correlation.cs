namespace CANalyzer
{
    public class Correlation
    {
        public string Signal1 { get; set; } = string.Empty;
        public string Signal2 { get; set; } = string.Empty;
        public double CorrelationValue { get; set; }
        public string Strength { get; set; } = string.Empty;
    }
}