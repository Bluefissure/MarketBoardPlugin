// <copyright file="MarketBoardWindow.cs" company="Florian Maunier">
// Copyright (c) Florian Maunier. All rights reserved.
// </copyright>

namespace MarketBoardPlugin.GUI
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Numerics;
  using System.Reflection;
  using System.Text.RegularExpressions;
  using System.Threading;
  using System.Threading.Tasks;

  using Dalamud.Data.LuminaExtensions;
  using Dalamud.Game.Chat;
  using Dalamud.Game.Internal;
  using Dalamud.Plugin;

  using ImGuiNET;
  using ImGuiScene;

  using Lumina.Excel.GeneratedSheets;

  using MarketBoardPlugin.Extensions;
  using MarketBoardPlugin.Helpers;
  using MarketBoardPlugin.Models.Universalis;

  /// <summary>
  /// The market board window.
  /// </summary>
  public class MarketBoardWindow : IDisposable
  {
    private readonly List<Item> items;

    private readonly DalamudPluginInterface pluginInterface;

    private readonly Dictionary<ItemSearchCategory, List<Item>> sortedCategoriesAndItems;

    private readonly List<(string, string)> worldList = new List<(string, string)>();

    private CancellationTokenSource hoveredItemChangeTokenSource;

    private bool isDisposed;

    private bool itemIsBeingHovered;

    private float progressPosition;

    private string searchString = string.Empty;

    private Item selectedItem;

    private TextureWrap selectedItemIcon;

    private bool watchingForHoveredItem = true;

    private ulong playerId = 0;

    private int selectedWorld = -1;

    private MarketDataResponse marketData;

    private int selectedListing = -1;

    private int selectedHistory = -1;

    private ImFontPtr fontPtr;

    private bool hasListingsHQColumnWidthBeenSet = false;

    private bool hasHistoryHQColumnWidthBeenSet = false;

    private Dictionary<uint, string> cnWorldDC = new Dictionary<uint, string>();
    /// <summary>
    /// Initializes a new instance of the <see cref="MarketBoardWindow"/> class.
    /// </summary>
    /// <param name="pluginInterface">The <see cref="DalamudPluginInterface"/>.</param>
    public MarketBoardWindow(DalamudPluginInterface pluginInterface)
    {
      if (pluginInterface == null)
      {
        throw new ArgumentNullException(nameof(pluginInterface));
      }

      this.items = pluginInterface.Data.GetExcelSheet<Item>().GetRows();
      this.pluginInterface = pluginInterface;
      this.sortedCategoriesAndItems = this.SortCategoriesAndItems();

      pluginInterface.Framework.OnUpdateEvent += this.HandleFrameworkUpdateEvent;
      pluginInterface.Framework.Gui.HoveredItemChanged += this.HandleHoveredItemChange;
      pluginInterface.UiBuilder.OnBuildFonts += this.HandleBuildFonts;

      pluginInterface.UiBuilder.RebuildFonts();

      // mannually add these cuz dc is set to 0 in game dat
      this.cnWorldDC.Add(1167, "LuXingNiao");
      this.cnWorldDC.Add(1081, "LuXingNiao");
      this.cnWorldDC.Add(1042, "LuXingNiao");
      this.cnWorldDC.Add(1044, "LuXingNiao");
      this.cnWorldDC.Add(1060, "LuXingNiao");
      this.cnWorldDC.Add(1173, "LuXingNiao");
      this.cnWorldDC.Add(1174, "LuXingNiao");
      this.cnWorldDC.Add(1175, "LuXingNiao");
      this.cnWorldDC.Add(1172, "MoGuLi");
      this.cnWorldDC.Add(1076, "MoGuLi");
      this.cnWorldDC.Add(1171, "MoGuLi");
      this.cnWorldDC.Add(1170, "MoGuLi");
      this.cnWorldDC.Add(1113, "MoGuLi");
      this.cnWorldDC.Add(1121, "MoGuLi");
      this.cnWorldDC.Add(1166, "MoGuLi");
      this.cnWorldDC.Add(1176, "MoGuLi");
      this.cnWorldDC.Add(1043, "MaoXiaoPang");
      this.cnWorldDC.Add(1169, "MaoXiaoPang");
      this.cnWorldDC.Add(1106, "MaoXiaoPang");
      this.cnWorldDC.Add(1045, "MaoXiaoPang");
      this.cnWorldDC.Add(1177, "MaoXiaoPang");
      this.cnWorldDC.Add(1178, "MaoXiaoPang");
      this.cnWorldDC.Add(1179, "MaoXiaoPang");

#if DEBUG
      this.worldList.Add(("Chaos", "Chaos"));
      this.worldList.Add(("Moogle", "Moogle"));
#endif
    }

    /// <summary>
    /// Gets or sets a value indicating whether the Market Board window is open or not.
    /// </summary>
    public bool IsOpen { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
      this.Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Draws the window.
    /// </summary>
    /// <returns>A value indicating whether the window is open.</returns>
    public bool Draw()
    {
      var windowOpen = true;
      var enumerableCategoriesAndItems = this.sortedCategoriesAndItems.ToList();

      if (!string.IsNullOrWhiteSpace(this.searchString))
      {
        enumerableCategoriesAndItems = enumerableCategoriesAndItems
          .Select(kv => new KeyValuePair<ItemSearchCategory, List<Item>>(
            kv.Key,
            kv.Value
              .Where(i =>
                i.Name.ToUpperInvariant().Contains(this.searchString.ToUpperInvariant()))
              .ToList()))
          .Where(kv => kv.Value.Count > 0)
          .ToList();
      }

      ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);

      if (!ImGui.Begin("Market Board", ref windowOpen, ImGuiWindowFlags.NoScrollbar))
      {
        ImGui.End();
        return windowOpen;
      }

      ImGui.BeginChild("itemListColumn", new Vector2(267, 0), true);

      ImGui.SetNextItemWidth(-1);
      ImGuiOverrides.InputTextWithHint("##searchString", "Search for item", ref this.searchString, 256);
      ImGui.Separator();

      ImGui.BeginChild("itemTree", new Vector2(0, -2.0f * ImGui.GetFrameHeightWithSpacing()), false, ImGuiWindowFlags.HorizontalScrollbar);

      foreach (var category in enumerableCategoriesAndItems)
      {
        if (ImGui.TreeNode(category.Key.Name + "##cat" + category.Key.RowId))
        {
          ImGui.Unindent(ImGui.GetTreeNodeToLabelSpacing());

          foreach (var item in category.Value)
          {
            var nodeFlags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

            if (item.RowId == this.selectedItem?.RowId)
            {
              nodeFlags |= ImGuiTreeNodeFlags.Selected;
            }

            ImGui.TreeNodeEx(item.Name + "##item" + item.RowId, nodeFlags);

            if (ImGui.IsItemClicked())
            {
              this.ChangeSelectedItem(item.RowId);
            }
          }

          ImGui.Indent(ImGui.GetTreeNodeToLabelSpacing());
          ImGui.TreePop();
        }
      }

      ImGui.EndChild();

      ImGui.Checkbox("Watch for hovered item", ref this.watchingForHoveredItem);
      ImGui.SameLine();
      ImGui.TextDisabled("(?)");
      if (ImGui.IsItemHovered())
      {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
        ImGui.TextUnformatted("Automatically select the item hovered in any of the in-game inventory window after 1 second.");
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
      }

      if (this.itemIsBeingHovered)
      {
        if (this.progressPosition < 1.0f)
        {
          this.progressPosition += ImGui.GetIO().DeltaTime;
        }
        else
        {
          this.progressPosition = 1.0f;
        }
      }
      else
      {
        this.progressPosition = 0.0f;
      }

      ImGui.ProgressBar(this.progressPosition, new Vector2(-1, 0), string.Empty);

      ImGui.EndChild();
      ImGui.SameLine();
      ImGui.BeginChild("tabColumn", new Vector2(0, 0), false, ImGuiWindowFlags.NoScrollbar);

      if (this.selectedItem?.RowId > 0)
      {
        if (this.selectedItemIcon != null)
        {
          ImGui.Image(this.selectedItemIcon.ImGuiHandle, new Vector2(40, 40));
        }
        else
        {
          ImGui.SetCursorPos(new Vector2(40, 40));
        }

        ImGui.PushFont(this.fontPtr);
        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (ImGui.GetFontSize() / 2.0f) + 19);
        ImGui.Text(this.selectedItem?.Name);
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 250);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (ImGui.GetFontSize() / 2.0f) - 19);
        ImGui.PopFont();
        ImGui.SetNextItemWidth(250);
        if (ImGui.BeginCombo("##worldCombo", this.selectedWorld > -1 ? this.worldList[this.selectedWorld].Item2 : string.Empty))
        {
          foreach (var world in this.worldList)
          {
            var isSelected = this.selectedWorld == this.worldList.IndexOf(world);
            if (ImGui.Selectable(world.Item2, isSelected))
            {
              this.selectedWorld = this.worldList.IndexOf(world);
              this.RefreshMarketData();
            }

            if (isSelected)
            {
              ImGui.SetItemDefaultFocus();
            }
          }

          ImGui.EndCombo();
        }

        if (this.marketData != null)
        {
          ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X - 250);
          ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetTextLineHeight());
          ImGui.SetNextItemWidth(250);
          ImGui.Text(
            $"Last update: {DateTimeOffset.FromUnixTimeMilliseconds(this.marketData.LastUploadTime).LocalDateTime:G}");
          ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetTextLineHeight() - ImGui.GetTextLineHeightWithSpacing());
        }

        if (ImGui.BeginTabBar("tabBar"))
        {
          if (ImGui.BeginTabItem("Market Data##marketDataTab"))
          {
            ImGui.PushFont(this.fontPtr);
            var tableHeight = (ImGui.GetContentRegionAvail().Y / 2) - (ImGui.GetTextLineHeightWithSpacing() * 2);
            ImGui.Text("Current listings (Includes 5%% GST)");
            ImGui.PopFont();

            ImGui.BeginChild("currentListings", new Vector2(0.0f, tableHeight));
            ImGui.Columns(5, "currentListingsColumns");

            if (!this.hasListingsHQColumnWidthBeenSet)
            {
              ImGui.SetColumnWidth(0, 30.0f);
              this.hasListingsHQColumnWidthBeenSet = true;
            }

            ImGui.Separator();
            ImGui.Text("HQ");
            ImGui.NextColumn();
            ImGui.Text("Price");
            ImGui.NextColumn();
            ImGui.Text("Qty");
            ImGui.NextColumn();
            ImGui.Text("Total");
            ImGui.NextColumn();
            ImGui.Text("Retainer");
            ImGui.NextColumn();
            ImGui.Separator();

            var marketDataListings = this.marketData?.Listings.OrderBy(l => l.PricePerUnit).ToList();
            if (marketDataListings != null)
            {
              foreach (var listing in marketDataListings)
              {
                var index = marketDataListings.IndexOf(listing);

                if (ImGui.Selectable(
                  $"{(listing.Hq ? SeIconChar.HighQuality.AsString() : string.Empty)}##listing{index}",
                  this.selectedListing == index,
                  ImGuiSelectableFlags.SpanAllColumns))
                {
                  this.selectedListing = index;
                }

                ImGui.NextColumn();
                ImGui.Text($"{listing.PricePerUnit:##,###}");
                ImGui.NextColumn();
                ImGui.Text($"{listing.Quantity:##,###}");
                ImGui.NextColumn();
                ImGui.Text($"{listing.Total:##,###}");
                ImGui.NextColumn();
                ImGui.Text($"{listing.RetainerName}{(this.selectedWorld == 0 ? $" {SeIconChar.CrossWorld.ToChar()} {listing.WorldName}" : string.Empty)}");
                ImGui.NextColumn();
                ImGui.Separator();
              }
            }

            ImGui.EndChild();

            ImGui.Separator();

            ImGui.PushFont(this.fontPtr);
            ImGui.Text("Recent history");
            ImGui.PopFont();

            ImGui.BeginChild("recentHistory", new Vector2(0.0f, tableHeight));
            ImGui.Columns(6, "recentHistoryColumns");

            if (!this.hasHistoryHQColumnWidthBeenSet)
            {
              ImGui.SetColumnWidth(0, 30.0f);
              this.hasHistoryHQColumnWidthBeenSet = true;
            }

            ImGui.Separator();
            ImGui.Text("HQ");
            ImGui.NextColumn();
            ImGui.Text("Price");
            ImGui.NextColumn();
            ImGui.Text("Qty");
            ImGui.NextColumn();
            ImGui.Text("Total");
            ImGui.NextColumn();
            ImGui.Text("Date");
            ImGui.NextColumn();
            ImGui.Text("Buyer");
            ImGui.NextColumn();
            ImGui.Separator();

            var marketDataRecentHistory = this.marketData?.RecentHistory.OrderByDescending(h => h.Timestamp).ToList();
            if (marketDataRecentHistory != null)
            {
              foreach (var history in marketDataRecentHistory)
              {
                var index = marketDataRecentHistory.IndexOf(history);

                if (ImGui.Selectable(
                  $"{(history.Hq ? SeIconChar.HighQuality.AsString() : string.Empty)}##history{index}",
                  this.selectedHistory == index,
                  ImGuiSelectableFlags.SpanAllColumns))
                {
                  this.selectedHistory = index;
                }

                ImGui.NextColumn();
                ImGui.Text($"{history.PricePerUnit:##,###}");
                ImGui.NextColumn();
                ImGui.Text($"{history.Quantity:##,###}");
                ImGui.NextColumn();
                ImGui.Text($"{history.Total:##,###}");
                ImGui.NextColumn();
                ImGui.Text($"{DateTimeOffset.FromUnixTimeSeconds(history.Timestamp).LocalDateTime:G}");
                ImGui.NextColumn();
                ImGui.Text($"{history.BuyerName}{(this.selectedWorld == 0 ? $" {SeIconChar.CrossWorld.ToChar()} {history.WorldName}" : string.Empty)}");
                ImGui.NextColumn();
                ImGui.Separator();
              }
            }

            ImGui.EndChild();
            ImGui.Separator();
            ImGui.EndTabItem();
          }

          if (ImGui.BeginTabItem("Charts##chartsTab"))
          {
            var tableHeight = (ImGui.GetContentRegionAvail().Y / 2) - (ImGui.GetTextLineHeightWithSpacing() * 2);
            var marketDataRecentHistory = this.marketData?.RecentHistory
              .GroupBy(h => DateTimeOffset.FromUnixTimeSeconds(h.Timestamp).LocalDateTime.Date)
              .Select(g => (Date: g.Key, PriceAvg: (float)g.Average(h => h.PricePerUnit),
                QtySum: (float)g.Sum(h => h.Quantity)))
              .ToList();

            if (marketDataRecentHistory != null)
            {
              for (var day = marketDataRecentHistory.Min(h => h.Date);
                day <= marketDataRecentHistory.Max(h => h.Date);
                day = day.AddDays(1))
              {
                if (!marketDataRecentHistory.Exists(h => h.Date == day))
                {
                  marketDataRecentHistory.Add((day, 0, 0));
                }
              }

              marketDataRecentHistory = marketDataRecentHistory
                .OrderBy(h => h.Date)
                .ToList();

              ImGui.PushFont(this.fontPtr);
              ImGui.Text("Price variations (per unit)");
              ImGui.PopFont();

              var pricePlotValues = marketDataRecentHistory
                .Select(h => h.PriceAvg)
                .ToArray();
              ImGui.SetNextItemWidth(-1);
              ImGui.PlotLines(
                "##pricePlot",
                ref pricePlotValues[0],
                pricePlotValues.Length,
                0,
                null,
                float.MaxValue,
                float.MaxValue,
                new Vector2(0, tableHeight));

              ImGui.Separator();

              ImGui.PushFont(this.fontPtr);
              ImGui.Text("Traded volumes");
              ImGui.PopFont();

              var qtyPlotValues = marketDataRecentHistory
                .Select(h => h.QtySum)
                .ToArray();
              ImGui.SetNextItemWidth(-1);
              ImGui.PlotHistogram(
                "##qtyPlot",
                ref qtyPlotValues[0],
                qtyPlotValues.Length,
                0,
                null,
                float.MaxValue,
                float.MaxValue,
                new Vector2(0, tableHeight));
            }

            ImGui.EndTabItem();
          }

          ImGui.EndTabBar();
        }
      }

      ImGui.SetCursorPosY(ImGui.GetWindowContentRegionMax().Y - ImGui.GetTextLineHeightWithSpacing());
      ImGui.Text("Data provided by Universalis (https://universalis.app/)");

      ImGui.EndChild();
      ImGui.End();

      return windowOpen;
    }

    internal void ChangeSelectedItem(uint itemId)
    {
      this.selectedItem = this.items.Single(i => i.RowId == itemId);

      var iconId = this.selectedItem.Icon;
      var iconTexFile = this.pluginInterface.Data.GetIcon(iconId);
      this.selectedItemIcon?.Dispose();
      this.selectedItemIcon = this.pluginInterface.UiBuilder.LoadImageRaw(iconTexFile.GetRgbaImageData(), iconTexFile.Header.Width, iconTexFile.Header.Height, 4);

      this.RefreshMarketData();
    }

    /// <summary>
    /// Protected implementation of Dispose pattern.
    /// </summary>
    /// <param name="disposing">A value indicating whether we are disposing.</param>
    protected virtual void Dispose(bool disposing)
    {
      if (this.isDisposed)
      {
        return;
      }

      if (disposing)
      {
        this.pluginInterface.Framework.OnUpdateEvent -= this.HandleFrameworkUpdateEvent;
        this.pluginInterface.Framework.Gui.HoveredItemChanged -= this.HandleHoveredItemChange;
        this.pluginInterface.UiBuilder.OnBuildFonts -= this.HandleBuildFonts;
        this.hoveredItemChangeTokenSource?.Dispose();
        this.selectedItemIcon?.Dispose();
      }

      this.isDisposed = true;
    }

    private Dictionary<ItemSearchCategory, List<Item>> SortCategoriesAndItems()
    {
      var itemSearchCategories = this.pluginInterface.Data.GetExcelSheet<ItemSearchCategory>().GetRows();

      var sortedCategories = itemSearchCategories
        .Where(c => c.Category > 0)
        .OrderBy(c => c.Category)
        .ThenBy(c => c.Order)
        .ToDictionary(c => c, c => this.items.Where(i => i.ItemSearchCategory.Row == c.RowId).OrderBy(i => i.Name).ToList());

      return sortedCategories;
    }

    private void HandleBuildFonts()
    {
      var fontPath = Path.Combine(Path.GetDirectoryName(Assembly.GetAssembly(typeof(DalamudPluginInterface)).Location) ?? string.Empty, "UIRes", "NotoSansCJKsc-Medium.otf");
      this.fontPtr = ImGui.GetIO().Fonts.AddFontFromFileTTF(fontPath, 24.0f, null, ImGui.GetIO().Fonts.GetGlyphRangesChineseFull());
    }

    private string LocalizeWorldName(string displayName)
    {
      displayName = Regex.Replace(displayName, "HongYuHai", "红玉海", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "ShenYiZhiDi", "神意之地", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "LaNuoXiYa", "拉诺西亚", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "HuanYingQunDao", "幻影群岛", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "MengYaChi", "萌芽池", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "YuZhouHeYin", "宇宙和音", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "WoXianXiRan", "沃仙曦染", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "ChenXiWangZuo", "晨曦王座", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "BaiYinXiang", "白银乡", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "BaiJinHuanXiang", "白金幻象", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "ShenQuanHen", "神拳痕", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "ChaoFengTing", "潮风亭", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "LvRenZhanQiao", "旅人栈桥", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "FuXiaoZhiJian", "拂晓之间", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "Longchaoshendian", "龙巢神殿", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "MengYuBaoJing", "梦羽宝境", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "ZiShuiZhanQiao", "紫水栈桥", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "YanXia", "延夏", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "JingYuZhuangYuan", "静语庄园", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "MoDuNa", "摩杜纳", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "HaiMaoChaWu", "海猫茶屋", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "RouFengHaiWan", "柔风海湾", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "HuPoYuan", "琥珀原", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "LuXingNiao", "陆行鸟", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "MoGuLi", "莫古力", RegexOptions.IgnoreCase);
      displayName = Regex.Replace(displayName, "MaoXiaoPang", "猫小胖", RegexOptions.IgnoreCase);
      return displayName;
    }

    private void HandleFrameworkUpdateEvent(Framework framework)
    {
      var localPlayer = this.pluginInterface.ClientState.LocalPlayer;

      if (localPlayer == null)
      {
        return;
      }

      if (this.playerId != this.pluginInterface.ClientState.LocalContentId)
      {
        this.playerId = this.pluginInterface.ClientState.LocalContentId;

        var currentDc = localPlayer.CurrentWorld.GameData.DataCenter;
        var dcWorlds = this.pluginInterface.Data.GetExcelSheet<World>().GetRows()
          .Where(w =>
          {
            if (this.cnWorldDC.ContainsKey(localPlayer.CurrentWorld.GameData.RowId))
            {
              if (!this.cnWorldDC.ContainsKey(w.RowId))
              {
                return false;
              }

              return this.cnWorldDC[localPlayer.CurrentWorld.GameData.RowId] == this.cnWorldDC[w.RowId];
            }

            return w.DataCenter.Row == currentDc.Row;
          })
          .OrderBy(w => w.Name)
          .Select(w =>
          {
            var displayName = w.Name;
            displayName = this.LocalizeWorldName(displayName);
            if (localPlayer.CurrentWorld.Id == w.RowId)
            {
              displayName += $" {SeIconChar.Hyadelyn.ToChar()}";
            }

            return (w.Name, displayName);
          });

        this.worldList.Clear();
        var dcName = currentDc.Value?.Name;
        if (this.cnWorldDC.ContainsKey(localPlayer.CurrentWorld.GameData.RowId))
        {
          dcName = this.LocalizeWorldName(this.cnWorldDC[localPlayer.CurrentWorld.GameData.RowId]);
        }

        this.worldList.Add((dcName, $"跨服 {SeIconChar.CrossWorld.ToChar()}"));
        this.worldList.AddRange(dcWorlds);

        this.selectedWorld = this.worldList.FindIndex(w => w.Item1 == localPlayer.CurrentWorld.GameData.Name);
      }
    }

    private void HandleHoveredItemChange(object sender, ulong itemId)
    {
      if (!this.watchingForHoveredItem)
      {
        return;
      }

      if (this.hoveredItemChangeTokenSource != null)
      {
        if (!this.hoveredItemChangeTokenSource.IsCancellationRequested)
        {
          this.hoveredItemChangeTokenSource.Cancel();
        }

        this.hoveredItemChangeTokenSource.Dispose();
      }

      this.progressPosition = 0.0f;

      if (itemId == 0)
      {
        this.itemIsBeingHovered = false;
        this.hoveredItemChangeTokenSource = null;
        return;
      }

      this.itemIsBeingHovered = true;
      this.hoveredItemChangeTokenSource = new CancellationTokenSource();

      if (this.IsOpen)
      {
        Task.Run(async () =>
        {
          try
          {
            await Task.Delay(1000, this.hoveredItemChangeTokenSource.Token).ConfigureAwait(false);
            this.ChangeSelectedItem(Convert.ToUInt32(itemId >= 1000000 ? itemId - 1000000 : itemId));
          }
          catch (TaskCanceledException)
          {
          }
        });
      }
    }

    private void RefreshMarketData()
    {
      Task.Run(async () =>
      {
        this.marketData = await UniversalisClient
          .GetMarketData(this.selectedItem.RowId, this.worldList[this.selectedWorld].Item1, CancellationToken.None)
          .ConfigureAwait(false);
      });
    }
  }
}
