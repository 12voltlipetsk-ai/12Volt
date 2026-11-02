# CANalyzer (ранний снимок)

> ⚠️ **Актуальная версия проекта развивается в репозитории [CANalyzer.ReverseEngineering](https://github.com/12voltlipetsk-ai/CANalyzer.ReverseEngineering).**
> Этот репозиторий — ранний снимок от 07.12.2025, сохранён для истории.

Инструмент для анализа CAN-логов и генерации DBC-файлов (WPF, .NET 8). Загружает логи CSV/ASC, обнаруживает сигналы и генерирует DBC.

## Структура

Весь код лежит в папке [`CANalyzer/`](CANalyzer/):

```
CANalyzer/
├── CANalyzer.sln
├── CANalyzer.WPF/          # WPF-приложение (точка входа)
├── CANalyzer.Core/         # Модели, парсеры, анализаторы, DBC, ML, сервисы
├── CANalyzer.ML/           # ML-классификация сигналов
├── CANalyzer.Correlation/  # Корреляционный анализ
├── CANalyzer.Scripts/      # Консольные утилиты
└── CANalyzer.Tests/        # Тесты
```

## Сборка и запуск

Требуется Windows и [.NET 8.0 SDK](https://dotnet.microsoft.com/download).

```powershell
cd CANalyzer
dotnet restore CANalyzer.sln
dotnet build CANalyzer.sln
dotnet run --project CANalyzer.WPF\CANalyzer.WPF.csproj
```
