# Space Station 14 · Android

<img src="src/SS14.Android/Resources/drawable-nodpi/ss14_logo.png" width="80" alt="Space Station 14">

[![Android build](https://github.com/CibargStar/SS14-Android/actions/workflows/android-client.yml/badge.svg)](https://github.com/CibargStar/SS14-Android/actions/workflows/android-client.yml)
[![Engine builds](https://github.com/CibargStar/SS14-Android/actions/workflows/engine-versions.yml/badge.svg)](https://github.com/CibargStar/SS14-Android/actions/workflows/engine-versions.yml)

Неофициальный Android-порт **оригинальных SS14.Launcher и RobustToolbox**. Исходные экраны лаунчера, хаб, авторизация, загрузчик и игровой клиент используются из закреплённых upstream-репозиториев. Android-изменения хранятся в проверяемых [патчах](patches/README.md).

**Статус: экспериментальная альфа.** Пользователь подтвердил вход из хаба в игру на Samsung S25 Ultra. Полный раунд, все форки и все устройства пока не проверены. Цель — подключение к любым серверам; сейчас опубликованы Android-сборки 16 точных версий движка. [Совместимость и ограничения](docs/compatibility.md).

## Скачать

[**Тестовые APK в GitHub Releases**](https://github.com/CibargStar/SS14-Android/releases?q=android-host&expanded=true) · [Установка и управление](docs/install.md) · [Изменения](CHANGELOG.md)

Для Samsung S25 Ultra и других ARM64-телефонов выбирайте `arm64-coreclr.apk`. Вариант `x64` предназначен для соответствующих устройств и эмуляторов. Контент выбранного сервера скачивается приложением; отдельные игровые файлы вручную не нужны.

## Возможности

- Оригинальный лаунчер: официальный хаб, избранное, прямое подключение и вход в аккаунт.
- Оригинальный игровой клиент в отдельном процессе, с проверкой подписей и sandbox.
- Скачивание нужной подготовленной версии движка и проверяемые обновления APK.
- Стик ходьбы, сенсорные действия, настройка масштаба интерфейса и скрытие управления.
- Жесты камеры двумя пальцами на свободном игровом поле; полноэкранный игровой Activity.

## Документация

| Задача | Документ |
| --- | --- |
| Установить APK, проверить управление, собрать логи | [Инструкция](docs/install.md) |
| Собрать из исходников | [Сборка](docs/building.md) |
| Понять устройство порта | [Архитектура](docs/architecture.md) |
| Запустить проверки | [Тестирование](docs/testing.md) |
| Посмотреть подтверждённые результаты | [Валидация](docs/current-validation.md) |
| Подготовить подписанный релиз | [Публикация](docs/releasing.md) |
| Помочь проекту | [CONTRIBUTING](CONTRIBUTING.md) |
| Следующие задачи | [ROADMAP](ROADMAP.md) |

## Лицензии и авторы

Код интеграции — [MIT](LICENSE). Upstream, ресурсы и native-библиотеки сохраняют свои лицензии. Источники мобильных адаптаций и авторы указаны в [patches/README.md](patches/README.md). Иконка SS14: AftrLite, CC-BY-SA-4.0; [атрибуция](src/SS14.Android/Assets/ss14-icon-attribution.txt).
