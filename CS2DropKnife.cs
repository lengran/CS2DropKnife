using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API;
using System.Numerics;
using CounterStrikeSharp.API.Modules.Entities;

namespace CS2DropKnife;

public class CS2DropKnife : BasePlugin
{
    public override string ModuleName => "CS2 Drop Knife";

    public override string ModuleVersion => "2.0.0";

    private List<int> player_slot_ids = new List<int>();

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

        // Drop knives
        for (int i = 0; i < 4; i++)
        {
            player.GiveNamedItem("weapon_knife");
        }

        // No more chance to drop in this round
        player_slot_ids.Remove(player.Slot);

        return;

        // var weapons = player.PlayerPawn.Value.WeaponServices?.MyWeapons;

        // // Player might have no weapon.
        // if (weapons == null) 
        // {
        //     return;
        // }

        // // Find the knife.
        // foreach (var weapon in weapons)
        // {
        //     if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
        //     {
        //         if (weapon.Value.DesignerName.Contains("knife") || weapon.Value.DesignerName.Contains("bayonet"))
        //         {
        //             // Console.WriteLine("[CS2DropKnife] knife index = " + weapon.Index + ", entityindex = " + weapon.Value.Index + ", designer name = " + weapon.Value.DesignerName);
        //             for (int i = 0; i < 5; i++)
        //             {
        //                 player.GiveNamedItem(weapon.Value.DesignerName);
        //             }

        //             player_slot_ids.Remove(player.Slot);

        //             return;
        //         }
        //     }
        // }

        // player.PrintToChat("[CS2DropKnife] Can't find a knife on you. Get one and try again please.");
    }
}
