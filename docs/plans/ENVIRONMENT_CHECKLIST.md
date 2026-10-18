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
| Bannerlord.Harmony (Workshop) | ✅ v2.4.2.248 | + ButterLib v2.12.0, UIExtenderEx v3.0.3 |
| Diplomacy (Workshop) | ⚠ v1.5.4 | **отключать в лаунчере на время разработки/тестов** — меняет кампанию |

## Инструменты разработки

| Компонент | Статус | Что сделать |
|---|---|---|
| .NET SDK | ✅ 8.0.426 | достаточно (ilspycmd 9.1 работает) |
| .NET Framework 4.7.2 Developer Pack | ✅ | `Reference Assemblies\...\v4.7.2` есть |
| ilspycmd | ✅ 9.1.0.7988 | установлен на .NET 8 SDK |
| ILSpy (GUI) | ❓ | рекомендую поставить (удобно смотреть типы руками) |
| IDE | ⚠ только VS Code | **Это не расширение VS Code**: «.NET desktop development» — компонент установщика *Visual Studio 2022* (отдельная программа, visualstudio.microsoft.com). |
| (старое) | **Рекомендую Visual Studio 2022 Community** (нагрузка «.NET desktop development») — нужна для отладки с подключением к `Bannerlord.exe`. Альтернатива: VS Code + C# Dev Kit (сборка да, отладка .NET Framework — нет) |
| Git | ✅ | `C:\Program Files\Git` |
| Python | ✅ | есть (для вспомогательных скриптов) |
| Node.js | ❌ | нужен плагинам Claude (gitkraken, pdf) — поставить Node.js LTS, если нужны плагины |
| GitHub CLI (`gh`) | ❌ | желательно: `winget install GitHub.cli`, затем `gh auth login` |

## Claude и интеграции

| Компонент | Статус | Что сделать |
|---|---|---|
| Claude GitHub App на `Torivald/BannelordCOOP` | ✅ | push из облачной сессии работает |
| Claude Code (Windows) | ✅ v2.1.302 | запускается из `E:\Claude Bannerlord` |
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
