# Тестирование

Команды выполняются из корня после [подготовки исходников](building.md). [Результаты текущего релиза](current-validation.md).

## Автоматические проверки

```bash
dotnet run --project tests/OriginalLauncher.Tests -p:AndroidLauncher=true -c Debug
dotnet run --project tests/AndroidDisplay.Checks -c Release
dotnet run --project tests/Thumbstick.Checks -c Release
dotnet run --project tests/CameraGesture.Checks -c Release
dotnet run --project tests/AndroidInput.Checks -c Release
python3 -m unittest discover -s tests -p 'test_*.py'
```

| Набор | Покрытие |
| --- | --- |
| OriginalLauncher.Tests | App/ViewModel/SQLite, Android-окна, диалоги, ссылки/RID, подписи и установка движков; headless-хаб с 180 локальными строками и пятью размерами окна, быстрым избранным без раскрытия строки, исходными иконками и безопасной областью системных панелей |
| AndroidDisplay.Checks | Масштаб, размеры экранов, общие настройки launcher/game |
| Thumbstick.Checks | Владелец, мёртвая зона, направления, отпускание, учёт камеры |
| CameraGesture.Checks | Щипок/поворот, пороги, выбор между UI и миром, отмена, нажатия/перетаскивание |
| AndroidInput.Checks | Соответствие pointer/finger ID, несколько пальцев, отпускание и отмена |
| test_update_catalog.py | Официальные redirect, отозванные цели, циклы и каталог |

Проверка подготовленной дельты — точные байты, версия/источник, повреждения, лимиты и отмена:

```bash
dotnet run --project tests/EnginePatch.Checks -p:AndroidLauncher=true -- \
  official.zip prepared.ss14patch android.zip 294.0.0
```

## Исходный клиент на Linux offscreen

`OriginalClient.VisualCheck` загружает подписанный Android-движок через MobileLoader и исходные Content.Client/Shared/Shared.Database. Нужны соответствующий версии `SS14.Client.zip`, Linux-библиотеки SDL3/OpenAL/FreeType и Mesa EGL/GLES.

```bash
export SDL_VIDEODRIVER=offscreen EGL_PLATFORM=surfaceless ALSOFT_DRIVERS=null
export SS14_CHECK_INPUT=1 SS14_CHECK_CAMERA=1 SS14_CHECK_THUMBSTICK=1
export SS14_CHECK_HUD=1 SS14_CHECK_LOBBY=1 ROBUST_ANDROID_MOBILE_UI=true
dotnet run --project tests/OriginalClient.VisualCheck -p:AndroidLauncher=true -- \
  engine.zip SIGNATURE_HEX public.pem SS14.Client.zip native-dir artifacts/visual
```

| Область | Проверки |
| --- | --- |
| Ввод | Native `SDL_PushEvent → Clyde → GameController → UI/InputManager`, стик down/motion/up, потерянный SDL_UP, второй палец, повторный pointer ID, исходная кнопка Options |
| Камера | Проекция, вращение, выбор предмета, сохранение Eye/FOV, обычный tap без случайного клика при жесте |
| HUD | Исходный DefaultGameScreen, три режима чата, привязка к углу, иконки, действие на down без повторения на up/подсказки, свайп без клика, submit, черновик, widget lookup/ancestor handler и восстановление UI |
| Лобби и руководство | Исходные панели, границы окна/кнопки закрытия, узкая компоновка, оглавление, touch-прокрутка, фиксированный чат снизу, полный футер, штатное переключение лобби, полноэкранный редактор и исходный профиль, выбор разделов через native popup, ограниченные по ширине поля/цвета, сохранение сущности предпросмотра и вращение, вкладки персонажей, восстановление порядка и родителей при отключении, отсутствие сброса компоновки гайда при обновлении без изменений |
| Late Join | Исходные роли и ограничения, границы окна/закрытия, выбор отдела касанием, число мест, повторная сборка списка сервером |
| Debug | Открытие SDL-нажатием, измерение FPS/CPU/памяти, остановка опроса после закрытия |

Карта и eye создаются в fixture. Прямой вызов `UI.TouchFinger` не заменяет native-цепочку: он пропускает ошибки подписки на события. HUD-submit проверяется без сетевой отправки; IME, сообщения и popup дополнительно проверяются на телефоне.

## Android и runtime

- `tests/OriginalClient.Smoke` проверяет загрузку модулей и handshake. `scripts/stage-game-smoke.py` переносит fixture в rooted userdebug-эмулятор. Игровой Activity остаётся приватным; обычному телефону этот способ не требуется.
- `bash scripts/run-probe.sh` проверяет DynamicMethod, Expression.Compile, внешние DLL разных версий и upstream NetSerializer. Android-вариант — отдельный APK `src/RuntimeProbe.Android` и `scripts/stage-android-fixtures.sh`.
- [Чек-лист на устройстве](install.md). В отчёте указывайте среду и точную версию: pure .NET, Linux, эмулятор или телефон. Проверка предыдущего APK не подтверждает новый.
