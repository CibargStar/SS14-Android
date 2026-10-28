#!/usr/bin/env python3
"""Add opt-in LAN test settings to an original Hybrid ACZ server package."""
import pathlib
import sys
import zipfile

config = """[net]
bindto = "0.0.0.0"
port = 1212
upnp = false
max_connections = 8

[status]
bind = "*:1212"

[auth]
mode = 2

[hub]
advertise = false

[game]
hostname = "SS14 Android LAN test"
defaultpreset = "sandbox"
lobbyenabled = true
lobbyduration = 600
role_timers = false

[build]
fork_id = "ss14-android-local-test"
version = "5758de66-engine294.0.1"

[console]
loginlocal = true
"""

instructions = """SS14 Android: локальный тестовый сервер

Игра: 5758de66ded8251d09087b3cda0cd0bf9a854aa3
Оригинальный движок: 294.0.1 (160be31f57129c24c1715b873b92f14d028de8b8).
Это отдельный сервер для ПК, а не файлы для копирования в телефон.

1. Установите .NET 10 Runtime для своей системы: https://dotnet.microsoft.com/download/dotnet/10.0
2. Полностью распакуйте архив в отдельную папку.
3. Откройте терминал в этой папке и выполните:
   dotnet Robust.Server.dll --config-file local-test.toml
4. Оставьте окно открытым. Дождитесь окончания загрузки сервера.
5. Подключите телефон и компьютер к одной локальной сети/Wi-Fi.
6. Узнайте IPv4 компьютера (Windows: ipconfig, Linux: ip -4 addr).
7. Откройте на телефоне http://IP_КОМПЬЮТЕРА:1212/status — должен появиться JSON.
8. В оригинальном лаунчере на телефоне добавьте сервер:
   ss14://IP_КОМПЬЮТЕРА:1212
   Например: ss14://192.168.1.50:1212
9. Подключитесь. Лаунчер должен скачать контент с этого сервера автоматически.
   Content.Client.zip оставьте рядом с Robust.Server.dll.
10. Для начала раунда в консоли сервера выполните: startround

Разрешите входящие TCP 1212 (статус и скачивание контента) и UDP 1212 (игра)
в брандмауэре компьютера для частной сети. Гостевой Wi-Fi с изоляцией клиентов
не подходит. localhost/127.0.0.1 на телефоне обозначает сам телефон.

local-test.toml отключает проверку аккаунтов только на этом тестовом сервере,
не публикует его в хабе и не включает UPnP. Не пробрасывайте его в Интернет.
Сам лаунчер может по-прежнему запросить вход в SS14-аккаунт.

Сборка Android ранняя: успешная загрузка /status не подтверждает игровой вход.
Если клиент завершится или зависнет, отправьте журналы через настройки лаунчера.
"""

archive = pathlib.Path(sys.argv[1])
with zipfile.ZipFile(archive, "a", compression=zipfile.ZIP_DEFLATED) as package:
    package.writestr("local-test.toml", config)
    package.writestr("README-LOCAL-TEST.txt", instructions)
