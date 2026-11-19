using Lumina.App.Backup;
using Lumina.Data.Backup;
using Lumina.Data.Cities;
using Lumina.Data.Clipboard;
using Lumina.Data.Countdowns;
using Lumina.Data.Files;
using Lumina.Data.Focus;
using Lumina.Data.Migrations;
using Lumina.Data.Notes;
using Lumina.Data.Reminders;
using Lumina.Data.Schedules;
using Lumina.Data.Screenshots;
using Lumina.Data.Settings;
using Lumina.Data.Sqlite;
using Lumina.Data.Todos;
using Lumina.Data.Vault;
using Lumina.Domain.Stores;
using Lumina.Services.Backup;
using Lumina.Services.Calendar;
using Lumina.Services.Capture;
using Lumina.Services.Clipboard;
using Lumina.Services.Clock;
using Lumina.Services.Colors;
using Lumina.Services.Compatibility;
using Lumina.Services.Completion;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Countdowns;
using Lumina.Services.Focus;
using Lumina.Services.Hotkeys;
using Lumina.Services.Logging;
using Lumina.Services.Noise;
using Lumina.Services.Notes;
using Lumina.Services.Notifications;
using Lumina.Services.Reminders;
using Lumina.Services.Security;
using Lumina.Services.Settings;
using Lumina.Services.Todos;
using Lumina.Services.Vault;
using Lumina.SystemAdapters.Audio;
using Lumina.SystemAdapters.Clipboard;
using Lumina.SystemAdapters.Compatibility;
using Lumina.SystemAdapters.Hotkeys;
using Lumina.SystemAdapters.Imaging;
using Lumina.SystemAdapters.Ocr;
using Lumina.SystemAdapters.ScreenCapture;
using Lumina.SystemAdapters.Startup;
using Lumina.SystemAdapters.Theming;
using Lumina.SystemAdapters.Vault;
using Lumina.UI.Wpf.Audio;
using Lumina.UI.Wpf.Capture;
using Lumina.UI.Wpf.Clipboard;
using Lumina.UI.Wpf.Themes;
using Lumina.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lumina.App;

