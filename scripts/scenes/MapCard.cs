using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

public partial class MapCard : PanelContainer
{
    private Control mapHolder;
    private Control rightPanelContainer;

    private MarginContainer contentContainer;

    private VBoxContainer info;
    private VBoxContainer topText;
    private HBoxContainer bottomText;

    private PanelContainer coverHolder;
    private PanelContainer blurCoverHolder;
    private PanelContainer rankingPill;
    private PanelContainer notablePill;

    private Panel coverDim;
    private Panel blurCoverDim;
    private Panel rightPanel;

    private Button downloadButton;

    private Label titleLabel;
    private Label difficultyLabel;
    private Label noteCountLabel;
    private Label rankingLabel;
    private Label durationLabel;

    private RichTextLabel mappersLabel;

    private TextureRect coverImage;
    private TextureRect blurCoverImage;
    private TextureRect downloadIcon;

    private static readonly Texture2D downloadIconTexture = GD.Load<Texture2D>("res://user/skins/default/ui/buttons/import.png");
    private static readonly Texture2D deleteIconTexture = GD.Load<Texture2D>("res://user/skins/default/ui/buttons/delete.png");
    private static readonly Texture2D throbberTexture = GD.Load<Texture2D>("res://textures/throbber.png");

    private Tween mapCardTween;
    private Tween downloadButtonTween;

    private StyleBoxFlat downloadButtonStyle;

    private Map downloadedMap;

    private DownloadState state = DownloadState.NotDownloaded;

    private enum DownloadState
    {
        NotDownloaded,
        Downloading,
        Downloaded,
        Failed
    }

    public override void _Ready()
    {
        mapHolder = GetNode<Control>("Row/Map");
        rightPanelContainer = GetNode<Control>("Row/RightPanel");

        contentContainer = mapHolder.GetNode<MarginContainer>("Content");

        info = mapHolder.GetNode<VBoxContainer>("Content/Info");
        topText = info.GetNode<VBoxContainer>("Top/Text");
        bottomText = info.GetNode<HBoxContainer>("Bottom");

        coverHolder = GetNode<PanelContainer>("Row/CoverHolder");
        blurCoverHolder = mapHolder.GetNode<PanelContainer>("BlurCoverHolder");
        rankingPill = bottomText.GetNode<PanelContainer>("RankingPill");
        notablePill = bottomText.GetNode<PanelContainer>("NotablePill");

        coverDim = coverHolder.GetNode<Panel>("Dim");
        blurCoverDim = blurCoverHolder.GetNode<Panel>("Dim");
        rightPanel = rightPanelContainer.GetNode<Panel>("Panel");

        downloadButton = rightPanel.GetNode<Button>("Download");

        titleLabel = topText.GetNode<Label>("TitleHolder/Title");
        difficultyLabel = topText.GetNode<Label>("Details/VBoxContainer/Difficulty");
        noteCountLabel = topText.GetNode<Label>("Details/VBoxContainer/Notes");
        rankingLabel = rankingPill.GetNode<Label>("Ranking");
        durationLabel = bottomText.GetNode<Label>("Duration");

        mappersLabel = topText.GetNode<RichTextLabel>("Details/VBoxContainer/Mapper");

        coverImage = coverHolder.GetNode<TextureRect>("Cover");
        blurCoverImage = mapHolder.GetNode<TextureRect>("BlurCoverHolder/BlurCover");
        downloadIcon = downloadButton.GetNode<TextureRect>("Icon");

        downloadButtonStyle = (StyleBoxFlat)downloadButton.GetThemeStylebox("normal").Duplicate();

        downloadButton.AddThemeStyleboxOverride("normal", downloadButtonStyle);
        downloadButton.AddThemeStyleboxOverride("hover", downloadButtonStyle);
        downloadButton.AddThemeStyleboxOverride("pressed", downloadButtonStyle);
        downloadButton.AddThemeStyleboxOverride("hover_pressed", downloadButtonStyle);
    }

    public void Bind(JsonElement map, CancellationTokenSource source)
    {
        titleLabel.Text = $"{map.GetProperty("artist").GetString()} - {map.GetProperty("title").GetString()}";
        mappersLabel.Text = $"[color=#c8c8c8]by[/color] {string.Join(", ", map.GetProperty("mappers").EnumerateArray().Select(x => x.GetProperty("name").GetString()))}";
        notablePill.Visible = map.GetProperty("mappers").EnumerateArray().Any(x => x.GetProperty("isNotable").GetBoolean());

        int difficulty = map.GetProperty("difficulty").GetInt32();
        string difficultyName = map.GetProperty("difficultyName").GetString();
        string difficultyText = string.IsNullOrEmpty(difficultyName) ? Constants.DIFFICULTIES[difficulty] : difficultyName;

        difficultyLabel.Text = difficultyText;
        difficultyLabel.LabelSettings = (LabelSettings)difficultyLabel.LabelSettings.Duplicate();
        difficultyLabel.LabelSettings.FontColor = Constants.DIFFICULTY_COLORS[difficulty];

        noteCountLabel.Text = $"{map.GetProperty("noteCount").GetInt32().ToString(CultureInfo.InvariantCulture)} notes";

        bool isRanked = map.GetProperty("isRanked").GetBoolean();
        var rankingPillStyle = (StyleBoxFlat)rankingPill.GetThemeStylebox("panel").Duplicate();
        rankingLabel.Text = isRanked ? "RANKED" : "UNRANKED";
        rankingPillStyle.BgColor = isRanked ? Constants.RANKED_COLOR : Constants.UNRANKED_COLOR;
        rankingPill.AddThemeStyleboxOverride("panel", rankingPillStyle);

        var mapLength = TimeSpan.FromMilliseconds(map.GetProperty("length").GetDouble());
        durationLabel.Text = mapLength.TotalHours >= 1
            ? mapLength.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : mapLength.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

        downloadedMap = getDownloadedMap(map.GetProperty("noteHash").GetString());
        state = downloadedMap != null ? DownloadState.Downloaded : DownloadState.NotDownloaded;
        updateDownloadIcon();

        _ = loadCover(map.GetProperty("covers").GetProperty("128"), coverImage, blurCoverImage, source);
    }

    private static async Task loadCover(JsonElement coverUrl, TextureRect coverTexture, TextureRect blurCoverTexture, CancellationTokenSource source)
    {
        var token = source.Token;

        Texture2D cover = null;

        if (!token.IsCancellationRequested && coverUrl.ValueKind != JsonValueKind.Null)
        {
            var coverImage = await MapBrowserService.GetCoverImage(coverUrl.GetString(), token);
            cover = ImageTexture.CreateFromImage(coverImage);
        }

        if (!(IsInstanceValid(coverTexture) && IsInstanceValid(blurCoverTexture)))
            return;

        coverTexture.Texture = cover;
        blurCoverTexture.Texture = cover;
    }

    private static Map getDownloadedMap(string hash)
    {
        return MapManager.Maps.FirstOrDefault(m => m.ObjectHash == hash);
    }

    private void updateDownloadIcon()
    {
        downloadIcon.Texture = state == DownloadState.Downloaded ? deleteIconTexture : downloadIconTexture;
    }
}
