# Game Control Mapper — публичная beta

Это unsigned self-contained beta для Windows 10/11 x64, а не финальный 1.0 и не installer. Отдельный .NET Runtime не требуется.

Полностью распакуйте архив и запустите `GameControlMapper.exe`. Не включайте mapping, пока не выбрано правильное foreground окно. `F8` запускает mapping, `F9` останавливает и инициирует release всех контактов.

Реализованы touch bindings, target client coordinates, multitouch allocator, camera handoff, profiles/backups и privacy-filtered diagnostics. Их статус остаётся `AutomatedOnly` до принятого ручного report. Конкретные игры, включая Tanks Blitz, — `Experimental`. XInput, Macro/Sequence, Android/ADB, Interception, ViGEm, pinch и rotation не поддерживаются. XVM/Olenemer отсутствует в beta UI.

Логи находятся в `%LocalAppData%\GameControlMapper\Logs`; профили — в `Profiles` рядом с EXE. Diagnostic ZIP не включает profile JSON и повторно фильтрует историю ввода. Просмотрите ZIP перед отправкой.

Для безопасной проверки используйте отдельный архив `GameControlMapper-TouchTestHarness-<version>-win-x64.zip` и [MANUAL_RELEASE_VALIDATION.md](MANUAL_RELEASE_VALIDATION.md). Совместимость с играми, античитом и endpoint security не гарантируется; обход защиты не реализован.
