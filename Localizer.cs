using System.Globalization;

namespace VictusModeSwitch;

internal sealed class Localizer
{
    public Localizer(string? requestedLanguage)
    {
        RequestedLanguage = NormalizeRequestedLanguage(requestedLanguage);
        Language = RequestedLanguage == "system" ? GetSystemLanguage() : RequestedLanguage;
    }

    public string RequestedLanguage { get; }
    public string Language { get; }
    public string this[string key] => Translate(Language, key);
    public string Format(string key, params object[] values) => string.Format(this[key], values);

    public string ModeName(AppMode mode) => this[mode switch
    {
        AppMode.Eco => "ModeEco",
        AppMode.Performance => "ModePerformance",
        _ => "ModeStandard"
    }];

    public string ModeDescription(AppMode mode) => this[mode switch
    {
        AppMode.Eco => "ModeEcoDescription",
        AppMode.Performance => "ModePerformanceDescription",
        _ => "ModeStandardDescription"
    }];

    public IReadOnlyList<LanguageOption> GetLanguageOptions() => new[]
    {
        new LanguageOption("system", this["LanguageSystem"]),
        new LanguageOption("ru", "Русский"),
        new LanguageOption("uk", "Українська"),
        new LanguageOption("en", "English")
    };

    private static string NormalizeRequestedLanguage(string? language) => language?.ToLowerInvariant() switch
    {
        "ru" => "ru",
        "uk" => "uk",
        "en" => "en",
        _ => "system"
    };

