using System.Text.RegularExpressions;

namespace PhotoApp.Core.Profiles;

/// <summary>
/// La table des profils de sources (spec de phase 1, §6). L'ordre compte : le premier motif reconnu l'emporte.
/// Ajouter une source = ajouter un profil ici et ses cas dans SourceProfilesTests.
/// </summary>
public static class SourceProfiles
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private const string Ymd = @"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})";
    private const string Hms = @"(?<hour>\d{2})(?<minute>\d{2})(?<second>\d{2})";
    private const string IsoDate = @"(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})";
    private const string Apostrophe = "[’']";
    private const string AndroidCamera = @"^(?:IMG|VID|MVIMG|PANO)_" + Ymd + "_" + Hms + @"(?:_(?<counter>\d+))?(?:_[A-Z]+\d*)*$";

    public static IReadOnlyList<SourceProfile> All { get; } =
    [
        // Messageries : le nom fait foi.
        new("whatsapp", "WhatsApp", ProfileFamily.Messaging,
        [
            P(@"^(?:IMG|VID|AUD|PTT|STK|DOC)-" + Ymd + @"-WA(?<counter>\d+)$"),
            P(@"^WhatsApp (?:Image|Video) " + IsoDate + @" at (?<hour>\d{1,2})\.(?<minute>\d{2})\.(?<second>\d{2})(?:\s?(?<ampm>[AP]M))?$")
        ]),
        new("signal", "Signal", ProfileFamily.Messaging,
        [
            P(@"^signal-" + IsoDate + "-" + Hms + @"(?:[_-](?<counter>\d+))?$"),
            P(@"^signal-" + IsoDate + @"-(?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2})(?:-(?<fraction>\d{3}))?(?:[_-](?<counter>\d+))?$")
        ]),
        new("telegram", "Telegram", ProfileFamily.Messaging,
        [
            P(@"^(?:photo|video)_" + IsoDate + @"_(?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2})(?: \((?<counter>\d+)\))?$")
        ]),

        // Captures d'écran : le nom est l'heure de capture.
        new("capture-android-kde", "Capture Android / KDE Spectacle", ProfileFamily.Screenshot,
        [
            P(@"^Screenshot_" + Ymd + "[_-]" + Hms + @"(?:[_ -].*)?$"),
            P(@"^Screenshot_" + IsoDate + @"-(?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2})(?:[_ -].*)?$")
        ]),
        new("capture-windows", "Capture Windows (Outil Capture)", ProfileFamily.Screenshot,
        [
            P(@"^(?:Screenshot|Capture d" + Apostrophe + "écran) " + IsoDate + " " + Hms + @"(?: \((?<counter>\d+)\))?$")
        ]),
        new("capture-windows-impr-ecran", "Capture Windows (Impr. écran)", ProfileFamily.Screenshot,
        [
            P(@"^(?:Screenshot|Capture d" + Apostrophe + @"écran) \((?<counter>\d+)\)$")
        ]),
        new("capture-gnome", "Capture GNOME", ProfileFamily.Screenshot,
        [
            P(@"^(?:Capture d" + Apostrophe + "écran du|Screenshot from) " + IsoDate + @" (?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2})(?:-(?<counter>\d+))?$")
        ]),
        new("capture-macos", "Capture macOS", ProfileFamily.Screenshot,
        [
            P(@"^(?:Screenshot|Screen Shot|Capture d" + Apostrophe + "écran|Bildschirmfoto|Captura de pantalla) " + IsoDate
              + @" (?:at|à|um|a las) (?<hour>\d{1,2})[.:](?<minute>\d{2})[.:](?<second>\d{2})(?:\s?(?<ampm>[AP]M))?(?: \((?<counter>\d+)\))?$")
        ]),

        // Appareils.
        new("pixel", "Google Pixel", ProfileFamily.Camera,
        [
            P(@"^PXL_" + Ymd + "_" + Hms + @"(?<fraction>\d{3})(?:[~.].*)?$")
        ], TimeSemantics: NameTimeSemantics.Utc, MakePattern: P("^Google$")),
        new("xiaomi", "Xiaomi", ProfileFamily.Camera, [P(AndroidCamera)], MakePattern: P("^(?:Xiaomi|Redmi|POCO)$"), NameNeedsMake: true),
        new("huawei", "Huawei / Honor", ProfileFamily.Camera, [P(AndroidCamera)], MakePattern: P("^(?:HUAWEI|HONOR)$"), NameNeedsMake: true),
        new("oneplus", "OnePlus", ProfileFamily.Camera, [P(AndroidCamera)], MakePattern: P("^OnePlus$"), NameNeedsMake: true),
        new("android", "Appareil Android", ProfileFamily.Camera, [P(AndroidCamera)]),
        new("samsung", "Samsung", ProfileFamily.Camera,
        [
            P("^" + Ymd + "_" + Hms + @"(?:\((?<counter>\d+)\))?$")
        ], MakePattern: P("^samsung$")),
        new("nextcloud-ios", "iPhone (Nextcloud iOS)", ProfileFamily.Camera,
        [
            P(@"^(?<year>\d{2})-(?<month>\d{2})-(?<day>\d{2}) (?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2}) (?<counter>\d{4})$")
        ]),
        new("iphone", "iPhone", ProfileFamily.Camera,
        [
            P(@"^IMG_E?(?<counter>\d{4})$")
        ], MakePattern: P("^Apple$"), NameNeedsMake: true),

        // Exports et téléchargements.
        new("wechat", "WeChat", ProfileFamily.Export, [P(@"^mmexport(?<epoch>\d{13}|\d{10})(?!\d)")], TimeSemantics: NameTimeSemantics.Utc),
        new("facebook", "Facebook", ProfileFamily.Export, [P(@"^FB_IMG_(?<epoch>\d{13}|\d{10})(?!\d)")], TimeSemantics: NameTimeSemantics.Utc),
        new("snapchat", "Snapchat", ProfileFamily.Export, [P(@"^Snapchat-\d+$")], FolderPattern: P("^Snapchat$")),
        new("miniature-video", "Miniature téléchargée", ProfileFamily.Export, [P(@"^(?:hq|mq|sd|maxres)?default(?:-\w+)?$")]),
        new("epoch", "Horodatage epoch", ProfileFamily.Export, [P(@"^(?<epoch>\d{13}|\d{10})$")], TimeSemantics: NameTimeSemantics.Utc)
    ];

    private static Regex P(string pattern) => new(pattern, Options);
}
