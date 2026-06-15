using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

[assembly: CLSCompliant(false)]
namespace SteamFreeLicenseRemover;

internal static class Program
{
    private const int ItemsPerRequest = 5000;

    private static SteamClient steamClient;
    private static CallbackManager manager;

    private static SteamUser steamUser;
    private static SteamApps steamApps;
    private static SteamUnifiedMessages steamUnifiedMessages;
    private static UserAccount userAccountService;

    private static bool isRunning;
    private static bool isFirstLicenseList = true;
    private static Dictionary<uint, (EPaymentMethod PaymentMethod, ELicenseType LicenseType)> previousLicenses = [];
    private static HashSet<uint> previouslyFailedAppIds = new();
    private static bool isExiting;
    private static string user;
    private static string pass;
    private static string accessToken;
    private static int reconnectDelaySeconds = 5;

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed class CredentialsConfig
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string RefreshToken { get; set; }
    }

    private static void LoadCredentials()
    {
        string path = "credentials.json";
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var config = System.Text.Json.JsonSerializer.Deserialize<CredentialsConfig>(json, JsonOptions);
                if (config != null)
                {
                    user = config.Username;
                    pass = config.Password;
                    accessToken = config.RefreshToken;
                }
            }
            catch (IOException) { }
            catch (System.Text.Json.JsonException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void SaveCredentials(string username, string password, string token)
    {
        try
        {
            var config = new CredentialsConfig
            {
                Username = username,
                Password = password,
                RefreshToken = token
            };
            string json = System.Text.Json.JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText("credentials.json", json);
        }
        catch (IOException e)
        {
            Console.WriteLine($"Error saving credentials.json (IO): {e.Message}");
        }
        catch (UnauthorizedAccessException e)
        {
            Console.WriteLine($"Error saving credentials.json (Permissions): {e.Message}");
        }
    }

    private static void LoadFailedApps()
    {
        string path = "blacklist.json";
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var list = System.Text.Json.JsonSerializer.Deserialize<HashSet<uint>>(json, JsonOptions);
                if (list != null)
                {
                    previouslyFailedAppIds = list;
                }
            }
            catch (IOException) { }
            catch (System.Text.Json.JsonException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void SaveFailedApp()
    {
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(previouslyFailedAppIds, JsonOptions);
            File.WriteAllText("blacklist.json", json);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("This program will login to your Steam account and remove all Complimentary (free) package licenses.");
        Console.WriteLine("Apps covered by other license types (purchased, gifted, etc.) will be protected and skipped.");
        Console.WriteLine();
        Console.ResetColor();

        LoadCredentials();
        LoadFailedApps();

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            ReadCredentialsAgain();
            SaveCredentials(user, pass, accessToken);
        }

        InitializeSteamKit();

        while (Console.KeyAvailable)
        {
            Console.ReadKey(true);
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    private static void ReadCredentialsAgain()
    {
        do
        {
            Console.Write("Enter your Steam username: ");
            user = ReadUserInput(true);
        } while (string.IsNullOrEmpty(user));

        do
        {
            Console.Write("Enter your Steam password: ");
            pass = ReadUserInput();
        } while (string.IsNullOrEmpty(pass));
    }

    private static void InitializeSteamKit()
    {
        steamClient = new SteamClient();
        manager = new CallbackManager(steamClient);

        steamUser = steamClient.GetHandler<SteamUser>();
        steamApps = steamClient.GetHandler<SteamApps>();
        steamUnifiedMessages = steamClient.GetHandler<SteamUnifiedMessages>();
        userAccountService = steamUnifiedMessages.CreateService<UserAccount>();

        manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        manager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);
        isRunning = true;

        Console.WriteLine("Connecting to Steam...");

        steamClient.Connect();

        while (isRunning)
        {
            manager.RunWaitAllCallbacks(TimeSpan.FromSeconds(5));
        }
    }

    private static string ReadUserInput(bool showFirstChar = false)
    {
        var password = string.Empty;
        var info = Console.ReadKey(true);

        while (info.Key != ConsoleKey.Enter && info.Key != ConsoleKey.Tab)
        {
            if (info.Key != ConsoleKey.Backspace && info.KeyChar != 0)
            {
                if (showFirstChar && password.Length == 0)
                {
                    Console.Write(info.KeyChar.ToString());
                }
                else
                {
                    Console.Write("*");
                }

                password += info.KeyChar;
            }
            else if (info.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password = password[..^1];
                var pos = Console.CursorLeft;
                Console.SetCursorPosition(pos - 1, Console.CursorTop);
                Console.Write(" ");
                Console.SetCursorPosition(pos - 1, Console.CursorTop);
            }

            info = Console.ReadKey(true);
        }

        Console.WriteLine();

        return password;
    }

    private static async void OnConnected(SteamClient.ConnectedCallback callback)
    {
        Console.WriteLine("Connected to Steam! Logging in...");
        try
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                var authSession = await steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
                {
                    Username = user,
                    Password = pass,
                    DeviceFriendlyName = nameof(SteamFreeLicenseRemover),
                    Authenticator = new UserConsoleAuthenticator(),
                }).ConfigureAwait(false);
                var pollResponse = await authSession.PollingWaitForResultAsync().ConfigureAwait(false);

                user = pollResponse.AccountName;
                accessToken = pollResponse.RefreshToken;
                SaveCredentials(user, pass, accessToken);
            }

            steamUser.LogOn(new SteamUser.LogOnDetails
            {
                LoginID = 3939934623,
                Username = user,
                AccessToken = accessToken,
                ShouldRememberPassword = false,
            });
        }
        catch (AuthenticationException e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(e.Result == EResult.InvalidPassword
                ? "You have entered an invalid username or password."
                : $"Authentication failed: {e.Result}");
            Console.ResetColor();

            accessToken = null;
            ReadCredentialsAgain();
            SaveCredentials(user, pass, accessToken);
        }
