using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Events;
using System.Collections.Concurrent;
using System.Text.Json;

namespace 背水平台;

public class 背水平台 : BasePlugin
{
    public override string ModuleName => "背水平台";
    public override string ModuleVersion => "4.2.1"; // 版本号递增以区分
    public override string ModuleAuthor => "优化版";
    public override string ModuleDescription => "允许玩家掉落/分享刀具给队友";

    // 记录本回合已经发过刀的玩家（用于每回合一次限制）
    private HashSet<int> _usedThisRound = new();
    private ConcurrentDictionary<int, DateTime> _cooldowns = new();

    // 缓存游戏规则对象（短暂缓存，减少查询）
    private CCSGameRules? _cachedGameRules;
    private DateTime _lastRulesCacheTime = DateTime.MinValue;

    private PluginConfig _config = new();
    private string _configPath = string.Empty;

    public override void Load(bool hotReload)
    {
        base.Load(hotReload);

        _configPath = Path.Combine(ModuleDirectory, "config.json");
        Console.WriteLine($"[背水平台] 插件加载中... 路径: {_configPath}");

        LoadConfig();

        // 注册必要的事件
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
        RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Pre);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Pre);

        // 启用服务器掉刀功能（地图切换时也会再次执行）
        Server.ExecuteCommand("mp_drop_knife_enable 1");

        Console.WriteLine($"[背水平台] 插件已加载 v{ModuleVersion}");
    }

    public override void Unload(bool hotReload)
    {
        _usedThisRound.Clear();
        _cooldowns.Clear();
        Console.WriteLine("[背水平台] 插件已卸载");
        base.Unload(hotReload);
    }

    private void LoadConfig()
    {
        try
        {
            if (!Directory.Exists(ModuleDirectory))
            {
                Directory.CreateDirectory(ModuleDirectory);
            }

            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<PluginConfig>(json) ?? new PluginConfig();
                Console.WriteLine($"[背水平台] 配置加载成功");
            }
            else
            {
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[背水平台] 加载配置时出错: {ex.Message}");
            _config = new PluginConfig();
        }
    }

    private void SaveConfig()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(_configPath, json);
            Console.WriteLine($"[背水平台] 配置已保存");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[背水平台] 保存配置时出错: {ex.Message}");
        }
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // 重置本回合已发刀记录
        _usedThisRound.Clear();
        _cachedGameRules = null;

        // 再次确保掉刀功能启用（防止地图 cfg 覆盖）
        Server.ExecuteCommand("mp_drop_knife_enable 1");

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        // 清理过期的冷却记录
        var now = DateTime.Now;
        var expired = _cooldowns.Where(kvp => (now - kvp.Value).TotalSeconds > _config.CooldownSeconds)
                                 .Select(kvp => kvp.Key)
                                 .ToList();
        foreach (var key in expired)
        {
            _cooldowns.TryRemove(key, out _);
        }

        return HookResult.Continue;
    }

    public void OnMapStartHandler(string map)
    {
        // 重置所有运行时状态
        _usedThisRound.Clear();
        _cooldowns.Clear();
        _cachedGameRules = null;

        // 确保掉刀功能启用
        Server.ExecuteCommand("mp_drop_knife_enable 1");

        Console.WriteLine($"[背水平台] 地图 {map} 已开始");
    }

    [ConsoleCommand("css_drop", "掉落刀具给队友")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnDropCommand(CCSPlayerController player, CommandInfo commandInfo)
    {
        DropKnife(player);
    }

    [ConsoleCommand("css_takeknife", "掉落刀具给队友")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnTakeKnifeCommand(CCSPlayerController player, CommandInfo commandInfo)
    {
        DropKnife(player);
    }

    [ConsoleCommand("css_dropadmin", "管理员强制掉落刀具")]
    [CommandHelper(minArgs: 1, usage: "<玩家名或#userid>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnAdminDropCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand("用法: css_dropadmin <玩家名或#userid>");
            return;
        }

        var targetName = command.GetArg(1);
        CCSPlayerController? target = null;

        if (targetName.StartsWith("#") && int.TryParse(targetName[1..], out int userId))
        {
            target = Utilities.GetPlayerFromUserid(userId);
        }
        else
        {
            target = Utilities.GetPlayers()
                .FirstOrDefault(p => p.PlayerName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        }

        if (target == null || !target.IsValid)
        {
            command.ReplyToCommand($"找不到玩家: {targetName}");
            return;
        }

        ForceDropKnife(target);
        command.ReplyToCommand($"已强制 {target.PlayerName} 发刀！");
    }

    [ConsoleCommand("css_dropcfg", "配置插件")]
    [CommandHelper(minArgs: 2, usage: "<设置名> <值>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnConfigCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.ArgCount < 3)
        {
            command.ReplyToCommand("可用设置: freezetime, directsend, onceperround, chatfilter, cooldown, maxknives");
            return;
        }

        var setting = command.GetArg(1).ToLower();
        var value = command.GetArg(2);

        bool success = false;
        string message = "";

        switch (setting)
        {
            case "freezetime":
                if (bool.TryParse(value, out bool freezeTime))
                {
                    _config.FreezeTimeOnly = freezeTime;
                    success = true;
                    message = $"冻结时间限制: {freezeTime}";
                }
                break;

            case "directsend":
                if (bool.TryParse(value, out bool directSend))
                {
                    _config.DirectSend = directSend;
                    success = true;
                    message = $"直接传送: {directSend}";
                }
                break;

            case "onceperround":
                if (bool.TryParse(value, out bool oncePerRound))
                {
                    _config.OncePerRound = oncePerRound;
                    success = true;
                    message = $"每回合一次: {oncePerRound}";
                }
                break;

            case "chatfilter":
                if (bool.TryParse(value, out bool chatFilter))
                {
                    _config.ChatFiltering = chatFilter;
                    success = true;
                    message = $"聊天过滤: {chatFilter}";
                }
                break;

            case "cooldown":
                if (int.TryParse(value, out int cooldown) && cooldown >= 0)
                {
                    _config.CooldownSeconds = cooldown;
                    success = true;
                    message = $"冷却时间: {cooldown}秒";
                }
                break;

            case "maxknives":
                if (int.TryParse(value, out int maxKnives) && maxKnives > 0)
                {
                    _config.MaxKnivesPerDrop = maxKnives;
                    success = true;
                    message = $"最大刀具数: {maxKnives}";
                }
                break;

            default:
                message = "未知设置";
                break;
        }

        if (success)
        {
            SaveConfig();
            command.ReplyToCommand($"\x04[背水平台]\x01 {message}");
        }
        else
        {
            command.ReplyToCommand($"\x04[背水平台]\x01 无效值: {message}");
        }
    }

    private void DropKnife(CCSPlayerController player)
    {
        if (!IsValidPlayer(player) || !player.PawnIsAlive)
            return;

        // 每回合一次限制检查
        if (_config.OncePerRound && _usedThisRound.Contains(player.Slot))
        {
            PrintToPlayer(player, "你本回合已经发过刀了！");
            return;
        }

        // 冷却时间检查
        if (_config.CooldownSeconds > 0 && _cooldowns.TryGetValue(player.Slot, out var lastDrop))
        {
            var cooldownLeft = _config.CooldownSeconds - (DateTime.Now - lastDrop).TotalSeconds;
            if (cooldownLeft > 0)
            {
                PrintToPlayer(player, $"请等待 {Math.Ceiling(cooldownLeft)} 秒后再使用！");
                return;
            }
        }

        // 冻结时间限制检查
        if (_config.FreezeTimeOnly)
        {
            var gameRules = GetGameRules();
            if (gameRules != null && !gameRules.FreezePeriod)
            {
                PrintToPlayer(player, "只能在冻结时间使用发刀功能！");
                return;
            }
        }

        // 获取所有活着的队友（实时查询，不依赖缓存）
        var teammates = GetTeammates(player);
        if (teammates.Count == 0)
        {
            PrintToPlayer(player, "没有可发刀的队友！");
            return;
        }

        // 限制单次发刀数量
        if (_config.MaxKnivesPerDrop > 0 && teammates.Count > _config.MaxKnivesPerDrop)
        {
            teammates = teammates.Take(_config.MaxKnivesPerDrop).ToList();
        }

        // 查找玩家当前刀具
        var knifeInfo = GetPlayerKnife(player);
        if (knifeInfo.KnifePointer == IntPtr.Zero || string.IsNullOrEmpty(knifeInfo.DesignerName))
        {
            PrintToPlayer(player, "你没有可掉落的刀！");
            return;
        }

        // 为每个队友生成刀具
        int successfulDrops = 0;
        foreach (var teammate in teammates)
        {
            if (DropKnifeForTeammate(player, teammate, knifeInfo.DesignerName))
                successfulDrops++;
        }

        if (successfulDrops > 0)
        {
            PrintToPlayer(player, $"已为 {successfulDrops} 名队友发刀！");

            // 标记本回合已使用（仅在每回合一次限制启用时）
            if (_config.OncePerRound)
                _usedThisRound.Add(player.Slot);

            // 记录冷却时间
            if (_config.CooldownSeconds > 0)
                _cooldowns[player.Slot] = DateTime.Now;
        }
    }

    private void ForceDropKnife(CCSPlayerController player)
    {
        if (!IsValidPlayer(player) || !player.PawnIsAlive)
            return;

        var teammates = GetTeammates(player);
        var knifeInfo = GetPlayerKnife(player);

        if (knifeInfo.KnifePointer == IntPtr.Zero)
            return;

        int successfulDrops = 0;
        foreach (var teammate in teammates)
        {
            if (DropKnifeForTeammate(player, teammate, knifeInfo.DesignerName))
                successfulDrops++;
        }

        Console.WriteLine($"[背水平台] 管理员强制掉落: {player.PlayerName} 为 {successfulDrops} 名队友发刀");
    }

    private (IntPtr KnifePointer, string DesignerName) GetPlayerKnife(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null)
            return (IntPtr.Zero, "");

        // 先检查当前激活武器
        var activeWeapon = pawn.WeaponServices.ActiveWeapon.Value;
        if (activeWeapon != null && IsKnifeWeapon(activeWeapon.DesignerName))
        {
            return ((IntPtr)activeWeapon.Index, activeWeapon.DesignerName);
        }

        // 再遍历所有武器
        var weapons = pawn.WeaponServices.MyWeapons;
        if (weapons == null)
            return (IntPtr.Zero, "");

        foreach (var weaponHandle in weapons)
        {
            if (weaponHandle.IsValid && weaponHandle.Value != null)
            {
                var weapon = weaponHandle.Value;
                if (IsKnifeWeapon(weapon.DesignerName))
                {
                    return ((IntPtr)weapon.Index, weapon.DesignerName);
                }
            }
        }

        return (IntPtr.Zero, "");
    }

    private bool DropKnifeForTeammate(CCSPlayerController fromPlayer, CCSPlayerController teammate, string knifeDesignerName)
    {
        if (!IsValidPlayer(teammate) || !teammate.PawnIsAlive)
            return false;

        try
        {
            IntPtr knifePointer = fromPlayer.GiveNamedItem(knifeDesignerName);

            if (knifePointer != IntPtr.Zero && _config.DirectSend)
            {
                var knife = new CBasePlayerWeapon(knifePointer);
                if (knife.IsValid && teammate.Pawn?.Value != null)
                {
                    knife.Teleport(teammate.Pawn.Value.AbsOrigin);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[背水平台] 掉落刀具时出错: {ex.Message}");
            return false;
        }
    }

    private List<CCSPlayerController> GetTeammates(CCSPlayerController player)
    {
        var teammates = new List<CCSPlayerController>();
        var allPlayers = Utilities.GetPlayers();

        foreach (var p in allPlayers)
        {
            if (!IsValidPlayer(p) || !p.PawnIsAlive)
                continue;
            if (p.Slot == player.Slot)
                continue;
            if (p.Team == player.Team)
                teammates.Add(p);
        }

        return teammates;
    }

    private CCSGameRules? GetGameRules()
    {
        if (_cachedGameRules != null && (DateTime.Now - _lastRulesCacheTime).TotalSeconds < 5)
            return _cachedGameRules;

        var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        _cachedGameRules = proxy?.GameRules;
        _lastRulesCacheTime = DateTime.Now;

        return _cachedGameRules;
    }

    private bool IsValidPlayer(CCSPlayerController? player)
    {
        return player != null &&
               player.IsValid &&
               !player.IsHLTV &&
               !player.IsBot &&
               player.PlayerPawn != null &&
               player.PlayerPawn.IsValid &&
               player.PlayerPawn.Value != null;
    }

    private bool IsKnifeWeapon(string designerName)
    {
        if (string.IsNullOrEmpty(designerName))
            return false;

        string lowerName = designerName.ToLower();

        return lowerName.Contains("knife") ||
               lowerName.Contains("bayonet") ||
               lowerName.Contains("karambit") ||
               lowerName.Contains("m9_bayonet") ||
               lowerName.Contains("butterfly");
    }

    private void PrintToPlayer(CCSPlayerController player, string message)
    {
        player.PrintToChat($"[\x04背水平台\x01] {message}");
    }

    [GameEventHandler]
    public HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        if (!_config.ChatFiltering)
            return HookResult.Continue;

        try
        {
            var playerController = @event.Userid;
            if (playerController <= 0)
                return HookResult.Continue;

            var player = Utilities.GetPlayerFromUserid(playerController);
            if (!IsValidPlayer(player))
                return HookResult.Continue;

            string message = @event.Text?.ToLower() ?? "";

            if (message.StartsWith("!drop") || message.StartsWith("/drop") || message.StartsWith(".drop") ||
                message == "!d" || message == "/d" || message == ".d" ||
                message.StartsWith("!takeknife") || message.StartsWith("/takeknife") || message.StartsWith(".takeknife"))
            {
                DropKnife(player);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[背水平台] 处理聊天事件时出错: {ex.Message}");
        }

        return HookResult.Continue;
    }
}

public class PluginConfig
{
    public bool FreezeTimeOnly { get; set; } = true;
    public bool DirectSend { get; set; } = true;
    public bool OncePerRound { get; set; } = true;
    public bool ChatFiltering { get; set; } = true;
    public int CooldownSeconds { get; set; } = 10;
    public int MaxKnivesPerDrop { get; set; } = 10;
}