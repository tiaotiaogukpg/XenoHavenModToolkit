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
                ? "Steam：未连接"
                : $"Steam：未连接（{FailureReason}）";
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
                FailureReason = "Steamworks Packsize 检测失败（运行时架构不匹配）";
                return;
            }

            if (!DllCheck.Test())
            {
                FailureReason = "未找到匹配的 steam_api64.dll";
                return;
            }

            if (!SteamAPI.IsSteamRunning())
            {
                FailureReason = "Steam 客户端未运行，请先启动并登录 Steam";
                return;
            }

            var initResult = SteamAPI.InitEx(out var errMsg);
            if (initResult != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                FailureReason = string.IsNullOrWhiteSpace(errMsg)
                    ? $"SteamAPI.Init 失败：{initResult}"
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
