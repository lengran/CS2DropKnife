public override void Load(bool hotReload)
{
    base.Load(hotReload);

    // 初始化配置路径
    _configPath = Path.Combine(ModuleDirectory, "config.json");

    Console.WriteLine($"[CS2DropKnife] Plugin loading... Path: {_configPath}");

    // 加载配置
    LoadConfig();

    // 注册事件监听器
    RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
    RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Pre);
    RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Pre);
    RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull, HookMode.Pre);

    // 启用服务器掉刀功能
    Server.ExecuteCommand("mp_drop_knife_enable 1");

    Console.WriteLine($"[CS2DropKnife] Plugin loaded v{ModuleVersion}");
}

private void LoadConfig()
{
    try
    {
        // 确保目录存在
        if (!Directory.Exists(ModuleDirectory))
        {
            Console.WriteLine($"[CS2DropKnife] Creating plugin directory: {ModuleDirectory}");
            Directory.CreateDirectory(ModuleDirectory);
        }

        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            _config = JsonSerializer.Deserialize<PluginConfig>(json) ?? new PluginConfig();
            Console.WriteLine($"[CS2DropKnife] Configuration loaded successfully: {_configPath}");
        }
        else
        {
            Console.WriteLine($"[CS2DropKnife] Configuration file does not exist, creating default: {_configPath}");
            SaveConfig();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CS2DropKnife] Error loading configuration: {ex.Message}");
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
        Console.WriteLine($"[CS2DropKnife] Configuration saved: {_configPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CS2DropKnife] Error saving configuration: {ex.Message}");
    }
}

[GameEventHandler]
public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
{
    // ... 代码省略 ...
    Console.WriteLine($"[CS2DropKnife] Round started: {_allowedPlayerSlots.Count} players registered");
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

    Console.WriteLine($"[CS2DropKnife] Map {map} started");
}

[ConsoleCommand("css_dropadmin", "管理员强制掉落刀具")]
[CommandHelper(minArgs: 1, usage: "<玩家名或#userid>", whoCanExecute: CommandUsage.SERVER_ONLY)]
public void OnAdminDropCommand(CCSPlayerController? caller, CommandInfo command)
{
    if (command.ArgCount < 2)
    {
        command.ReplyToCommand("Usage: css_dropadmin <player name or #userid>");
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
        command.ReplyToCommand($"Player not found: {targetName}");
        return;
    }

    ForceDropKnife(target);
    command.ReplyToCommand($"Forced {target.PlayerName} to drop knives");
}

[ConsoleCommand("css_dropcfg", "配置插件")]
[CommandHelper(minArgs: 2, usage: "<设置名> <值>", whoCanExecute: CommandUsage.SERVER_ONLY)]
public void OnConfigCommand(CCSPlayerController? caller, CommandInfo command)
{
    if (command.ArgCount < 3)
    {
        command.ReplyToCommand("Available settings: freezetime, directsend, onceperround, chatfilter, cooldown, maxknives");
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
                message = $"Freeze time restriction: {freezeTime}";
            }
            break;

        case "directsend":
            if (bool.TryParse(value, out bool directSend))
            {
                _config.DirectSend = directSend;
                success = true;
                message = $"Direct send: {directSend}";
            }
            break;

        case "onceperround":
            if (bool.TryParse(value, out bool oncePerRound))
            {
                _config.OncePerRound = oncePerRound;
                success = true;
                message = $"Once per round: {oncePerRound}";
            }
            break;

        case "chatfilter":
            if (bool.TryParse(value, out bool chatFilter))
            {
                _config.ChatFiltering = chatFilter;
                success = true;
                message = $"Chat filtering: {chatFilter}";
            }
            break;

        case "cooldown":
            if (int.TryParse(value, out int cooldown) && cooldown >= 0)
            {
                _config.CooldownSeconds = cooldown;
                success = true;
                message = $"Cooldown: {cooldown} seconds";
            }
            break;

        case "maxknives":
            if (int.TryParse(value, out int maxKnives) && maxKnives > 0)
            {
                _config.MaxKnivesPerDrop = maxKnives;
                success = true;
                message = $"Maximum knives per drop: {maxKnives}";
            }
            break;

        default:
            message = "Unknown setting";
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
        command.ReplyToCommand($"\x04[CS2DropKnife]\x01 Invalid value: {message}");
    }
}

private void DropKnife(CCSPlayerController player)
{
    if (!IsValidPlayer(player) || !player.PawnIsAlive)
        return;

    // 检查玩家是否允许掉刀
    if (_config.OncePerRound && !_allowedPlayerSlots.Contains(player.Slot))
    {
        PrintToPlayer(player, "You have already used the knife drop feature this round!");
        return;
    }

    // 检查冷却时间
    if (_config.CooldownSeconds > 0 && _cooldowns.TryGetValue(player.Slot, out var lastDrop))
    {
        var cooldownLeft = _config.CooldownSeconds - (DateTime.Now - lastDrop).TotalSeconds;
        if (cooldownLeft > 0)
        {
            PrintToPlayer(player, $"Please wait {Math.Ceiling(cooldownLeft)} seconds before using again!");
            return;
        }
    }

    // 检查冻结时间限制
    if (_config.FreezeTimeOnly)
    {
        var gameRules = GetGameRules();
        if (gameRules != null && !gameRules.FreezePeriod)
        {
            PrintToPlayer(player, "You can only use knife drop during freeze time!");
            return;
        }
    }

    // 获取队友
    var teammates = GetTeammates(player);
    if (teammates.Count == 0)
    {
        PrintToPlayer(player, "No teammates to share with!");
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
        PrintToPlayer(player, "You don't have a knife to drop!");
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
        PrintToPlayer(player, $"Successfully dropped knives for {successfulDrops} teammates!");

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

    Console.WriteLine($"[CS2DropKnife] Admin forced drop: {player.PlayerName} dropped knives for {successfulDrops} teammates");
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
        Console.WriteLine($"[CS2DropKnife] Error processing chat event: {ex.Message}");
    }

    return HookResult.Continue;
}