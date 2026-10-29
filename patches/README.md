# Платформенные патчи Android

Android-изменения веду как патчи к официальным исходникам, закреплённым submodule.

| Патч | База | Подготовка |
| --- | --- | --- |
| `launcher-android-platform.patch` | SS14.Launcher `18fa9830576023595bffb417b0eaeb929fee5934` | `bash scripts/prepare-original-launcher.sh` |
| `robust-android-platform.patch` | RobustToolbox `160be31f57129c24c1715b873b92f14d028de8b8` | `bash scripts/prepare-engine.sh` |
| `engine-versions/286.0.2.patch` | Точный Robust `v286.0.2`, API атласа/сериализации этой ветки | `python3 scripts/prepare-engine-version.py 286.0.2` |
| `engine-versions/*.patch` | Семейства точных upstream-версий | `python3 scripts/prepare-engine-version.py VERSION` |

Лаунчер: Avalonia single-view lifetime, Android-окна/диалоги, storage, intents и обновления поверх исходных XAML, ViewModel и сервисов.

Движок: SDL/GLES, шейдеры, touch, масштаб и жесты камеры. Сохранены загрузчик, API/сеть, IL-verifier, sandbox, lighting и FOV. JNI/Activity и упаковка native-библиотек — в `src/SS14.Android` и `scripts/build-native-*.sh`.

Номер version-family patch обозначает базу; каждую целевую версию нужно проверять отдельно. Патч 264.0.15 — рабочий материал, движок не опубликован. [Выпущенные версии](../docs/compatibility.md).

## Источники и лицензии

Начальная мобильная адаптация: [SolomonNumb1/space-station-14-mobile](https://github.com/SolomonNumb1/space-station-14-mobile/tree/87fe4c76c638b9fee3be9008f06cb9b94c213e62), `patches/mobile.patch`, MIT. Первоначальная Android/native работа — SolomonNumb1; multitouch и scrolling — Ixee24. Лицензия сохранена в [mobile-MIT-LICENSE](mobile-MIT-LICENSE).

Лицензия upstream-лаунчера сохранена в [launcher-upstream-LICENSE.txt](launcher-upstream-LICENSE.txt). RobustToolbox, игровые ресурсы, NetSerializer и native-зависимости сохраняют собственные лицензии в исходных деревьях; native build scripts копируют их рядом с библиотеками. Иконка: AftrLite, CC-BY-SA-4.0, [атрибуция](../src/SS14.Android/Assets/ss14-icon-attribution.txt).

## Обновление патчей

Измените подготовленный submodule, включите новые файлы через `git add -N`, экспортируйте diff относительно официального HEAD и проверьте на чистом дереве. Сужение контекста необязательных API проверяйте на нескольких точных версиях. [Правила изменений](../CONTRIBUTING.md).
