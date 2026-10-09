using System.Diagnostics;
using System.Windows.Documents;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;

namespace GuideMate.App;

internal static class ReleaseNotesDocument
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseAutoLinks().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough).DisableHtml().Build();
    private static readonly Brush CodeBackground = new SolidColorBrush(Color.FromRgb(240, 243, 241));
    private static readonly Brush Rule = new SolidColorBrush(Color.FromRgb(222, 227, 224));

    public static FlowDocument Create(string? markdown)
    {
        var document = new FlowDocument
        {
            FontFamily = new("Segoe UI Variable Text, Microsoft YaHei UI"), FontSize = 13,
            Foreground = Ui.Ink, Background = Brushes.Transparent, PagePadding = new(14),
            ColumnWidth = double.PositiveInfinity, LineHeight = 21
        };
        var parsed = Markdown.Parse(string.IsNullOrWhiteSpace(markdown) ? "此版本未提供更新说明。" : markdown, Pipeline);
        AddBlocks(document.Blocks, parsed);
        return document;
    }

    private static void AddBlocks(BlockCollection target, ContainerBlock source)
    {
        foreach (var block in source)
        {
            WpfBlock rendered;
            switch (block)
            {
                case HeadingBlock heading:
                    var title = Paragraph(heading);
                    title.FontSize = heading.Level switch { 1 => 22, 2 => 18, _ => 15 };
                    title.FontWeight = FontWeights.SemiBold; title.Margin = new(0, 6, 0, 10);
                    rendered = title;
                    break;
                case ParagraphBlock paragraph:
                    rendered = Paragraph(paragraph);
                    break;
                case ListBlock list:
                    var items = new System.Windows.Documents.List
                    {
                        MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Padding = new(22, 0, 0, 0), Margin = new(0, 0, 0, 10)
                    };
                    if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start)) items.StartIndex = start;
                    foreach (var child in list.OfType<ListItemBlock>())
                    {
                        var item = new ListItem { Margin = new(0, 0, 0, 4) };
                        AddBlocks(item.Blocks, child);
                        if (list.IsLoose == false)
                            foreach (var entry in item.Blocks) entry.Margin = new(0);
                        items.ListItems.Add(item);
                    }
                    rendered = items;
                    break;
                case QuoteBlock quote:
                    var section = new Section
                    {
                        BorderBrush = Ui.Green, BorderThickness = new(3, 0, 0, 0),
                        Padding = new(12, 0, 0, 0), Margin = new(0, 4, 0, 12), Foreground = Ui.Muted
                    };
                    AddBlocks(section.Blocks, quote); rendered = section;
                    break;
                case CodeBlock code:
                    rendered = new Paragraph(new Run(code.Lines.ToString()))
                    {
                        FontFamily = new("Consolas, Microsoft YaHei UI"), FontSize = 12,
                        Background = CodeBackground, Padding = new(10), Margin = new(0, 4, 0, 12)
                    };
                    break;
                case MdTable table:
                    var grid = new System.Windows.Documents.Table { CellSpacing = 0, Margin = new(0, 4, 0, 12) };
                    var rows = new TableRowGroup(); grid.RowGroups.Add(rows);
                    foreach (var row in table.OfType<MdTableRow>())
                    {
                        var renderedRow = new TableRow(); rows.Rows.Add(renderedRow);
                        foreach (var cell in row.OfType<MdTableCell>())
                        {
                            var renderedCell = new TableCell
                            {
                                Padding = new(8, 5, 8, 5), BorderBrush = Rule, BorderThickness = new(0, 0, 0, 1),
                                FontWeight = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal,
                                Background = row.IsHeader ? CodeBackground : Brushes.Transparent
                            };
                            AddBlocks(renderedCell.Blocks, cell); renderedRow.Cells.Add(renderedCell);
                        }
                    }
                    rendered = grid;
                    break;
                case ThematicBreakBlock:
                    rendered = new Paragraph { BorderBrush = Rule, BorderThickness = new(0, 0, 0, 1),
                        FontSize = 1, LineHeight = 1, Margin = new(0, 8, 0, 12) };
                    break;
                case ContainerBlock container:
                    AddBlocks(target, container);
                    continue;
                default:
                    continue;
            }
            target.Add(rendered);
        }
    }

    private static Paragraph Paragraph(LeafBlock source)
    {
        var paragraph = new Paragraph { Margin = new(0, 0, 0, 10) };
        AddInlines(paragraph.Inlines, source.Inline);
        return paragraph;
    }

    private static void AddInlines(InlineCollection target, ContainerInline? source)
    {
        if (source == null) return;
        foreach (var inline in source)
        {
            WpfInline rendered;
            switch (inline)
            {
                case LiteralInline literal:
                    rendered = new Run(literal.Content.ToString());
                    break;
                case HtmlEntityInline entity:
                    rendered = new Run(entity.Transcoded.ToString());
                    break;
                case AutolinkInline autolink:
                    var automatic = WebLink(autolink.Url); automatic.Inlines.Add(new Run(autolink.Url));
                    rendered = automatic;
                    break;
                case CodeInline code:
                    rendered = new Run(code.Content) { FontFamily = new("Consolas, Microsoft YaHei UI"), Background = CodeBackground };
                    break;
                case LineBreakInline line:
                    rendered = line.IsHard ? new LineBreak() : new Run(" ");
                    break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount == 2) span.FontWeight = FontWeights.Bold;
                    else span.FontStyle = FontStyles.Italic;
                    AddInlines(span.Inlines, emphasis); rendered = span;
                    break;
                case LinkInline link:
                    // Release notes never load images or execute HTML; image descriptions remain readable.
                    var label = link.IsImage ? new Span() : WebLink(link.Url);
                    AddInlines(label.Inlines, link); rendered = label;
                    break;
                case ContainerInline container:
                    AddInlines(target, container);
                    continue;
                default:
                    continue;
            }
            target.Add(rendered);
        }
    }

    private static Span WebLink(string? destination)
    {
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return new Span();
        var hyperlink = new Hyperlink { NavigateUri = uri, Foreground = Ui.Green, ToolTip = uri.AbsoluteUri };
        hyperlink.RequestNavigate += (_, e) =>
        {
            e.Handled = true;
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { MessageBox.Show("无法打开链接：" + ex.Message, "随引更新"); }
        };
        return hyperlink;
    }
}
