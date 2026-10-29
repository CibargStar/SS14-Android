# Публикация тестового релиза

Релизы публикую в GitHub Releases, каталоги — в ветке `updates`. Движки подписываются постоянным Ed25519-ключом, APK — прежним сертификатом. Приватные ключи хранятся вне Git и Actions.

## 1. Зафиксировать исходники

Обновите version code/display version, патчи, CHANGELOG и результаты проверок. Проверьте патчи на чистом upstream и сохраните source SHA запусков CI. Для изменений ввода обязателен [OriginalClient.VisualCheck](testing.md) на окончательном подписанном пакете: `SS14_CHECK_INPUT=1`, `SS14_CHECK_CAMERA=1`, `SS14_CHECK_THUMBSTICK=1`, `SS14_CHECK_HUD=1`, `ROBUST_ANDROID_MOBILE_UI=true`. События должны проходить native SDL.

Запустите `engine-versions.yml` с CSV точных версий, а `android-client.yml` — для `android-arm64` и `android-x64`, `runtime=coreclr`, `engine_channel=published` и новым `version_code`.

Host и движки можно собирать параллельно; перед подписью APK замените его engine assets окончательными пакетами.

## 2. Проверить и подписать движки

Для каждого артефакта сохраните ID, SHA-256, source SHA и upstream tag. Commit тега сверьте с официальным RobustToolbox. Импортируйте последовательно в общий output:

```bash
python3 scripts/import-engine-artifact.py downloaded-artifact.zip \
  --artifact-sha256 GITHUB_ARTIFACT_SHA256 \
  --version VERSION --upstream UPSTREAM_COMMIT \
  --key /private/channel-key.pem --output artifacts/release-engines
```

Импорт проверяет хеши, временную CI-подпись и provenance, затем подписывает пакет постоянным ключом. Публичная часть должна совпадать с `updates/android-engine-public.pub`.

Опционально создайте дельту для известного официального ZIP:

```bash
python3 scripts/create-engine-patch.py --source official.zip \
  --target artifacts/release-engines/Robust.Client.Android-VERSION.zip \
  --version VERSION --output artifacts/release-engines/Robust.Client.Android-VERSION.ss14patch \
  --key /private/channel-key.pem
```

Проверьте дельту через `tests/EnginePatch.Checks`: она подходит только к указанному исходному ZIP.

## 3. Опубликовать движки

Подготовьте временную ветку `engine-staging`: подписанные ZIP/дельты и `.sig`, `payloads.json` с SHA-256 каждого файла, `notes.md` с источниками и проверками. Запустите `publish-signed-engines.yml` с **новым** неизменяемым тегом. Workflow сверяет хеши и подписи закреплённым публичным ключом.

После публикации сравните размеры и SHA-256 всех release assets с локальными файлами. Только затем сформируйте каталог:

```bash
python3 scripts/create-update-catalog.py --official official-manifest.json \
  --engines artifacts/release-engines \
  --release-base https://github.com/CibargStar/SS14-Android/releases/download/NEW_ENGINE_TAG \
  --output engines.json
```

Официальный manifest получают с `https://robust-builds.cdn.spacestation14.com/manifest.json`. Генератор отклоняет отозванные бинарники и сохраняет только официальные redirect к опубликованной разрешённой цели.

## 4. Подготовить APK

Сверьте SHA-256 скачанного CI-артефакта с GitHub. Замените только engine assets:

```bash
python3 scripts/rebundle-apk-engines.py --apk host-from-ci.apk \
  --engines artifacts/release-engines --public-key updates/android-engine-public.pub \
  --version 294.0.0 --version 294.0.1 --output unsigned.apk
zipalign -P 16 -f 4 unsigned.apk aligned.apk
apksigner sign --ks /private/channel.keystore --out signed.apk aligned.apk
apksigner verify --print-certs-pem signed.apk
```

Используйте прежний APK-сертификат: иначе Android не установит обновление поверх старого. SHA-256 DER-сертификата должен совпадать с `updates/apk-certificate-sha256.txt`. Сверьте package ID `io.ss14.android`, version code, ABI, CoreCLR, engine key, встроенные версии и оригинальную иконку. Все записи host вне engine assets должны совпадать с CI. `zipalign -P 16` не исправляет ELF-выравнивание зависимостей.

## 5. Опубликовать APK и каталог

Workflow `publish-test-host.yml` читает временную ветку `host-staging`. В ней находятся `notes.md`, APK-части до 40 МиБ и `apks.json`: массив объектов `{name, sha256, chunks: [{name, sha256}]}`. Workflow восстанавливает APK и проверяет хеш, сертификат и закреплённый ключ движка перед публикацией.

Если proxy ограничивает размер push, загрузите blob группами во временные orphan-ветки и соберите общий tree/commit через Git Data API.

Запустите workflow с новым `android-host-*` тегом. После сверки опубликованных digest/размеров обновите `engines.json` и `apk-android-{arm64,x64}.json` в ветке `updates`; прочитайте их обратно по raw HTTPS. APK descriptor содержит `applicationId`, `versionCode`, `url`, `sha256`, `minSdk`.

Удаляйте временные staging/upload-ветки только после завершения workflow и проверки релиза. Сохраняйте опубликованные бинарники неизменными: исправление выпускается под новым тегом.
