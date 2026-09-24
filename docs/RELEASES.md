# Релизы: контракт имён и версий

Единый источник версии продукта — файл `VERSION` в корне репозитория
(формат `X.Y.Z`). Сборка принимает `-Version X.Y.Z`; если параметр не задан,
читается `VERSION`. При сборке генерируется `obj/VersionInfo.cs` (каталог `obj/`
уже в `.gitignore`) с `AssemblyVersion` / `AssemblyFileVersion` /
`AssemblyInformationalVersion` и классом `BuildInfo.Version`.

## Стабильные имена артефактов

Каталог `dist/release/` всегда содержит ровно четыре файла с фиксированными
именами (без номера версии в имени):

| Файл | Назначение |
|------|------------|
| `FlightBridge.exe` | портативный одиночный EXE |
| `FlightBridge-portable.zip` | EXE + `README.md` |
| `FlightBridge-Setup.exe` | установщик |
| `SHA256.txt` | контрольные суммы трёх файлов выше |

Формат строк в `SHA256.txt` (как у `sha256sum`):

```text
<lowercase-sha256>  <bare-filename>
```

Два пробела между хешем и именем; только имя файла, без папок.

Человекочитаемая папка `dist/Flight Bridge <ver> - CURRENT/` может сохраняться
для локальной работы; публикуемые артефакты GitHub Actions / Release берутся
из `dist/release/`.

## Теги и draft release

Workflow `.github/workflows/release.yml` срабатывает на тег `v*`:

1. Из тега берётся версия (`v0.5.1` → `0.5.1`); иначе провал.
2. Запускается `./build.ps1 -Version <ver>`.
3. Проверяется, что `FileVersion` у `dist/release/FlightBridge.exe` равен `<ver>.0`.
4. Создаётся **draft** release с именем `Flight Bridge <ver>` и вложениями
   ровно из `dist/release/` (четыре файла выше). `prerelease: false`, `draft: true`.

## Контракт для модуля Updater

Updater (отдельная работа) должен:

1. Читать `GET .../releases/latest` (GitHub API игнорирует draft и prerelease).
2. Сравнивать тег `vX.Y.Z` с `BuildInfo.Version` в текущей сборке.
3. Скачивать актив по **стабильному имени** (`FlightBridge.exe` или
   `FlightBridge-portable.zip` / `FlightBridge-Setup.exe` — по сценарию).
4. Сверять SHA-256 с соответствующей строкой в `SHA256.txt`.

Не опираться на имена с номером версии в названии файла.
