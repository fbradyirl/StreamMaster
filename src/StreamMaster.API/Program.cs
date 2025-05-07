using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

using MediatR;

using Microsoft.AspNetCore.HttpOverrides;
using Reinforced.Typings.Attributes;

using StreamMaster.API;
using StreamMaster.Application;
using StreamMaster.Application.Hubs;
using StreamMaster.Application.Statistics.Commands;
using StreamMaster.Domain.Helpers;
using StreamMaster.EPG;
using StreamMaster.Infrastructure;
using StreamMaster.Infrastructure.EF;
using StreamMaster.Infrastructure.EF.PGSQL;
using StreamMaster.Infrastructure.Logger;
using StreamMaster.PlayList;
using StreamMaster.SchedulesDirect;
using StreamMaster.SchedulesDirect.Services;
using StreamMaster.Streams;

[assembly: TsGlobal(CamelCaseForProperties = false, CamelCaseForMethods = false, UseModules = true, DiscardNamespacesWhenUsingModules = true, AutoOptionalProperties = true, WriteWarningComment = false, ReorderMembers = true)]
DirectoryHelper.CreateApplicationDirectories();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

static void Log(string format, params object[] args)
{
    string message = string.Format(format, args);
    Console.WriteLine(message);
    Debug.WriteLine(message);
}

builder.WebHost.ConfigureKestrel((_, serverOptions) =>
{
    serverOptions.AllowSynchronousIO = true;
    serverOptions.Limits.MaxRequestBodySize = null;
});

List<string> settingsFiles = BuildInfo.GetSettingFiles();

// Set base configuration path
string configPath = Directory.Exists(BuildInfo.SettingsFolder) ? BuildInfo.AppDataFolder : BuildInfo.StartUpPath;
builder.Configuration.SetBasePath(configPath).AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

// Load and validate settings
LoadAndSetSettings<CommandProfileDict, CommandProfile>(BuildInfo.CommandProfileSettingsFile, SettingFiles.DefaultCommandProfileSetting);
LoadAndSetSettings<OutputProfileDict, OutputProfile>(BuildInfo.OutputProfileSettingsFile, SettingFiles.DefaultOutputProfileSetting);
//LoadAndValidateSettings<HLSSettings>(BuildInfo.HLSSettingsFile, new HLSSettings());
LoadAndValidateSettings<Setting>(BuildInfo.SettingsFile, new Setting());
LoadAndValidateSettings<SDSettings>(BuildInfo.SDSettingsFile, new SDSettings());

LoadAndSetSettings<CustomLogoDict, CustomLogo>(BuildInfo.CustomLogosSettingsFile, SettingFiles.DefaultCustomLogoSetting);

// Add additional settings files if they exist
foreach (string file in settingsFiles)
{
    if (File.Exists(file))
    {
        Log($"Using settings file {file}");
        builder.Configuration.AddJsonFile(file, optional: true, reloadOnChange: true);
    }
}

// Configure services with settings
ConfigureSettings<Setting>(builder);
ConfigureSettings<SDSettings>(builder);
//ConfigureSettings<HLSSettings>(builder);
ConfigureSettings<CommandProfileDict>(builder);
ConfigureSettings<OutputProfileDict>(builder);
ConfigureSettings<CustomLogoDict>(builder);

// Update ScheduleDirect UserAgent to reflect assembly version
var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0";
SDSettings? sdSettings = SettingsHelper.GetSetting<SDSettings>(BuildInfo.SDSettingsFile);
if (sdSettings != null)
{
    // Update the UserAgent with the version
    sdSettings.UserAgent = $"StreamMaster/{version}";
    SettingsHelper.UpdateSetting(sdSettings);
}

void LoadAndSetSettings<TDict, TProfile>(string settingsFile, TDict defaultSetting)
    where TDict : IProfileDict<TProfile>
{
    // Load the settings
    TDict? setting = SettingsHelper.GetSetting<TDict>(settingsFile);
    if (setting == null)
    {
        // If the setting is null, apply the entire default setting
        SettingsHelper.UpdateSetting(defaultSetting);
        return;
    }
    else
    {
        // If the setting is not null, apply the default setting for any missing profiles
        foreach (global::System.Collections.Generic.KeyValuePair<string, TProfile> defaultProfile in defaultSetting.Profiles)
        {
            if (!setting.Profiles.ContainsKey(defaultProfile.Key))
            {
                // Add missing entries
                setting.AddProfile(defaultProfile.Key, defaultProfile.Value);
            }
            else
            {
                if (defaultSetting.IsReadOnly(defaultProfile.Key))
                {
                    setting.Profiles[defaultProfile.Key] = defaultProfile.Value;
                }
            }
        }
    }

    // Save the updated settings if changes were made
    SettingsHelper.UpdateSetting(setting);
}

