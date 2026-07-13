# Известные ограничения публичной beta

- Только Windows 10/11 x64 и self-contained RID `win-x64`.
- Реальный Windows Touch Injection, mixed-DPI, multi-monitor и negative-origin пока имеют статус `AutomatedOnly`, а не `Supported`.
- Tanks Blitz и любые конкретные игры/эмуляторы имеют статус `Experimental`; обновление UI игры может потребовать перенастройки координат.
- Swipe реализован только горизонтально. Pinch и rotation отсутствуют.
- XInput, Macro/Sequence runtime, ADB, Interception и ViGEm отсутствуют.
- Внутренний источник относительного движения мыши не является стабильным публичным API.
- Потеря focus или изменение target geometry останавливает mapping; автоматического restart нет.
- Camera handoff ограничен возможностями Touch Injection и конкретной игрой; визуальную плавность должен подтвердить ручной двухминутный тест.
- EXE не имеет process mutex. BAT предотвращает обычный повторный запуск только проверкой имени процесса.
- Нет installer, digital signature, auto-update и механизма обхода защитного ПО.
- Crash handling для AppDomain fatal exception является только bounded best effort.
