# Архитектура Game Control Mapper

## Границы системы

Game Control Mapper — WPF-приложение `net8.0-windows` для Windows x64. Оно использует низкоуровневые keyboard/mouse hooks, внутренний источник относительных физических движений мыши и Windows Touch Injection. DLL в целевой процесс не внедряется, память игры не читается и не изменяется, сетевого протокола и драйвера нет.

## Основные модули

- `Models` — сериализуемые профили, bindings, camera/window/gamepad settings, touch frame models и capability matrix.
- `Services/InputMappingEngine` — lifecycle mapping, generation checks, suppression snapshot и маршрутизация действий.
- `Services/TargetWindow*` — foreground identity, client geometry, generation и fail-closed invalidation.
- `Services/WindowCoordinateTransformer` — чистое преобразование profile coordinates в физические screen pixels клиентской области.
- `Services/TouchEngine` — создание, движение и запрос освобождения контакта.
- `Services/TouchContactAllocator` — динамические lease ID в пределах backend capacity.
- `Services/ContactManager` — состояние контактов и формирование общего кадра.
- `Services/TouchScheduler` — отправка кадров, final Up и fatal-failure callback.
- `Services/WindowsTouchBackend` — единственный production backend, вызывающий `InitializeTouchInjection`/`InjectTouchInput`.
- `ViewModels` и `UI/Views` — MVVM UI, редактор поверх target и debug overlay.
- `GameControlMapper.TouchTestHarness` — отдельный принимающий touch стенд и guided validation report.

Удалённые legacy-классы SendInput, `WindowsTouchSimulator`, фиксированные ID, старый `CoordinateScaler` и XInput mapper не компилируются в production assembly и не зарегистрированы DI.

## Путь события

```text
physical keyboard/button event
  → KeyboardHookService / MouseHookService
  → GeneratedInputEvent(target generation)
  → InputMappingEngine
  → RuntimeInputPolicy + current InputPermissionSnapshot
  → profile binding + WindowCoordinateTransformer
  → TouchEngine.Start/Move/EndTouch
  → TouchContactAllocator lease + ContactManager
  → TouchScheduler shared frame
  → WindowsTouchBackend.SendFrame
  → InjectTouchInput
```

Suppression публикуется immutable snapshot и разрешается только активному, поддерживаемому binding текущего foreground generation. Macro, Sequence, XInput, unsupported input mode, invalid hotkey и inactive binding физический ввод не подавляют.

Одноразовые keyboard actions принимают только initial key press. Auto-repeat отбрасывается до `KeyUp`; уже выполняющееся действие не образует неограниченную очередь. MouseArea и WASD держат по одному lease на binding.

## Target и координаты

`TargetWindowSessionManager` нормализует HWND к `GA_ROOT`, сохраняет PID, profile size, физический `PhysicalClientRect`, scale mode и monotonic generation. Старт разрешён только foreground root с тем же PID.

`GameWindowGeometryProvider` получает размер через `GetClientRect`, а origin через `ClientToScreen`. Процесс PerMonitorV2-aware, поэтому эти координаты уже считаются физическими pixels и дополнительно на WPF DPI не умножаются. Production использует `Stretch`; математическое ядро также тестирует `UniformFit`. Profile bounds half-open: `0 <= x < width`, `0 <= y < height`.

WinEvent geometry monitor и fallback timer обнаруживают move, resize, minimize, destroy, PID/geometry failure. Поздние callbacks проверяют generation. Ошибка callback перехватывается и не выходит в process-wide unhandled exception.

## Контакты и shutdown

Каждый владелец получает `TouchContactLease` с ID, session generation, owner ID и sequence. ID возвращается в pool только после подтверждённого backend `Up`. Неуспешный `Up` переводит ID в quarantine до backend reset.

Остановка едина для F9, focus loss, geometry invalidation, scheduler failure, crash attempt и application shutdown:

1. атомарно публикуется denied input permission;
2. новые контакты запрещаются;
3. camera capture останавливается;
4. active bindings запрашивают release;
5. scheduler делает bounded final frame;
6. результат и причина сохраняются в `MappingSessionDiagnostics`.

Параллельные причины присоединяются к одному `_stopTask`. Fatal scheduler failure вызывается вне scheduler lock и не перезапускает worker автоматически.

## Камера

Камера использует относительные `dx/dy`, не позицию курсора. `Ctrl` только вооружает режим. После первой значимой дельты создаётся направленная проводка внутри safe client-area ellipse. Пакеты накапливаются до touch frame; sensitivity, inversion, dead zone, acceleration, smoothing и max speed применяются в production `CameraMouseLookService`.

При достижении границы выполняется handoff: старый `Up` и новый `Down` входят в один frame, накопленная дельта сохраняется. Это даёт неограниченный логический поворот без движения системного курсора.

Перед camera session считываются position, clip и visibility для ownership record. Реализация не изменяет position/clip и восстанавливает только изменённую ей visibility. Restore idempotent, generation-safe и bounded; `ShowCursor` не крутится в бесконечном цикле.

## Профили и UI

`ProfileStore` валидирует модель до записи, пишет временный JSON, проверяет его и атомарно заменяет primary. Предыдущая валидная версия сохраняется как `.json.bak`. `camera: null`, `window: null`, `gamepad: null` и `bindings: null` дают структурированную `ProfileValidationException`.

`AsyncRelayCommand` наблюдает exceptions, восстанавливает CanExecute и сообщает error handler. Инициализация `MainViewModel` имеет safe wrapper и fallback profile. `TouchDebugViewModel` снимает immutable allocator snapshot и coalesces updates через `IUiDispatcher`; после Dispose callbacks игнорируются.

## Логи и diagnostics

Production provider принимает Information и выше, но privacy policy отбрасывает шаблоны истории ввода. Diagnostic export применяет фильтр повторно, редактирует user-home path и не копирует profile JSON. Разрешены session lifecycle, target state, backend/profile/hook errors и итог release.

## Capability status

`ApplicationCapabilities.Beta` различает `Supported`, `AutomatedOnly`, `Experimental`, `Unsupported` и `Unavailable`. До принятого manual report реализованные touch/DPI/monitor сценарии остаются `AutomatedOnly`; Tanks Blitz — `Experimental`. Capability matrix описывает проверенный статус, а не планируемые функции.
