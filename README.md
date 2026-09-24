# FlightBridge Grok — EXPERIMENTAL / WORK IN PROGRESS

> **This is not a finished product.** Experimental repository. Code is under active development by the Grok team.
> **Do not install** and **do not use on live simulator profiles** until a stable release is clearly published here. Any builds are test-only.
> Original Codex project (separate): [Flight-Bridge](https://github.com/DenisUgarov/Flight-Bridge).

---

### Same warning in every app language

| Lang | Warning |
|------|---------|
| **English** | **EXPERIMENTAL / WORK IN PROGRESS.** Not a finished product. Do not install. Do not use on live MSFS profiles until a stable release is published. Test builds only. |
| **Русский** | **ЭКСПЕРИМЕНТ / В РАЗРАБОТКЕ.** Это не готовая программа. Не устанавливайте. Не используйте на рабочих профилях симулятора, пока не будет стабильного релиза. Сборки — только тестовые. |
| **Deutsch** | **EXPERIMENTELL / IN ENTWICKLUNG.** Kein fertiges Produkt. Nicht installieren. Nicht auf produktiven Simulator-Profilen verwenden, bis ein stabiles Release veröffentlicht ist. Builds nur zum Testen. |
| **Français** | **EXPÉRIMENTAL / EN COURS DE DÉVELOPPEMENT.** Ce n’est pas un produit fini. Ne pas installer. Ne pas utiliser sur des profils simulateur de production tant qu’une version stable n’est pas publiée. Builds de test uniquement. |
| **Español** | **EXPERIMENTAL / EN DESARROLLO.** No es un producto terminado. No instalar. No usar en perfiles reales del simulador hasta que haya una versión estable. Solo builds de prueba. |
| **Italiano** | **SPERIMENTALE / IN SVILUPPO.** Non è un prodotto finito. Non installare. Non usare su profili reali del simulatore finché non viene pubblicata una release stabile. Solo build di test. |
| **Português** | **EXPERIMENTAL / EM DESENVOLVIMENTO.** Não é um produto acabado. Não instalar. Não usar em perfis reais do simulador até haver um lançamento estável. Apenas builds de teste. |
| **Polski** | **EKSPERYMENTALNE / W TRAKCIE PRAC.** To nie jest gotowy produkt. Nie instaluj. Nie używaj na roboczych profilach symulatora, dopóki nie będzie stabilnego wydania. Tylko buildy testowe. |
| **Українська** | **ЕКСПЕРИМЕНТ / У РОЗРОБЦІ.** Це не готова програма. Не встановлюйте. Не використовуйте на робочих профілях симулятора, доки не буде стабільного релізу. Збірки лише тестові. |
| **Türkçe** | **DENEYSEL / GELİŞTİRME AŞAMASINDA.** Bitmiş bir ürün değildir. Kurmayın. Kararlı sürüm yayınlanana kadar canlı simülatör profillerinde kullanmayın. Yalnızca test derlemeleri. |
| **简体中文** | **实验性 / 开发中。** 这不是成品。请勿安装。在明确发布稳定版之前，请勿用于实际模拟器配置。构建版本仅供测试。 |
| **日本語** | **実験的 / 開発中。** 完成品ではありません。インストールしないでください。安定版が公開されるまで、本番のシミュレーター設定では使わないでください。ビルドはテスト専用です。 |
| **한국어** | **실험적 / 개발 중.** 완성된 제품이 아닙니다. 설치하지 마세요. 안정 버전이 명시적으로 나올 때까지 실제 시뮬레이터 프로필에 사용하지 마세요. 빌드는 테스트 전용입니다. |

---


## Что изменено относительно оригинала и почему

- **Главный путь** — запись в уже зарегистрированные профили MSFS 2024 через фасад `Migration` поверх `MigrationTransaction` (полный бэкап, SHA-256, атомарная запись, автооткат). Экспорт файла (`ExportForImport`) — запасной сценарий, когда `CanWriteToGame=false` (см. `NextStep` / `FallbackReason`).
- **Подтверждение** — `WriteConfirmation.Confirm(preview)` или `FromUiDialog(summary)` для UI. Без подтверждения или с подтверждением от другого превью запись отклоняется.
- **Аккаунт Steam** выбирается сам, если ровно один аккаунт содержит профили обеих игр (1250410 и 2537590). Если таких аккаунтов несколько, `Prepare(null)` возвращает понятный Issue/NextStep «выберите аккаунт».
- **Категория** для превью/экспорта берётся из профиля 2024; пары по категории не режутся. Для General — предупреждение.
- **Однозначный перенос KEY_*** между контекстами остаётся включённым в авто-плане; каждый перенос — Warning. Пропуски пишутся в `SkippedBinding` (`Reason` машинный + `Message` простым языком).
- **GUID** — только подсказка при нескольких кандидатах с одним ProductID+DeviceName.
- **`remotecache.vdf` не меняется** (проверка хэша после записи). В Notices — подсказка про конфликт Steam Cloud (Upload local) и непроверенное облако Xbox.
- **Диагностика** (`Diagnose`, `CreateDiagnosticReport`) — только чтение; отчёт без сырых путей и ID аккаунтов.
- Папки бэкапа/экспорта/отчёта по умолчанию: Documents/Flight Bridge/... (параметр folder необязателен).


## Исходная база (код 0.5.1)

Flight Bridge автоматически переносит существующие настройки контроллеров из
Microsoft Flight Simulator 2020 в зарегистрированные профили Microsoft Flight
Simulator 2024.

Репозиторий содержит полный исходный код приложения и установщика, сценарий
сборки, автоматические тесты и безопасные синтетические примеры формата профилей.
Реальные игровые профили, резервные копии и собранные EXE в репозиторий не входят.

## Сборка из исходников

На Windows 10/11 откройте PowerShell в корне проекта и выполните:

```powershell
.\build.ps1
```

Сценарий компилирует приложение и установщик штатным C#-компилятором .NET
Framework, запускает все тесты и создаёт папку текущего выпуска в `dist`.

## Как пользоваться

1. Установите и запустите Flight Bridge. Программа сама найдёт обе игры,
   хранилища и совместимые профили.
2. Полностью закройте MSFS 2020, MSFS 2024 и Steam, если используется Steam.
3. Нажмите **«Перенести настройки»**.
4. Запустите MSFS 2024 и проверьте оси и кнопки перед полётом.

Если версия 0.5.0 уже выполняла перенос на этом компьютере, версия 0.5.1 сама
обнаружит его резервную копию. Одна кнопка сначала вернёт исходные профили, а
затем сразу выполнит исправленный перенос.

Выбирать папки, XML-файлы или технические параметры не требуется. Если профиль
нельзя сопоставить однозначно, он остаётся без изменений. Программа не угадывает
и не создаёт незарегистрированные облачные записи.

Установщик и приложение поддерживают 13 языков: английский, русский, немецкий,
французский, испанский, итальянский, португальский, польский, украинский,
турецкий, упрощённый китайский, японский и корейский. Язык Windows выбирается
автоматически. При необходимости его можно изменить в видимом списке языка в
правом верхнем углу окна.

## Резервная копия и возврат

Перед любой записью Flight Bridge обязательно копирует целиком исходное и
целевое облачное хранилище, вычисляет SHA-256 каждого файла и повторно проверяет,
что данные игры не изменились во время подготовки. Отключить бэкап нельзя.

Копии по умолчанию находятся в `Документы\Flight Bridge\Backups`. Место хранения
можно заранее изменить в свёрнутом разделе приложения, например выбрать внешний
диск.

Каждый целевой профиль заменяется атомарно. После записи файл повторно читается и
проверяется. При ошибке программа автоматически возвращает уже изменённые файлы.
После прерывания питания или завершения процесса следующий запуск потребует сначала
нажать **«Вернуть настройки из последней копии»**.

Восстановление проверяет всю копию и возвращает только файлы, изменённые конкретным
переносом. Если пользователь позднее изменил профиль в игре, автоматическое
восстановление остановится, чтобы не уничтожить новые настройки.

## Поддерживаемые хранилища

- Steam: MSFS 2020 (`1250410`) и MSFS 2024 (`2537590`), включая библиотеки на
  других дисках и активный аккаунт Steam.
- Microsoft Store/Xbox WGS: изменение только существующего файла профиля внутри
  зарегистрированного контейнера. `containers.index` и метаданные контейнера не
  создаются и не переписываются.
- Смешанная установка Steam/Microsoft Store поддерживается.

Полный перенос и восстановление проверены на изолированных копиях нескольких
реальных форматов профилей. Сами игровые хранилища в эти проверки не записываются.

## Ограничения

- Flight Bridge изменяет только уже существующие пользовательские профили MSFS
  2024. Если нужного профиля ещё нет, один раз сохраните его в игре и повторите
  запуск программы.
- Общие команды меню и камер могут отсутствовать в профильной категории самолёта
  MSFS 2024. Такие команды оставляются без изменений и показываются только в
  подробностях.
- Первая проверка результата внутри MSFS 2024 выполняется пользователем после
  установки этой сборки.
- Публичной Authenticode-подписи пока нет, поэтому Windows может показать обычное
  предупреждение неизвестного издателя.

© 2026 Denis Ugarov