#pragma warning disable CA1031
        catch (Exception e)
#pragma warning restore CA1031
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Steam connection interrupted during login: {e.Message}");
            Console.ResetColor();
        }
    }

    private static async void OnDisconnected(SteamClient.DisconnectedCallback callback)
    {
        if (isExiting)
        {
            isRunning = false;
            Console.WriteLine("Exiting...");

            return;
        }

        Console.WriteLine($"Disconnected from Steam, reconnecting in {reconnectDelaySeconds} seconds...");

        await Task.Delay(TimeSpan.FromSeconds(reconnectDelaySeconds)).ConfigureAwait(false);
        
        reconnectDelaySeconds = 5;

        steamClient.Connect();
    }

    private static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result != EResult.OK)
        {
            if (callback.Result is EResult.ServiceUnavailable or EResult.TryAnotherCM)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Steam is currently having issues ({callback.Result})...");
                Console.ResetColor();
            }
            else if (callback.Result is EResult.InvalidPassword or EResult.InvalidSignature or EResult.AccessDenied or EResult.Expired or EResult.Revoked)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Login failed ({callback.Result}). Token might be expired or access denied. Clearing token and reconnecting...");
                Console.ResetColor();

                accessToken = null;
                SaveCredentials(user, pass, accessToken);
                
                steamClient.Disconnect();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Unable to logon to Steam: {callback.Result} ({callback.ExtendedResult})");
                Console.ResetColor();

                isRunning = false;
            }

            return;
        }

        Console.WriteLine("Logged on, waiting for licenses...");
    }

    private static void OnLicenseList(SteamApps.LicenseListCallback licenseList)
    {
        var currentLicenses = new Dictionary<uint, (EPaymentMethod PaymentMethod, ELicenseType LicenseType)>();
        foreach (var license in licenseList.LicenseList)
        {
            currentLicenses.TryAdd(license.PackageID, (license.PaymentMethod, license.LicenseType));
        }

        if (!isFirstLicenseList)
        {
            var addedIds = currentLicenses.Keys.Except(previousLicenses.Keys).ToList();
            var removedIds = previousLicenses.Keys.Except(currentLicenses.Keys).ToList();

            if (addedIds.Count > 0 || removedIds.Count > 0)
            {
                foreach (var packageId in addedIds)
                {
                    var (paymentMethod, licenseType) = currentLicenses[packageId];
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"LICENSE ADDED: PackageID {packageId} (PaymentMethod: {paymentMethod}, LicenseType: {licenseType})");
                    Console.ResetColor();
                }

                foreach (var packageId in removedIds)
                {
                    var (paymentMethod, licenseType) = previousLicenses[packageId];
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"LICENSE REMOVED: PackageID {packageId} (PaymentMethod: {paymentMethod}, LicenseType: {licenseType})");
                    Console.ResetColor();
                }
            }

            previousLicenses = currentLicenses;
            return;
        }

        isFirstLicenseList = false;
        previousLicenses = currentLicenses;
        Task.Run(async () =>
        {
            bool finished = false;

            try
            {
                await ProcessLicenses(licenseList).ConfigureAwait(false);
                finished = true;
            }
            catch (TaskCanceledException)
            {
                await Console.Error.WriteLineAsync().ConfigureAwait(false);
                Console.ForegroundColor = ConsoleColor.Yellow;
                await Console.Error.WriteLineAsync("Network timeout / Rate limit reached. Sleeping for 30 minutes before next attempt...").ConfigureAwait(false);
                Console.ResetColor();
                
                reconnectDelaySeconds = 1800;
                isFirstLicenseList = true; 
            }
#pragma warning disable CA1031
            catch (Exception e)
#pragma warning restore CA1031
            {
                await Console.Error.WriteLineAsync().ConfigureAwait(false);
                Console.ForegroundColor = ConsoleColor.Red;
                await Console.Error.WriteLineAsync(e.ToString()).ConfigureAwait(false);
                Console.ResetColor();
                
                finished = true;
            }

            if (finished)
            {
                isExiting = true;
            }
            
            steamUser.LogOff();
        });
    }

    private static async Task ProcessLicenses(SteamApps.LicenseListCallback licenseList)
    {
        var complimentaryPackageIds = new HashSet<uint>();
        var packages = new List<SteamApps.PICSRequest>();

        foreach (var license in licenseList.LicenseList)
        {
            if ((license.LicenseFlags & ELicenseFlags.Borrowed) != 0)
            {
                continue;
            }

            packages.Add(new SteamApps.PICSRequest(license.PackageID, license.AccessToken));
            if (license.PaymentMethod == EPaymentMethod.Complimentary && license.LicenseType == ELicenseType.SinglePurchase)
            {
                complimentaryPackageIds.Add(license.PackageID);
            }
        }

        Console.WriteLine($"Total licenses: {licenseList.LicenseList.Count}");
        Console.WriteLine($"Complimentary licenses: {complimentaryPackageIds.Count}");
        Console.WriteLine($"Non-complimentary licenses: {packages.Count - complimentaryPackageIds.Count}");
        Console.WriteLine($"Known bad AppIDs (Blacklist): {previouslyFailedAppIds.Count}");

        if (complimentaryPackageIds.Count == 0)
        {
            Console.WriteLine("No complimentary licenses to remove.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Fetching package info...");

        var (complimentaryApps, protectedAppIds) = await RequestPackageInfo(packages, complimentaryPackageIds).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"Apps in complimentary packages: {complimentaryApps.Values.SelectMany(x => x).Distinct().Count()}");
        Console.WriteLine($"Protected apps (covered by paid licenses): {protectedAppIds.Count}");

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("Are you sure you want to remove complimentary licenses? Type 'yes' to confirm (Auto-start 'yes' in 60 seconds): ");
        Console.ResetColor();

        string confirmation = "yes";
        var inputTask = Task.Run(() => Console.ReadLine());
        var delayTask = Task.Delay(TimeSpan.FromSeconds(60));

        var completedTask = await Task.WhenAny(inputTask, delayTask).ConfigureAwait(false);

        if (completedTask == inputTask)
        {
            confirmation = (await inputTask.ConfigureAwait(false))?.Trim() ?? "yes";
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("No input detected. Automatically proceeding with 'yes' after 60 seconds timeout.");
        }

        if (!string.Equals(confirmation, "yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Operation cancelled.");
            return;
        }

        Console.WriteLine();
        await RemoveComplimentaryLicenses(complimentaryApps, protectedAppIds).ConfigureAwait(false);
    }

    private static async Task<(Dictionary<uint, HashSet<uint>> ComplimentaryApps, HashSet<uint> ProtectedAppIds)> RequestPackageInfo(
        IReadOnlyCollection<SteamApps.PICSRequest> subInfoRequests,
        HashSet<uint> complimentaryPackageIds)
    {
        var complimentaryApps = new Dictionary<uint, HashSet<uint>>();
        var protectedAppIds = new HashSet<uint>();

        foreach (var chunk in subInfoRequests.Chunk(ItemsPerRequest))
        {
            var info = await steamApps.PICSGetProductInfo([], chunk);
            if (info.Results == null)
            {
                continue;
            }

            foreach (var result in info.Results)
            {
                foreach (var package in result.Packages.Values)
                {
                    var appIds = new HashSet<uint>();
                    foreach (var id in package.KeyValues["appids"].Children)
                    {
                        appIds.Add(id.AsUnsignedInteger());
                    }

                    if (!complimentaryPackageIds.Contains(package.ID))
                    {
                        foreach (var appId in appIds)
                        {
                            if (protectedAppIds.Add(appId))
                            {
                                Console.WriteLine($"AppID {appId} protected: paid package {package.ID}");
                            }
                        }

                        continue;
                    }

                    var status = (EPackageStatus)package.KeyValues["status"].AsInteger();
                    if (status != EPackageStatus.Available)
                    {
                        foreach (var appId in appIds)
                        {
                            if (protectedAppIds.Add(appId))
                            {
                                Console.WriteLine($"AppID {appId} protected: package {package.ID} status is {status}");
                            }
                        }

                        continue;
                    }

                    var licenseType = (ELicenseType)package.KeyValues["licensetype"].AsInteger();
                    if (licenseType != ELicenseType.SinglePurchase)
                    {
                        Console.WriteLine($"PackageID {package.ID} skipped: license type is {licenseType}");
                        continue;
                    }

                    var billingType = (EBillingType)package.KeyValues["billingtype"].AsInteger();
                    if (billingType != EBillingType.FreeOnDemand)
                    {
                        foreach (var appId in appIds)
                        {
                            if (protectedAppIds.Add(appId))
                            {
                                Console.WriteLine($"AppID {appId} protected: package {package.ID} billing type is {billingType}");
                            }
                        }

                        continue;
                    }

                    complimentaryApps[package.ID] = appIds;
                }
            }
        }

        return (complimentaryApps, protectedAppIds);
    }

    private static async Task<EResult> RemoveLicenseForApp(uint appId)
    {
        var request = new CUserAccount_CancelLicenseForApp_Request { appid = appId };
        var response = await userAccountService.CancelLicenseForApp(request);
        return response.Result;
    }

    private static async Task RemoveComplimentaryLicenses(Dictionary<uint, HashSet<uint>> complimentaryApps, HashSet<uint> protectedAppIds)
    {
        using var logWriter = new StreamWriter($"RemovedLicenses_{steamUser.SteamID.AccountID}.log", append: true) { AutoFlush = true };
        await logWriter.WriteLineAsync($"--- Session started at {DateTime.UtcNow:O} ---").ConfigureAwait(false);

        var allAppIds = complimentaryApps.Values.SelectMany(x => x).Distinct().ToList();
        var totalApps = allAppIds.Count;
        var currentApp = 0;

        var processedAppIds = new HashSet<uint>();
        var removedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;

        foreach (var (packageId, appIds) in complimentaryApps)
        {
            foreach (var appId in appIds)
            {
                if (!processedAppIds.Add(appId))
                {
                    continue;
                }

                currentApp++;
                var progress = $"[{currentApp}/{totalApps}]";

                if (protectedAppIds.Contains(appId))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{progress} APP SKIPPED: {appId} is covered by a paid license");
                    Console.ResetColor();
                    await logWriter.WriteLineAsync($"SKIP: AppID {appId} (PackageID {packageId}) - covered by a paid license").ConfigureAwait(false);
                    skippedCount++;
                    continue;
                }

                if (previouslyFailedAppIds.Contains(appId))
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"{progress} APP SKIPPED: {appId} (Previously failed)");
                    Console.ResetColor();
                    await logWriter.WriteLineAsync($"SKIP: AppID {appId} (PackageID {packageId}) - previously failed").ConfigureAwait(false);
                    skippedCount++;
                    continue;
                }

                var result = await RemoveLicenseForApp(appId).ConfigureAwait(false);
                if (result == EResult.OK)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"{progress} APP REMOVED: {appId}");
                    Console.ResetColor();
                    await logWriter.WriteLineAsync($"REMOVED: AppID {appId} (PackageID {packageId})").ConfigureAwait(false);
                    removedCount++;
                }
                else
                {
                    string errorReason = result == EResult.InvalidParam
                        ? "InvalidParam (Game is tied to a package / cannot be removed individually)"
                        : result.ToString();

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"{progress} APP FAILED: {appId} - {errorReason}");
                    Console.ResetColor();
                    await logWriter.WriteLineAsync($"FAILED: AppID {appId} (PackageID {packageId}) - {errorReason}").ConfigureAwait(false);
                    failedCount++;

                    if (previouslyFailedAppIds.Add(appId))
                    {
                        SaveFailedApp();
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"   -> AppID {appId} added to blacklist.json");
                        Console.ResetColor();
                    }
                }
            }
        }

        await logWriter.WriteLineAsync($"--- Summary: Removed {removedCount}, Skipped {skippedCount}, Failed {failedCount} ---").ConfigureAwait(false);
        await logWriter.WriteLineAsync().ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"Summary: Removed {removedCount}, Skipped {skippedCount}, Failed {failedCount}");
    }
}
