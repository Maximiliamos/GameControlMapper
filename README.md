# Game Control Mapper

Game Control Mapper — экспериментальное Windows-приложение, которое преобразует клавиатуру и кнопки мыши в независимые сенсорные контакты Windows Touch Injection. Основной сценарий — управление сенсорной игрой, запущенной в окне Windows или Android-эмулятора.

> Статус: **публичная beta**. Автоматические тесты пройдены, но полный ручной отчёт на реальном touch-стенде, mixed-DPI и нескольких мониторах ещё не принят. Совместимость с конкретной игрой, эмулятором или защитным ПО не гарантируется.

## Что реализовано

- Tap, DoubleTap, Hold и горизонтальный Swipe;
- экранный джойстик `WASD`;
- MouseArea для ЛКМ, ПКМ, Mouse4 и Mouse5;
- одновременные независимые контакты в пределах лимита Windows backend;
- камера по относительному физическому движению мыши;
- запись назначаемой клавиши кнопкой **Rec**;
- профили JSON с атомарным сохранением и соседней резервной копией `.bak`;
- координаты клиентской области выбранного окна, включая отрицательный origin;
- fail-closed остановка при потере фокуса, изменении геометрии или отказе scheduler;
- локальные privacy-фильтрованные логи и диагностический ZIP без JSON-профилей.

`Ctrl` переключает боевой режим камеры. Само переключение не создаёт касания. Первый camera `Down` появляется только после реального движения мыши. Для длинного поворота камера выполняет handoff контакта внутри безопасного эллипса клиентской области: кадр стыка содержит старый `Up` и новый `Down`, без пустого промежуточного кадра. Системный курсор не используется для координат `WASD` и кнопок; камера сохраняет его состояние, скрывает на время своей сессии и восстанавливает только принадлежавшее ей изменение видимости.

## Что не поддерживается

- XInput и управление геймпадом;
- Macro и Sequence runtime;
- ADB/прямая отправка событий на Android;
- Interception и ViGEm;
- pinch и rotation;
- гарантированная поддержка Tanks Blitz, MuMu или античитов.

Интеграция Blitz XVM/Olenemer удалена из beta UI и production-сборки. Поля старых профилей сохраняются только для совместимости и не означают наличие runtime.

Полный фактический статус: [docs/SUPPORT_MATRIX.md](docs/SUPPORT_MATRIX.md).

## Требования

- Windows 10 или Windows 11 x64;
- выбранное целевое окно должно принимать Windows Touch Injection;
- mapper и игра должны иметь совместимый уровень привилегий;
- для сборки из исходников — .NET SDK из [global.json](global.json);
- для готового self-contained ZIP отдельный .NET Runtime не требуется.

Приложение не внедряет DLL, не изменяет память игры и не обходит защиту. Глобальные hooks и Touch Injection могут блокироваться политиками безопасности.

## Запуск self-contained beta ZIP

1. Полностью распакуйте `GameControlMapper-<version>-win-x64.zip`.
2. Запустите `GameControlMapper.exe`.
3. Запустите игру, обновите список окон и выберите её целевое окно.
4. Проверьте профиль в редакторе поверх игры и сохраните его.
5. Нажмите `F8` для запуска mapping и `F9` для гарантированной остановки.

Не запускайте EXE прямо из ZIP. Готовый архив не требует SDK.

## Запуск из репозитория

```powershell
dotnet restore GameControlMapper.sln -r win-x64
dotnet build GameControlMapper.sln -c Release --no-restore
dotnet test GameControlMapper.sln -c Release --no-build
dotnet run --project src/GameControlMapper/GameControlMapper.csproj -c Release
```

Файл `Запустить Game Control Mapper.bat` предназначен только для рабочей копии: он ищет Release EXE, при необходимости вызывает `dotnet build`, затем запускает приложение. BAT проверяет уже работающий процесс по имени. В самом EXE межпроцессного mutex нет, поэтому прямой повторный запуск EXE пока не является строго single-instance.

## Управление

| Клавиша | Действие |
|---|---|
| `F8` | запустить mapping после проверки target |
| `F9` | остановить mapping и освободить контакты |
| `F10` | показать/скрыть debug overlay |
| `F11` | открыть редактор поверх игры |
| `Ctrl` | переключить камеру и свободную мышь |

При потере фокуса mapping останавливается и автоматически не возобновляется. Возвратите фокус и снова нажмите `F8`.

## Сборка проверяемой beta

Скрипт выполняет clean, restore, Release build, все тесты, self-contained publish `win-x64`, формирует ZIP, `manifest.json` и SHA-256, после чего повторно проверяет содержимое и версии бинарников:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1 `
  -Version 1.0.0-beta.3
```

Счётчик тестов берётся из TRX и записывается в manifest; он не захардкожен в документации или скриптах. Финальный `1.0.0` запрещён без принятого guided validation report и отдельной пересборки тем же commit.

## Приватность и данные

Диагностический ZIP можно создать кнопкой «Диагностика» или без запуска mapping командой `GameControlMapper.exe --export-diagnostics <путь-к-zip>`.

- профили и `.bak`: каталог `Profiles` рядом с EXE;
- логи и crash fallback: `%LocalAppData%\GameControlMapper\Logs`;
- телеметрии и сетевой отправки нет;
- production logger отбрасывает историю клавиш, кнопок, координаты joystick, camera deltas и номера кадров;
- диагностический экспорт повторно фильтрует журналы, редактирует путь профиля пользователя и не включает JSON-профили.

Privacy-фильтры покрыты поведенческими тестами, но любой архив всё равно рекомендуется просмотреть перед передачей третьим лицам. Подробнее: [docs/PRIVACY.md](docs/PRIVACY.md).

## Архитектура

```text
KeyboardHook / MouseHook / относительные пакеты физической мыши
                         ↓
                 InputMappingEngine
                         ↓
TargetWindowSessionManager + WindowCoordinateTransformer
                         ↓
TouchEngine + TouchContactAllocator + ContactManager
                         ↓
                  TouchScheduler
                         ↓
               WindowsTouchBackend
                         ↓
                InjectTouchInput
```

Production DI не содержит старые SendInput/touch simulator/XInput пути. Подробнее: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) и [docs/CAMERA_CONTROL_SPEC.md](docs/CAMERA_CONTROL_SPEC.md).

## Проверка и ограничения

Windows CI собирает Release с warnings-as-errors, публикует TRX, исполняет release scripts и проверяет отсутствие legacy production references. Число автоматических тестов смотрите в актуальном CI или generated manifest конкретного архива.

Реальные Touch Injection, конкретные игры, mixed-DPI, несколько мониторов и отрицательный origin остаются `AutomatedOnly`/`Experimental`, пока соответствующий ручной report не принят. Инструкция: [docs/MANUAL_RELEASE_VALIDATION.md](docs/MANUAL_RELEASE_VALIDATION.md). Известные ограничения: [docs/KNOWN_LIMITATIONS.md](docs/KNOWN_LIMITATIONS.md).

## Лицензия

В репозитории пока нет файла лицензии. До её выбора действуют стандартные авторские права владельца проекта.
