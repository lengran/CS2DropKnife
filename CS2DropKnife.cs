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
    public override string ModuleName => "CS2 Drop Knife";
    public override string ModuleVersion => "4.2.3";
    public override string ModuleAuthor => "pro";
    public override string ModuleDescription => "pro";

    private HashSet<int> _usedThisRound = new();
    private ConcurrentDictionary<int, DateTime> _cooldowns = new();

    private CCSGameRules? _cachedGameRules;
    private DateTime _lastRulesCacheTime = DateTime.MinValue;

    private PluginConfig _config = new();
    private string _configPath = string.Empty;

    public override void Load(bool hotReload)
    {
        base.Load(hotReload);

        _configPath = Path.Combine(ModuleDirectory, "config.json");
        Console.WriteLine($"[CS2DropKnife] Loading plugin... Path: {_configPath}");

        LoadConfig();

        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
        RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Pre);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Pre);

        Server.ExecuteCommand("mp_drop_knife_enable 1");

        Console.WriteLine($"[CS2DropKnife] Plugin loaded v{ModuleVersion}");
    }

    public override void Unload(bool hotReload)
    {
        _usedThisRound.Clear();
        _cooldowns.Clear();
        Console.WriteLine("[CS2DropKnife] Plugin unloaded");
        base.Unload(hotReload);
    }

    private void LoadConfig()
    {
        try
        {
            if (!Directory.Exists(ModuleDirectory))
                Directory.CreateDirectory(ModuleDirectory);

            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<PluginConfig>(json) ?? new PluginConfig();
                Console.WriteLine($"[CS2DropKnife] Config loaded successfully");
            }
            else
            {
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CS2DropKnife] Error loading config: {ex.Message}");
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
            Console.WriteLine($"[CS2DropKnife] Config saved");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CS2DropKnife] Error saving config: {ex.Message}");
        }
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _usedThisRound.Clear();
        _cachedGameRules = null;
        Server.ExecuteCommand("mp_drop_knife_enable 1");
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        var now = DateTime.Now;
        var expired = _cooldowns.Where(kvp => (now - kvp.Value).TotalSeconds > _config.CooldownSeconds)
                                 .Select(kvp => kvp.Key)
                                 .ToList();
        foreach (var key in expired)
            _cooldowns.TryRemove(key, out _);

        return HookResult.Continue;
    }

    public void OnMapStartHandler(string map)
    {
        _usedThisRound.Clear();
        _cooldowns.Clear();
        _cachedGameRules = null;

        Server.NextFrame(() => {
            Server.ExecuteCommand("mp_drop_knife_enable 1");
            Console.WriteLine($"[CS2DropKnife] Ensured mp_drop_knife_enable is 1");
        });

        Console.WriteLine($"[CS2DropKnife] Map {map} started");
    }

    [ConsoleCommand("css_drop", "Drop knife to teammates")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnDropCommand(CCSPlayerController player, CommandInfo commandInfo)
    {
        DropKnife(player);
    }

    [ConsoleCommand("css_takeknife", "Drop knife to teammates")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnTakeKnifeCommand(CCSPlayerController player, CommandInfo commandInfo)
    {
        DropKnife(player);
    }

    [ConsoleCommand("css_dropadmin", "Admin force drop knife")]
    [CommandHelper(minArgs: 1, usage: "<player name or #userid>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnAdminDropCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand("Usage: css_dropadmin <player name or #userid>");
            return;
        }

        var targetName = command.GetArg(1);
        CCSPlayerController? target = null;

        if (targetName.StartsWith("#") && int.TryParse(targetName[1..], out int userId))
            target = Utilities.GetPlayerFromUserid(userId);
        else
            target = Utilities.GetPlayers()
                .FirstOrDefault(p => p.PlayerName.Equals(targetName, StringComparison.OrdinalIgnoreCase));

        if (target == null || !target.IsValid)
        {
            command.ReplyToCommand($"Player not found: {targetName}");
            return;
        }

        ForceDropKnife(target);
        command.ReplyToCommand($"Forced {target.PlayerName} to drop knife!");
    }

    [ConsoleCommand("css_dropcfg", "Configure plugin")]
    [CommandHelper(minArgs: 2, usage: "<setting> <value>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnConfigCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.ArgCount < 3)
        {
            command.ReplyToCommand("Available settings: freezetime, directsend, onceperround, chatfilter, cooldown, maxknives, enablemsg, prefixtag, successmsg, noteammessage, noknifemsg, cooldownmsg, onceperroundmsg, freezetimeonlymsg");
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
                    message = $"Freeze time only: {freezeTime}";
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
                    message = $"Cooldown seconds: {cooldown}";
                }
                break;
            case "maxknives":
                if (int.TryParse(value, out int maxKnives) && maxKnives > 0)
                {
                    _config.MaxKnivesPerDrop = maxKnives;
                    success = true;
                    message = $"Max knives per drop: {maxKnives}";
                }
                break;
            case "enablemsg":
                if (bool.TryParse(value, out bool enableMsg))
                {
                    _config.EnableDropMessage = enableMsg;
                    success = true;
                    message = $"Enable drop message: {enableMsg}";
                }
                break;
            case "prefixtag":
                _config.PrefixTag = value;
                success = true;
                message = $"Prefix tag set to: {value}";
                break;
            case "successmsg":
                _config.DropSuccessMessage = value;
                success = true;
                message = $"Success message set to: {value}";
                break;
            case "noteammessage":
                _config.NoTeammatesMessage = value;
                success = true;
                message = $"No teammate message set to: {value}";
                break;
            case "noknifemsg":
                _config.NoKnifeMessage = value;
                success = true;
                message = $"No knife message set to: {value}";
                break;
            case "cooldownmsg":
                _config.CooldownMessage = value;
                success = true;
                message = $"Cooldown message set to: {value}";
                break;
            case "onceperroundmsg":
                _config.OncePerRoundMessage = value;
                success = true;
                message = $"Once per round message set to: {value}";
                break;
            case "freezetimeonlymsg":
                _config.FreezeTimeOnlyMessage = value;
                success = true;
                message = $"Freeze time only message set to: {value}";
                break;
            default:
                message = "Unknown setting";
                break;
        }

        if (success)
        {
            SaveConfig();
            command.ReplyToCommand($"\x04[{_config.PrefixTag}]\x01 {message}");
        }
        else
        {
            command.ReplyToCommand($"\x04[{_config.PrefixTag}]\x01 Invalid value: {message}");
        }
    }

    private void DropKnife(CCSPlayerController player)
    {
        if (!IsValidPlayer(player) || !player.PawnIsAlive)
            return;

        if (_config.OncePerRound && _usedThisRound.Contains(player.Slot))
        {
            PrintToPlayer(player, _config.OncePerRoundMessage);
            return;
        }

        if (_config.CooldownSeconds > 0 && _cooldowns.TryGetValue(player.Slot, out var lastDrop))
        {
            var cooldownLeft = _config.CooldownSeconds - (DateTime.Now - lastDrop).TotalSeconds;
            if (cooldownLeft > 0)
            {
                string msg = _config.CooldownMessage.Replace("{seconds}", Math.Ceiling(cooldownLeft).ToString());
                PrintToPlayer(player, msg);
                return;
            }
        }

        if (_config.FreezeTimeOnly)
        {
            var gameRules = GetGameRules();
            if (gameRules != null && !gameRules.FreezePeriod)
            {
                PrintToPlayer(player, _config.FreezeTimeOnlyMessage);
                return;
            }
        }

        var teammates = GetTeammates(player);
        if (teammates.Count == 0)
        {
            PrintToPlayer(player, _config.NoTeammatesMessage);
            return;
        }

        if (_config.MaxKnivesPerDrop > 0 && teammates.Count > _config.MaxKnivesPerDrop)
        {
            teammates = teammates.Take(_config.MaxKnivesPerDrop).ToList();
        }

        var knifeInfo = GetPlayerKnife(player);
        if (knifeInfo.KnifePointer == IntPtr.Zero || string.IsNullOrEmpty(knifeInfo.DesignerName))
        {
            PrintToPlayer(player, _config.NoKnifeMessage);
            return;
        }

        int successfulDrops = 0;
        foreach (var teammate in teammates)
        {
            if (DropKnifeForTeammate(player, teammate, knifeInfo.DesignerName))
                successfulDrops++;
        }

        if (successfulDrops > 0)
        {
            if (_config.EnableDropMessage)
            {
                string msg = _config.DropSuccessMessage.Replace("{count}", successfulDrops.ToString());
                PrintToPlayer(player, msg);
            }

            if (_config.OncePerRound)
                _usedThisRound.Add(player.Slot);

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

        Console.WriteLine($"[CS2DropKnife] Admin forced drop: {player.PlayerName} dropped for {successfulDrops} teammates");
    }

    private (IntPtr KnifePointer, string DesignerName) GetPlayerKnife(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn?.WeaponServices == null)
            return (IntPtr.Zero, "");

        var activeWeapon = pawn.WeaponServices.ActiveWeapon.Value;
        if (activeWeapon != null && IsKnifeWeapon(activeWeapon.DesignerName))
        {
            return ((IntPtr)activeWeapon.Index, activeWeapon.DesignerName);
        }

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
            Console.WriteLine($"[CS2DropKnife] Error dropping knife: {ex.Message}");
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
        player.PrintToChat($"[\x04{_config.PrefixTag}\x01] {message}");
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
            Console.WriteLine($"[CS2DropKnife] Error processing chat event: {ex.Message}");
        }

        return HookResult.Continue;
    }
}

public class PluginConfig
{
    // Core settings
    public bool FreezeTimeOnly { get; set; } = true;
    public bool DirectSend { get; set; } = true;
    public bool OncePerRound { get; set; } = true;
    public bool ChatFiltering { get; set; } = true;
    public int CooldownSeconds { get; set; } = 10;
    public int MaxKnivesPerDrop { get; set; } = 10;

    // Message customization
    public bool EnableDropMessage { get; set; } = true;
    public string PrefixTag { get; set; } = "CS2DropKnife"; // can include color codes like \x04
    public string DropSuccessMessage { get; set; } = "Dropped knife for {count} teammate(s)!";
    public string NoTeammatesMessage { get; set; } = "No teammates to drop knife to!";
    public string NoKnifeMessage { get; set; } = "You don't have a knife to drop!";
    public string CooldownMessage { get; set; } = "Please wait {seconds} seconds before using again!";
    public string OncePerRoundMessage { get; set; } = "You have already dropped a knife this round!";
    public string FreezeTimeOnlyMessage { get; set; } = "You can only drop knives during freeze time!";
}