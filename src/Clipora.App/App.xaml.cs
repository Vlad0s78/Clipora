using System.Globalization;
using Clipora.App.ViewModels;
using Clipora.App.Services;
using Clipora.Core;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;
using Clipora.Core.Services;
using Clipora.FFmpeg;
using Clipora.Media;
using Clipora.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;
using Serilog;

namespace Clipora.App;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;

    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public static IServiceProvider Services { get; private set; } = null!;

    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public App()
    {
        Services = ConfigureServices();
        UnhandledException += OnUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        ILogger<App> logger = Services.GetRequiredService<ILogger<App>>();

        try
        {
            CliporaLaunchRequest launchRequest;
            if (!CliporaCommandLine.TryParse(
                    Environment.GetCommandLineArgs().Skip(1).ToArray(),
                    out launchRequest,
                    out CliporaCommandLineError commandLineError))
            {
                logger.LogWarning(
                    "Аргументы запуска Clipora отклонены: {CommandLineError}.",
                    commandLineError);
                launchRequest = CliporaLaunchRequest.Empty;
            }

            AppSettings settings = await Services
                .GetRequiredService<ISettingsService>()
                .LoadAsync(CancellationToken.None);
            ITrimShortcutService shortcutService = Services.GetRequiredService<ITrimShortcutService>();
            if (!shortcutService.TryApply(settings.TrimShortcuts, out TrimShortcutValidationError shortcutError))
            {
                logger.LogWarning(
                    "Настройки горячих клавиш не применены: {ValidationError}",
                    shortcutError);
            }
            string language = LanguagePolicy.Resolve(
                settings.Language,
                Windows.System.UserProfile.GlobalizationPreferences.Languages);
            ApplicationLanguages.PrimaryLanguageOverride = language;
            CultureInfo culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            InitializeComponent();

            AppVersionInfo versionInfo = Services.GetRequiredService<AppVersionInfo>();
            var tools = Services.GetRequiredService<IBundledToolResolver>().Resolve();
            logger.LogInformation(
                "Clipora {Version} build {Build} запускается. FFmpeg: {FfmpegPath}; FFprobe: {FfprobePath}",
                versionInfo.Version,
                versionInfo.Build,
                tools.FfmpegPath,
                tools.FfprobePath);

            Window = Services.GetRequiredService<MainWindow>();
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Window.Closed += (_, _) =>
            {
                Services.GetRequiredService<MainPageViewModel>().Dispose();
                Log.CloseAndFlush();
                Exit();
            };
            Window.Activate();

            await Services
                .GetRequiredService<MainPageViewModel>()
                .HandleLaunchRequestAsync(launchRequest, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Не удалось запустить Clipora.");
            Log.CloseAndFlush();
            throw;
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AppDataPaths dataPaths = AppDataPathResolver.Resolve(AppContext.BaseDirectory, localAppData);
        Directory.CreateDirectory(dataPaths.LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(dataPaths.LogDirectory, "clipora-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        ServiceCollection services = new();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
        });
        services.AddSingleton(dataPaths);
        services.AddSingleton(_ => AppVersionInfo.FromAssembly(typeof(App).Assembly));
        services.AddSingleton<IBundledToolResolver, BundledToolResolver>();
        services.AddSingleton<IFFprobeService, FfprobeService>();
        services.AddSingleton<IEncoderDetector, EncoderDetector>();
        services.AddSingleton<IFFmpegCommandBuilder, FFmpegCommandBuilder>();
        services.AddSingleton<IFFmpegRunner, FfmpegRunner>();
        services.AddSingleton<IOutputFileService, OutputFileService>();
        services.AddSingleton<ISettingsService>(_ => new JsonSettingsService(dataPaths.SettingsFilePath));
        services.AddSingleton<ITrimShortcutService, TrimShortcutService>();
        services.AddSingleton<IVideoProcessingService, VideoProcessingService>();
        services.AddSingleton<IThumbnailService>(provider => new ThumbnailService(
            provider.GetRequiredService<IBundledToolResolver>(),
            dataPaths.ThumbnailCacheDirectory));
        services.AddSingleton<IVideoFilePicker, VideoFilePicker>();
        services.AddSingleton<IExplorerIntegrationService>(_ => dataPaths.IsPortable
            ? new DisabledExplorerIntegrationService()
            : new ExplorerIntegrationService());
        services.AddSingleton<IOutputFolderPicker, OutputFolderPicker>();
        services.AddSingleton<ILogFolderLauncher>(_ => new LogFolderLauncher(dataPaths.LogDirectory));
        services.AddSingleton<IFFmpegVersionService, FFmpegVersionService>();
        services.AddSingleton(_ => new ResourceLoader());
        services.AddSingleton<MainPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<AboutPageViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        Services.GetRequiredService<ILogger<App>>()
            .LogError(args.Exception, "Необработанная ошибка интерфейса.");
    }
}
