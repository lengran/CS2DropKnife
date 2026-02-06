using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Events;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CS2DropKnife;

public class CS2DropKnife : BasePlugin
{
    public override string ModuleName => "CS2DropKnife";
    public override string ModuleVersion => "4.2.0";
    public override string ModuleAuthor => "优化版";
    public override string ModuleDescription => "允许玩家掉落/分享刀具给队友";

    // 使用 HashSet 提高查找效率
    private HashSet<int> _allowedPlayerSlots = new();
    private ConcurrentDictionary<int, DateTime> _cooldowns = new();
    
    // 缓存阵营玩家列表
    private HashSet<int> _ctPlayers = new();
    private HashSet<int> _tPlayers = new();
    
    // 缓存游戏规则对象
    private CCSGameRules? _cachedGameRules;
    private DateTime _lastRulesCacheTime = DateTime.MinValue;
    
    private PluginConfig _config = new();
    private string _configPath = string.Empty;

    public override void Load(bool hotReload)
    {
        base.Load(hotReload);

        // 初始化配置路径
        _configPath = Path.Combine(ModuleDirectory, "config.json");
        
        Console.WriteLine($"[CS2DropKnife] 插件加载中... 路径: {_configPath}");
        
        // 加载配置
        LoadConfig();
        
        // 注册事件监听器
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
        RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Pre);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Pre);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull, HookMode.Pre);
        
        // 启用服务器掉刀功能
        Server.ExecuteCommand("mp_drop_knife_enable 1");
        
        Console.WriteLine($"[CS2DropKnife] 插件已加载 v{ModuleVersion}");
    }

    public override void Unload(bool hotReload)
    {
        // 清理数据
        _allowedPlayerSlots.Clear();
        _ctPlayers.Clear();
        _tPlayers.Clear();
        _cooldowns.Clear();
        
        Console.WriteLine("[CS2DropKnife] 插件已卸载");
        base.Unload(hotReload);
    }

    private void LoadConfig()
    {
        try
        {
            // 确保目录存在
            if (!Directory.Exists(ModuleDirectory))
            {
                Console.WriteLine($"[CS2DropKnife] 创建插件目录: {ModuleDirectory}");
                Directory.CreateDirectory(ModuleDirectory);
            }

            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<PluginConfig>(json) ?? new PluginConfig();
                Console.WriteLine($"[CS2DropKnife] 配置加载成功: {_configPath}");
            }
            else
            {
                Console.WriteLine($"[CS2DropKnife] 配置文件不存在，创建默认配置: {_configPath}");
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CS2DropKnife] 加载配置时出错: {ex.Message}");
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
            Console.WriteLine($"[CS2DropKnife] 配置已保存: {_configPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CS2DropKnife] 保存配置时出错: {ex.Message}");
        }
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // 清除上回合数据
        _allowedPlayerSlots.Clear();
        _ctPlayers.Clear();
        _tPlayers.Clear();
        _cachedGameRules = null;
        
        // 收集当前所有玩家
        var players = Utilities.GetPlayers();
        foreach (var player in players)
        {
            if (!IsValidPlayer(player))
                continue;

            _allowedPlayerSlots.Add(player.Slot);
            
            // 按阵营分组
            if (player.Team == CsTeam.CounterTerrorist)
                _ctPlayers.Add(player.Slot);
            else if (player.Team == CsTeam.Terrorist)
                _tPlayers.Add(player.Slot);
        }
        
        Console.WriteLine($"[CS2DropKnife] 回合开始: {_allowedPlayerSlots.Count} 名玩家已注册");
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        // 清理冷却时间过期的记录
        var now = DateTime.Now;
        var expiredKeys = new List<int>();
        
        foreach (var kvp in _cooldowns)
        {
            if ((now - kvp.Value).TotalSeconds > _config.CooldownSeconds)
            {
                expiredKeys.Add(kvp.Key);
            }
        }
        
        foreach (var key in expiredKeys)
        {
            _cooldowns.TryRemove(key, out _);
        }
        
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        
        if (!IsValidPlayer(player))
            return HookResult.Continue;

        // 新玩家添加到允许列表
        _allowedPlayerSlots.Add(player.Slot);
        
        // 根据实际阵营添加
        if (player.Team == CsTeam.CounterTerrorist)
            _ctPlayers.Add(player.Slot);
        else if (player.Team == CsTeam.Terrorist)
            _tPlayers.Add(player.Slot);
        
        return HookResult.Continue;
    }

    public void OnMapStartHandler(string map)
    {
        // 重置所有数据
        _allowedPlayerSlots.Clear();
        _ctPlayers.Clear();
        _tPlayers.Clear();
        _cooldowns.Clear();
        _cachedGameRules = null;
        
        // 确保掉刀功能启用
        Server.ExecuteCommand("mp_drop_knife_enable 1");
        
        Console.WriteLine($"[CS2DropKnife] 地图 {map} 已开始");
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

        // 尝试通过 userid 查找
        if (targetName.StartsWith("#") && int.TryParse(targetName[1..], out int userId))
        {
            target = Utilities.GetPlayerFromUserid(userId);
        }
        else
        {
            // 通过名称查找
            target = Utilities.GetPlayers()
                .FirstOrDefault(p => p.PlayerName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        }

        if (target == null || !target.IsValid)
        {
            command.ReplyToCommand($"找不到玩家: {targetName}");
            return;
        }

        ForceDropKnife(target);
        command.ReplyToCommand($"已强制 {target.PlayerName} 掉落刀具");
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
            // 使用绿色前缀回复管理员
            command.ReplyToCommand($"\x04[CS2DropKnife]\x01 {message}");
        }
        else
        {
            command.ReplyToCommand($"\x04[CS2DropKnife]\x01 无效值: {message}");
        }
    }

    private void DropKnife(CCSPlayerController player)
    {
        if (!IsValidPlayer(player) || !player.PawnIsAlive)
            return;

        // 检查玩家是否允许掉刀
        if (_config.OncePerRound && !_allowedPlayerSlots.Contains(player.Slot))
        {
            PrintToPlayer(player, "你本回合已经使用过掉刀功能了！");
            return;
        }

        // 检查冷却时间
        if (_config.CooldownSeconds > 0 && _cooldowns.TryGetValue(player.Slot, out var lastDrop))
        {
            var cooldownLeft = _config.CooldownSeconds - (DateTime.Now - lastDrop).TotalSeconds;
            if (cooldownLeft > 0)
            {
                PrintToPlayer(player, $"请等待 {Math.Ceiling(cooldownLeft)} 秒后再使用！");
                return;
            }
        }

        // 检查冻结时间限制
        if (_config.FreezeTimeOnly)
        {
            var gameRules = GetGameRules();
            if (gameRules != null && !gameRules.FreezePeriod)
            {
                PrintToPlayer(player, "只能在冻结时间使用掉刀功能！");
                return;
            }
        }

        // 获取队友
        var teammates = GetTeammates(player);
        if (teammates.Count == 0)
        {
            PrintToPlayer(player, "没有可分享的队友！");
            return;
        }

        // 限制生成数量
        if (_config.MaxKnivesPerDrop > 0 && teammates.Count > _config.MaxKnivesPerDrop)
        {
            teammates = teammates.Take(_config.MaxKnivesPerDrop).ToList();
        }

        // 查找玩家当前刀具
        var knifeInfo = GetPlayerKnife(player);
        if (knifeInfo.KnifePointer == IntPtr.Zero || string.IsNullOrEmpty(knifeInfo.DesignerName))
        {
            PrintToPlayer(player, "你没有可掉落的刀具！");
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
            PrintToPlayer(player, $"已为 {successfulDrops} 名队友掉落刀具！");
            
            // 应用限制
            if (_config.OncePerRound)
                _allowedPlayerSlots.Remove(player.Slot);
            
            // 设置冷却时间
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

        Console.WriteLine($"[CS2DropKnife] 管理员强制掉落: {player.PlayerName} 为 {successfulDrops} 名队友掉落刀具");
    }

    private (IntPtr KnifePointer, string DesignerName) GetPlayerKnife(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null)
            return (IntPtr.Zero, "");

        // 先检查活动武器
        var activeWeapon = pawn.WeaponServices.ActiveWeapon.Value;
        if (activeWeapon != null && IsKnifeWeapon(activeWeapon.DesignerName))
        {
            return ((IntPtr)activeWeapon.Index, activeWeapon.DesignerName);
        }

        // 检查所有武器
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
            // 为队友生成刀具
            IntPtr knifePointer = fromPlayer.GiveNamedItem(knifeDesignerName);
            
            if (knifePointer != IntPtr.Zero && _config.DirectSend)
            {
                // 直接传送给队友
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
            Console.WriteLine($"[CS2DropKnife] 掉落刀具时出错: {ex.Message}");
            return false;
        }
    }

    private List<CCSPlayerController> GetTeammates(CCSPlayerController player)
    {
        var teammates = new List<CCSPlayerController>();
        var teamSlots = player.Team == CsTeam.CounterTerrorist ? _ctPlayers : _tPlayers;

        foreach (var slot in teamSlots)
        {
            if (slot == player.Slot)
                continue;

            var teammate = Utilities.GetPlayerFromSlot(slot);
            if (IsValidPlayer(teammate) && teammate.PawnIsAlive)
                teammates.Add(teammate);
        }

        return teammates;
    }

    private CCSGameRules? GetGameRules()
    {
        // 缓存游戏规则对象，避免重复查找
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
        // 使用绿色前缀：\x04 表示绿色，\x01 表示恢复到默认颜色
        player.PrintToChat($"[\x04CS2DropKnife\x01] {message}");
    }

    [GameEventHandler]
    public HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        if (!_config.ChatFiltering)
            return HookResult.Continue;

        try
        {
            var playerController = @event.Userid;
            
            // 从事件中获取玩家
            if (playerController <= 0)
                return HookResult.Continue;

            var player = Utilities.GetPlayerFromUserid(playerController);
            if (!IsValidPlayer(player))
                return HookResult.Continue;

            string message = @event.Text?.ToLower() ?? "";

            // 检查聊天命令
            if (message.StartsWith("!drop") || message.StartsWith("/drop") || message.StartsWith(".drop") ||
                message == "!d" || message == "/d" || message == ".d" ||
                message.StartsWith("!takeknife") || message.StartsWith("/takeknife") || message.StartsWith(".takeknife"))
            {
                DropKnife(player);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CS2DropKnife] 处理聊天事件时出错: {ex.Message}");
        }

        return HookResult.Continue;
    }
}

// 插件配置类
public class PluginConfig
{
    public bool FreezeTimeOnly { get; set; } = true;
    public bool DirectSend { get; set; } = true;
    public bool OncePerRound { get; set; } = true;
    public bool ChatFiltering { get; set; } = true;
    public int CooldownSeconds { get; set; } = 10;
    public int MaxKnivesPerDrop { get; set; } = 10;
    
    public PluginConfig() { }
}