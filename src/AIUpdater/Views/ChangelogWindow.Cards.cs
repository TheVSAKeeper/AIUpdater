using AIUpdater.Infrastructure;
using AIUpdater.Models;
using AIUpdater.Services;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace AIUpdater.Views;

public partial class ChangelogWindow
{
    private UIElement CreateReleaseCard(ChangelogRelease release, int index, UpdateStage? stage)
    {
        var releaseKey = GetReleaseKey(release);
        var isLatest = index == 0;
        var isTranslating = string.Equals(_translatingReleaseKey, releaseKey, StringComparison.OrdinalIgnoreCase);
        var showRuForCard = _showTranslation && release.HasTranslation && !_originalOverrideReleaseKeys.Contains(releaseKey);
        var isInstalled = stage?.InstalledVersion is { Length: > 0 } inst
                          && string.Equals(TrimVersion(release.Version), TrimVersion(inst), StringComparison.OrdinalIgnoreCase);
        var isAvailable = stage?.AvailableVersion is { Length: > 0 } avail
                          && stage.HasUpdate
                          && string.Equals(TrimVersion(release.Version), TrimVersion(avail), StringComparison.OrdinalIgnoreCase);

        var cardBorder = new Border
        {
            Tag = releaseKey,
            Background = new SolidColorBrush(Color.FromRgb(16, 26, 20)),
            BorderBrush = isLatest
                ? new SolidColorBrush(Color.FromRgb(42, 82, 61))
                : (Brush)FindResource("BorderBrush"),
            BorderThickness = new(1),
            CornerRadius = new(8),
            Padding = new(16, 12, 16, 12),
            Margin = new(0, 0, 0, 10),
        };

        var rootStack = new StackPanel();
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var displayTitle = showRuForCard && !string.IsNullOrWhiteSpace(release.TranslatedTitle)
            ? release.TranslatedTitle!
            : !string.IsNullOrWhiteSpace(release.Title) ? release.Title : $"v{release.Version}";

        titleWrap.Children.Add(new TextBlock
        {
            Text = displayTitle,
            FontSize = _baseFontSize + 2,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new(0, 0, 10, 2),
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (!string.IsNullOrWhiteSpace(release.PublishedAt))
        {
            titleWrap.Children.Add(new TextBlock
            {
                Text = release.PublishedAt,
                FontSize = Math.Max(11, _baseFontSize - 2),
                Foreground = (Brush)FindResource("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new(0, 0, 10, 2),
            });
        }

        if (isLatest)
        {
            titleWrap.Children.Add(CreateBadge("Свежий", Color.FromRgb(22, 46, 34), Color.FromRgb(127, 226, 174)));
        }

        if (isInstalled)
        {
            titleWrap.Children.Add(CreateBadge("Установлено", Color.FromRgb(22, 46, 34), Color.FromRgb(127, 226, 174)));
        }
        else if (isAvailable)
        {
            titleWrap.Children.Add(CreateBadge("Доступно", Color.FromRgb(48, 39, 18), Color.FromRgb(252, 211, 77)));
        }

        if (release.HasTranslation && showRuForCard)
        {
            titleWrap.Children.Add(CreateBadge("Обработано ИИ", Color.FromRgb(22, 42, 34), Color.FromRgb(147, 197, 172)));
        }

        headerGrid.Children.Add(titleWrap);

        var rightHeader = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (isTranslating)
        {
            var cancelBtn = CreateCompactHeaderButton("Отменить", "Остановить обработку этого релиза");
            cancelBtn.Click += (_, _) => CancelSafe(_translationCancellation);
            rightHeader.Children.Add(cancelBtn);
        }
        else if (!release.HasTranslation)
        {
            var hasError = _translationErrors.ContainsKey(releaseKey);
            var processBtn = CreateCompactHeaderButton(
                hasError ? "Повторить обработку" : "Обработать этот релиз",
                "Перевести изменения на русский и составить резюме релиза и блоков через Antigravity Flash");
            processBtn.Click += async (_, _) => await TranslateReleaseOnDemandAsync(release, true);
            rightHeader.Children.Add(processBtn);
        }
        else
        {
            var toggleCardLangBtn = CreateCompactHeaderButton(
                showRuForCard ? "Оригинал" : "Русский (ИИ)",
                "Переключить между русским ИИ-обзором и оригиналом для этого релиза");
            toggleCardLangBtn.Click += (_, _) =>
            {
                if (showRuForCard)
                {
                    _originalOverrideReleaseKeys.Add(releaseKey);
                }
                else
                {
                    _originalOverrideReleaseKeys.Remove(releaseKey);
                    _showTranslation = true;
                }

                if (_currentDocument is not null)
                {
                    RenderDocument(_currentDocument, _currentStage);
                }
            };
            rightHeader.Children.Add(toggleCardLangBtn);

            var reprocessBtn = CreateCompactHeaderButton(
                "Обработать заново",
                "Повторно сформировать перевод и резюме этого релиза через Antigravity Flash");
            reprocessBtn.Click += async (_, _) => await TranslateReleaseOnDemandAsync(release, true);
            rightHeader.Children.Add(reprocessBtn);
        }

        var groups = showRuForCard && release.TranslatedGroups is { Count: > 0 }
            ? release.TranslatedGroups
            : release.Groups;

        var totalItems = groups.Sum(g => g.Items.Count);
        var isReleaseCollapsed = _collapsedReleaseKeys.Contains(releaseKey);
        var bodyStack = new StackPanel
        {
            Margin = new(0, 6, 0, 0),
            Visibility = isReleaseCollapsed ? Visibility.Collapsed : Visibility.Visible,
        };

        var expandReleaseBtn = CreateCompactHeaderButton(
            isReleaseCollapsed ? $"Развернуть ({totalItems})" : "Свернуть",
            "Свернуть или развернуть этот релиз");
        expandReleaseBtn.Click += (_, _) =>
        {
            var nowCollapsed = bodyStack.Visibility == Visibility.Visible;
            bodyStack.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            expandReleaseBtn.Content = nowCollapsed ? $"Развернуть ({totalItems})" : "Свернуть";
            if (nowCollapsed)
            {
                _collapsedReleaseKeys.Add(releaseKey);
            }
            else
            {
                _collapsedReleaseKeys.Remove(releaseKey);
            }
        };
        rightHeader.Children.Add(expandReleaseBtn);

        Grid.SetColumn(rightHeader, 1);
        headerGrid.Children.Add(rightHeader);
        rootStack.Children.Add(headerGrid);

        var inlineStatus = CreateInlineTranslationBanner(release, releaseKey, isTranslating);
        if (inlineStatus is not null)
        {
            rootStack.Children.Add(inlineStatus);
        }

        var summaryText = showRuForCard && !string.IsNullOrWhiteSpace(release.TranslatedSummary)
            ? release.TranslatedSummary
            : release.Summary;

        if (!string.IsNullOrWhiteSpace(summaryText))
        {
            bodyStack.Children.Add(CreateReleaseSummaryBox(summaryText!));
        }

        foreach (var group in groups)
        {
            bodyStack.Children.Add(CreateGroupSection(releaseKey, group));
        }

        rootStack.Children.Add(bodyStack);
        cardBorder.Child = rootStack;
        return cardBorder;
    }

    private UIElement? CreateInlineTranslationBanner(
        ChangelogRelease release,
        string releaseKey,
        bool isTranslating)
    {
        if (isTranslating)
        {
            var elapsed = Math.Max(0, (int)(DateTimeOffset.UtcNow - _translationStartedAt).TotalSeconds);
            var stack = new StackPanel { Margin = new(0, 8, 0, 2) };
            var progressText = new TextBlock
            {
                Text = $"Обработка через Antigravity Flash ({elapsed} сек) · разделов: {release.Groups.Count}, пунктов: {release.Groups.Sum(g => g.Items.Count)} · генерация резюме и перевода…",
                FontSize = Math.Max(11, _baseFontSize - 2),
                Foreground = (Brush)FindResource("AccentTextBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new(0, 0, 0, 5),
            };
            _activeInlineProgressText = progressText;
            stack.Children.Add(progressText);
            stack.Children.Add(new ProgressBar
            {
                Height = 2,
                Background = new SolidColorBrush(Color.FromRgb(22, 38, 30)),
                Foreground = (Brush)FindResource("AccentBrush"),
                BorderThickness = new(0),
                IsIndeterminate = true,
            });
            return stack;
        }

        if (_translationErrors.TryGetValue(releaseKey, out var errorText))
        {
            return new TextBlock
            {
                Text = errorText,
                FontSize = Math.Max(11, _baseFontSize - 2),
                Foreground = new SolidColorBrush(Color.FromRgb(248, 165, 165)),
                Margin = new(0, 6, 0, 2),
                TextWrapping = TextWrapping.Wrap,
            };
        }

        return null;
    }

    private UIElement CreateReleaseSummaryBox(string summaryText)
    {
        var summaryBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(46, 98, 72)),
            BorderThickness = new(2, 0, 0, 0),
            Padding = new(10, 1, 0, 1),
            Margin = new(0, 4, 0, 6),
        };

        var contentBlock = BuildFormattedItemTextBlock(summaryText);
        contentBlock.FontSize = Math.Max(12, _baseFontSize - 1);
        contentBlock.Foreground = new SolidColorBrush(Color.FromRgb(196, 222, 207));
        summaryBorder.Child = contentBlock;
        return summaryBorder;
    }

    private UIElement CreateGroupSection(string releaseKey, ChangelogGroup group)
    {
        var groupKey = GetGroupKey(releaseKey, group.Category);
        var isCollapsed = _collapsedGroupKeys.Contains(groupKey);
        var titleColor = group.Category switch
        {
            ChangeCategory.Added => Color.FromRgb(127, 226, 174),
            ChangeCategory.Fixed => Color.FromRgb(147, 197, 226),
            ChangeCategory.Security => Color.FromRgb(248, 165, 165),
            _ => Color.FromRgb(186, 208, 195),
        };

        var section = new StackPanel { Margin = new(0, 6, 0, 4) };
        var headerGrid = new Grid
        {
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Margin = new(0, 2, 0, 3),
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var chevronText = new TextBlock
        {
            Text = isCollapsed ? "▸" : "▾",
            FontSize = Math.Max(11, _baseFontSize - 2),
            Foreground = (Brush)FindResource("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(0, 0, 6, 0),
        };
        headerGrid.Children.Add(chevronText);

        var titleText = new TextBlock
        {
            Text = $"{group.Title} ({group.Items.Count})",
            FontSize = Math.Max(12, _baseFontSize - 1),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(titleColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(0, 0, 8, 0),
        };
        Grid.SetColumn(titleText, 1);
        headerGrid.Children.Add(titleText);

        var hasAiGroupSummary = !string.IsNullOrWhiteSpace(group.Summary);
        var collapsedPreview = hasAiGroupSummary ? group.Summary! : ChangelogService.BuildGroupSummary(group);
        var summaryPreview = new TextBlock
        {
            Text = hasAiGroupSummary ? $"– {group.Summary}" : $"– {collapsedPreview}",
            ToolTip = collapsedPreview,
            FontSize = Math.Max(11, _baseFontSize - 2),
            Foreground = (Brush)FindResource("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = hasAiGroupSummary || isCollapsed ? Visibility.Visible : Visibility.Collapsed,
        };
        Grid.SetColumn(summaryPreview, 2);
        headerGrid.Children.Add(summaryPreview);

        var itemsStack = new StackPanel
        {
            Margin = new(14, 2, 0, 0),
            Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible,
        };

        var bulletBrush = (Brush)FindResource("MutedBrush");
        foreach (var item in group.Items)
        {
            var row = new Grid { Margin = new(0, 2, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bullet = new TextBlock
            {
                Text = "•",
                FontSize = _baseFontSize,
                Foreground = bulletBrush,
                VerticalAlignment = VerticalAlignment.Top,
            };

            var textBlock = BuildFormattedItemTextBlock(item);
            Grid.SetColumn(textBlock, 1);
            row.Children.Add(bullet);
            row.Children.Add(textBlock);
            itemsStack.Children.Add(row);
        }

        headerGrid.MouseLeftButtonUp += (_, e) =>
        {
            var nowCollapsed = itemsStack.Visibility == Visibility.Visible;
            itemsStack.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            chevronText.Text = nowCollapsed ? "▸" : "▾";
            summaryPreview.Visibility = hasAiGroupSummary || nowCollapsed ? Visibility.Visible : Visibility.Collapsed;
            if (nowCollapsed)
            {
                _collapsedGroupKeys.Add(groupKey);
            }
            else
            {
                _collapsedGroupKeys.Remove(groupKey);
            }

            e.Handled = true;
        };

        section.Children.Add(headerGrid);
        section.Children.Add(itemsStack);
        return section;
    }

    private Button CreateCompactHeaderButton(string content, string toolTip)
    {
        return new Button
        {
            Content = content,
            ToolTip = toolTip,
            MinHeight = 24,
            Padding = new(8, 2, 8, 2),
            Margin = new(6, 0, 0, 2),
            FontSize = Math.Max(11, _baseFontSize - 2),
        };
    }

    private TextBlock BuildFormattedItemTextBlock(string text)
    {
        var block = new TextBlock
        {
            FontSize = _baseFontSize,
            LineHeight = Math.Round(_baseFontSize * 1.42),
            Foreground = new SolidColorBrush(Color.FromRgb(218, 232, 223)),
            TextWrapping = TextWrapping.Wrap,
        };

        var parts = Regex.Split(text, @"(`[^`]+`)");
        foreach (var part in parts)
        {
            if (part.Length >= 2 && part.StartsWith('`') && part.EndsWith('`'))
            {
                block.Inlines.Add(new Run(" " + part[1..^1] + " ")
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = Math.Max(11, _baseFontSize - 1),
                    Background = new SolidColorBrush(Color.FromRgb(22, 38, 30)),
                    Foreground = new SolidColorBrush(Color.FromRgb(167, 232, 197)),
                });
            }
            else if (part.Length > 0)
            {
                block.Inlines.Add(new Run(part));
            }
        }

        return block;
    }

    private Border CreateBadge(string text, Color bg, Color fg)
    {
        return new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new(4),
            Padding = new(6, 1, 6, 1),
            Margin = new(0, 0, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = Math.Max(10, _baseFontSize - 3),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fg),
            },
        };
    }

    private void ShowFallbackText(string text)
    {
        ReleaseCardsPanel.Children.Clear();
        ReleasesScrollViewer.Visibility = Visibility.Collapsed;
        ChangelogTextBox.Visibility = Visibility.Visible;
        SetRawText(text);
    }

    private void SetRawText(string text)
    {
        _suppressTextSync = true;
        try
        {
            ChangelogTextBox.Text = text;
        }
        finally
        {
            _suppressTextSync = false;
        }
    }

    private void ChangelogTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextSync)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ChangelogTextBox.Text))
        {
            return;
        }

        var parsed = ChangelogService.ParseReleases(ChangelogTextBox.Text, "markdown");
        if (parsed.Count > 0)
        {
            _suppressTextSync = true;
            try
            {
                _currentDocument = new ChangelogDocument
                {
                    StageId = "preview",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Releases = parsed,
                };

                ReleaseCardsPanel.Children.Clear();
                for (var index = 0; index < parsed.Count; index++)
                {
                    ReleaseCardsPanel.Children.Add(CreateReleaseCard(parsed[index], index, _currentStage));
                }

                ReleasesScrollViewer.Visibility = Visibility.Visible;
                ChangelogTextBox.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _suppressTextSync = false;
            }
        }
        else
        {
            ReleasesScrollViewer.Visibility = Visibility.Collapsed;
            ChangelogTextBox.Visibility = Visibility.Visible;
        }
    }

    private static string GetReleaseKey(ChangelogRelease release)
    {
        return !string.IsNullOrWhiteSpace(release.Version) ? release.Version.Trim() : release.Title.Trim();
    }

    private static string GetGroupKey(string releaseKey, ChangeCategory category)
    {
        return $"{releaseKey}::{category}";
    }

    private static bool IsReleaseAlreadyRussian(ChangelogRelease release)
    {
        var cyr = 0;
        var lat = 0;
        foreach (var ch in release.Groups.SelectMany(g => g.Items).SelectMany(i => i))
        {
            if (ch is >= 'А' and <= 'я' or 'Ё' or 'ё')
            {
                cyr++;
            }
            else if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                lat++;
            }
        }

        return cyr > lat;
    }

    private static string TrimVersion(string version)
    {
        return version.Trim().TrimStart('v', 'V');
    }

    private void SourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_source?.PageUrl is not { } url)
        {
            return;
        }

        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception exception) { AppDialog.Show(this, exception.Message, "Не удалось открыть источник"); }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
