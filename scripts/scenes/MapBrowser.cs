using System;
using System.Globalization;
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
    private Timer searchTimer;
    private HBoxContainer filters;
    private ScrollContainer results;
    private PanelContainer mapCardTemplate;

    private CancellationTokenSource source = new();

    public override void _Ready()
    {
        Instance = this;

        hideButton = GetNode<Button>("Hide");
        holder = GetNode<Panel>("Holder");
        topBar = holder.GetNode<VBoxContainer>("Background/Layout/TopBarBackground/Margin/TopBar");
        searchBar = topBar.GetNode<HBoxContainer>("SearchBar");
        search = searchBar.GetNode<LineEdit>("Search");
        searchTimer = GetNode<Timer>("SearchTimer");
        results = holder.GetNode<ScrollContainer>("Background/Layout/Results");
        mapCardTemplate = results.GetNode<PanelContainer>("RowsMargin/Rows/MapCardTemplate");

        mapCardTemplate.Visible = false;

        Shown = false;
        Visible = false;

        search.TextChanged += _ => searchTimer.Start();
        searchTimer.Timeout += onSearchTimerTimeout;

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
        source = new CancellationTokenSource();

        JsonElement[] maps;

        try
        {
            maps = await MapBrowserService.Search(queryParameters);
        }
        catch (Exception exception)
        {
            await ToastNotification.Notify("Failed to load maps", 2);
            Logger.Error(exception);
            return;
        }

        foreach (var map in maps)
        {
            if (mapCardTemplate.Duplicate() is not PanelContainer mapCard)
            {
                continue;
            }

            mapCard.Visible = true;
            mapCardTemplate.GetParent().AddChild(mapCard);

            Map downloadedMap = getDownloadedMap(map.GetProperty("noteHash").GetString());
            bool isDownloaded = downloadedMap != null;
            bool isDownloading = false;

            downloadIcon.Texture = isDownloaded ? deleteIconTexture : downloadIconTexture;
            downloadIcon.PivotOffset = downloadIcon.Size / 2;


            mapCard.MouseEntered += () =>
            {
                if (isDownloading)
                    return;

                mapCardTween?.Kill();
                mapCardTween = tweenMapCard(mapCard, blurCoverDim, rightPanel, contentContainer, downloadIcon, true);

                downloadButton.SetMouseFilter(MouseFilterEnum.Stop);
            };
            mapCard.MouseExited += () =>
            {
                if (isDownloading || downloadButton.GetGlobalRect().HasPoint(downloadButton.GetGlobalMousePosition()))
                    return;

                mapCardTween?.Kill();
                mapCardTween = tweenMapCard(mapCard, blurCoverDim, rightPanel, contentContainer, downloadIcon);

                downloadButton.SetMouseFilter(MouseFilterEnum.Ignore);
            };
            downloadButton.MouseEntered += () =>
            {
                if (isDownloading) return;

                downloadButtonTween?.Kill();
                downloadButtonTween = tweenDownloadButton(downloadButton, downloadIcon, downloadButtonStyle, true);
            };
            downloadButton.MouseExited += () =>
            {
                if (isDownloading) return;

                downloadButtonTween?.Kill();
                downloadButtonTween = tweenDownloadButton(downloadButton, downloadIcon, downloadButtonStyle);

                if (mapCard.GetGlobalRect().HasPoint(mapCard.GetGlobalMousePosition())) return;

                mapCardTween?.Kill();
                mapCardTween = tweenMapCard(mapCard, blurCoverDim, rightPanel, contentContainer, downloadIcon);

                downloadButton.SetMouseFilter(MouseFilterEnum.Ignore);
            };
            downloadButton.ButtonDown += () =>
            {
                if (isDownloading) return;

                downloadButtonTween?.Kill();
                downloadButtonTween =
                    tweenDownloadButton(downloadButton, downloadIcon, downloadButtonStyle, true, true);
            };
            downloadButton.ButtonUp += () =>
            {
                if (isDownloading) return;

                downloadButtonTween?.Kill();
                downloadButtonTween = tweenDownloadButton(downloadButton, downloadIcon, downloadButtonStyle, downloadButton.IsHovered());
            };
            downloadButton.Pressed += async () =>
            {
                downloadButton.Disabled = true;

                if (isDownloaded)
                {
                    MapManager.Delete(downloadedMap);
                    downloadedMap = null;
                    isDownloaded = false;
                }
                else
                {
                    isDownloading = true;
                    downloadIcon.Texture = throbberTexture;

                    Tween throbberTween = downloadIcon.CreateTween().SetLoops();
                    throbberTween.TweenProperty(downloadIcon, "rotation", Mathf.Tau, 1).AsRelative();

                    downloadedMap = await downloadMap(map.GetProperty("fileUrl"), map.GetProperty("legacyId").GetString(), source);
                    isDownloaded = downloadedMap != null;

                    throbberTween.Kill();

                    downloadIcon.Rotation = 0;
                    isDownloading = false;
                }

                downloadButtonTween?.Kill();
                downloadButton.Disabled = false;

                bool hovered = downloadButton.IsHovered();

                downloadButtonTween = tweenDownloadButton(downloadButton, downloadIcon, downloadButtonStyle, hovered);

                downloadIcon.Texture = isDownloaded ? deleteIconTexture : downloadIconTexture;
            };

            _ = loadCover(map.GetProperty("covers").GetProperty("128"), coverImage, blurCoverImage, source);

            int difficulty = map.GetProperty("difficulty").GetInt32();
            string difficultyName = map.GetProperty("difficultyName").GetString();
            string difficultyText = string.IsNullOrEmpty(difficultyName) ? Constants.DIFFICULTIES[difficulty] : difficultyName;
            bool isRanked = map.GetProperty("isRanked").GetBoolean();
            var rankingPillStyle = (StyleBoxFlat)rankingPill.GetThemeStylebox("panel").Duplicate();
            var mapLength = TimeSpan.FromMilliseconds(map.GetProperty("length").GetDouble());

            titleLabel.Text = $"{map.GetProperty("artist").GetString()} - {map.GetProperty("title").GetString()}";
            mappersLabel.Text = $"[color=#c8c8c8]by[/color] {string.Join(", ", map.GetProperty("mappers").EnumerateArray().Select(x => x.GetProperty("name").GetString()))}";
            notablePill.Visible = map.GetProperty("mappers").EnumerateArray().Any(x => x.GetProperty("isNotable").GetBoolean());
            difficultyLabel.Text = difficultyText;
            difficultyLabel.LabelSettings = (LabelSettings)difficultyLabel.LabelSettings.Duplicate();
            difficultyLabel.LabelSettings.FontColor = Constants.DIFFICULTY_COLORS[difficulty];
            noteCountLabel.Text = $"{map.GetProperty("noteCount").GetInt32().ToString(CultureInfo.InvariantCulture)} notes";
            rankingLabel.Text = isRanked ? "RANKED" : "UNRANKED";

            rankingPillStyle.BgColor = isRanked ? Constants.RANKED_COLOR : Constants.UNRANKED_COLOR;
            rankingPill.AddThemeStyleboxOverride("panel", rankingPillStyle);

            durationLabel.Text =
                mapLength.TotalHours >= 1
                    ? mapLength.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
                    : mapLength.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        }
    }

    private static async Task<Map> downloadMap(JsonElement fileUrl, string mapId, CancellationTokenSource source)
    {
        var token = source.Token;

        byte[] buffer;

        try
        {
            buffer = await MapBrowserService.GetMapFile(fileUrl.GetString(), token);
        }
        catch (Exception exception)
        {
            await ToastNotification.Notify("Failed to download map", 2);
            Logger.Error(exception);
            return null;
        }

        try
        {
            var map = MapParser.PHXM(buffer, mapId);
            MapParser.Encode(map);
            Callable.From(() => MapParser.Instance.EmitSignal(MapParser.SignalName.MapsImportFinished, new[] { map })).CallDeferred();
            _ = ToastNotification.Notify("Map downloaded");
            return map;
        }
        catch (Exception exception)
        {
            await ToastNotification.Notify("Map is corrupted", 2);
            Logger.Error(exception);
            return null;
        }
    }

    private void onSearchTimerTimeout()
    {
        clearResults();
        _ = populate(buildQueryParameters());
    }

    private MapQueryParameters buildQueryParameters()
    {
        return new MapQueryParameters { Query = search.Text.Trim() };
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

    private static Tween tweenMapCard(PanelContainer mapCard, Panel blurCoverDim, Panel rightPanel, MarginContainer contentContainer, TextureRect downloadIcon, bool hover = false, double duration = 0.2)
    {
        Tween mapCardTween = mapCard.CreateTween().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut).SetParallel();
        mapCardTween.TweenProperty(blurCoverDim, "modulate", Color.Color8(255, 255, 255, (byte)(hover ? 64 : 0)), duration);
        mapCardTween.TweenProperty(rightPanel, "custom_minimum_size", new Vector2(hover ? 30 : 10, 0), duration);
        mapCardTween.TweenProperty(contentContainer, "theme_override_constants/margin_right", hover ? 30 : 10, duration);
        mapCardTween.TweenProperty(downloadIcon, "modulate", Color.Color8(255, 255, 255, (byte)(hover ? 204 : 0)), duration);

        return mapCardTween;
    }

    private static Tween tweenDownloadButton(Button downloadButton, TextureRect downloadIcon, StyleBoxFlat downloadButtonStyle, bool hover = false, bool pressed = false, double duration = 0.2)
    {
        Tween downloadButtonTween = downloadButton.CreateTween().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut).SetParallel();
        downloadButtonTween.TweenProperty(downloadIcon, "modulate", Color.Color8(255, 255, 255, (byte)(hover ? 255 : 204)), duration);
        downloadButtonTween.TweenProperty(downloadButtonStyle, "bg_color", Color.Color8(255, 255, 255, (byte)(pressed ? 70 : hover ? 40 : 0)), duration);

        return downloadButtonTween;
    }
}
