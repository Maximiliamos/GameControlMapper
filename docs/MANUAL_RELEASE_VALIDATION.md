# Guided manual validation публичной beta

Проверка должна выполняться на неизменённых self-contained ZIP, созданных одним full commit. Report связывает product version, application/harness commit, SHA-256 обоих архивов и реальные monitor/DPI metadata.

## Подготовка

1. Распакуйте application и TouchTestHarness ZIP в разные каталоги.
2. Запустите harness и mapper; в mapper выберите окно harness как target.
3. Убедитесь, что в UI написано `Beta`, а capability matrix не объявляет непроверенные сценарии `Supported`.
4. Не используйте XInput, Macro/Sequence, ADB, pinch или rotation.
5. Запустите guided validation. Нельзя вручную поставить PASS для machine-evidence сценария без принятых counters/timestamps.

Harness записывает для каждого реального монитора безопасный ID, X/Y/Width/Height, DPI X/Y, scale, primary, harness monitor и target monitor. `NotAvailable` допускается только если соответствующего hardware действительно нет. Отрицательные координаты и mixed DPI не подменяются значениями по умолчанию.

## Обязательные сценарии

- Tap: один Down и один Up.
- Hold: Down, достаточная длительность/Move, один Up.
- DoubleTap: два полных lifecycle.
- Swipe: Down, упорядоченные Move, Up.
- WASD и MouseArea: независимые ID, одновременно с другими действиями.
- Camera: минимум две минуты движения; handoff без orphan Up и пустого кадра.
- Multitouch: измеренная одновременность и capacity; одиннадцатый контакт безопасно отклоняется.
- F9, Alt+Tab, move/resize/minimize/close target: final Up, zero active contacts, отсутствие поздних Move.
- Повторный Start: новая generation без событий предыдущей.
- Profiles: create/save/backup/corrupt/import/write failure.
- Diagnostics: session ID/stop reason сохранены, input history/profile JSON/user path отсутствуют.
- DPI 100/125/150%, несколько мониторов, negative origin и mixed DPI — если hardware присутствует.

## Критерий отчёта

Report получает `Failed`, если есть NotStarted/InProgress/Failed, protocol errors, active contacts at end, несогласованный verdict, неизвестные/дублированные/переименованные scenario IDs или отсутствующий machine evidence. При честно отсутствующем hardware допустим `PassedWithUnverifiedEnvironments`, но это не повышает 해당 capability до `Supported`.

Проверьте report скриптом:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/validate-manual-release.ps1 `
  -ReportPath <report.json> `
  -ApplicationArchive <GameControlMapper-beta.zip> `
  -HarnessArchive <TouchTestHarness-beta.zip> `
  -ExpectedVersion <version> `
  -ExpectedCommit <full-commit> `
  -CandidateManifest <manifest.json>
```

После ручной проверки production binaries менять нельзя. Финальный `1.0.0` должен быть отдельно пересобран из того же clean commit; переименование beta ZIP запрещено.

## Ограничение текущей задачи

Автоматический запуск harness не заменяет физическую touch-проверку человеком. Если Tap/Hold/WASD/Camera/MouseArea/F9/Alt+Tab не были реально выполнены, в отчёте следует явно писать `Not performed`, а remediation нельзя объявлять полностью принятой.
