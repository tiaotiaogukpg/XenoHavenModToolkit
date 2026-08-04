using System.IO;
using System.Threading;
using Steamworks;

namespace XenoHavenModToolkit;

/// <summary>
/// Steam 客户端会话：Init / RunCallbacks / 账号信息 / Shutdown。
/// 身份来自本机已登录的 Steam 客户端，无账密输入。
/// </summary>
internal sealed class SteamSession : IDisposable
{
    private readonly object gate = new();
    private Thread? callbackThread;
    private volatile bool runCallbacks;
    private bool disposed;

    public bool IsAvailable { get; private set; }
    public string? FailureReason { get; private set; }
    public string? PersonaName { get; private set; }
    public ulong SteamId64 { get; private set; }
    public uint AccountId { get; private set; }
    public uint AppId { get; private set; }

    public string StatusText
    {
        get
        {
            if (IsAvailable)
            {
                return $"{PersonaName} | SteamID:{SteamId64} | AppID:{AppId}";
            }

            return string.IsNullOrWhiteSpace(FailureReason)
                ? Loc.Get("Str.Main.SteamDisconnected")
                : Loc.Format("Str.Main.SteamDisconnectedWithReason", FailureReason);
        }
    }

    public static SteamSession TryStart(uint appId)
    {
        var session = new SteamSession();
        session.Start(appId);
        return session;
    }

    private void Start(uint appId)
    {
        AppId = appId;
        var steamInitialized = false;

        try
        {
            // Steam 从进程当前目录读取 steam_appid.txt
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            EnsureSteamAppIdFile(appId);

            if (!Packsize.Test())
            {
                FailureReason = Loc.Get("Str.Steam.PacksizeFail");
                return;
            }

            if (!DllCheck.Test())
            {
                FailureReason = Loc.Get("Str.Steam.DllMissing");
                return;
            }

            if (!SteamAPI.IsSteamRunning())
            {
                FailureReason = Loc.Get("Str.Steam.ClientNotRunning");
                return;
            }

            var initResult = SteamAPI.InitEx(out var errMsg);
            if (initResult != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                FailureReason = string.IsNullOrWhiteSpace(errMsg)
                    ? Loc.Format("Str.Steam.InitFailed", initResult)
                    : errMsg.Trim();
                return;
            }

            steamInitialized = true;

            var steamId = SteamUser.GetSteamID();
            SteamId64 = steamId.m_SteamID;
            AccountId = steamId.GetAccountID().m_AccountID;
            PersonaName = SteamFriends.GetPersonaName();
            var utilsAppId = SteamUtils.GetAppID().m_AppId;
            if (utilsAppId != 0)
            {
                AppId = utilsAppId;
            }

            IsAvailable = true;
            runCallbacks = true;
            callbackThread = new Thread(CallbackLoop)
            {
                IsBackground = true,
                Name = "SteamAPI.RunCallbacks"
            };
            callbackThread.Start();
        }
        catch (Exception ex)
        {
            FailureReason = ex.Message;
            if (steamInitialized)
            {
                try
                {
                    SteamAPI.Shutdown();
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private void CallbackLoop()
    {
        while (runCallbacks)
        {
            try
            {
                SteamAPI.RunCallbacks();
            }
            catch
            {
                // 保持泵线程存活；具体错误由后续 API 调用面暴露
            }

            Thread.Sleep(100);
        }
    }

    private static void EnsureSteamAppIdFile(uint appId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
        var text = appId.ToString() + Environment.NewLine;
        File.WriteAllText(path, text);
    }

    private void SafeShutdown()
    {
        runCallbacks = false;
        var thread = callbackThread;
        callbackThread = null;
        if (thread != null && thread.IsAlive)
        {
            thread.Join(1000);
        }

        if (IsAvailable)
        {
            try
            {
                SteamAPI.Shutdown();
            }
            catch
            {
                // ignore shutdown races
            }
        }

        IsAvailable = false;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            SafeShutdown();
        }
    }
}
