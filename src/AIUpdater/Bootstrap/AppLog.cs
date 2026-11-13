using AIUpdater.Models;
using Microsoft.Extensions.Logging;

namespace AIUpdater.Bootstrap;

internal static partial class AppLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Оболочка AI Updater запущена ({StartupMs} мс)")]
    public static partial void AppStarted(this ILogger logger, long startupMs);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Обнаружен уже запущенный экземпляр AI Updater – повторный запуск завершён")]
    public static partial void SecondaryInstanceDetected(this ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Не удалось запустить AI Updater")]
    public static partial void AppStartupFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Показано уведомление о сбое сохранения настроек")]
    public static partial void SettingsWriteToastShown(this ILogger logger);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Завершение работы AI Updater (код выхода {ExitCode})")]
    public static partial void AppExiting(this ILogger logger, int exitCode);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Запуск автономной галереи в {OutputDirectory} (кадров: {PageCount}, масштаб: {FontScale})")]
    public static partial void GalleryStarted(this ILogger logger, string outputDirectory, int pageCount, double fontScale);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "Автономная галерея завершена с кодом {ExitCode} за {ElapsedMs} мс")]
    public static partial void GalleryCompleted(this ILogger logger, int exitCode, long elapsedMs);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Запуск скрипта {Vendor}: действие {Action}, только проверка: {CheckOnly}, компонент: {StageId}, скрипт: {ScriptPath}")]
    public static partial void PowerShellProcessStarting(this ILogger logger, string vendor, string action, bool checkOnly, string? stageId, string scriptPath);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Скрипт {Vendor} ({Action}) завершился с кодом {ExitCode} за {ElapsedMs} мс")]
    public static partial void PowerShellProcessCompleted(this ILogger logger, string vendor, string action, int exitCode, long elapsedMs);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning, Message = "Процесс скрипта {Vendor} ({Action}) принудительно остановлен по запросу отмены")]
    public static partial void PowerShellProcessTerminatedOnCancel(this ILogger logger, string vendor, string action);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning, Message = "Не удалось завершить дочерний процесс PowerShell для {Vendor}")]
    public static partial void PowerShellProcessKillFailed(this ILogger logger, Exception exception, string vendor);

    [LoggerMessage(EventId = 1110, Level = LogLevel.Information, Message = "Начат запуск обновления: действие {Action}, только проверка: {CheckOnly}, компонентов: {StageCount}, поставщиков: {VendorCount}, пакетный режим: {IsBulk}")]
    public static partial void UpdaterRunStarting(this ILogger logger, string action, bool checkOnly, int stageCount, int vendorCount, bool isBulk);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Information, Message = "Запуск {Action} (только проверка: {CheckOnly}) завершён – код {ExitCode}, ошибки: {SawError}, остановлен: {StopRequested}, итог: {StatusText}")]
    public static partial void UpdaterRunFinished(this ILogger logger, string action, bool checkOnly, int exitCode, bool sawError, bool stopRequested, string statusText);

    [LoggerMessage(EventId = 1112, Level = LogLevel.Error, Message = "Сбой выполнения скрипта поставщика {Vendor} ({Action})")]
    public static partial void VendorRunFailed(this ILogger logger, Exception exception, string vendor, string action);

    [LoggerMessage(EventId = 1113, Level = LogLevel.Warning, Message = "Скрипт поставщика {Vendor} завершился без маркера UI_RESULT для {StageId}")]
    public static partial void VendorStageResultMissing(this ILogger logger, string vendor, string stageId);

    [LoggerMessage(EventId = 1114, Level = LogLevel.Information, Message = "Запуск операции .NET: {Action} {PackageKind} (ветка {Channel}, версия: {Version}, пререлизы: {IncludePrerelease})")]
    public static partial void DotNetOperationStarting(this ILogger logger, string action, string packageKind, string channel, string? version, bool includePrerelease);

    [LoggerMessage(EventId = 1115, Level = LogLevel.Information, Message = "Операция .NET {Action} {PackageKind} (ветка {Channel}) завершена за {ElapsedMs} мс – код {ExitCode}, ошибки: {SawError}, остановлена: {Stopped}")]
    public static partial void DotNetOperationFinished(this ILogger logger, string action, string packageKind, string channel, int exitCode, bool sawError, bool stopped, long elapsedMs);

    [LoggerMessage(EventId = 1116, Level = LogLevel.Warning, Message = "Не удалось создать каталог журналов {LogDirectory}")]
    public static partial void LogDirectoryCreateFailed(this ILogger logger, Exception exception, string logDirectory);

    [LoggerMessage(EventId = 1117, Level = LogLevel.Warning, Message = "Не удалось прочитать журнал прошлого запуска {LogPath}")]
    public static partial void PreviousLogReadFailed(this ILogger logger, Exception exception, string logPath);

    [LoggerMessage(EventId = 1118, Level = LogLevel.Debug, Message = "Не удалось удалить временный журнал {TempLogPath}")]
    public static partial void TempLogDeleteFailed(this ILogger logger, Exception exception, string tempLogPath);

    [LoggerMessage(EventId = 1119, Level = LogLevel.Warning, Message = "Не удалось подготовить файл журнала операции {LogPath}")]
    public static partial void RunLogResetFailed(this ILogger logger, Exception exception, string logPath);

    [LoggerMessage(EventId = 1120, Level = LogLevel.Debug, Message = "Получен результат {Vendor} / {StageId}: {Result} (установлено: {Installed}, доступно: {Available})")]
    public static partial void StageCheckResultParsed(this ILogger logger, string vendor, string stageId, string result, string? installed, string? available);

    [LoggerMessage(EventId = 1121, Level = LogLevel.Warning, Message = "Некорректный пакет UI_RESULT от скрипта поставщика {Vendor}")]
    public static partial void StageCheckResultMalformed(this ILogger logger, Exception exception, string vendor);

    [LoggerMessage(EventId = 1122, Level = LogLevel.Warning, Message = "Скрипт поставщика {Vendor} сообщил о сбое этапа {StageId}")]
    public static partial void StageReportedFailureMarker(this ILogger logger, string vendor, string stageId);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Warning, Message = "Запуск отменён – скрипт обновления не найден по пути {ScriptPath}")]
    public static partial void UpdaterScriptMissing(this ILogger logger, string scriptPath);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Error, Message = "Необработанное исключение во время операции {Action}")]
    public static partial void UpdaterUnhandledRunException(this ILogger logger, Exception exception, string action);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "Пользователь запросил остановку операции (в работе: {ActiveCount}, в очереди: {QueuedCount})")]
    public static partial void UpdaterStopRequested(this ILogger logger, int activeCount, int queuedCount);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Information, Message = "Запрошено действие {Action} для компонента {StageId}")]
    public static partial void ComponentActionRequested(this ILogger logger, string stageId, string action);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Information, Message = "Удаление компонента {StageId} отменено пользователем")]
    public static partial void ComponentUninstallCancelled(this ILogger logger, string stageId);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Information, Message = "Запущено пакетное обновление выбранных компонентов ({SelectedCount})")]
    public static partial void BulkUpdateRequested(this ILogger logger, int selectedCount);

    [LoggerMessage(EventId = 1210, Level = LogLevel.Information, Message = "Открыт файл или каталог журналов {Path}")]
    public static partial void ExternalLogOpened(this ILogger logger, string path);

    [LoggerMessage(EventId = 1211, Level = LogLevel.Debug, Message = "Файл журнала последнего запуска ещё не создан: {Path}")]
    public static partial void ExternalLogMissing(this ILogger logger, string path);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Загрузка каталога выпусков .NET (принудительно: {ForceRefresh}, пререлизы: {IncludePrerelease})")]
    public static partial void DotNetCatalogLoading(this ILogger logger, bool forceRefresh, bool includePrerelease);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "Каталог .NET собран за {ElapsedMs} мс – каналов: {ChannelCount}, SDK: {SdkCount}, сред выполнения: {RuntimeCount}, автономных пакетов: {BundleCount}, офлайн-режим: {OfflineFallback}")]
    public static partial void DotNetCatalogLoaded(this ILogger logger, int channelCount, int sdkCount, int runtimeCount, int bundleCount, bool offlineFallback, long elapsedMs);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Debug, Message = "Индекс выпусков .NET прочитан из актуального кэша {CacheFile}")]
    public static partial void DotNetIndexLoadedFromFreshCache(this ILogger logger, string cacheFile);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Warning, Message = "Сеть недоступна при загрузке индекса .NET – использован сохранённый кэш {CacheFile}")]
    public static partial void DotNetIndexFallbackToStaleCache(this ILogger logger, Exception exception, string cacheFile);

    [LoggerMessage(EventId = 1304, Level = LogLevel.Warning, Message = "Не удалось загрузить индекс выпусков .NET, локальный кэш отсутствует")]
    public static partial void DotNetIndexOfflineWithoutCache(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1305, Level = LogLevel.Debug, Message = "Не удалось обновить метаданные ветки .NET {Channel} – использован локальный кэш")]
    public static partial void DotNetChannelDetailFallbackToCache(this ILogger logger, Exception exception, string channel);

    [LoggerMessage(EventId = 1306, Level = LogLevel.Warning, Message = "Пропущен недоверенный адрес метаданных ветки .NET {Channel}: {Url}")]
    public static partial void DotNetChannelMetadataUntrustedUrl(this ILogger logger, string channel, string url);

    [LoggerMessage(EventId = 1307, Level = LogLevel.Debug, Message = "Исполняемый файл dotnet.exe не найден в PATH и Program Files")]
    public static partial void DotNetCliNotFound(this ILogger logger);

    [LoggerMessage(EventId = 1308, Level = LogLevel.Warning, Message = "Сбой опроса локального инвентаря dotnet {Subcommand}")]
    public static partial void DotNetCliInventoryFailed(this ILogger logger, Exception exception, string subcommand);

    [LoggerMessage(EventId = 1309, Level = LogLevel.Warning, Message = "Сбой чтения автономных установок .NET из реестра Windows")]
    public static partial void DotNetRegistryScanFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1310, Level = LogLevel.Warning, Message = "Не удалось разобрать JSON метаданных .NET ({Target})")]
    public static partial void DotNetMetadataJsonInvalid(this ILogger logger, Exception exception, string target);

    [LoggerMessage(EventId = 1311, Level = LogLevel.Error, Message = "Ошибка обновления каталога .NET в представлении")]
    public static partial void DotNetCatalogViewModelFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1312, Level = LogLevel.Information, Message = "Подтверждено удаление пакета .NET {Kind} {Version} (ветка {Channel})")]
    public static partial void DotNetUninstallConfirmed(this ILogger logger, DotNetPackageKind kind, string version, string channel);

    [LoggerMessage(EventId = 1313, Level = LogLevel.Information, Message = "Удаление пакета .NET {Kind} {Version} заблокировано (владелец: {Owner})")]
    public static partial void DotNetUninstallBlockedByOwner(this ILogger logger, DotNetPackageKind kind, string version, DotNetInstallOwner owner);

    [LoggerMessage(EventId = 1400, Level = LogLevel.Debug, Message = "Источник истории изменений не изменился (HTTP 304): {Url}")]
    public static partial void ChangelogHttpNotModified(this ILogger logger, string url);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "Загружен источник истории изменений {Url} ({CharCount} симв., ETag: {HasETag}) за {ElapsedMs} мс")]
    public static partial void ChangelogHttpFetched(this ILogger logger, string url, int charCount, bool hasETag, long elapsedMs);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Warning, Message = "Сбой загрузки {Url} – переключение на Atom-ленту {AtomUrl}")]
    public static partial void ChangelogHttpFallbackToAtom(this ILogger logger, Exception exception, string url, string atomUrl);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Information, Message = "Команда {Executable} (аргументов: {ArgCount}) выполнена за {ElapsedMs} мс ({OutputLength} симв.)")]
    public static partial void ExternalCommandCompleted(this ILogger logger, string executable, int argCount, int outputLength, long elapsedMs);

    [LoggerMessage(EventId = 1404, Level = LogLevel.Warning, Message = "Выполнение команды {Executable} прервано по отмене или таймауту ({TimeoutSeconds} с)")]
    public static partial void ExternalCommandTimedOutOrCancelled(this ILogger logger, string executable, int timeoutSeconds);

    [LoggerMessage(EventId = 1410, Level = LogLevel.Debug, Message = "История изменений {StageId} отдана из актуального кэша (выпусков: {ReleaseCount})")]
    public static partial void ChangelogServedFromFreshCache(this ILogger logger, string stageId, int releaseCount);

    [LoggerMessage(EventId = 1411, Level = LogLevel.Information, Message = "История изменений {StageId} ({Format}) обновлена за {ElapsedMs} мс – выпусков: {ReleaseCount}, без изменений (304): {NotModified}")]
    public static partial void ChangelogDocumentUpdated(this ILogger logger, string stageId, string format, int releaseCount, bool notModified, long elapsedMs);

    [LoggerMessage(EventId = 1412, Level = LogLevel.Warning, Message = "Источник истории изменений {StageId} недоступен – показан сохранённый кэш (выпусков: {ReleaseCount})")]
    public static partial void ChangelogFallbackToCachedDocument(this ILogger logger, Exception exception, string stageId, int releaseCount);

    [LoggerMessage(EventId = 1413, Level = LogLevel.Warning, Message = "Не удалось получить релизы Antigravity CLI с GitHub – переключение на локальную команду agy changelog")]
    public static partial void AntigravityCliGitHubFallbackToLocal(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1414, Level = LogLevel.Debug, Message = "Догрузка коммитов сравнения прервана на выпуске {Version}")]
    public static partial void CompareCommitsEnrichmentStopped(this ILogger logger, Exception exception, string version);

    [LoggerMessage(EventId = 1415, Level = LogLevel.Debug, Message = "Не удалось получить список плагинов через {CliName}")]
    public static partial void PluginListFetchFailed(this ILogger logger, Exception exception, string cliName);

    [LoggerMessage(EventId = 1420, Level = LogLevel.Warning, Message = "Не удалось загрузить историю изменений для компонента {StageId}")]
    public static partial void ChangelogStageLoadFailed(this ILogger logger, Exception exception, string stageId);

    [LoggerMessage(EventId = 1421, Level = LogLevel.Information, Message = "Переключён канал истории изменений {StageId}: {Channel} (видимых выпусков: {VisibleCount})")]
    public static partial void ChangelogChannelSwitched(this ILogger logger, string? stageId, ReleaseChannel channel, int visibleCount);

    [LoggerMessage(EventId = 1500, Level = LogLevel.Information, Message = "Начат перевод выпуска {Version} через {BackendId} (модель: {Model}, уровень: {Effort}, частей: {ChunkCount}, повторно: {ForceRetranslate})")]
    public static partial void ReleaseTranslationStarting(this ILogger logger, string version, string backendId, string model, string effort, int chunkCount, bool forceRetranslate);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Information, Message = "Выпуск {Version} переведён через {BackendId} за {ElapsedMs} мс (секций: {GroupCount})")]
    public static partial void ReleaseTranslationSucceeded(this ILogger logger, string version, string backendId, int groupCount, long elapsedMs);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Warning, Message = "Ответ агента {BackendId} для выпуска {Version} (часть {ChunkIndex}, {ResponseLength} симв.) не прошёл проверку формата")]
    public static partial void ReleaseTranslationInvalidResponse(this ILogger logger, string version, string backendId, int chunkIndex, int responseLength);

    [LoggerMessage(EventId = 1503, Level = LogLevel.Information, Message = "Результат перевода выпуска {Version} отброшен – {Reason}")]
    public static partial void ReleaseTranslationDiscardedStale(this ILogger logger, string version, string reason);

    [LoggerMessage(EventId = 1504, Level = LogLevel.Error, Message = "Ошибка перевода выпуска {Version} через {BackendId}")]
    public static partial void ReleaseTranslationFailed(this ILogger logger, Exception exception, string version, string backendId);

    [LoggerMessage(EventId = 1510, Level = LogLevel.Information, Message = "Настройки агента перевода ({BackendId}) перенесены из устаревшего файла {LegacyPath}")]
    public static partial void AgentSettingsMigratedFromLegacy(this ILogger logger, string legacyPath, string backendId);

    [LoggerMessage(EventId = 1511, Level = LogLevel.Warning, Message = "Не удалось прочитать настройки агента перевода из {Path} – применены значения по умолчанию")]
    public static partial void AgentSettingsLoadFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1512, Level = LogLevel.Information, Message = "Сохранены настройки агента перевода: {BackendId}, модель: {Model}, уровень: {Effort}, автоперевод: {Automatic} (поколение {Generation})")]
    public static partial void AgentSettingsSaved(this ILogger logger, string backendId, string model, string effort, bool automatic, int generation);

    [LoggerMessage(EventId = 1520, Level = LogLevel.Information, Message = "Обнаружен CLI агента {BackendId} ({Version}) по пути {ExecutablePath}, моделей в каталоге: {ModelCount}")]
    public static partial void AgentCliDetected(this ILogger logger, string backendId, string version, string executablePath, int modelCount);

    [LoggerMessage(EventId = 1521, Level = LogLevel.Warning, Message = "CLI агента {BackendId} не найден (указанный путь: {ConfiguredPath})")]
    public static partial void AgentCliNotFound(this ILogger logger, string backendId, string configuredPath);

    [LoggerMessage(EventId = 1522, Level = LogLevel.Warning, Message = "Ошибка проверки CLI агента {BackendId}")]
    public static partial void AgentCliDetectFailed(this ILogger logger, Exception exception, string backendId);

    [LoggerMessage(EventId = 1600, Level = LogLevel.Debug, Message = "Загружен кэш истории изменений {StageId} (выпусков: {ReleaseCount}, схема v{SchemaVersion})")]
    public static partial void ChangelogCacheLoaded(this ILogger logger, string stageId, int releaseCount, int schemaVersion);

    [LoggerMessage(EventId = 1601, Level = LogLevel.Debug, Message = "Пропущен кэш истории изменений {StageId} (схема v{SchemaVersion}, выпусков: {ReleaseCount})")]
    public static partial void ChangelogCacheIncompatibleOrEmpty(this ILogger logger, string stageId, int schemaVersion, int releaseCount);

    [LoggerMessage(EventId = 1602, Level = LogLevel.Warning, Message = "Не удалось прочитать файл кэша истории изменений {Path}")]
    public static partial void ChangelogCacheReadFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1603, Level = LogLevel.Debug, Message = "Сохранён кэш истории изменений {StageId} (выпусков: {ReleaseCount})")]
    public static partial void ChangelogCacheSaved(this ILogger logger, string stageId, int releaseCount);

    [LoggerMessage(EventId = 1604, Level = LogLevel.Warning, Message = "Не удалось записать кэш истории изменений {StageId} в {Directory}")]
    public static partial void ChangelogCacheWriteFailed(this ILogger logger, Exception exception, string stageId, string directory);

    [LoggerMessage(EventId = 1610, Level = LogLevel.Information, Message = "Восстановлены результаты проверки для {RestoredCount} компонентов из {ResultsPath}")]
    public static partial void StageResultsLoaded(this ILogger logger, int restoredCount, string resultsPath);

    [LoggerMessage(EventId = 1611, Level = LogLevel.Warning, Message = "Не удалось загрузить сохранённые результаты проверки из {ResultsPath}")]
    public static partial void StageResultsLoadFailed(this ILogger logger, Exception exception, string resultsPath);

    [LoggerMessage(EventId = 1612, Level = LogLevel.Debug, Message = "Сохранены результаты проверки ({SavedCount} компонентов) в {ResultsPath}")]
    public static partial void StageResultsSaved(this ILogger logger, int savedCount, string resultsPath);

    [LoggerMessage(EventId = 1613, Level = LogLevel.Warning, Message = "Не удалось сохранить результаты проверки в {ResultsPath}")]
    public static partial void StageResultsSaveFailed(this ILogger logger, Exception exception, string resultsPath);

    [LoggerMessage(EventId = 1700, Level = LogLevel.Debug, Message = "Переход в раздел {SectionKey}")]
    public static partial void ShellNavigated(this ILogger logger, string sectionKey);

    [LoggerMessage(EventId = 1701, Level = LogLevel.Debug, Message = "Переход в раздел {SectionKey} отклонён из-за активного модального окна")]
    public static partial void ShellNavigationBlockedByModal(this ILogger logger, string sectionKey);

    [LoggerMessage(EventId = 1702, Level = LogLevel.Warning, Message = "Попытка перехода в неизвестный раздел {SectionKey}")]
    public static partial void ShellNavigationUnknownKey(this ILogger logger, string sectionKey);

    [LoggerMessage(EventId = 1703, Level = LogLevel.Information, Message = "Запрос закрытия приложения во время операции – подтверждено: {Confirmed}")]
    public static partial void ShellCloseConfirmedWhileBusy(this ILogger logger, bool confirmed);

    [LoggerMessage(EventId = 1710, Level = LogLevel.Warning, Message = "Не удалось открыть внешний путь или ссылку {Target}")]
    public static partial void ExternalTargetOpenFailed(this ILogger logger, string target);

    [LoggerMessage(EventId = 1720, Level = LogLevel.Information, Message = "Параметры внешнего вида сброшены к значениям по умолчанию")]
    public static partial void AppearanceResetToDefaults(this ILogger logger);
}
