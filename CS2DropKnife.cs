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
using CounterStrikeSharp.API.Modules.Events;

namespace CS2DropKnife;

public class CS2DropKnife : BasePlugin
{
    public override string ModuleName => "CS2 Drop Knife";

    public override string ModuleVersion => "4.0.0";

    private List<int> player_slot_ids = new List<int>();

    private List<int> ct_players = new List<int>();
    private List<int> t_players = new List<int>();

    private DropRules ?_settings;

    public override void Load(bool hotReload)
    {
        base.Load(hotReload);

        Console.WriteLine("[CS2DropKnife] Registering listeners.");
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);

        _settings = new DropRules(ModuleDirectory);
        _settings.LoadSettings();
        
        Server.ExecuteCommand("mp_drop_knife_enable 1");
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
        if (_settings == null || _settings.FreezeTimeOnly)
        {
            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")?.FirstOrDefault()?.GameRules;
            if (gameRules != null && !gameRules.FreezePeriod)
            {
                return;
            }
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

        // First find the held knife
        string knife_designer_name = "";
        int held_knife_index = -1;

        var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons!;
        if (weapons == null)
        {
            return;
        }
        foreach (var weapon in weapons)
        {
            if (weapon != null && weapon.IsValid && weapon.Value != null)
            {
                if (weapon.Value.DesignerName.Contains("knife") || weapon.Value.DesignerName.Contains("bayonet"))
                {
                    knife_designer_name = weapon.Value.DesignerName;
                    held_knife_index = (int)weapon.Value.Index;
                    // Server.PrintToChatAll($"[CS2DropKnife] DEBUG knife_designer_name = {knife_designer_name}, index = {held_knife_index}"); // DEBUG
                    break;
                }
            }
        }

        if (held_knife_index != -1)
        {
            // Drop knives
            List<nint> knife_pointers = new List<nint>();
            for (int i = 0; i < teammates.Count; i++)
            {
                nint pointer = player.GiveNamedItem(knife_designer_name);
                knife_pointers.Add(pointer);
            }

            // Then find dropped knives and teleport
            if (_settings == null || _settings.DirectSend)
            {
                if (knife_pointers.Count >= teammates.Count)
                {
                    for (int i = 0; i < teammates.Count; i++)
                    {
                        CBasePlayerWeapon? knife = new(knife_pointers[i]);
                        if (knife == null || !knife.IsValid)
                        {
                            // Server.PrintToChatAll($"[CS2DropKnife] DEBUG failed to find the knife {(int)knife_pointers[i]}"); // DEBUG
                            continue;
                        }
                        // Server.PrintToChatAll($"[CS2DropKnife] DEBUG successfully found the knife with pointer {(int)knife_pointers[i]}, the index is {knife.Index}"); // DEBUG
                        var teammate = Utilities.GetPlayerFromSlot(teammates[i]);
                        if (teammate != null && teammate.IsValid && !teammate.IsBot && !teammate.IsHLTV
                            && teammate.PawnIsAlive && teammate.Pawn != null && teammate.Pawn.IsValid && teammate.Pawn.Value != null)
                        {
                            knife.Teleport(teammate.Pawn.Value.AbsOrigin);
                        }
                    }
                }

                // List<int> knife_indices = new List<int>();

                // // Find dropped knives (possible bug: what if multiple players drop knife at the same time?)
                // var entities = Utilities.FindAllEntitiesByDesignerName<CCSWeaponBaseGun>(knife_designer_name);
                // foreach (var entity in entities)
                // {
                //     if (!entity.IsValid)
                //     {
                //         continue;
                //     }
            
                //     if (entity.State != CSWeaponState_t.WEAPON_NOT_CARRIED)
                //     {
                //         continue;
                //     }
            
                //     if (entity.DesignerName.StartsWith("weapon_") == false)
                //     {
                //         continue;
                //     }

                //     Server.PrintToChatAll($"[CS2DropKnife] DEBUG knife on the ground index = {(int)entity.Index}"); // DEBUG

                //     if ((int)entity.Index == held_knife_index)
                //     {
                //         continue;
                //     }
            
                //     knife_indices.Add((int)entity.Index);
                // }

                // Teleport
                // if (knife_indices.Count >= teammates.Count)
                // {
                //    for (int i = 0; i < teammates.Count; i++)
                //    {
                //         var knife = Utilities.GetEntityFromIndex<CBasePlayerWeapon>(knife_indices[i]);
                //         if (knife != null && knife.IsValid)
                //         {
                //             var teammate = Utilities.GetPlayerFromSlot(teammates[i]);
                //             if (teammate != null && teammate.IsValid // DEBUG && !teammate.IsBot && !teammate.IsHLTV
                //             && teammate.PawnIsAlive && teammate.Pawn != null && teammate.Pawn.IsValid && teammate.Pawn.Value != null)
                //             {
                //                 knife.Teleport(teammate.Pawn.Value.AbsOrigin);
                //             }
                //         }
                //    }
            }
        }

        // No more chance to drop in this round
        if (_settings == null || _settings!.OncePerRound)
        {
            player_slot_ids.Remove(player.Slot);
        }

        return;
    }


    // Enable this for chat filtering (might cause performance issues)
    [GameEventHandler]
    public HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        if (_settings != null && _settings.ChatFiltering)
        {
            int player_slot = @event.Userid;

            try
            {
                CCSPlayerController player = Utilities.GetPlayerFromSlot(player_slot)!;
                if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
                {
                    return HookResult.Continue;
                }

                string chat_message = @event.Text;

                if (chat_message.StartsWith("!drop") 
                || chat_message.StartsWith("/drop")
                || chat_message.StartsWith(".drop")
                || chat_message.Equals("!d")
                || chat_message.Equals("/d")
                || chat_message.Equals(".d")
                || chat_message.StartsWith("!takeknife")
                || chat_message.StartsWith("/takeknife")
                || chat_message.StartsWith(".takeknife"))
                {
                    DropKnife(player);
                }
            }
            catch (System.Exception)
            {
                return HookResult.Continue;
            }
        }

        return HookResult.Continue;
    }
}
