// RepeatGather
// Memoriza o item que você clicou na janela "Gathering" e repete o clique
// a cada tentativa até o node acabar. Para ao fechar a janela de coleta.
// Janela de config: /repeatgather (abre/fecha) ou pelo botão de config no /xlplugins.

using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Configuration;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace RepeatGather;

[Serializable]
public class RepeatGatherConfig : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public bool Enabled = true;
    public int DelayMs = 700;
}

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("RepeatGather", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
    }

    public override void Draw()
    {
        var cfg = plugin.Config;

        if (ImGui.Checkbox("Ativar repetição automática", ref cfg.Enabled))
        {
            plugin.ResetItem();
            plugin.SaveConfig();
        }

        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderInt("Intervalo (ms)", ref cfg.DelayMs, 400, 2000))
            plugin.SaveConfig();

        ImGui.TextDisabled("Clique em um item na janela de coleta; ele será repetido.");
        ImGui.TextDisabled("Fechar a janela de coleta faz o plugin esquecer o item.");
    }
}

public sealed unsafe class Plugin : IDalamudPlugin
{
    private const string Addon = "Gathering";
    private const string Cmd = "/repeatgather";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IAddonLifecycle lifecycle;
    private readonly ICommandManager commands;
    private readonly IPluginLog log;
    private readonly WindowSystem windows = new("RepeatGather");
    private readonly ConfigWindow configWindow;

    public RepeatGatherConfig Config { get; }

    private int lastIndex = -1;          // último item clicado pelo jogador
    private DateTime nextFire = DateTime.MinValue;
    private bool firing;                 // evita capturar o nosso próprio clique

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IAddonLifecycle lifecycle,
        ICommandManager commands,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.lifecycle = lifecycle;
        this.commands = commands;
        this.log = log;

        Config = pluginInterface.GetPluginConfig() as RepeatGatherConfig ?? new RepeatGatherConfig();

        configWindow = new ConfigWindow(this);
        windows.AddWindow(configWindow);
        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        pluginInterface.UiBuilder.OpenMainUi += ToggleWindow;

        lifecycle.RegisterListener(AddonEvent.PreReceiveEvent, Addon, OnClick);
        lifecycle.RegisterListener(AddonEvent.PostRefresh, Addon, OnRefresh);
        lifecycle.RegisterListener(AddonEvent.PreFinalize, Addon, OnClose);

        commands.AddHandler(Cmd, new CommandInfo((_, _) => ToggleWindow())
        {
            HelpMessage = "Abre/fecha a janela de configuração do RepeatGather."
        });
    }

    public void SaveConfig() => pluginInterface.SavePluginConfig(Config);

    public void ResetItem() => lastIndex = -1;

    private void ToggleWindow() => configWindow.Toggle();

    // 1) Captura o item que o jogador clicou.
    private void OnClick(AddonEvent type, AddonArgs args)
    {
        if (!Config.Enabled || firing || args is not AddonReceiveEventArgs e) return;

        if ((AtkEventType)e.AtkEventType == AtkEventType.ButtonClick)
        {
            lastIndex = e.EventParam;
            nextFire = DateTime.UtcNow.AddMilliseconds(Config.DelayMs);
        }
    }

    // 2) A cada refresh da janela, repete a seleção se já passou o intervalo.
    private void OnRefresh(AddonEvent type, AddonArgs args)
    {
        if (!Config.Enabled || lastIndex < 0 || DateTime.UtcNow < nextFire) return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible) return;

        firing = true;
        try { addon->FireCallbackInt(lastIndex); }
        finally { firing = false; }

        nextFire = DateTime.UtcNow.AddMilliseconds(Config.DelayMs);
    }

    // 3) Janela de coleta fechou: esquece o item.
    private void OnClose(AddonEvent type, AddonArgs args) => lastIndex = -1;

    public void Dispose()
    {
        lifecycle.UnregisterListener(OnClick, OnRefresh, OnClose);
        commands.RemoveHandler(Cmd);

        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        windows.RemoveAllWindows();
    }
}
