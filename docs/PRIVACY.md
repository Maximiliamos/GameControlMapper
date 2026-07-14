# Приватность

Game Control Mapper не содержит телеметрии и не отправляет данные по сети. Профили, backups, логи, crash fallback и diagnostic export остаются локальными.

Production-файл не должен сохранять virtual-key/scan codes, KeyDown/KeyUp, MouseDown/MouseUp, hotkey и binding names из input path, pressed sequences, joystick coordinates, camera deltas или scheduler frame numbers. На Information сохраняются только session lifecycle, target generation/state, profile operations, backend initialization, diagnostic export и итог stop/release. Ошибки backend, hooks, geometry, profiles, cursor restore и protocol violations пишутся без истории ввода.

Diagnostic ZIP применяет второй privacy filter, даже если upstream message был сформирован ошибочно. Он не включает JSON-профили, process/window lists или содержимое пользовательских файлов и заменяет user-home path на `%USERPROFILE%`.

Фильтры проверяются реальными временными логами, ZIP export и нагрузкой из множества joystick updates. Это снижает риск регрессии, но перед передачей архива третьим лицам всё равно просмотрите его вручную.

Blitz XVM/Olenemer не читается beta-сборкой. DLL, player lists и файлы из `%LocalAppData%\Olenemer` не загружаются.

Удаление данных: остановите mapping, закройте приложение, удалите распакованный каталог и при необходимости `%LocalAppData%\GameControlMapper\Logs` и созданные вручную diagnostic ZIP.
