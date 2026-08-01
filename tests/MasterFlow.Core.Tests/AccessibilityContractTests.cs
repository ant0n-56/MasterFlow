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
