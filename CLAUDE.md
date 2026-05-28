# CLAUDE.md — Clinkon1C

Это инструкции для Claude Code при работе с проектом Clinkon1C.

## Что это за проект

Clinkon1C — консольная TUI-утилита для администраторов 1С на Windows.
Написана на C# (.NET 8), использует Terminal.Gui для интерфейса.
Собирается в self-contained .exe без зависимостей.

**Полное ТЗ:** `docs/ТЗ.md`

## Текущая фаза разработки

**Phase 1 (MVP)** — только модуль Кэш + главное дерево.
Логи и Шаблоны — Phase 2, не реализовывать сейчас.

## Стек

- C# .NET 8 LTS
- Terminal.Gui (NuGet: Terminal.Gui)
- Сборка: `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true`

## Структура проекта

```
src/
├── Core/
│   ├── IModule.cs              — интерфейс модуля (реализуют все модули)
│   ├── TreeBuilder.cs          — построение главного дерева из модулей
│   ├── ProfileFinder.cs        — поиск профилей через реестр ProfileList
│   ├── ProcessHelper.cs        — работа с процессами 1С
│   ├── RestartManagerHelper.cs — Windows Restart Manager API (P/Invoke)
│   ├── SafeDelete.cs           — удаление с защитой файлов и Backup
│   ├── RegistryHelper.cs       — настройки и исключения в реестре
│   └── Logger.cs               — лог операций + Windows EventLog
├── Modules/
│   ├── Cache/
│   │   └── CacheModule.cs      — MVP: парсинг ibases.v8i, дерево кэша
│   ├── Logs/
│   │   └── LogsModule.cs       — Phase 2, заглушка
│   └── Templates/
│       └── TemplatesModule.cs  — Phase 2, заглушка
├── UI/
│   ├── MainTree.cs             — главное окно с TreeView
│   ├── ActionBar.cs            — нижняя панель действий
│   └── Dialogs.cs              — диалоги подтверждения (Y/N, ввод слова)
└── Program.cs                  — точка входа, Mutex, предупреждение, запуск UI
```

## Ключевые архитектурные решения

### IModule
Каждый верхний узел дерева реализует интерфейс:
```csharp
public interface IModule
{
    string Name { get; }           // Название узла ("Кэш", "Логи", ...)
    string GetSize();              // Суммарный размер для отображения
    IEnumerable<TreeNode> GetTree(); // Дерево узлов для TreeView
    void Delete(IEnumerable<TreeNode> selected); // Удаление выделенного
    void DryRun(IEnumerable<TreeNode> selected); // Предпросмотр
}
```

### SafeDelete
Все удаления только через SafeDelete. Нельзя обойти.
Защищённые маски (хардкод): `*.lic`, `*.pfl`, `*.usr`, `1CV8Clnt.flt`, `*.1CD`, `*.dbf`
При занятом файле — сначала Restart Manager API, потом retry x3.

### Реестр
- Настройки: `HKCU:\Software\Clinkon1C\Settings`
- Исключения: `HKCU:\Software\Clinkon1C\Exclusions`

### Backup
По умолчанию **выключен**. Путь: `C:\Temp\Clinkon1C\Backup\{datetime}\`

### Версия
Константа в `Program.cs`:
```csharp
public const string VERSION = "1.0.0";
```
При обновлении менять только здесь.

## Язык интерфейса

Только русский. Все строки UI, сообщения, подтверждения — на русском.

## Цветовая схема Terminal.Gui

| Элемент | Цвет |
|---|---|
| Заголовки | Cyan |
| Активные узлы | White |
| Недоступные | DarkGray |
| Выделенные | Yellow |
| Успех | Green |
| Ошибка | Red |
| Dry Run | Magenta |
| Исключения | DarkCyan |

## Чего не делать

- Не реализовывать Phase 2 и Phase 3 в рамках MVP
- Не добавлять CLI-параметры (Phase 4)
- Не удалять файлы напрямую, минуя SafeDelete
- Не хранить настройки в файлах рядом с exe (только реестр)
- Не делать UI на русском через enum/константы — строки прямо в коде нормально для MVP

## GitHub Actions

Файл: `.github/workflows/build.yml`
Триггер: тег `v*.*.*`
Артефакт: `Clinkon1C.exe` в Releases
