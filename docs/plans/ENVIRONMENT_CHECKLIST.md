# Чек-лист окружения

Проверено Claude 2026-10-18 через доступ к файлам ПК (наличие папок; версии программ — только где видно по папкам).

## Игра и серверы

| Компонент | Статус | Комментарий |
|---|---|---|
| Bannerlord 1.4.8 (клиент) | ✅ | `bin/Win64_Shipping_Client/Version.xml` = v1.4.8 |
| Modding Tools 1.4.8 | ✅ | `bin/Win64_Shipping_wEditor` = v1.4.8 |
| Mount & Blade II Dedicated Server | ✅ установлен | версию сверить с клиентом (1.4.8) в Phase 0 |
| Логи игры | ✅ папка есть | `C:\ProgramData\Mount and Blade II Bannerlord\logs` |
| War Sails DLC | ✅ не установлен | хорошо — меньше несовместимостей |
| Bannerlord.Harmony (Workshop) | ❓ | в `Modules` игры нет; подписаться в Workshop, проверить в лаунчере |

## Инструменты разработки

| Компонент | Статус | Что сделать |
|---|---|---|
| .NET SDK | ⚠ только 8.0.426 | **Установить .NET 10 SDK** (нужен для ilspycmd; 8 оставить) |
| .NET Framework 4.7.2 Developer Pack | ✅ | `Reference Assemblies\...\v4.7.2` есть |
| ilspycmd | ❌ | после .NET 10 SDK: `dotnet tool uninstall -g ilspycmd` → `dotnet tool install -g ilspycmd` |
| ILSpy (GUI) | ❓ | рекомендую поставить (удобно смотреть типы руками) |
| IDE | ⚠ только VS Code | **Рекомендую Visual Studio 2022 Community** (нагрузка «.NET desktop development») — нужна для отладки с подключением к `Bannerlord.exe`. Альтернатива: VS Code + C# Dev Kit (сборка да, отладка .NET Framework — нет) |
| Git | ✅ | `C:\Program Files\Git` |
| Python | ✅ | есть (для вспомогательных скриптов) |
| Node.js | ❌ | нужен плагинам Claude (gitkraken, pdf) — поставить Node.js LTS, если нужны плагины |
| GitHub CLI (`gh`) | ❌ | желательно: `winget install GitHub.cli`, затем `gh auth login` |

## Claude и интеграции

| Компонент | Статус | Что сделать |
|---|---|---|
| Claude GitHub App на `Torivald/BannelordCOOP` | ✅ | push из облачной сессии работает |
| Claude Code (Windows) | ⚠ установлен, не в PATH | добавить `C:\Users\Torivald\.local\bin` в PATH пользователя, перезапустить терминал |
| Локальная папка как Git-репо | ❌ | см. «Шаги» ниже |
| Навыки bannerlord-research / coop-architecture / mvp-check | ✅ | сохранены |
| Память проекта + документы проекта claude.ai | ✅ | |
| MCP-серверы | не нужны | 0 внешних MCP — осознанно (ADR-0001) |

## Для тестов с 4 игроками (понадобится с Phase 4–5)

| Что | Зачем |
|---|---|
| 2–4 ПК или друзья-тестеры с **своей копией игры** | Steam-приглашения требуют разных Steam-аккаунтов, каждый владеет игрой |
| Режим прямого подключения по IP (LAN/localhost) | разработка без Steam; проверить, можно ли запустить 2+ экземпляра игры на одном ПК |
| Hamachi/WireGuard (уже есть) | тесты через интернет без проброса портов |
