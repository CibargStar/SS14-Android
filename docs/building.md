# Сборка из исходников

Проверяемая среда — Linux и GitHub Actions Ubuntu. Версии инструментов и команды CI: [.github/workflows/android-client.yml](../.github/workflows/android-client.yml).

## Инструменты

- .NET SDK из [global.json](../global.json): 10.0.403; Android workload 36.1.2.
- JDK 21, Android SDK platform/build-tools 36.
- Android NDK r27c, **27.2.12479018**. Не выбирайте автоматически самый новый NDK.
- Git, Python 3, OpenSSL, CMake, Ninja, make, curl, unzip и autotools для libsodium.
- Место для submodule, native-сборок, NuGet и APK; рабочая среда проекта использует около 30 ГБ.

```bash
git clone --recurse-submodules https://github.com/CibargStar/SS14-Android.git
cd SS14-Android
dotnet workload install android --skip-manifest-update
export ANDROID_HOME=/absolute/path/android-sdk
export JAVA_HOME=/absolute/path/jdk-21
export ANDROID_NDK_HOME="$ANDROID_HOME/ndk/27.2.12479018"
export SS14_ANDROID_ABI=arm64-v8a
```

SDK platform, build-tools и NDK должны быть установлены через `sdkmanager`. Для x64 используйте `SS14_ANDROID_ABI=x86_64` и `RuntimeIdentifier=android-x64` ниже.

## Native-библиотеки и движок

```bash
bash scripts/prepare-original-launcher.sh
bash scripts/build-native-sdl.sh
bash scripts/build-native-libraries.sh
bash scripts/build-native-sodium.sh
bash scripts/build-engine.sh
```

Скрипты закрепляют исходники native-зависимостей и применяют патч к закреплённому RobustToolbox. Повторный запуск распознаёт уже применённый патч; посторонние изменения submodule не сбрасываются автоматически.

Для локальной development-сборки создайте **собственный** ключ в новом файле. Не заменяйте существующий ключ и не добавляйте приватные ключи в Git.

```bash
mkdir -p .keys
chmod 700 .keys
test ! -e .keys/local-engine.pem && \
  openssl genpkey -algorithm ED25519 -out .keys/local-engine.pem
chmod 600 .keys/local-engine.pem
python3 scripts/package-android-engine.py   --key .keys/local-engine.pem --version 294.0.1
```

Пакет, подпись, публичный ключ и bundled-manifest появляются в `artifacts/engines`. Такой ключ принадлежит только вашей сборке. Для официального тестового канала нужен отдельный процесс [публикации](releasing.md).

## APK

```bash
bash scripts/build-original-launcher.sh   -p:AndroidSdkDirectory="$ANDROID_HOME"   -p:JavaSdkDirectory="$JAVA_HOME"   -p:RuntimeIdentifier=android-arm64   -p:UseMonoRuntime=false
```

Результат: `src/SS14.Android/bin/Debug/net10.0-android/android-arm64/*-Signed.apk`. Скрипт передаёт глобальное `AndroidLauncher=true`, одинаковое для restore и compilation зависимых проектов.

Публикуемые тестовые APK используют экспериментальный CoreCLR .NET 10. Mono остаётся диагностическим вариантом (`UseMonoRuntime=true`). AOT, interpreter и trimming отключены: клиент загружает DLL выбранного сервера и генерирует код во время выполнения.

## Другая точная версия движка

```bash
python3 scripts/prepare-engine-version.py 294.0.0
dotnet build engine/versions/294.0.0/Robust.Client/Robust.Client.csproj   -c Release -p:FullRelease=True -p:TargetOS=Android -m:1 -p:BuildInParallel=false
python3 scripts/package-android-engine.py   --engine engine/versions/294.0.0 --version 294.0.0   --key .keys/local-engine.pem
```

Скрипт получает именно upstream tag этой версии и выбирает применимый проверенный патч. Отсутствие подходящего патча — причина для ручного портирования, а не подмены версии. Подготовленное дерево защищено маркером от незамеченного изменения патча.
