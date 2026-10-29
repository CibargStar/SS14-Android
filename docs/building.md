# Сборка из исходников

Собираю порт на Linux; CI использует GitHub Actions Ubuntu. Версии инструментов и команды: [.github/workflows/android-client.yml](../.github/workflows/android-client.yml).

## Инструменты

- .NET SDK из [global.json](../global.json): 10.0.403; Android workload 36.1.2.
- JDK 21, Android SDK platform/build-tools 36.
- Android NDK r27c, **27.2.12479018**. Не выбирайте автоматически самый новый NDK.
- Git, Python 3, OpenSSL, CMake, Ninja, make, curl, unzip и autotools для libsodium.
- Около 30 ГБ для исходников, native-сборок, NuGet и APK.

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

Скрипты используют закреплённые native-исходники и патч RobustToolbox. Повторный запуск распознаёт применённый патч; посторонние изменения submodule сохраняются.

Для локальной сборки создайте собственный Ed25519-ключ в новом файле. Приватный ключ в Git не добавляется.

```bash
mkdir -p .keys
chmod 700 .keys
test ! -e .keys/local-engine.pem && \
  openssl genpkey -algorithm ED25519 -out .keys/local-engine.pem
chmod 600 .keys/local-engine.pem
python3 scripts/package-android-engine.py --key .keys/local-engine.pem --version 294.0.1
```

Пакет, подпись, публичный ключ и bundled-manifest появятся в `artifacts/engines`. Этот ключ используется только вашей сборкой; ключ релизного канала описан в [публикации](releasing.md).

## APK

```bash
bash scripts/build-original-launcher.sh \
  -p:AndroidSdkDirectory="$ANDROID_HOME" \
  -p:JavaSdkDirectory="$JAVA_HOME" \
  -p:RuntimeIdentifier=android-arm64 -p:UseMonoRuntime=false
```

Результат: `src/SS14.Android/bin/Debug/net10.0-android/android-arm64/*-Signed.apk`. Скрипт передаёт `AndroidLauncher=true` для restore и сборки зависимых проектов.

Релизные APK используют экспериментальный CoreCLR .NET 10; `UseMonoRuntime=true` включает диагностический Mono. AOT, interpreter и trimming отключены для загрузки серверных DLL и генерации кода во время выполнения.

## Другая точная версия движка

```bash
python3 scripts/prepare-engine-version.py 294.0.0
dotnet build engine/versions/294.0.0/Robust.Client/Robust.Client.csproj \
  -c Release -p:FullRelease=True -p:TargetOS=Android \
  -m:1 -p:BuildInParallel=false
python3 scripts/package-android-engine.py \
  --engine engine/versions/294.0.0 --version 294.0.0 \
  --key .keys/local-engine.pem
```

Подготовка использует точный upstream tag и проверенный патч. Если подходящего патча нет, версию нужно портировать. Маркер подготовленного дерева обнаруживает изменение патча.
