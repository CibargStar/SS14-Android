# Публикация тестового релиза

Релиз состоит из исходников, подписанных движков, подписанного APK и каталога обновлений. APK и native-библиотеки не хранятся в main. Приватные ключи не передаются в Git или Actions.

## 1. Зафиксировать исходники

Обновите version code/display version, патчи, CHANGELOG и результаты проверок. Проверьте чистое применение патчей. Зафиксируйте source SHA каждого запуска CI. Сборка без ошибок не заменяет тест настоящего клиента. Перед публикацией изменений ввода обязателен `OriginalClient.VisualCheck` с `SS14_CHECK_INPUT=1`, `SS14_CHECK_CAMERA=1` и `SS14_CHECK_THUMBSTICK=1` на окончательном подписанном пакете: события должны пройти native SDL, без прямого вызова UI-обработчиков.

Запустите `engine-versions.yml` с CSV точных версий, а `android-client.yml` — для `android-arm64` и `android-x64`, `runtime=coreclr`, `engine_channel=published` и новым `version_code`.

Host и движки можно собирать параллельно: перед подписью APK его engine assets заменяются окончательными пакетами. Не выпускайте новый host со старыми движками случайно.

## 2. Проверить и подписать движки

Для каждого артефакта Actions сохраните ID, SHA-256, source SHA и точный upstream tag. Получите commit тега независимо из официального RobustToolbox. Импорт выполняйте последовательно в общий output:

```bash
python3 scripts/import-engine-artifact.py downloaded-artifact.zip \
  --artifact-sha256 GITHUB_ARTIFACT_SHA256 \
  --version VERSION --upstream UPSTREAM_COMMIT \
  --key /private/channel-key.pem --output artifacts/release-engines
```

Скрипт проверяет хеш артефакта, временную CI-подпись, хеш пакета и provenance, затем подписывает пакет постоянным ключом. Его публичная часть должна совпадать с `updates/android-engine-public.pub`.

Опционально создайте дельту для известного официального ZIP:

```bash
python3 scripts/create-engine-patch.py --source official.zip \
  --target artifacts/release-engines/Robust.Client.Android-VERSION.zip \
  --version VERSION --output artifacts/release-engines/Robust.Client.Android-VERSION.ss14patch \
  --key /private/channel-key.pem
```

Проверьте дельту через `tests/EnginePatch.Checks`. Нельзя объявлять дельту совместимой с другим исходным ZIP.

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

Для большого staging через ограничивающий proxy загружайте небольшие группы blob во временные orphan-ветки; затем создавайте общий tree/commit через Git Data API из уже загруженных blob. Это транспортный обход, байты APK не меняются.

Запустите workflow с новым `android-host-*` тегом. После сверки опубликованных digest/размеров обновите `engines.json` и `apk-android-{arm64,x64}.json` в ветке `updates`; прочитайте их обратно по raw HTTPS. APK descriptor содержит `applicationId`, `versionCode`, `url`, `sha256`, `minSdk`.

Удаляйте временные staging/upload-ветки только после завершения workflow и проверки релиза. Сохраняйте опубликованные бинарники неизменными: исправление выпускается под новым тегом.