/// <summary>
/// 依赖注入装配点：日志、数据层、迁移、配置中枢、主题与系统适配器。
/// P1 起在此继续注册业务服务实现。
/// </summary>
public static class CompositionRoot
{
    public static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new RollingFileLoggerProvider(new RollingFileLoggerOptions
            {
                Directory = AppPaths.LogsDirectory,
                FileName = "app.log",
                MaxFileBytes = 5 * 1024 * 1024,
                RetainedFiles = 3,
                MinimumLevel = LogLevel.Information,
            }));
        });

        var database = new SqliteDatabase(AppPaths.DatabaseFile);
        services.AddSingleton(database);
        services.AddSingleton(new DatabaseBackupService(database, AppPaths.BackupsDirectory));
        services.AddSingleton(new PendingRestoreStore(AppPaths.PendingRestoreFile));
        services.AddSingleton<DatabaseRecoveryService>(sp => new DatabaseRecoveryService(
            sp.GetRequiredService<SqliteDatabase>(),
            AppPaths.CorruptDirectory));
        services.AddSingleton<PendingRestoreProcessor>();
        services.AddSingleton<IBackupCoordinator, AppBackupCoordinator>();
        services.AddSingleton<IReadOnlyList<Migration>>(MigrationCatalog.All);
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton(new OrphanFileReconciler(database, AppPaths.FilesDirectory));

        services.AddSingleton<ISettingsBackend, SqliteSettingsBackend>();
        services.AddSingleton<ISettingsStore, SettingsStore>();

        services.AddSingleton<ISystemThemeSource, WindowsThemeSource>();
        services.AddSingleton<ThemeManager>();

        services.AddSingleton<IAutoStartupService, RegistryAutoStartupService>();
        services.AddSingleton<IHotkeyManager, HotkeyManager>();
        services.AddSingleton<HotkeyBindingService>();
        services.AddSingleton<HotkeyRegistrationService>();

        services.AddSingleton<IClockService, ClockService>();
        services.AddSingleton<IWorldCityStore, WorldCityRepository>();
        services.AddSingleton<IWorldClockService, WorldClockService>();
        services.AddSingleton<IScheduleStore, ScheduleRepository>();
        services.AddSingleton<IScheduleService, ScheduleService>();
        services.AddSingleton<ITodoStore, TodoRepository>();
        services.AddSingleton<ITodoService, TodoService>();
        services.AddSingleton<IClipboardStore, ClipboardRepository>();
        services.AddSingleton<ClipboardRetentionService>();
        services.AddSingleton<IgnoreRuleService>();
        services.AddSingleton<IClipboardMonitor, WindowsClipboardMonitor>();
        services.AddSingleton<IClipboardWriter, WindowsClipboardWriter>();
        services.AddSingleton<ClipboardBoardService>();
        services.AddSingleton<ClipboardFloatController>();
        services.AddSingleton<IScreenCaptureService, WindowsScreenCaptureService>();
        services.AddSingleton<IColorProbe, ColorProbeService>();
        services.AddSingleton<IOcrService, WindowsOcrService>();
        services.AddSingleton<ICompatibilityProbe, WindowsCompatibilityProbe>();
        services.AddSingleton<ColorHistoryService>();
        services.AddSingleton<ScreenshotFileService>();
        services.AddSingleton<IScreenshotCatalog, ScreenshotRepository>();
        services.AddSingleton<IScreenshotStore>(sp => new ScreenshotStore(
            sp.GetRequiredService<ScreenshotFileService>(),
            sp.GetRequiredService<IScreenshotCatalog>(),
            sp.GetRequiredService<ISettingsStore>(),
            ScreenshotThumbnailer.Create));
        services.AddSingleton<CaptureFlowController>();
        services.AddSingleton<CompletionStyleHub>();
        services.AddSingleton<ICalendarService, CalendarService>();
        services.AddSingleton<FocusSessionHub>();
        services.AddSingleton<IFocusSessionStore, FocusSessionRepository>();
        services.AddSingleton<IDailyGoalStore, DailyGoalRepository>();
        services.AddSingleton<IFocusTracker>(sp => new FocusTrackerService(
            sp.GetRequiredService<IFocusSessionStore>(),
            sp.GetRequiredService<ITodoService>()));
        services.AddSingleton<IDailyGoalService, DailyGoalService>();
        services.AddSingleton<IFocusStatsService>(sp => new FocusStatsService(
            sp.GetRequiredService<IFocusSessionStore>(),
            sp.GetRequiredService<IDailyGoalStore>(),
            sp.GetRequiredService<ITodoService>()));
        services.AddSingleton<FocusSessionRecorder>();
        services.AddSingleton<ITimerEngine, TimerEngine>();
        services.AddSingleton<IStopwatchEngine, StopwatchEngine>();
        services.AddSingleton<IPomodoroService, PomodoroService>();
        services.AddSingleton<IWidgetHost, WidgetHost>();

        services.AddSingleton<INoteStore, NoteRepository>();
        services.AddSingleton<INoteService, NoteService>();

        services.AddSingleton<IVaultStore, VaultRepository>();
        services.AddSingleton<IVaultSecretProtector, DpapiVaultSecretProtector>();
        services.AddSingleton<ICryptoProvider>(_ => new Pbkdf2CryptoProvider());
        services.AddSingleton<IVaultService, VaultService>();
        services.AddSingleton<ISecureClipboardChannel, WindowsSecureClipboardChannel>();
        services.AddSingleton<ISecureClipboard>(provider => new SecureClipboardBroker(
            provider.GetRequiredService<ISecureClipboardChannel>(),
            provider.GetRequiredService<IVaultService>(),
            logger: provider.GetRequiredService<ILogger<SecureClipboardBroker>>()));

        services.AddSingleton<IReminderDedupStore, ReminderDedupRepository>();
        services.AddSingleton<ICountdownStore, CountdownRepository>();
        services.AddSingleton<ICountdownService, CountdownService>();
        services.AddSingleton<SystemNotifierRouter>();
        services.AddSingleton<INotifier>(provider => provider.GetRequiredService<SystemNotifierRouter>());
        services.AddSingleton<INotificationSoundPlayer, NotificationSoundPlayer>();
        services.AddSingleton<SessionCompletionNotifier>();
        services.AddSingleton<IReminderSource, ScheduleReminderSource>();
        services.AddSingleton<IReminderSource, CountdownReminderSource>();
        services.AddSingleton<ReminderScheduler>();

        services.AddSingleton(NoiseLibrary.Locate());
        services.AddSingleton<INoiseAudioOutput, WasapiNoiseOutput>();
        services.AddSingleton<INoiseMixer, NoiseMixer>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
