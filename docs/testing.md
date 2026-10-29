# Тестирование

Команды выполняются из корня после подготовки исходников по [инструкции сборки](building.md).

## Автоматические проверки

```bash
dotnet run --project tests/OriginalLauncher.Tests -p:AndroidLauncher=true -c Debug
dotnet run --project tests/AndroidDisplay.Checks -c Release
dotnet run --project tests/Thumbstick.Checks -c Release
dotnet run --project tests/CameraGesture.Checks -c Release
dotnet run --project tests/AndroidInput.Checks -c Release
python3 -m unittest discover -s tests -p 'test_*.py'
```

| Набор | Что проверяет |
| --- | --- |
| OriginalLauncher.Tests | Настоящие App/ViewModel/SQLite, Android-адаптер окон, отмена/вложенные диалоги, ссылки и RID, подписи и установка движков |
| AndroidDisplay.Checks | Расчёт масштаба, размеры экранов и общее хранилище launcher/game |
| Thumbstick.Checks | Один владелец, мёртвая зона, устойчивость направлений, отпускание, движение относительно камеры |
| CameraGesture.Checks | Щипок/поворот, пороги, UI/world arbitration, отмена и обычные нажатия/перетаскивание |
| AndroidInput.Checks | Соответствие Android pointer ID и SDL finger ID, несколько пальцев, отпускание и отмена |
| test_update_catalog.py | Официальные redirect, отозванные цели, циклы и формирование безопасного каталога |

Для готовой бинарной дельты:

```bash
dotnet run --project tests/EnginePatch.Checks -p:AndroidLauncher=true --   official.zip prepared.ss14patch android.zip 294.0.0
```

Проверяются точные итоговые байты, неподходящая версия/источник, повреждения, лимиты и отмена. Это тест подготовленного патча, не автоматического портирования неизвестной версии.

## Настоящий клиент на Linux offscreen

`tests/OriginalClient.VisualCheck` загружает подписанный Android-движок через исходный MobileLoader, проверяет оригинальные Content.Client/Shared/Shared.Database, создаёт настоящий UI и сохраняет снимки/геометрию. Нужны отдельно собранный оригинальный `SS14.Client.zip`, Linux native-библиотеки SDL3/OpenAL/FreeType и Mesa EGL/GLES.

```bash
export SDL_VIDEODRIVER=offscreen EGL_PLATFORM=surfaceless ALSOFT_DRIVERS=null
export SS14_CHECK_INPUT=1 SS14_CHECK_CAMERA=1 SS14_CHECK_THUMBSTICK=1
dotnet run --project tests/OriginalClient.VisualCheck -p:AndroidLauncher=true --   engine.zip SIGNATURE_HEX public.pem SS14.Client.zip native-dir artifacts/visual
```

События отправляются через native `SDL_PushEvent` и проходят штатный SDL event watch → Clyde → GameController → UI/InputManager. Проверяются стик (down/motion/up), настоящая кнопка Options, масштаб проекции, вращение, обратное преобразование выбора, сохранение серверного Eye/FOV, отсутствие случайного клика и обычный tap. Тестовая карта/eye создаются только внутри fixture. Прямой вызов UI.TouchFinger не подходит: он скрывает отсутствие подписки на события, которое вызвало регрессию 0.6.0.

`tests/OriginalClient.Smoke` служит для загрузки модулей и сетевого handshake. `scripts/stage-game-smoke.py` переносит fixture в rooted userdebug-эмулятор; обычному телефону этот способ не нужен и не подходит. Не открывайте приватный игровой Activity наружу ради теста.

В 0.7.0 native-стенд дополнительно проверяет возврат стика в центр, потерянный SDL_UP, отпускание владельца при другом удерживаемом пальце и повторное использование pointer ID. Панель Debug открывается настоящим SDL-нажатием и должна получить измеренные FPS/CPU/память. Launcher-стенд отображает исходный авторизованный хаб с 180 локальными fixture-строками и проверяет вписывание окна и навигации в четыре размера, включая логические размеры S25 Ultra; реальный аккаунт и сеть не используются.

## Runtime probe

`bash scripts/run-probe.sh` проверяет DynamicMethod, Expression.Compile, внешние DLL разных версий и upstream NetSerializer. Android-вариант — `src/RuntimeProbe.Android`; после его установки используйте `scripts/stage-android-fixtures.sh`. Это отдельный диагностический APK, он не заменяет клиент и не проверяет сетевой протокол целиком.

## Проверка на устройстве

Чек-лист и данные для отчёта: [install.md](install.md). В результатах всегда указывайте среду: pure .NET, Linux с настоящим клиентом, Android-эмулятор или физическое устройство. Результаты предыдущего APK не подтверждают новый бинарник.