// Helper method to load and validate settings
void LoadAndValidateSettings<T>(string settingsFile, object defaultSetting)
{
    T? setting = SettingsHelper.GetSetting<T>(settingsFile);
    if (EqualityComparer<T>.Default.Equals(setting, default(T)))
    {
        SettingsHelper.UpdateSetting(defaultSetting);
    }
}

// Helper method to configure settings in services
void ConfigureSettings<T>(WebApplicationBuilder builder) where T : class
{
    builder.Services.Configure<T>(builder.Configuration);
}

bool enableSsl = false;

string? sslCertPath = builder.Configuration["SSLCertPath"];
string? sslCertPassword = builder.Configuration["sslCertPassword"];

if (!bool.TryParse(builder.Configuration["EnableSSL"], out enableSsl))
{
}

Setting? setting = SettingsHelper.GetSetting<Setting>(BuildInfo.SettingsFile);

List<string> urls = [$"http://0.0.0.0:{BuildInfo.DEFAULT_PORT}"];

if (enableSsl && !string.IsNullOrEmpty(sslCertPath))
{
    urls.Add($"https://0.0.0.0:{BuildInfo.DEFAULT_SSL_PORT}");
}

builder.WebHost.UseUrls([.. urls]);

if (!string.IsNullOrEmpty(sslCertPath))
{
    if (string.IsNullOrEmpty(sslCertPassword))
    {
        sslCertPassword = "";
    }

    _ = builder.WebHost.ConfigureKestrel(options =>
    options.ConfigureHttpsDefaults(configureOptions =>
       configureOptions.ServerCertificate = ValidateSslCertificate(Path.Combine(BuildInfo.AppDataFolder, sslCertPath), sslCertPassword)
    ));
}

// GetOrAdd services to the container.
builder.Services.AddEPGServices();
builder.Services.AddSchedulesDirectAPIServices();
builder.Services.AddSchedulesDirectServices();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureEFPGSQLServices();
builder.Services.AddInfrastructureEFServices();
builder.Services.AddInfrastructureServices();
builder.Services.AddInfrastructureServicesEx();
builder.Services.AddStreamsServices();
builder.Services.AddCustomPlayListServices();

builder.Services.AddWebUIServices(builder, setting?.EnableDBDebug ?? false);

builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

builder.Services.AddControllers(options =>
{
    options.RespectBrowserAcceptHeader = true;
    options.ReturnHttpNotAcceptable = true;
}).AddXmlSerializerFormatters()
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

builder.Services.AddSingleton<ILoggerProvider, SMLoggerProvider>();

//builder.Services.AddControllers(options =>
//{
//    options.Conventions.Insert(0, new GlobalRoutePrefixConvention(BuildInfo.PATH_BASE));
//});
//builder.Services.AddControllers().AddMvcOptions(options =>
//{
//    options.Conventions.Add(new RoutePrefixConvention(BuildInfo.PATH_BASE));
//});

WebApplication app = builder.Build();
app.UseResponseCompression();
app.UseForwardedHeaders();

string basePath = Environment.GetEnvironmentVariable("PATH_BASE") ?? BuildInfo.PATH_BASE ?? "";

app.Use(async (context, next) =>
{
    // Console.WriteLine("---- Incoming Headers ----");
    // foreach (var h in context.Request.Headers)
    //     Console.WriteLine($"{h.Key}: {h.Value}");
    // Console.WriteLine("--------------------------");
        
    // first try the old header, then the HA/NGINX one
    string? prefix = context.Request.Headers["X-Forwarded-Prefix"].FirstOrDefault()
                  ?? context.Request.Headers["X-Ingress-Path"].FirstOrDefault();

    if (!string.IsNullOrEmpty(prefix))
    {
        // set both the Request.PathBase and your basePath var
        context.Request.PathBase = prefix;
        basePath = prefix;
        Console.WriteLine($"Using ingress prefix: '{prefix}'");
    }

    await next();
});

IHostApplicationLifetime? lifetime = app.Services.GetService<IHostApplicationLifetime>();
lifetime?.ApplicationStopping.Register(OnShutdown);

void OnShutdown()
{
    ISender sender = app.Services.GetRequiredService<ISender>();
    sender.Send(new SetIsSystemReadyRequest(false)).Wait();
    ProcessHelper.KillProcessByName("ffmpeg");
    PGSQLRepositoryContext repositoryContext = app.Services.GetRequiredService<PGSQLRepositoryContext>();
    repositoryContext.Dispose();
    //IImageDownloadService imageDownloadService = app.Services.GetRequiredService<IImageDownloadService>();
    //imageDownloadService.StopAsync(CancellationToken.None).Wait();

    //DirectoryHelper.EmptyDirectory(BuildInfo.HLSOutputFolder);

    FileUtil.Backup().Wait();
}

