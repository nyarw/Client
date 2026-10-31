using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Timer = Godot.Timer;

public partial class MapBrowser : Control
{
    public static MapBrowser Instance;

    public bool Shown;

    private Button hideButton;
    private Panel holder;
    private VBoxContainer topBar;
    private HBoxContainer searchBar;
    private LineEdit search;
    private OptionButton sort;
    private Button direction;
    private Button refresh;
    private MenuButton difficulty;
    private OptionButton status;
    private Timer searchTimer;
    private HBoxContainer filters;
    private ScrollContainer results;
    private MapCard mapCardTemplate;
    private SortDirection sortDirection;

    private enum SortDirection
    {
        Descending,
        Ascending,
    }

    // multiple of 3 because the results grid has 3 columns
    private const int page_size = 18;

    private CancellationTokenSource source = new();

    public override void _Ready()
    {
        Instance = this;

        hideButton = GetNode<Button>("Hide");
        holder = GetNode<Panel>("Holder");
        topBar = holder.GetNode<VBoxContainer>("Background/Layout/TopBarBackground/Margin/TopBar");
        searchBar = topBar.GetNode<HBoxContainer>("SearchBar");
        search = searchBar.GetNode<LineEdit>("Search");
        sort = searchBar.GetNode<OptionButton>("Sort");
        direction = searchBar.GetNode<Button>("Direction");
        refresh = searchBar.GetNode<Button>("Refresh");
        difficulty = topBar.GetNode<HBoxContainer>("Filters").GetNode<MenuButton>("Difficulty");
        status = topBar.GetNode<HBoxContainer>("Filters").GetNode<OptionButton>("Status");
        searchTimer = GetNode<Timer>("SearchTimer");
        results = holder.GetNode<ScrollContainer>("Background/Layout/Results");
        mapCardTemplate = results.GetNode<MapCard>("RowsMargin/Rows/MapCardTemplate");

        mapCardTemplate.Visible = false;

        Shown = false;
        Visible = false;
        sortDirection = SortDirection.Descending;
        direction.Text = "Descending";

        search.TextChanged += _ => searchTimer.Start();
        searchTimer.Timeout += onSearchTimerTimeout;
        sort.ItemSelected += _ => refreshResults();
        direction.Pressed += toggleDirection;
        refresh.Pressed += refreshResults;
        difficulty.GetPopup().IdPressed += toggleDifficulty;
        status.ItemSelected += _ => refreshResults();

        hideButton.Pressed += HideMenu;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            return;
        if (!Shown)
            return;
        ShowMenu(false);
        GetViewport().SetInputAsHandled();
    }

    public void ShowMenu(bool show = true)
    {
        Shown = show;
        hideButton.MouseFilter = show ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;

        CallDeferred("move_to_front");

        if (Shown)
        {
            Visible = true;
            holder.OffsetTop = 10;
            holder.OffsetBottom = 10;

            clearResults();
            _ = populate(buildQueryParameters());
        }

        Tween tween = CreateTween().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out).SetParallel();
        tween.TweenProperty(this, "modulate", Color.Color8(255, 255, 255, (byte)(Shown ? 255 : 0b0)), 0.25);
        tween.TweenProperty(holder, "offset_top", Shown ? 0 : 10, 0.25);
        tween.TweenProperty(holder, "offset_bottom", Shown ? 0 : 10, 0.25);
        tween
            .Chain()
            .TweenCallback(
                Callable.From(() =>
                {
                    Visible = Shown;
                })
            );
    }

    public void HideMenu()
    {
        ShowMenu(false);
    }

    private async Task populate(MapQueryParameters queryParameters)
    {
        CancellationTokenSource populationSource = new();
        source = populationSource;

        JsonElement[] maps;

        try
        {
            maps = await MapBrowserService.Search(queryParameters);
        }
        catch (OperationCanceledException) when (populationSource.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            await ToastNotification.Notify("Failed to load maps", 2);
            Logger.Error(exception);
            return;
        }

        if (populationSource.IsCancellationRequested)
            return;

        foreach (var map in maps)
        {
            if (mapCardTemplate.Duplicate() is not MapCard mapCard)
            {
                continue;
            }

            mapCard.Visible = true;
            mapCardTemplate.GetParent().AddChild(mapCard);

            try
            {
                mapCard.Bind(map, populationSource);
            }
            catch (Exception exception)
            {
                Logger.Error(exception);
                mapCard.QueueFree();
            }
        }
    }

    private void onSearchTimerTimeout()
    {
        refreshResults();
    }

    private void toggleDifficulty(long id)
    {
        PopupMenu popup = difficulty.GetPopup();
        int index = popup.GetItemIndex((int)id);
        if (index < 0)
            return;

        popup.SetItemChecked(index, !popup.IsItemChecked(index));
        refreshResults();
    }

    private MapQueryParameters buildQueryParameters()
    {
        int[] selectedDifficulties = Enumerable
            .Range(0, difficulty.GetPopup().ItemCount)
            .Where(index => difficulty.GetPopup().IsItemChecked(index))
            .Select(index => difficulty.GetPopup().GetItemId(index))
            .ToArray();

        string[] statuses = status.Selected switch
        {
            0 => ["UNRANKED"],
            1 => ["RANKED"],
            _ => Array.Empty<string>(),
        };

        return new MapQueryParameters
        {
            Limit = page_size,
            Query = search.Text.Trim(),
            SortBy = sort.Selected switch
            {
                1 => "length",
                2 => "difficulty",
                _ => "createdAt",
            },
            SortDirection = sortDirection switch
            {
                SortDirection.Ascending => "asc",
                _ => "desc",
            },
            Difficulties = selectedDifficulties,
            Status = statuses,
        };
    }

    private void toggleDirection()
    {
        sortDirection = sortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
        direction.Text = sortDirection.ToString();
        refreshResults();
    }

    private void refreshResults()
    {
        clearResults();
        _ = populate(buildQueryParameters());
    }

    private void clearResults()
    {
        source.Cancel();
        source.Dispose();

        var parent = mapCardTemplate.GetParent();
        foreach (Node child in parent.GetChildren())
        {
            if (child != mapCardTemplate)
            {
                child.QueueFree();
            }
        }
    }
}
