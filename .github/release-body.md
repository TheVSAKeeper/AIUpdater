## Скачать

| Редакция                    | ZIP¹                                                                                                                          | Portable²                                                                                                                                        |
|-----------------------------|-------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------|
| **AIUpdater** (Windows x64) | [![ZIP](https://img.shields.io/badge/ZIP-1f6feb)](__REPO__/releases/download/v__VERSION__/AIUpdater-v__VERSION__-win-x64.zip) | [![Portable](https://img.shields.io/badge/Portable-8957e5)](__REPO__/releases/download/v__VERSION__/AIUpdater-v__VERSION__-win-x64-portable.zip) |

¹ Нужен [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
² Self-contained – рантайм не нужен, архив крупнее.

Распакуй архив целиком и запусти `AIUpdater.exe`: три PowerShell-скрипта должны оставаться рядом.
Журналы и кэш списков изменений хранятся в `%LOCALAPPDATA%\AIUpdater`.
Контрольные суммы – `SHA256SUMS.txt` в ассетах релиза (`sha256sum -c SHA256SUMS.txt`).

## Статистика

Скачиваний этого релиза: [![v__VERSION__](https://img.shields.io/github/downloads/__SLUG__/v__VERSION__/total?label=&color=2ea44f)](__REPO__/releases/tag/v__VERSION__) · всего по проекту: [![всего](https://img.shields.io/github/downloads/__SLUG__/total?label=&color=1f6feb)](__REPO__/releases)
