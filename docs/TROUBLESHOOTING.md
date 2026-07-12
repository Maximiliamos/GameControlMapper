# Troubleshooting

- Touch does not work: confirm Windows 10/11 x64, target-window focus and review the application log for the Win32 error code. Do not repeatedly enable mapping after initialization failure.
- Focus loss: mapping stops intentionally and does not restart automatically. Return focus and start it manually.
- Cursor appears clipped or hidden: stop mapping with F9 and close the application normally. If another application retained capture, use Ctrl+Alt+Delete and return to the desktop.
- Profile recovery: keep the primary JSON untouched and restore the adjacent `.bak` through the documented backup workflow.
- Diagnostic ZIP: use **Экспорт диагностики** and attach it without adding profiles or other personal files.
- Anti-cheat/security software may block injection or flag the process. The project does not bypass protection and provides no compatibility guarantee.
