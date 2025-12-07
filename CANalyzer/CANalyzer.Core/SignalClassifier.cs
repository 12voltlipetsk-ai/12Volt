using System.Collections.Generic;
using CANalyzer.Core.Models;

namespace CANalyzer.ML.NeuralNetworks
{
    public class SignalClassifier
    {
        public void ClassifySignals(List<CANSignal> signals)
        {
            // Простая реализация классификатора сигналов
            foreach (var signal in signals)
            {
                // Здесь будет логика машинного обучения
                // Пока используем простую эвристику
                double range = signal.MaxPhysical - signal.MinPhysical;
                
                if (range < 2)
                    signal.Type = SignalType.State;
                else if (range > 1000)
                    signal.Type = SignalType.Sensor;
                else
                    signal.Type = SignalType.Cyclic;
            }
        }
    }
}