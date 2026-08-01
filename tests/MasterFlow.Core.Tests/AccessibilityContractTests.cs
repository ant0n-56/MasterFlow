using System.Xml.Linq;

namespace MasterFlow.Core.Tests;

public sealed class AccessibilityContractTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void MainWindow_AllButtonsHaveAccessibleNames()
    {
        var document = LoadMainWindow();
        var buttons = document.Descendants(Presentation + "Button").ToList();

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button =>
            Assert.False(string.IsNullOrWhiteSpace(button.Attribute("AutomationProperties.Name")?.Value)));
    }

    [Fact]
    public void MainWindow_AllEditableFieldsHaveAccessibleNames()
    {
        var document = LoadMainWindow();
        var fields = document.Descendants()
            .Where(element => element.Name == Presentation + "TextBox" ||
                              element.Name == Presentation + "DatePicker" ||
                              element.Name == Presentation + "ComboBox")
            .ToList();

        Assert.NotEmpty(fields);
        Assert.All(fields, field =>
            Assert.False(string.IsNullOrWhiteSpace(field.Attribute("AutomationProperties.Name")?.Value)));
    }

    [Fact]
    public void MainWindow_NavigationIsAnAccessibleTree()
    {
        var tree = LoadMainWindow().Descendants(Presentation + "TreeView").Single();

        Assert.Equal("Дерево разделов программы", tree.Attribute("AutomationProperties.Name")?.Value);
        Assert.NotEmpty(tree.Descendants(Presentation + "TreeViewItem"));
    }

    [Fact]
    public void MainWindow_StatusUsesPoliteLiveAnnouncements()
    {
        var status = LoadMainWindow().Descendants(Presentation + "TextBlock")
            .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "StatusText");

        Assert.Equal("Polite", status.Attribute("AutomationProperties.LiveSetting")?.Value);
        Assert.Equal("{Binding Text, RelativeSource={RelativeSource Self}}", status.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("Состояние программы", status.Attribute("AutomationProperties.HelpText")?.Value);
    }

    [Fact]
    public void MainWindow_DoesNotSelectNavigationBeforeWindowIsLoaded()
    {
        var items = LoadMainWindow().Descendants(Presentation + "TreeViewItem");

        Assert.DoesNotContain(items, item => item.Attribute("IsSelected")?.Value == "True");
    }

    [Fact]
    public void MainWindow_ListsGiveEveryItemAnAccessibleSummary()
    {
        var lists = LoadMainWindow().Descendants(Presentation + "ListBox").ToList();

        Assert.NotEmpty(lists);
        Assert.All(lists, list => Assert.Contains(
            list.Descendants(Presentation + "Setter"),
            setter => setter.Attribute("Property")?.Value == "AutomationProperties.Name" &&
                      setter.Attribute("Value")?.Value == "{Binding AccessibleSummary}"));
    }

    [Fact]
    public void MainWindow_AvitoBrowserHasAnAccessibleName()
    {
        var browser = LoadMainWindow().Descendants()
            .Single(element => element.Name.LocalName == "WebView2");

        Assert.Equal("Страница Avito для выбора отзывов", browser.Attribute("AutomationProperties.Name")?.Value);
        Assert.False(string.IsNullOrWhiteSpace(browser.Attribute("AutomationProperties.HelpText")?.Value));
    }

    [Fact]
    public void MainWindow_ReviewSummaryUsesLiveAnnouncements()
    {
        var summary = LoadMainWindow().Descendants(Presentation + "TextBlock")
            .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "ReviewImportSummaryText");

        Assert.Equal("Polite", summary.Attribute("AutomationProperties.LiveSetting")?.Value);
    }

    [Fact]
    public void MainWindow_ImportedReviewsWrapLongTextWithoutHorizontalScrolling()
    {
        var list = LoadMainWindow().Descendants(Presentation + "ListBox")
            .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "ImportedReviewsList");
        var reviewText = list.Descendants(Presentation + "TextBlock").Single();

        Assert.Equal("Disabled", list.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value);
        Assert.Equal("Wrap", reviewText.Attribute("TextWrapping")?.Value);
        Assert.Equal("{Binding AccessibleSummary}", reviewText.Attribute("Text")?.Value);
    }

    [Fact]
    public void MainWindow_ReviewAnalysisHasAccessibleControlsAndLiveSummary()
    {
        var document = LoadMainWindow();
        var analyzeButton = document.Descendants(Presentation + "Button")
            .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "AnalyzeReviewsButton");
        var summary = document.Descendants(Presentation + "TextBlock")
            .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "ReviewAnalysisSummaryText");
        var analysisLists = new[] { "ReviewStrengthsList", "ReviewAttentionList", "ReviewRecommendationsList" }
            .Select(name => document.Descendants(Presentation + "ListBox")
                .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == name))
            .ToArray();

        Assert.Equal("Проанализировать импортированные отзывы", analyzeButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("Polite", summary.Attribute("AutomationProperties.LiveSetting")?.Value);
        Assert.All(analysisLists, list =>
            Assert.False(string.IsNullOrWhiteSpace(list.Attribute("AutomationProperties.Name")?.Value)));

        var source = File.ReadAllText(Path.Combine(
            FindProjectRoot().FullName,
            "src",
            "MasterFlow.App",
            "MainWindow.xaml.cs"));
        Assert.Contains("ItemContainerGenerator.ContainerFromIndex(0)", source);
        Assert.Contains("item.Focus()", source);
    }

    [Fact]
    public void MainWindow_ClientCardHasAccessibleSearchEditHistoryAndDestructiveConfirmation()
    {
        var document = LoadMainWindow();
        string[] requiredFields =
        [
            "ClientSearchTextBox",
            "ClientCardNameTextBox",
            "ClientCardContactTextBox",
            "ClientCardSourceTextBox",
            "ClientCardNotesTextBox"
        ];
        string[] requiredButtons =
        [
            "Сохранить изменения карточки клиента",
            "Создать следующую запись для выбранного клиента",
            "Удалить выбранного клиента и его записи"
        ];

        Assert.All(requiredFields, name =>
        {
            var field = document.Descendants(Presentation + "TextBox")
                .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == name);
            Assert.False(string.IsNullOrWhiteSpace(field.Attribute("AutomationProperties.Name")?.Value));
        });
        Assert.All(requiredButtons, name => Assert.Contains(
            document.Descendants(Presentation + "Button"),
            button => button.Attribute("AutomationProperties.Name")?.Value == name));

        var history = document.Descendants(Presentation + "ListBox")
            .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "ClientHistoryList");
        Assert.Equal("История и будущие записи выбранного клиента", history.Attribute("AutomationProperties.Name")?.Value);

        var source = LoadMainWindowSource();
        Assert.Contains("new ConfirmDeleteDialog", source);
        var dialogPath = Path.Combine(
            FindProjectRoot().FullName,
            "src",
            "MasterFlow.App",
            "ConfirmDeleteDialog.xaml");
        var dialog = XDocument.Load(dialogPath);
        var dialogButtons = dialog.Descendants(Presentation + "Button").ToArray();
        Assert.Equal(2, dialogButtons.Length);
        Assert.All(dialogButtons, button =>
            Assert.False(string.IsNullOrWhiteSpace(button.Attribute("AutomationProperties.Name")?.Value)));
        var cancel = dialogButtons.Single(button => button.Attribute("IsCancel")?.Value == "True");
        Assert.Equal("True", cancel.Attribute("IsDefault")?.Value);
    }

    [Fact]
    public void MainWindow_ProtectsWorkspaceForCurrentWindowsUser()
    {
        var source = File.ReadAllText(Path.Combine(
            FindProjectRoot().FullName,
            "src",
            "MasterFlow.App",
            "WindowsWorkspaceProtector.cs"));
        var windowSource = LoadMainWindowSource();

        Assert.Contains("DataProtectionScope.CurrentUser", source);
        Assert.Contains("Environment.SpecialFolder.LocalApplicationData", windowSource);
        Assert.Contains("MASTERFLOW_DATA_FOLDER", windowSource);
    }

    [Fact]
    public void MainWindow_AutomaticallyImportsWhenReviewDialogAppears()
    {
        var source = File.ReadAllText(Path.Combine(
            FindProjectRoot().FullName,
            "src",
            "MasterFlow.App",
            "MainWindow.xaml.cs"));

        Assert.Contains("MutationObserver", source);
        Assert.Contains("DOMContentLoaded", source);
        Assert.Contains("ReviewDetectionTimer_Tick", source);
        Assert.Contains("ReviewDialogVisibleScript", source);
        Assert.Contains("сначала\\s+новые", source);
        Assert.Contains("masterflow:reviews-visible", source);
        Assert.Contains("ImportReviewsFromOpenPageAsync(isAutomatic: true)", source);
    }

    [Fact]
    public void MainWindow_AutomaticImportScrollsReviewsAndReadsOverallRating()
    {
        var source = File.ReadAllText(Path.Combine(
            FindProjectRoot().FullName,
            "src",
            "MasterFlow.App",
            "MainWindow.xaml.cs"));

        Assert.Contains("CollectReviewBlocksAsync", source);
        Assert.Contains("ReviewResetScrollScript", source);
        Assert.Contains("scrollable.scrollTop", source);
        Assert.Contains("OverallRating", source);
    }

    private static XDocument LoadMainWindow()
    {
        var directory = FindProjectRoot();
        var path = Path.Combine(directory.FullName, "src", "MasterFlow.App", "MainWindow.xaml");
        return XDocument.Load(path);
    }

    private static string LoadMainWindowSource() => File.ReadAllText(Path.Combine(
        FindProjectRoot().FullName,
        "src",
        "MasterFlow.App",
        "MainWindow.xaml.cs"));

    private static DirectoryInfo FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MasterFlow.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory;
    }
}