app.UseOpenApi();
app.UseSwaggerUi();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    _ = app.UseDeveloperExceptionPage();
    //_ = app.UseMigrationsEndPoint();
}
else
{
    _ = app.UseExceptionHandler("/Error");
    _ = app.UseHsts();
}

//app.UseHttpLogging();
//app.UseMigrationsEndPoint();
// app.UseSession();

//app.UseHangfireDashboard();

using (IServiceScope scope = app.Services.CreateScope())
{
    // Perform additional default data initialization
    RepositoryContextInitializer initialiser = scope.ServiceProvider.GetRequiredService<RepositoryContextInitializer>();
    await initialiser.EnsureDefaultData().ConfigureAwait(false);
    if (app.Environment.IsDevelopment())
    {
        initialiser.CreateApplicationDirecotries();
    }
}

// string basePath = Environment.GetEnvironmentVariable("PATH_BASE") ?? BuildInfo.PATH_BASE ?? "";

// // UsePathBase should be before UseRouting, UseStaticFiles, etc.
// if (!string.IsNullOrEmpty(basePath))
// {
//     app.UsePathBase(basePath.StartsWith("/") ? basePath : $"/{basePath}");
// }

Console.WriteLine($"PATH_BASE--: '{basePath}'");

app.UseDefaultFiles();

app.UseStaticFiles();

app.UseRouting();

// _ = app.Environment.IsDevelopment() ? app.UseCors("DevPolicy") : app.UseCors();
// _ = app.UseCors();
// app.UseCors(VersionedApiControllerAttribute.API_CORS_POLICY); // or "AllowGet" if you prefer
app.UseCors("AllowGet");
app.UseAuthentication();
app.UseAuthorization();
//app.UseMiddleware<CacheHeaderMiddleware>();


// Initialize SMWebSocketManager
ISMWebSocketManager smWebSocketManager = app.Services.GetRequiredService<ISMWebSocketManager>();

app.UseWebSockets();

// WebSocket Endpoint
app.Map("/ws", smWebSocketManager.HandleWebSocketAsync);

// HTTP Endpoint to Trigger Reload
app.MapPost("/trigger-reload", async () =>
{
    await smWebSocketManager.BroadcastReloadAsync();
    return Results.Ok("Reload message sent to all clients.");
});


app.MapDefaultControllerRoute();

app.Map("/swagger", context =>
{
    context.Response.Redirect("/swagger/index.html");
    return Task.CompletedTask;
});

app.MapGet("/routes", async context =>
{
    EndpointDataSource endpointDataSource = context.RequestServices.GetRequiredService<EndpointDataSource>();

    foreach (Endpoint endpoint in endpointDataSource.Endpoints)
    {
        string routePattern = GetRoutePattern(endpoint);

        await context.Response.WriteAsync($"Route: {routePattern}\n");
    }
});

app.MapHub<StreamMasterHub>($"{basePath}/streammasterhub");//.RequireAuthorization("SignalR");

app.MapMethods($"{basePath}/streammasterhub/negotiate", new[] { "OPTIONS" }, () => Results.Ok());

// 1) Serve index.html when the user hits exactly the ingress root:
// app.MapGet($"{basePath}/", async context =>
// {
//     context.Response.ContentType = "text/html";
//     // WebRootPath points to your wwwroot folder
//     await context.Response.SendFileAsync(
//         Path.Combine(app.Environment.WebRootPath, "index.html"));
// });

// // 2) Fallback for any other SPA path under the ingress prefix:
// app.MapFallbackToFile($"{basePath}/{{*path}}", "index.html");

app.Run();

static string GetRoutePattern(Endpoint endpoint)
{
    RouteEndpoint? routeEndpoint = endpoint as RouteEndpoint;

    return routeEndpoint?.RoutePattern?.RawText is not null
        ? routeEndpoint.RoutePattern.RawText
        : "<unknown>";
}

static X509Certificate2 ValidateSslCertificate(string cert, string password)
{
    try
    {
        // Load the certificate using the new X509CertificateLoader API
        X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(cert, password);

        return certificate;
    }
    catch (CryptographicException ex)
    {
        if (ex.HResult is 0x2 or 0x2006D080)
        {
            throw new FileNotFoundException($"The SSL certificate file {cert} does not exist: {ex.Message}", ex);
        }

        throw;
    }
}