# Space Station 14 · Android

<img src="src/SS14.Android/Resources/drawable-nodpi/ss14_logo.png" width="80" alt="Space Station 14">

[![Android build](https://github.com/CibargStar/SS14-Android/actions/workflows/android-client.yml/badge.svg)](https://github.com/CibargStar/SS14-Android/actions/workflows/android-client.yml)
[![Engine builds](https://github.com/CibargStar/SS14-Android/actions/workflows/engine-versions.yml/badge.svg)](https://github.com/CibargStar/SS14-Android/actions/workflows/engine-versions.yml)

Я разрабатываю неофициальный Android-порт Space Station 14 на базе **оригинальных SS14.Launcher и RobustToolbox**. Платформенные изменения веду в [патчах](patches/README.md) к закреплённым upstream-исходникам.

**Экспериментальная альфа.** Цель — подключение к любым серверам. Сейчас опубликованы 17 версий движка; поддержка всех форков и полный игровой раунд ещё не проверены. [Совместимость](docs/compatibility.md) · [Результаты проверок](docs/current-validation.md).

## Скачать

[**0.11.0-dev · APK и описание релиза**](https://github.com/CibargStar/SS14-Android/releases/tag/android-host-0.11.0-dev) · [Установка и управление](docs/install.md) · [Изменения](CHANGELOG.md)

Для ARM64-устройств выбирайте `arm64-coreclr.apk`; для x86-64 — `x64-coreclr.apk`. Движок и контент сервера загружаются приложением. Отдельные игровые файлы и локальный сервер не нужны.

## Возможности

- Оригинальный лаунчер: официальный хаб, аккаунты, избранное и прямое подключение.
- Игровой клиент в отдельном процессе; проверка подписей и sandbox сохранены.
- Загрузка подготовленной версии движка и обновление APK из приложения.
- Стик, жесты масштабирования и поворота камеры, полноэкранный режим.
- Отключаемая [адаптация чата, меню, лобби, редактора персонажа, Late Join и руководства](docs/mobile-ui.md).
- Настройка масштаба, скрытие стика и панель FPS/CPU/памяти через кнопку **UI**.

## Документация

| Задача | Документ |
| --- | --- |
| Установить, проверить на телефоне, собрать логи | [Установка](docs/install.md) |
| Собрать из исходников | [Сборка](docs/building.md) |
| Разобраться в устройстве порта | [Архитектура](docs/architecture.md) |
| Запустить проверки | [Тестирование](docs/testing.md) |
| Выпустить подписанный релиз | [Публикация](docs/releasing.md) |
| Внести изменения | [CONTRIBUTING](CONTRIBUTING.md) |
| Посмотреть следующие задачи | [ROADMAP](ROADMAP.md) |

## Лицензии

Код интеграции — [MIT](LICENSE). Upstream, ресурсы и native-библиотеки сохраняют свои лицензии. [Источники адаптаций и авторы](patches/README.md). Иконка SS14: AftrLite, CC-BY-SA-4.0; [атрибуция](src/SS14.Android/Assets/ss14-icon-attribution.txt).
