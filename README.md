# SS14 Android — порт оригинального клиента и лаунчера

Цель: полноценный обновляемый Android-порт существующих SS14 и SS14.Launcher, с сенсорным вводом, официальным хабом и прямым подключением. Переписывание похожего приложения с нуля не входит в задачу.

**Текущая база — оригинальный код.** `upstream/SS14.Launcher` закреплён на `18fa9830576023595bffb417b0eaeb929fee5934` официального репозитория. `src/SS14.Android` — Android Activity для запуска оригинального Avalonia App. XAML содержимого главного окна, экранов входа, вкладок, фильтров, новостей и настроек используется непосредственно из upstream. Изменения для Android оформлены в `patches/launcher-android-platform.patch`: single-view lifetime, адаптер четырёх окон/диалогов, приватное хранилище, ссылки/ss14 intents, SQLite и native зависимости. Бизнес-логика авторизации, хаба, контента и обновлений остаётся исходной; наличие кода ещё не подтверждает работу всех сценариев на Android.

Прежний самописный хаб был ошибочным направлением. Он перенесён в `experiments/custom-hub` и **не входит в основной APK**. Опубликованный ранее на Диске 0.1.0-dev содержит именно тот эксперимент. Актуальная тестовая ветка — 0.5.0-dev на базе оригинального launcher 0.40.4; файлы находятся в GitHub Releases.

**Полный игровой порт ещё не завершён.** Оригинальный клиент запущен на Android через исходный SS14.Loader и отдельный SDL-процесс, с проверкой подписей и сохранённым sandbox. В эмуляторе подтверждены главное меню и сетевой handshake; пользователь сообщил о входе в серверное меню через хаб на S25 Ultra. В 0.4.0 добавлены настройки масштаба игры и скрытия управления, исправлены DPI/пиксельные координаты, используется оригинальная иконка игры. Полный игровой цикл, остальные версии движков и native-модули остаются обязательствами.

## Сборка

Нужны .NET SDK из `global.json`, Android workload, JDK 21, Android SDK, NDK r27c (`27.2.12479018`), CMake, Ninja, make и curl. В CI версия NDK закреплена: выбор самого нового установленного NDK меняет C++-заголовки сторонних библиотек и не воспроизводит проверенную сборку.

```bash
git submodule update --init --recursive
bash scripts/prepare-original-launcher.sh
# В среде с настроенными PATH и ANDROID_NDK_HOME:
bash scripts/build-native-libraries.sh
bash scripts/build-native-sodium.sh
bash scripts/build-native-sdl.sh
bash scripts/build-engine.sh
# Создать собственный локальный ключ для development-сборки. Не публиковать его.
mkdir -m 700 .keys
openssl genpkey -algorithm ED25519 -out .keys/android-engine-development.pem
python3 scripts/package-android-engine.py \
  --key .keys/android-engine-development.pem --version 294.0.1
bash scripts/build-original-launcher.sh \
  -p:AndroidSdkDirectory=/absolute/path/android-sdk \
  -p:JavaSdkDirectory=/absolute/path/jdk-21
```

`AndroidLauncher=true` должен быть глобальным MSBuild-параметром, чтобы restore и compilation выбирали одинаковый мобильный вариант shared-проекта. Build script передаёт его. Это сохраняет сборку исходного desktop-варианта при `AndroidLauncher=false`.

Проверки настоящего лаунчера без сети и реальных аккаунтов:

```bash
dotnet run --project tests/OriginalLauncher.Tests -p:AndroidLauncher=true -c Debug
```

Тесты инициализируют исходные App/ViewModels/SQLite и проверяют адаптацию окон, возврат результатов, отмену и вложенные диалоги, хранилище, внешние ссылки и выбор Android RID. Рендер оригинального экрана сохраняется в `artifacts/original-launcher-login.png`. Это desktop headless/Skia-стенд с мобильным адаптером, а не результат исполнения на Android. Живой хаб, авторизация реального аккаунта, игровые серверы и устройство им не проверяются.

Установщик движков проверяется на неверном SHA-256, неверной Ed25519-подписи, очистке незавершённых файлов и циклическом redirect. Android-загрузчик всегда проверяет подпись, включая Debug. В исходном DataManager исправлена гонка: Close мог завершиться до начала запланированной записи SQLite.

`src/SS14.Android/Assets/update-sources.json` задаёт HTTPS-каталоги движков и APK в ветке `updates`. В release APK встроены 294.0.0 и 294.0.1; онлайн доступны 16 точных версий и 40 официальных repair redirect. Все имеют исправления масштаба и стика; это ещё не проверка игры на всех серверах. APK устанавливается штатным установщиком Android после проверки ID, версии, SHA-256 и совпадения сертификата. Ключи хранятся вне Git; Actions получает только подписанные файлы и проверяет их перед публикацией. Сборка CI с `engine_channel=published` закрепляет постоянный публичный ключ, обычные временные CI-сборки используют отдельные ephemeral-ключи.

В настройках оригинального лаунчера находится раздел «Экран и управление в игре». Эти параметры также доступны через кнопку UI в игре. Автоматический масштаб учитывает размер framebuffer, ручной меняет только интерфейс. Сенсорные кнопки имеют независимый размер; их видимость и масштаб окон сохраняются в приватном общем хранилище launcher/game. Проверены 67 сценариев размеров/настроек, 51 сценарий лаунчера и native отрисовка исходного клиента для FHD+/QHD+ S25 Ultra на Linux offscreen-стенде; это не тест на физическом Samsung.

