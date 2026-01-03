using System;
using System.Collections.Generic;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;

namespace GlamSpy;

public sealed class GlamSpyPlugin : IDalamudPlugin
{

    private readonly IContextMenu contextMenu;
    private readonly IMenuItem equipMenuItem;
    private ulong hoveredItemId;

    public GlamSpyPlugin(IDalamudPluginInterface pluginInterface, IContextMenu contextMenu)
    {
        pluginInterface.Create<Services>();

        this.contextMenu = contextMenu;

        this.equipMenuItem = new MenuItem
        {
            Name = "Open in Console Games Wiki",
            Priority = 100,
            OnClicked = this.OnClickGearItem,
        };

        this.contextMenu.OnMenuOpened += this.OnMenuOpened;
        Services.GameGui.HoveredItemChanged += this.OnHoveredItemChanged;
    }

    private void OnHoveredItemChanged(object? sender, ulong itemId)
    {
        if (itemId <= 0) return;

        hoveredItemId = itemId;
        if (hoveredItemId > 1_000_000)
        {
            hoveredItemId -= 1_000_000;
        }
    }

    private void OnClickGearItem(IMenuItemClickedArgs args)
    {
        if (hoveredItemId <= 0) return;

        var itemRow = Services.DataManager.GetExcelSheet<Item>()?.GetRow((uint)hoveredItemId);
        var name = itemRow?.Name.ToString();
        if (string.IsNullOrWhiteSpace(name)) return;

        Util.OpenLink($"https://consolegameswiki.com/wiki/{Uri.EscapeDataString(name)}");
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Default)
            return;

        var validNames = new List<string>
        {
            "CharacterInspect"
        };

        var addonName = args.AddonName ?? "Unknown";
        if (validNames.Contains(addonName))
        {
            args.AddMenuItem((MenuItem)this.equipMenuItem);
        }
    }

    public void Dispose()
    {
        Services.GameGui.HoveredItemChanged -= this.OnHoveredItemChanged;
        this.contextMenu.OnMenuOpened -= this.OnMenuOpened;
        this.contextMenu.RemoveMenuItem(ContextMenuType.Inventory, this.equipMenuItem);
    }
}
