# Расхождения с IMigrationService (ветка vibe/ui-preview-confirm)

Файл `src/MigrationContract.cs` на ветку `prog/export-default-core` **не копируется**.
Фасад `Migration` / `MigrationService` использует те же имена DTO и методов, чтобы UI мог сменить адаптер.

## Совпадает
- DTO: PreviewItem, MigrationPreview (CanWriteToGame, NextStep, FallbackReason, CandidateSteamAccounts, HasSteamStore, HasMicrosoftStore), SteamAccount, ExportResult, BackupInfo, SkippedBinding (Action/Context/Reason/Message).
- Методы: Prepare, Diagnose, ListSteamAccounts, ListBackups, Restore(BackupInfo), CreateDiagnosticReport, ExportForImport, WriteToGame.
- WriteConfirmation.Confirm(preview) и публичное BoundPreview.

## Аддитивные отличия
| Тема | Programming (эта ветка) | UI-контракт PR |
|------|-------------------------|----------------|
| Диалог подтверждения | FromUiDialog(summary) | только Confirm |
| Confirm | требует CanWriteToGame | не проверяет |
| План | UnderlyingPlan / Plan | UnderlyingPlan internal |
| BackupInfo | + Folder, CreatedUtc | Manifest/Label/State |
| Результат записи | WriteToGameResult + Notices | WriteToGame → string |
| Тестовые перегрузки | пути steam/local/backup | только UI-сигнатуры |

## Продуктовое поведение вне UI-файла
- Автовыбор Steam: dual → ActiveUser → MostRecent.
- Запрет записи remotecache.vdf + проверка хэша.
- Полный бэкап / атомарная запись / откат через MigrationTransaction.

После слияния UI удаляет свой временный MigrationContract.cs и берёт типы из этой ветки.