    private static string GetSystemLanguage() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "ru" => "ru",
        "uk" => "uk",
        _ => "en"
    };

    private static string Translate(string language, string key) => language switch
    {
        "ru" => Russian(key),
        "uk" => Ukrainian(key),
        _ => English(key)
    };

    private static string Russian(string key) => key switch
    {
        "ModeEco" => "Eco",
        "ModeStandard" => "Стандартный",
        "ModePerformance" => "Performance",
        "ModeEcoDescription" => "Прохладный профиль BIOS и экономия энергии",
        "ModeStandardDescription" => "Сбалансированный тепловой профиль HP",
        "ModePerformanceDescription" => "Максимальный тепловой профиль HP",
        "MaxFan" => "Максимальные вентиляторы",
        "MaxFanOn" => "Max Fan включён",
        "MaxFanOff" => "Max Fan выключен",
        "MaxFanOnDescription" => "Вентиляторы работают на максимальной скорости",
        "MaxFanOffDescription" => "Автоматическое управление вентиляторами",
        "Settings" => "Настройки",
        "CheckUpdates" => "Проверить обновления",
        "OpenLog" => "Открыть журнал",
        "Exit" => "Выход",
        "ModeError" => "Не удалось переключить режим",
        "FanError" => "Не удалось переключить вентиляторы",
        "PartialMode" => "Режим включён частично. Подробности в журнале",
        "General" => "Основное",
        "Power" => "Питание",
        "About" => "О программе",
        "GeneralTitle" => "Основное",
        "GeneralSubtitle" => "Режимы, аппаратная кнопка и уведомления",
        "QuickControls" => "Быстрое управление",
        "CurrentMode" => "Текущий режим",
        "ChangingMode" => "Применение режима...",
        "ModeApplied" => "Режим применён",
        "FanDescription" => "Независимо включает максимальные обороты вентиляторов",
        "Language" => "Язык приложения",
        "LanguageSystem" => "Как в Windows",
        "LanguageDescription" => "По умолчанию используется язык интерфейса Windows",
        "Notifications" => "Уведомления о переключении",
        "NotificationsDescription" => "Показывать компактную плашку при использовании кнопки с ромбом",
        "DiamondButton" => "Кнопка с ромбом",
        "DiamondDescription" => "Жесты определяются по быстрым последовательным нажатиям",
        "SinglePress" => "Одно нажатие",
        "DoublePress" => "Два нажатия",
        "TriplePress" => "Три нажатия",
        "ToggleModes" => "Стандартный ↔ Performance",
        "ToggleMaxFan" => "Max Fan вкл. / выкл.",
        "EnableEco" => "Включить Eco",
        "PowerTitle" => "Питание Windows",
        "PowerSubtitle" => "Дополнительная настройка активной схемы питания",
        "PowerTuning" => "Настраивать Windows вместе с режимом",
        "PowerTuningDescription" => "Performance отдаёт приоритет скорости, Eco снижает энергопотребление. Стандартный возвращает исходные значения.",
        "EcoAcLimit" => "Предел процессора Eco от сети",
        "EcoAcDescription" => "Максимальная производительность процессора при подключённом питании",
        "EcoBatteryLimit" => "Предел процессора Eco от батареи",
        "EcoBatteryDescription" => "Максимальная производительность процессора без зарядного устройства",
        "PowerRestoreNote" => "Перед первым изменением приложение сохраняет параметры активной схемы и восстанавливает их в Стандартном режиме.",
        "AboutTitle" => "Victus Mode Switch",
        "AboutSubtitle" => "Лёгкий контроллер режимов для поддерживаемых HP Victus",
        "Version" => "Версия {0}",
        "SupportedHardware" => "Поддерживаемое устройство",
        "SupportedHardwareDescription" => "HP Victus 15-fa0020ua, системная плата 8A4F, Thermal Policy V0",
        "Updates" => "Обновления",
        "CheckForUpdates" => "Проверять обновления автоматически",
        "CheckForUpdatesDescription" => "Проверка GitHub Releases выполняется не чаще одного раза в сутки",
        "CheckNow" => "Проверить сейчас",
        "CheckingUpdate" => "Проверяем GitHub Releases...",
        "UpToDate" => "Установлена актуальная версия",
        "UpdateAvailable" => "Доступна версия {0}",
        "UpdateError" => "Не удалось проверить обновления",
        "OpenGitHub" => "Открыть проект на GitHub",
        "BuiltWithAi" => "Проект разработан при помощи искусственного интеллекта и проверен на одном ноутбуке.",
        "RiskNoticeTitle" => "Использование на свой риск",
        "RiskNotice" => "Программа обращается к недокументированному интерфейсу HP BIOS. Автор и участники не несут ответственности за повреждения, потерю данных или гарантийные последствия.",
        "UpdateReadyTitle" => "Доступно обновление",
        "UpdateReadyDescription" => "Victus Mode Switch {0} готов к установке.",
        "ReleaseNotes" => "Что изменилось",
        "InstallUpdate" => "Скачать и установить",
        "Later" => "Позже",
        "DownloadingUpdate" => "Загрузка установщика... {0}%",
        "VerifyingUpdate" => "Проверка SHA-256...",
        "UpdateFailed" => "Обновление не установлено: {0}",
        _ => English(key)
    };

    private static string Ukrainian(string key) => key switch
    {
        "ModeEco" => "Eco",
        "ModeStandard" => "Стандартний",
        "ModePerformance" => "Performance",
        "ModeEcoDescription" => "Прохолодний профіль BIOS та економія енергії",
        "ModeStandardDescription" => "Збалансований тепловий профіль HP",
        "ModePerformanceDescription" => "Максимальний тепловий профіль HP",
        "MaxFan" => "Максимальні вентилятори",
        "MaxFanOn" => "Max Fan увімкнено",
        "MaxFanOff" => "Max Fan вимкнено",
        "MaxFanOnDescription" => "Вентилятори працюють на максимальній швидкості",
        "MaxFanOffDescription" => "Автоматичне керування вентиляторами",
        "Settings" => "Налаштування",
        "CheckUpdates" => "Перевірити оновлення",
        "OpenLog" => "Відкрити журнал",
        "Exit" => "Вихід",
        "ModeError" => "Не вдалося перемкнути режим",
        "FanError" => "Не вдалося перемкнути вентилятори",
        "PartialMode" => "Режим застосовано частково. Подробиці в журналі",
        "General" => "Основне",
        "Power" => "Живлення",
        "About" => "Про програму",
        "GeneralTitle" => "Основне",
        "GeneralSubtitle" => "Режими, апаратна кнопка та сповіщення",
        "QuickControls" => "Швидке керування",
        "CurrentMode" => "Поточний режим",
        "ChangingMode" => "Застосування режиму...",
        "ModeApplied" => "Режим застосовано",
        "FanDescription" => "Незалежно вмикає максимальні оберти вентиляторів",
        "Language" => "Мова програми",
        "LanguageSystem" => "Як у Windows",
        "LanguageDescription" => "Типово використовується мова інтерфейсу Windows",
        "Notifications" => "Сповіщення про перемикання",
        "NotificationsDescription" => "Показувати компактне повідомлення під час використання кнопки з ромбом",
        "DiamondButton" => "Кнопка з ромбом",
        "DiamondDescription" => "Жести визначаються за швидкими послідовними натисканнями",
        "SinglePress" => "Одне натискання",
        "DoublePress" => "Два натискання",
        "TriplePress" => "Три натискання",
        "ToggleModes" => "Стандартний ↔ Performance",
        "ToggleMaxFan" => "Max Fan увімк. / вимк.",
        "EnableEco" => "Увімкнути Eco",
        "PowerTitle" => "Живлення Windows",
        "PowerSubtitle" => "Додаткове налаштування активної схеми живлення",
        "PowerTuning" => "Налаштовувати Windows разом із режимом",
        "PowerTuningDescription" => "Performance надає пріоритет швидкості, Eco знижує енергоспоживання. Стандартний повертає початкові значення.",
        "EcoAcLimit" => "Межа процесора Eco від мережі",
        "EcoAcDescription" => "Максимальна продуктивність процесора з підключеним живленням",
        "EcoBatteryLimit" => "Межа процесора Eco від батареї",
        "EcoBatteryDescription" => "Максимальна продуктивність процесора без зарядного пристрою",
        "PowerRestoreNote" => "Перед першою зміною програма зберігає параметри активної схеми та відновлює їх у Стандартному режимі.",
        "AboutTitle" => "Victus Mode Switch",
        "AboutSubtitle" => "Легкий контролер режимів для підтримуваних HP Victus",
        "Version" => "Версія {0}",
        "SupportedHardware" => "Підтримуваний пристрій",
        "SupportedHardwareDescription" => "HP Victus 15-fa0020ua, системна плата 8A4F, Thermal Policy V0",
        "Updates" => "Оновлення",
        "CheckForUpdates" => "Перевіряти оновлення автоматично",
        "CheckForUpdatesDescription" => "Перевірка GitHub Releases виконується не частіше одного разу на добу",
        "CheckNow" => "Перевірити зараз",
        "CheckingUpdate" => "Перевіряємо GitHub Releases...",
        "UpToDate" => "Встановлено актуальну версію",
        "UpdateAvailable" => "Доступна версія {0}",
        "UpdateError" => "Не вдалося перевірити оновлення",
        "OpenGitHub" => "Відкрити проєкт на GitHub",
        "BuiltWithAi" => "Проєкт розроблено за допомогою штучного інтелекту та перевірено на одному ноутбуці.",
        "RiskNoticeTitle" => "Використання на власний ризик",
        "RiskNotice" => "Програма звертається до недокументованого інтерфейсу HP BIOS. Автор та учасники не відповідають за пошкодження, втрату даних або гарантійні наслідки.",
        "UpdateReadyTitle" => "Доступне оновлення",
        "UpdateReadyDescription" => "Victus Mode Switch {0} готовий до встановлення.",
        "ReleaseNotes" => "Що змінилося",
        "InstallUpdate" => "Завантажити й установити",
        "Later" => "Пізніше",
        "DownloadingUpdate" => "Завантаження інсталятора... {0}%",
        "VerifyingUpdate" => "Перевірка SHA-256...",
        "UpdateFailed" => "Оновлення не встановлено: {0}",
        _ => English(key)
    };

    private static string English(string key) => key switch
    {
        "ModeEco" => "Eco",
        "ModeStandard" => "Standard",
        "ModePerformance" => "Performance",
        "ModeEcoDescription" => "Cooler BIOS profile and lower power use",
        "ModeStandardDescription" => "Balanced HP thermal profile",
        "ModePerformanceDescription" => "Maximum HP thermal profile",
        "MaxFan" => "Maximum fan speed",
        "MaxFanOn" => "Max Fan on",
        "MaxFanOff" => "Max Fan off",
        "MaxFanOnDescription" => "Fans are running at maximum speed",
        "MaxFanOffDescription" => "Automatic fan control",
        "Settings" => "Settings",
        "CheckUpdates" => "Check for updates",
        "OpenLog" => "Open log",
        "Exit" => "Exit",
        "ModeError" => "Could not switch mode",
        "FanError" => "Could not switch fan mode",
        "PartialMode" => "Mode was applied partially. See the log for details",
        "General" => "General",
        "Power" => "Power",
        "About" => "About",
        "GeneralTitle" => "General",
        "GeneralSubtitle" => "Modes, the hardware button, and notifications",
        "QuickControls" => "Quick controls",
        "CurrentMode" => "Current mode",
        "ChangingMode" => "Applying mode...",
        "ModeApplied" => "Mode applied",
        "FanDescription" => "Independently enables the maximum fan speed",
        "Language" => "App language",
        "LanguageSystem" => "Use Windows language",
        "LanguageDescription" => "The Windows display language is used by default",
        "Notifications" => "Switch notifications",
        "NotificationsDescription" => "Show a compact toast when the diamond button is used",
        "DiamondButton" => "Diamond button",
        "DiamondDescription" => "Gestures are recognized from quick consecutive presses",
        "SinglePress" => "One press",
        "DoublePress" => "Two presses",
        "TriplePress" => "Three presses",
        "ToggleModes" => "Standard ↔ Performance",
        "ToggleMaxFan" => "Toggle Max Fan",
        "EnableEco" => "Enable Eco",
        "PowerTitle" => "Windows power",
        "PowerSubtitle" => "Optional tuning of the active Windows power plan",
        "PowerTuning" => "Tune Windows together with the mode",
        "PowerTuningDescription" => "Performance favors speed and Eco lowers power use. Standard restores the original values.",
        "EcoAcLimit" => "Eco CPU limit on AC",
        "EcoAcDescription" => "Maximum processor performance while plugged in",
        "EcoBatteryLimit" => "Eco CPU limit on battery",
        "EcoBatteryDescription" => "Maximum processor performance without the charger",
        "PowerRestoreNote" => "The active plan is backed up before the first change and restored in Standard mode.",
        "AboutTitle" => "Victus Mode Switch",
        "AboutSubtitle" => "A lightweight mode controller for supported HP Victus laptops",
        "Version" => "Version {0}",
        "SupportedHardware" => "Supported device",
        "SupportedHardwareDescription" => "HP Victus 15-fa0020ua, system board 8A4F, Thermal Policy V0",
        "Updates" => "Updates",
        "CheckForUpdates" => "Check for updates automatically",
        "CheckForUpdatesDescription" => "GitHub Releases is checked no more than once per day",
        "CheckNow" => "Check now",
        "CheckingUpdate" => "Checking GitHub Releases...",
        "UpToDate" => "You have the latest version",
        "UpdateAvailable" => "Version {0} is available",
        "UpdateError" => "Could not check for updates",
        "OpenGitHub" => "Open project on GitHub",
        "BuiltWithAi" => "This project was developed with AI assistance and tested on one laptop.",
        "RiskNoticeTitle" => "Use at your own risk",
        "RiskNotice" => "The app calls an undocumented HP BIOS interface. The author and contributors accept no liability for damage, data loss, or warranty consequences.",
        "UpdateReadyTitle" => "Update available",
        "UpdateReadyDescription" => "Victus Mode Switch {0} is ready to install.",
        "ReleaseNotes" => "What's new",
        "InstallUpdate" => "Download and install",
        "Later" => "Later",
        "DownloadingUpdate" => "Downloading installer... {0}%",
        "VerifyingUpdate" => "Verifying SHA-256...",
        "UpdateFailed" => "Update was not installed: {0}",
        _ => key
    };
}

internal sealed record LanguageOption(string Code, string Name);
