using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API;
using System.Numerics;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.VisualBasic;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;

namespace CS2DropKnife;

public class CS2DropKnife : BasePlugin
{
    public override string ModuleName => "CS2 Drop Knife";

    public override string ModuleVersion => "3.0.0";

    private List<int> player_slot_ids = new List<int>();

    private List<int> ct_players = new List<int>();
    private List<int> t_players = new List<int>();

    public override void Load(bool hotReload)
    {
        base.Load(hotReload);

        Console.WriteLine("[CS2DropKnife] Registering listeners.");
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);

        if (hotReload)
        {
            Server.ExecuteCommand("mp_drop_knife_enable 1");
        }
    }

    
    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        player_slot_ids.Clear();

        foreach(var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
            {
                continue;
            }

            player_slot_ids.Add(player.Slot);

            if (player.Team == CsTeam.CounterTerrorist)
            {
                ct_players.Add(player.Slot);
            }
            if (player.Team == CsTeam.Terrorist)
            {
                t_players.Add(player.Slot);
            }
            
        }

        return HookResult.Continue;
    }


    public void OnMapStartHandler(string map)
    {
        Server.ExecuteCommand("mp_drop_knife_enable 1");
    }

    [ConsoleCommand("css_drop", "Drop 5 copies of player's knife on the ground.")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnDropCommand(CCSPlayerController player, CommandInfo commandInfo)
    {
        DropKnife(player);
    }

    [ConsoleCommand("css_takeknife", "Drop 5 copies of player's knife on the ground.")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnTakeKnifeCommand(CCSPlayerController player, CommandInfo commandInfo)
    {
        DropKnife(player);
    }


    public void DropKnife(CCSPlayerController player)
    {
        // Player might not be alive.
        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV || !player.PawnIsAlive || player.Pawn?.Value == null) // || player.PlayerPawn?.Value.WeaponServices == null || player.PlayerPawn?.Value.ItemServices == null)
        {
            return;
        }

        // It is not allowed for a single player to drop knives multiple times in a round
        if (!player_slot_ids.Contains(player.Slot))
        {
            return;
        }

        // Only allow dropping knife at freeze time
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")?.FirstOrDefault()?.GameRules;
        if (gameRules != null && !gameRules.FreezePeriod)
        {
            return;
        }

        // Find all teammates
        List<int> teammates = new List<int>();
        int self_slot = player.Slot;
        if (player.Team == CsTeam.CounterTerrorist)
        {
            foreach (int teammate in ct_players)
            {
                if (teammate != self_slot)
                {
                    teammates.Add(teammate);
                }
            }
        }
        if (player.Team == CsTeam.Terrorist)
        {
            foreach (int teammate in t_players)
            {
                if (teammate != self_slot)
                {
                    teammates.Add(teammate);
                }
            }
        }

        // Drop knives
        for (int i = 0; i < teammates.Count; i++)
        {
            player.GiveNamedItem("weapon_knife");

            // First find the held knife
            var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons!;
            if (weapons == null)
            {
                break;
            }
            int knife_index = -1;
            foreach (var weapon in weapons)
            {
                if (weapon != null && weapon.IsValid && weapon.Value != null)
                {
                    if (weapon.Value.DesignerName.Contains("knife") || weapon.Value.DesignerName.Contains("bayonet"))
                    {
                        knife_index = (int)weapon.Value.Index;
                        break;
                    }
                }
            }

            // Then drop and teleport
            if (knife_index != -1)
            {
                player.ExecuteClientCommand("slot3");
                player.DropActiveWeapon();

                // Find the knife by index and teleport it
                var knife = Utilities.GetEntityFromIndex<CBasePlayerWeapon>(knife_index);
                if (knife != null && knife.IsValid)
                {
                    var teammate = Utilities.GetPlayerFromSlot(teammates[i]);
                    if (teammate != null && teammate.IsValid  && !teammate.IsBot && !teammate.IsHLTV
                    && teammate.PawnIsAlive && teammate.Pawn != null && teammate.Pawn.IsValid && teammate.Pawn.Value != null)
                    {
                        knife.Teleport(teammate.Pawn.Value.AbsOrigin);
                    }
                }
            }
        }

        // No more chance to drop in this round
        player_slot_ids.Remove(player.Slot);

        return;
    }


    // Enable this for chat filtering (might cause performance issues)
    // [GameEventHandler]
    // public HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    // {
    //     int player_slot = @event.Userid;

    //     try
    //     {
    //         CCSPlayerController player = Utilities.GetPlayerFromSlot(player_slot)!;
    //         if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
    //         {
    //             return HookResult.Continue;
    //         }

    //         string chat_message = @event.Text;

    //         if (chat_message.StartsWith("!drop") 
    //         || chat_message.StartsWith("/drop")
    //         || chat_message.StartsWith(".drop")
    //         || chat_message.StartsWith("!takeknife")
    //         || chat_message.StartsWith("/takeknife")
    //         || chat_message.StartsWith(".takeknife"))
    //         {
    //             DropKnife(player);
    //         }
    //     }
    //     catch (System.Exception)
    //     {
    //         return HookResult.Continue;
    //     }

    //     return HookResult.Continue;
    // }
}
