# Changelog

## 1.0.0-beta.3

- Исправлены подтверждённые замечания независимого ревью: изоляция поколений касаний, terminal-fail state планировщика, доказательная база ручной валидации, конкурентное сохранение профилей и фильтрация координат из production-логов.

## Unreleased — audit remediation

- production-логи и диагностический ZIP очищены от истории ввода, binding names, joystick coordinates, camera deltas и scheduler frames;
- неподдерживаемые профили и действия больше не попадают в suppression snapshot;
- keyboard auto-repeat не создаёт очередь повторных Tap/Hold/DoubleTap/Swipe;
- fatal scheduler failure переводит mapping в fail-closed stop с bounded final Up;
- расширена null-safe валидация профилей, hotkeys, camera/window/gamepad и Windows filenames;
- camera cursor ownership восстанавливает исходную видимость idempotent и generation-safe;
- async UI failures наблюдаются, а TouchDebug обновляется через dispatcher с coalescing;
- harness сохраняет реальные monitor bounds, DPI и evidence; незавершённый report получает Failed;
- release manifest получает фактические TRX counters, версии, commit, RID и SHA-256 бинарников/архивов;
- удалены production legacy пути SendInput, старый touch simulator, фиксированные contacts, CoordinateScaler и XInput mapper;
- Blitz XVM/Olenemer удалён из публичного beta UI и production assembly;
- слабые source-text тесты заменены поведенческими и script integration checks;
- Windows CI проверяет Release build, TRX, self-contained ZIP, manifest negative cases и static safety.

## 1.0.0-beta.2

Планируемая публичная beta после независимого review и ограниченной ручной проверки. Это не финальный релиз и не заявление о совместимости с конкретной игрой.
