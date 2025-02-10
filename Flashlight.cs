using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using static CounterStrikeSharp.API.Core.Listeners;

namespace Flashlight;

public class Flashlight : BasePlugin
{
    public override string ModuleName => "Flashlight";
    public override string ModuleAuthor => "Oylsister";
    public override string ModuleVersion => "1.0";

    private Dictionary<CCSPlayerController, CBarnLight?>? _flashlightList = [];

    public ulong IN_LOOKATWEAPON = 1L << 35;

    public override void Load(bool hotReload)
    {
        VirtualFunctions.CCSPlayerPawnBase_PostThinkFunc.Hook(OnPostThink, HookMode.Post);
        RegisterListener<CheckTransmit>(CheckTransmit);
        RegisterListener<OnClientDisconnect>(OnClientDisconnect);
        _flashlightList = [];
    }

    public override void Unload(bool hotReload)
    {
        VirtualFunctions.CCSPlayerPawnBase_PostThinkFunc.Unhook(OnPostThink, HookMode.Post);
        RemoveListener<CheckTransmit>(CheckTransmit);
        RemoveListener<OnClientDisconnect>(OnClientDisconnect);
        _flashlightList = null;
    }

    public void OnClientDisconnect(int playerSlot)
    {
        var client = Utilities.GetPlayerFromSlot(playerSlot);

        if (client == null) return;

        if(_flashlightList?.ContainsKey(client) ?? false)
        {
            if (_flashlightList[client] != null)
            {
                if (_flashlightList[client]?.IsValid ?? false)
                {
                    _flashlightList[client]?.AcceptInput("Kill");
                    _flashlightList[client] = null;
                }
            }
        }

        _flashlightList?.Remove(client);
    }

    public void CheckTransmit(CCheckTransmitInfoList infoList)
    {
        foreach (var (info, player) in infoList)
        {
            var targets = Utilities.GetPlayers().Where(p => p != player);

            if (player == null || player.Connected != PlayerConnectedState.PlayerConnected)
                continue;

            foreach (var target in targets)
            {
                if(!_flashlightList?.ContainsKey(target) ?? false)
                    continue;

                var flashlight = _flashlightList?[target];

                if (flashlight == null || !flashlight.IsValid)
                    continue;

                info.TransmitEntities.Remove(flashlight);
            }
        }
    }

    public HookResult OnPostThink(DynamicHook hook)
    {
        if(_flashlightList == null)
            return HookResult.Continue;

        var client = hook.GetParam<CCSPlayerPawnBase>(0).Controller.Get()?.As<CCSPlayerController>();

        if(client == null)
            return HookResult.Continue;

        if(!_flashlightList.ContainsKey(client))
            _flashlightList.Add(client, null);

        var buttons = client.PlayerPawn.Value!.MovementServices!.Buttons.ButtonStates;

        if ((buttons[0] & IN_LOOKATWEAPON) != 0 && (buttons[1] & IN_LOOKATWEAPON) != 0)
            ToggleFlashlight(client);

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var client = @event.Userid;

        if (client == null)
            return HookResult.Continue;

        if(_flashlightList?.ContainsKey(client) ?? false)
        {
            if (_flashlightList[client] != null && (_flashlightList[client]?.IsValid ?? false))
            {
                _flashlightList[client]?.AcceptInput("Kill");
                _flashlightList[client] = null;
            }
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var client = @event.Userid;

        if (client == null)
            return HookResult.Continue;

        if (_flashlightList?.ContainsKey(client) ?? false)
        {
            if (_flashlightList[client] != null && (_flashlightList[client]?.IsValid ?? false))
            {

                _flashlightList[client]?.AcceptInput("Kill");
                _flashlightList[client] = null;
            }
        }

        return HookResult.Continue;
    }

    public void SpawnFlashlight(CCSPlayerController client)
    {
        if(client == null)
            return;

        if(!_flashlightList?.ContainsKey(client) ?? false)
            _flashlightList?.TryAdd(client, null);

        if (_flashlightList?[client] != null)
            return;

        var pawn = client.PlayerPawn.Value;

        if(pawn == null)
            return;

        Vector pos = new(pawn.AbsOrigin!.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
        Vector forward = new(), right = new(), up = new();
        
        NativeAPI.AngleVectors(pawn.EyeAngles.Handle, forward.Handle, right.Handle, up.Handle);

        pos.Z += 64f;
        pos += forward * 54f;

        var light = Utilities.CreateEntityByName<CBarnLight>("light_barn");

        if(light == null)
            return;

        light.Enabled = true;
        light.Color = Color.White;
        light.Brightness = 1f;
        light.Range = 2048f;
        light.SoftX = 1f;
        light.SoftY = 1f;
        light.Skirt = 0.5f;
        light.SkirtNear = 1f;
        light.SizeParams.X = 45f;
        light.SizeParams.Y = 45f;
        light.SizeParams.Z = 0.02f;
        light.CastShadows = 0;
        light.DirectLight = 3;
        light.Teleport(pos, pawn.EyeAngles);
        
        CEntityKeyValues kv = new CEntityKeyValues();

        kv.SetString("lightcookie", "materials/effects/lightcookies/flashlight.vtex");

        light.DispatchSpawn(kv);

        _flashlightList![client] = light;

        // create arm here.

        var handle = new CHandle<CCSGOViewModel>((IntPtr)(pawn!.ViewModelServices!.Handle + Schema.GetSchemaOffset("CCSPlayer_ViewModelServices", "m_hViewModel") + 4));
        if (!handle.IsValid)
        {
            CCSGOViewModel viewmodel = Utilities.CreateEntityByName<CCSGOViewModel>("predicted_viewmodel")!;
            viewmodel.DispatchSpawn();
            handle.Raw = viewmodel.EntityHandle.Raw;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_pViewModelServices");
        }

        Server.NextWorldUpdate(() =>
        {
            light.AcceptInput("SetParent", handle.Value, null, "!activator");
        });
    }

    public void ToggleFlashlight(CCSPlayerController client)
    {
        var found = _flashlightList!.TryGetValue(client, out var light);

        if (!found)
        {
            SpawnFlashlight(client);
            return;
        }

        if (light == null || !light.IsValid)
        {
            SpawnFlashlight(client);
            return;
        }

        light.AcceptInput(light.Enabled ? "Disable" : "Enable");
        client.ExecuteClientCommand("play sounds/common/talk.vsnd");
    }
}
