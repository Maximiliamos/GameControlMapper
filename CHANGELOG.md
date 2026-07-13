# Changelog

## Unreleased

- Исправлено удаление профиля: команда обновляет доступность, запрашивает подтверждение и удаляет recovery-артефакты.
- Ctrl переключает боевой режим; ЛКМ «Огонь» перехватывается только при активной камере и освобождается при выходе из режима.
- Камера накапливает Raw Input между сенсорными кадрами, использует безопасный расширенный радиус и продолжает движение сразу после смены контакта.
- Переработан главный интерфейс: повышены контраст и размеры текста, упрощена иерархия команд, добавлен индикатор режима мыши, журнал сделан сворачиваемым.
- Добавлено поэтапное техническое задание и критерии приёмки камеры.

## 1.0.0-rc.1

- Windows 10/11 x64 keyboard and mouse to Windows Touch Injection.
- Target-window coordinates, Tap, DoubleTap, Hold, horizontal Swipe, WASD joystick, camera and MouseArea.
- Focus-safe shutdown, profiles with backup/validation, diagnostics and TouchTestHarness.
- XInput, Macro/Sequence, Android/ADB, RawInput, Interception, ViGEm, pinch and rotation are not supported.
