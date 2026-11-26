using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The "Group by tag" button above the mod list: the mods of a tag together, under a heading.</summary>
public sealed class ModGroupByTagTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-group-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private static MainViewModel Model(AppPaths paths) =>
        new(new GameProfile { Id = "test", DisplayName = "Test" }, paths, new ArtSet(null, null));

    private void Add(MainViewModel model, ModRecord record) => model.Mods.Add(new ModItemViewModel(model, record, Path.Combine(folder, "library")));

    /// <summary>
    /// Alpha: no tag. Bravo: Skins, then Audio. Charlie: no tag, in the category Audio on Nexus Mods. Delta: Combat.
    /// Echo: skins, written small.
    /// </summary>
    private void With(Action<MainViewModel, AppPaths> check) => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        JsonFile.Write(paths.NexusCardsFile, new Dictionary<string, NexusListedMod>
        {
            ["nexusmods:testgame/7"] = new(7, "Charlie", "", null, null, null, "1.0", 0, 0, null, "Audio"),
        });
        var model = Model(paths);
        Fill(model);
        check(model, paths);
    });

    private void Fill(MainViewModel model)
    {
        Add(model, new ModRecord { Id = "a", Name = "Alpha" });
        Add(model, new ModRecord { Id = "b", Name = "Bravo", Tags = ["Skins", "Audio"] });
        Add(model, new ModRecord { Id = "c", Name = "Charlie", CatalogId = "nexusmods:testgame/7" });
        Add(model, new ModRecord { Id = "d", Name = "Delta", Tags = ["Combat"] });
        Add(model, new ModRecord { Id = "e", Name = "Echo", Tags = ["skins"] });
        model.RefreshTagFilters();
    }

    private static string[] Shown(MainViewModel model) => model.ModsView.Cast<ModItemViewModel>().Select(mod => mod.Name).ToArray();

    /// <summary>The headings in the list, each with the number of its mods.</summary>
    private static string[] Groups(MainViewModel model) =>
        model.ModsView.Groups?.Cast<CollectionViewGroup>().Select(group => $"{group.Name} ({group.ItemCount})").ToArray() ?? [];

    [Fact]
    public void The_list_has_no_groups_at_first() => With((model, _) =>
    {
        Assert.False(model.GroupByTag.IsActive);
        Assert.Empty(Groups(model));
        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta", "Echo"], Shown(model));
    });

    [Fact]
    public void By_tag_the_mods_of_a_tag_stand_together_and_the_ones_without_last() => With((model, _) =>
    {
        model.GroupByTag.Select.Execute(null);

        Assert.True(model.GroupByTag.IsActive);
        Assert.Equal(["Charlie", "Delta", "Bravo", "Echo", "Alpha"], Shown(model));
        Assert.Equal(["Audio (1)", "Combat (1)", "Skins (2)", "No tag (1)"], Groups(model));
    });

    [Fact]
    public void A_mod_goes_by_its_first_tag_and_without_one_by_its_category_on_the_site() => With((model, _) =>
    {
        var groups = model.Mods.ToDictionary(mod => mod.Name, mod => mod.Group);

        Assert.Null(groups["Alpha"]);
        Assert.Equal("Skins", groups["Bravo"]);
        Assert.Equal("Audio", groups["Charlie"]);
        Assert.Equal("No tag", model.Mods.Single(mod => mod.Name == "Alpha").GroupName);
    });

    [Fact]
    public void Another_click_puts_the_list_by_name_again_without_groups() => With((model, _) =>
    {
        model.GroupByTag.Select.Execute(null);

        model.GroupByTag.Select.Execute(null);

        Assert.False(model.GroupByTag.IsActive);
        Assert.Empty(Groups(model));
        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta", "Echo"], Shown(model));
    });

    [Fact]
    public void A_click_on_a_column_ends_the_groups() => With((model, _) =>
    {
        model.GroupByTag.Select.Execute(null);

        model.SortByCommand.Execute(ModSortField.Name);

        Assert.False(model.GroupByTag.IsActive);
        Assert.Empty(Groups(model));
    });

    [Fact]
    public void The_groups_are_kept_for_the_next_start() => With((model, paths) =>
    {
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.GroupByTag.Select.Execute(null);

        Assert.Contains(nameof(MainViewModel.GroupByTag), changed);
        Assert.Equal("Tag", AppSettings.Load(paths).ModsSort);
        var next = Model(paths);
        Fill(next);
        Assert.True(next.GroupByTag.IsActive);
        Assert.Equal(["Audio (1)", "Combat (1)", "Skins (2)", "No tag (1)"], Groups(next));
    });

    [Fact]
    public void The_filters_keep_working_inside_the_groups() => With((model, _) =>
    {
        model.GroupByTag.Select.Execute(null);

        model.SearchText = "ha";

        Assert.Equal(["Charlie", "Alpha"], Shown(model));
        Assert.Equal(["Audio (1)", "No tag (1)"], Groups(model));
    });
}
