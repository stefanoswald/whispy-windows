namespace Whispy;

/// <summary>History window: search, copy, re-insert, re-clean, delete.</summary>
public sealed class HistoryForm : Form
{
    private readonly HistoryStore _history;
    private readonly PipelineController _pipeline;
    private readonly AppSettings _settings;

    private readonly TextBox _search;
    private readonly ListView _list;

    public HistoryForm(HistoryStore history, PipelineController pipeline, AppSettings settings)
    {
        _history = history;
        _pipeline = pipeline;
        _settings = settings;

        Text = "Whispy History";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(680, 440);

        _search = new TextBox
        {
            Location = new Point(12, 12),
            Width = 656,
            PlaceholderText = "Search dictations…",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _search.TextChanged += (_, _) => Refresh_();

        _list = new ListView
        {
            Location = new Point(12, 44),
            Size = new Size(656, 384),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false
        };
        _list.Columns.Add("When", 130);
        _list.Columns.Add("Mode", 110);
        _list.Columns.Add("Text", 400);
        _list.ContextMenuStrip = BuildRowMenu();

        Controls.Add(_search);
        Controls.Add(_list);

        Refresh_();

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
        VisibleChanged += (_, _) => { if (Visible) Refresh_(); };
    }

    private void Refresh_()
    {
        _list.Items.Clear();
        foreach (var entry in _history.Search(_search.Text))
        {
            var item = new ListViewItem(entry.Date.ToString("MMM d, h:mm tt"));
            item.SubItems.Add(entry.Mode.DisplayName());
            item.SubItems.Add(entry.CleanedText.Replace("\n", " "));
            item.Tag = entry;
            _list.Items.Add(item);
        }
    }

    private HistoryEntry? Selected =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as HistoryEntry : null;

    private ContextMenuStrip BuildRowMenu()
    {
        var menu = new ContextMenuStrip();

        var copy = new ToolStripMenuItem("Copy");
        copy.Click += (_, _) =>
        {
            if (Selected is { } entry)
            {
                try { Clipboard.SetText(entry.CleanedText); } catch { }
            }
        };
        menu.Items.Add(copy);

        var reclean = new ToolStripMenuItem("Re-run cleanup as…");
        foreach (WritingMode mode in Enum.GetValues<WritingMode>())
        {
            var captured = mode;
            var item = new ToolStripMenuItem(mode.DisplayName());
            item.Click += (_, _) =>
            {
                if (Selected is { } entry)
                {
                    var newText = _pipeline.RecleanFromHistory(entry, captured);
                    _history.Add(new HistoryEntry
                    {
                        RawText = entry.RawText,
                        CleanedText = newText,
                        Mode = captured,
                        AppName = entry.AppName
                    }, _settings.HistoryLimit);
                    try { Clipboard.SetText(newText); } catch { }
                    Refresh_();
                }
            };
            reclean.DropDownItems.Add(item);
        }
        menu.Items.Add(reclean);

        var delete = new ToolStripMenuItem("Delete");
        delete.Click += (_, _) =>
        {
            if (Selected is { } entry)
            {
                _history.Delete(entry.Id);
                Refresh_();
            }
        };
        menu.Items.Add(delete);

        return menu;
    }
}
