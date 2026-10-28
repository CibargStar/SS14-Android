# Android platform patches

Патчи применяются к официальным исходникам, закреплённым submodule. Приватный upstream fork для сборки не требуется.

| Патч | База | Подготовка |
| --- | --- | --- |
| `launcher-android-platform.patch` | SS14.Launcher `18fa9830576023595bffb417b0eaeb929fee5934` | `bash scripts/prepare-original-launcher.sh` |
| `robust-android-platform.patch` | RobustToolbox `160be31f57129c24c1715b873b92f14d028de8b8` | `bash scripts/prepare-engine.sh` |
| `engine-versions/*.patch` | Семейства точных upstream-версий | `python3 scripts/prepare-engine-version.py VERSION` |

Патч лаунчера добавляет Avalonia single-view lifetime, Android-адаптер окон/диалогов, storage, intents и обновления. Оригинальные XAML, ViewModel и сервисы используются непосредственно из submodule.

Патч движка добавляет SDL/GLES, переносимость шейдеров, touch, мобильное управление, масштабирование и жесты камеры. Загрузчик контента, API/сеть, IL-verifier и sandbox сохраняются. Android-снижения lighting/FOV из исходной мобильной адаптации исключены. JNI/Activity и native packaging находятся в `src/SS14.Android` и `scripts/build-native-*.sh`.

Номер файла version-family patch означает его базу, а не гарантированную поддержку всех соседних версий. Патч 264.0.15 остаётся рабочим материалом; соответствующий движок сейчас не опубликован. Список выпущенных версий — [совместимость](../docs/compatibility.md).

## Источники и лицензии

Начальная мобильная адаптация: [SolomonNumb1/space-station-14-mobile](https://github.com/SolomonNumb1/space-station-14-mobile/tree/87fe4c76c638b9fee3be9008f06cb9b94c213e62), `patches/mobile.patch`, MIT. Первоначальная Android/native работа — SolomonNumb1; multitouch и scrolling — Ixee24. Лицензия сохранена в [mobile-MIT-LICENSE](mobile-MIT-LICENSE).

Лицензия upstream-лаунчера сохранена в [launcher-upstream-LICENSE.txt](launcher-upstream-LICENSE.txt). RobustToolbox, игровые ресурсы, NetSerializer и native-зависимости сохраняют собственные лицензии в исходных деревьях; native build scripts копируют их рядом с библиотеками. Иконка: AftrLite, CC-BY-SA-4.0, [атрибуция](../src/SS14.Android/Assets/ss14-icon-attribution.txt).

## Обновление патчей

Изменяйте подготовленный submodule, включайте новые файлы через `git add -N`, экспортируйте diff относительно официального HEAD и проверьте применение к чистому дереву. Контекст соседних необязательных API сужайте только после проверки нескольких точных версий. Не включайте build output, локальные ключи и случайные изменения. Полный процесс — [CONTRIBUTING](../CONTRIBUTING.md).
