# Каталог обновлений SS14 Android

Исходники и документация: [main](https://github.com/CibargStar/SS14-Android/tree/main). Готовые APK и движки: [Releases](https://github.com/CibargStar/SS14-Android/releases).

| Файл | Назначение |
| --- | --- |
| engines.json | Точные Android-движки и официальные repair redirect |
| apk-android-arm64.json | Обновление APK для ARM64-телефонов |
| apk-android-x64.json | Обновление APK для x86-64 |

Файлы добавляются после проверки опубликованных release assets. Движки имеют SHA-256 и Ed25519-подпись; публичный ключ закреплён в APK и main/updates/android-engine-public.pub. Обновление APK дополнительно проверяет package ID, version code и прежний сертификат. Приватных ключей в этой ветке нет.

Цель — любые серверы; текущий каталог содержит подготовленные версии, а не обещание совместимости каждого форка. [Ограничения](https://github.com/CibargStar/SS14-Android/blob/main/docs/compatibility.md) · [Процесс публикации](https://github.com/CibargStar/SS14-Android/blob/main/docs/releasing.md).