Иконка APK — неизменённый `Resources/Textures/Logo/icon/icon-256x256.png` из SS14, автор AftrLite, CC-BY-SA-4.0. Ссылка на источник и лицензию включены в `Assets/ss14-icon-attribution.txt`; лицензия рисунка сохраняется отдельно от кода интеграции.

В 0.5.0 улучшен только стик ходьбы: владение одним пальцем, устойчивые восемь направлений, мёртвая зона с разными порогами начала/остановки, ограниченный ход ручки, освобождение движения при потере фокуса и захвата мыши. Прочая раскладка и UI остаются как в 0.4.0. Проверены 32 сценария состояния, 8 интеграционных сценариев через исходный InputManager и 53 сценария лаунчера. Матрица точных движков собрана в run 40729989908; планы и оставшиеся зависимости — в [docs/development-plan.md](docs/development-plan.md).

Для внутренней проверки оригинального клиента в rooted userdebug-эмуляторе служит `scripts/stage-game-smoke.py --content /path/to/SS14.Client.zip`. Контент собирается отдельно штатным Content.Packaging; он не вшит в APK. Скрипт обращается к приватному игровому Activity от root, поэтому не требует публичного тестового входа в приложение и не работает на обычном пользовательском телефоне.

RobustToolbox также используется как официальный submodule с отдельным Android platform patch: `bash scripts/build-engine.sh`. Нативные SDL, OpenAL, FreeType, Zstd и libsodium собраны под ARM64. Исходные lighting/FOV-настройки, модульная загрузка и sandbox сохранены. Диагностика runtime остаётся отдельным вспомогательным проектом и не заменяет порт.

Известное ограничение Android 16/устройств с 16-КБ страницами: штатный SkiaSharp 2.88.9 из Avalonia 11.3 имеет native библиотеку с 4-КБ ELF-выравниванием. Сборка сообщает XA0141; готовность для таких устройств не заявляется. Остальные неподтверждённые возможности и остаточные задачи — в `docs/original-launcher-port.md`.

## Вспомогательный runtime-стенд

Стенд проверяет:

- выполнение `DynamicMethod` и `Expression.Compile()`;
- загрузку DLL из файлов вне APK;
- выполнение генерации IL внутри загруженной DLL;
- независимую работу двух разных DLL с одинаковым именем и assembly version;
- отклонение изменённых байтов до загрузки DLL.
- сериализацию и десериализацию через настоящую upstream-библиотеку Robust.NetSerializer, также загружаемую извне APK.

Две DLL собираются отдельно от Android-приложения, не входят в его ProjectReference и передаются через adb. Их SHA-256 — контроль целостности тестовых файлов, **не** доверенная подпись и **не** готовый протокол обновлений. Для production понадобится проверяемый подписанный каталог.

SDK: .NET 10.0.403, Android workload 36.1.2; Java 21. В `global.json` закреплена версия SDK. Начальный ARM64-стенд использует Mono JIT без AOT, интерпретатора и trimming. SDK также содержит экспериментальный CoreCLR backend; его нужно сравнить на устройствах, прежде чем выбирать runtime продукта.

Перед сборкой: `git submodule update --init --recursive`. NetSerializer закреплён на `ae4c2543cda63173536aaff6c531f2d2e1e3bf26`, той же ревизии, что используется скачанным официальным SS14. Это сторонний код под MPL-2.0: [исходники](https://github.com/space-wizards/netserializer/tree/ae4c2543cda63173536aaff6c531f2d2e1e3bf26), [лицензия](https://www.mozilla.org/MPL/2.0/).

Проверка на компьютере:

```bash
bash scripts/run-probe.sh
```

Успех на компьютере не подтверждает работу на Android. JSON содержит фактический runtime и RID.

Сборка Android Debug APK:

```bash
dotnet workload install android --skip-manifest-update
dotnet build src/RuntimeProbe.Android -t:InstallAndroidDependencies \
  -p:AndroidSdkDirectory=/absolute/path/android-sdk \
  -p:JavaSdkDirectory=/absolute/path/jdk-21 \
  -p:AcceptAndroidSDKLicenses=true
dotnet build src/RuntimeProbe.Android -c Debug -t:SignAndroidPackage \
  -p:AndroidSdkDirectory=/absolute/path/android-sdk \
  -p:JavaSdkDirectory=/absolute/path/jdk-21
```

После установки APK на ARM64-устройство:

```bash
adb install -r src/RuntimeProbe.Android/bin/Debug/net10.0-android/android-arm64/io.ss14.runtimeprobe-Signed.apk
bash scripts/stage-android-fixtures.sh
adb exec-out run-as io.ss14.runtimeprobe cat files/runtime-probe-result.json
```

Для сравнения CoreCLR собрать APK с `-p:UseMonoRuntime=false`; сохранить Mono-отчёт перед заменой APK и повторить передачу fixtures и запуск. Поддержка этого backend экспериментальная; результат сборки и запуска фиксируется отдельно.

Скрипт ждёт результат до 30 секунд, сохраняет `artifacts/android-probe-result.json` и возвращает ошибку при провале проверки. Перед запуском он удаляет старый отчёт.

Каждый запуск оставляет загруженные assembly contexts до завершения процесса. Это намеренно короткоживущий диагностический процесс. Стенд проверяет один NetSerializer round-trip; он не проверяет полный wire format SS14, выгрузку движка, SDL/OpenGL, P/Invoke, Robust sandbox, сетевой handshake или игровой контент.

Требования и этапы: [docs/architecture.md](docs/architecture.md). Актуальные проверенные результаты: [docs/current-validation.md](docs/current-validation.md).
