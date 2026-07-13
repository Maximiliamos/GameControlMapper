# Матрица поддержки публичной beta

Статусы:

- `Supported` — принят автоматический и требуемый ручной evidence;
- `AutomatedOnly` — production path и поведенческие тесты есть, ручной gate не принят;
- `Experimental` — функция или конкретная среда не гарантируется;
- `Unsupported` — формат может существовать для совместимости, runtime отсутствует;
- `Unavailable` — компонент не реализован.

| Область | Статус | Основание |
|---|---|---|
| Windows Touch Injection | AutomatedOnly | backend и lifecycle тестируются; manual report не принят |
| Keyboard/mouse → touch | AutomatedOnly | production integration tests без глобального системного ввода |
| Target client coordinates | AutomatedOnly | тесты origin/aspect/geometry invalidation |
| Multitouch до backend capacity | AutomatedOnly | allocator/frame/race tests |
| Camera mouse-look | AutomatedOnly | production soak и frame protocol tests; визуальный gate остаётся |
| Mixed DPI | AutomatedOnly | provider/transform tests; нужен реальный стенд |
| Несколько мониторов | AutomatedOnly | provider/negative geometry tests; нужен реальный стенд |
| Negative origin | AutomatedOnly | behavioral coordinate tests; нужен реальный стенд |
| Diagnostics/privacy | AutomatedOnly | реальные file/ZIP/privacy tests |
| Profile backup/recovery | AutomatedOnly | atomic storage и validation tests |
| Tanks Blitz | Experimental | начальный профиль не является гарантией совместимости |
| Blitz XVM/Olenemer | Experimental, вне beta UI | production integration удалена |
| XInput | Unsupported | mapper и P/Invoke удалены из production |
| Macro/Sequence | Unsupported | suppression/runtime запрещены |
| Pinch/rotation | Unsupported | жесты отсутствуют |
| Public Raw Input contract | Unavailable | внутренний источник не обещается как API |
| Interception/ViGEm/ADB | Unavailable | не реализованы |

Ни одна строка не повышается до `Supported` только по числу тестов. Принятый guided report должен относиться к тем же version, full commit и SHA-256 архивов.
