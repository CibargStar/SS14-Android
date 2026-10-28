> Историческая запись начального этапа. Основной Android host теперь портирует оригинальные SS14.Launcher и SS14.Loader. Самописный хаб ниже — предыдущий эксперимент 0.1.0-dev. Актуальные проверки: [current-validation.md](current-validation.md).

# Проверки

Дата: 28 октября 2026.

SDK 10.0.403 установлен в `/workspace/.dotnet`; Android workload 36.1.2 установлен. Java 21 предоставляется окружением.

`scripts/run-probe.sh`: пройдено, Linux x64 / .NET 10.0.14.

- DynamicMethod: прошёл.
- Expression.Compile: прошёл.
- Внешние DLL v1/v2 с одинаковой assembly identity в разных AssemblyLoadContext: прошли; IL генерируется и исполняется внутри обеих DLL.
- Изменённые байты DLL: отклонены до загрузки.
- Upstream Robust.NetSerializer `ae4c2543cda63173536aaff6c531f2d2e1e3bf26`: загружен извне, round-trip объекта с int, текстом и List<int> прошёл.

Android ARM64 Debug APK с Mono JIT: сборка прошла без предупреждений и ошибок. AOT, interpreter и trimming отключены. Это диагностическая конфигурация, а не финальный выбор runtime.

Android ARM64 Debug APK с CoreCLR: сборка прошла с ожидаемым XA1040 — backend экспериментальный и пока не рекомендован SDK для production. Это вариант для сравнения на устройствах.

Для обоих APK проверены подписи через apksigner и присутствие библиотек приложения внутри пакета: Mono использует отдельные DLL wrappers, CoreCLR — assembly blob. Они не требуют fast deployment с компьютера. Внешние fixtures передаются отдельно. Пакеты подписаны Debug-ключом, не production-ключом будущего приложения.

Отрицательная проверка CLI без каталога fixtures вернула код 1 и отчёт о провале; отсутствие внешних DLL не выдаётся за успешную проверку. SHA-256 готовых APK записаны в `artifacts/apk-manifest.json`. Архив для проверки устройства — `artifacts/ss14-runtime-device-test.zip`.

В окружении обнаружен только Java runtime без jar/javac. Для сборки отдельно установлен полный Temurin JDK 21 в `/workspace/jdk-21`. Android SDK установлен в `/workspace/android-sdk` из официальных Google-архивов с проверкой опубликованных SHA-1. Автоматический InstallAndroidDependencies не завершился из-за ошибки TLS-проверки его feed; сборка выполнена с явно указанными путями JDK/SDK и обычной проверкой TLS при скачивании.

`adb devices` не обнаружил устройств. APK не запускались. Linux-результат и успешная сборка не являются результатом исполнения на Android и не подтверждают совместимость с SS14. GitHub Actions workflow добавлен, но удалённо не запускался.
