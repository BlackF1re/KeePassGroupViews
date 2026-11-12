using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using KeePass;
using KeePass.App.Configuration;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePassLib;

namespace KeePassGroupViews
{
    /// <summary>Switches KeePass's native entry-list columns when a group is selected.</summary>
    public sealed class KeePassGroupViewsExt : Plugin
    {
        private const string ConfigKey = "KeePassGroupViews.Layouts";
        private IPluginHost _host;
        private LayoutStore _store;
        private TreeView _groups;
        private ListView _entries;
        private MethodInfo _updateColumns;
        private FieldInfo _sorterField;
        private string _currentRoot;
        private string _currentGroup;
        private bool _updating;
        private bool _disabled;

        public override bool Initialize(IPluginHost host)
        {
            if(host == null) return false;
            _host = host;
            try
            {
                _store = LayoutStore.Deserialize(host.CustomConfig.GetString(ConfigKey));
                _updateColumns = typeof(MainForm).GetMethod("UpdateColumnsEx",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(bool) }, null);
                _sorterField = typeof(MainForm).GetField("m_pListSorter",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if(_updateColumns == null || _sorterField == null)
                    throw new NotSupportedException("KeePass entry-list API is unavailable.");
                host.MainWindow.UIStateUpdated += OnUiStateUpdated;
                host.MainWindow.FormLoadPost += OnFormLoadPost;
                host.MainWindow.FileClosingPre += OnFileClosing;
                if(host.MainWindow.IsHandleCreated) OnFormLoadPost(this, EventArgs.Empty);
                return true;
            }
            catch(Exception ex)
            {
                CleanupEvents();
                MessageBox.Show("KeePassGroupViews could not initialize: " + ex.Message,
                    "KeePassGroupViews", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _host = null;
                return false;
            }
        }

        public override ToolStripMenuItem GetMenuItem(PluginMenuType t)
        {
            if(t != PluginMenuType.Main && t != PluginMenuType.Group) return null;

            var menu = new ToolStripMenuItem("Group Views");
            var save = new ToolStripMenuItem("Save columns for this group");
            var inherit = new ToolStripMenuItem("Use inherited columns");
            var defaults = new ToolStripMenuItem("Set current columns as default");

            save.Click += (sender, e) => Execute(SaveGroup);
            inherit.Click += (sender, e) => Execute(ClearGroup);
            defaults.Click += (sender, e) => Execute(SaveDefault);
            menu.DropDownItems.Add(save);
            menu.DropDownItems.Add(inherit);
            if(t == PluginMenuType.Main)
            {
                menu.DropDownItems.Add(new ToolStripSeparator());
                menu.DropDownItems.Add(defaults);
            }
            menu.DropDownOpening += (sender, e) =>
            {
                PwGroup selected = SelectedGroup();
                bool active = (!_disabled && selected != null &&
                    _host != null && _host.Database != null && _host.Database.IsOpen);
                save.Enabled = active;
                inherit.Enabled = active && _store.HasOverride(
                    _host.Database.RootGroup.Uuid.ToHexString(),
                    selected.Uuid.ToHexString());
                defaults.Enabled = !_disabled && _host != null;
            };
            return menu;
        }

        private void OnFormLoadPost(object sender, EventArgs e)
        {
            Execute(() =>
            {
                _groups = FindControl<TreeView>(_host.MainWindow, "m_tvGroups");
                _entries = FindControl<ListView>(_host.MainWindow, "m_lvEntries");
                if(_groups == null || _entries == null)
                    throw new NotSupportedException("KeePass entry or group list was not found.");
                if(_store.Default == null)
                {
                    _store.Default = Capture();
                    Persist();
                }
                SynchronizeGroup();
            });
        }

        private void OnUiStateUpdated(object sender, EventArgs e)
        {
            if(!_updating && !_disabled && _groups != null && _entries != null)
                Execute(SynchronizeGroup);
        }

        private void OnFileClosing(object sender, FileClosingEventArgs e)
        {
            if(!_disabled) Execute(() =>
            {
                CaptureCurrentOverride();
                _currentRoot = null;
                _currentGroup = null;
            });
        }

        private static T FindControl<T>(Control parent, string name) where T : Control
        {
            if(parent == null) return null;
            if(parent.Name == name && parent is T) return (T)parent;
            foreach(Control child in parent.Controls)
            {
                T result = FindControl<T>(child, name);
                if(result != null) return result;
            }
            return null;
        }

        private PwGroup SelectedGroup()
        {
            TreeNode selected = (_groups == null ? null : _groups.SelectedNode);
            return (selected == null ? null : selected.Tag as PwGroup);
        }

        private ColumnLayout Capture()
        {
            var config = Program.Config.MainWindow;
            List<AceColumn> source = config.EntryListColumns;
            if(source.Count == 0)
                throw new InvalidOperationException("The entry list has no columns.");

            var layout = new ColumnLayout();
            bool live = (_entries != null && _entries.Columns.Count == source.Count);
            for(int i = 0; i < source.Count; ++i)
            {
                AceColumn column = source[i];
                layout.Columns.Add(new ColumnSpec {
                    Type = (int)column.Type, Name = column.CustomName ?? "",
                    Mask = column.HideWithAsterisks,
                    Width = live ? _entries.Columns[i].Width : column.Width,
                    DisplayIndex = live ? _entries.Columns[i].DisplayIndex : i
                });
            }
            layout.Validate();
            return layout;
        }

        private static List<AceColumn> ToKeePassColumns(ColumnLayout layout)
        {
            layout.Validate();
            var result = new List<AceColumn>();
            foreach(ColumnSpec c in layout.Columns)
            {
                if(!Enum.IsDefined(typeof(AceColumnType), c.Type) ||
                    c.Type == (int)AceColumnType.Count)
                    throw new NotSupportedException("Unsupported KeePass column type.");
                result.Add(new AceColumn((AceColumnType)c.Type, c.Name, c.Mask, c.Width));
            }
            return result;
        }

        private static ListSorter TransferSorting(
            IList<AceColumn> previous, IList<AceColumn> next, ListSorter sort)
        {
            if(sort == null || sort.Column < 0 || sort.Column >= previous.Count ||
                sort.Order == SortOrder.None)
                return new ListSorter();

            AceColumn original = previous[sort.Column];
            for(int i = 0; i < next.Count; ++i)
            {
                AceColumn candidate = next[i];
                if(original.Type != candidate.Type ||
                    !string.Equals(original.CustomName, candidate.CustomName,
                        StringComparison.Ordinal))
                    continue;

                return new ListSorter(i, sort.Order,
                    candidate.Type != AceColumnType.Uuid,
                    AceColumn.IsTimeColumn(candidate.Type));
            }

            // Do not interpret a different field as the old sort column.
            return new ListSorter();
        }

        private void Apply(ColumnLayout layout)
        {
            if(layout == null || layout.SameAs(Capture())) return;
            var config = Program.Config.MainWindow;
            List<AceColumn> originalColumns = config.EntryListColumns;
            string originalOrder = config.EntryListColumnDisplayOrder;
            ListSorter originalSorter = (ListSorter)_sorterField.GetValue(_host.MainWindow);
            try
            {
                List<AceColumn> columns = ToKeePassColumns(layout);
                ListSorter newSorter = TransferSorting(
                    originalColumns, columns, originalSorter);
                config.EntryListColumns = columns;
                config.EntryListColumnDisplayOrder =
                    string.Join(" ", layout.Columns.Select(c => c.DisplayIndex.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)).ToArray());
                _updating = true;
                _sorterField.SetValue(_host.MainWindow, newSorter);
                _updateColumns.Invoke(_host.MainWindow, new object[] { false });
                _entries.ListViewItemSorter =
                    (newSorter.Column >= 0 ? newSorter : null);
                _host.MainWindow.UpdateUI(false, null, false, null, true, null, false, null);
            }
            catch
            {
                config.EntryListColumns = originalColumns;
                config.EntryListColumnDisplayOrder = originalOrder;
                _sorterField.SetValue(_host.MainWindow, originalSorter);
                try
                {
                    _updateColumns.Invoke(_host.MainWindow, new object[] { false });
                    _entries.ListViewItemSorter =
                        (originalSorter != null && originalSorter.Column >= 0 ?
                            originalSorter : null);
                    _host.MainWindow.UpdateUI(false, null, false, null, true, null, false, null);
                }
                catch { /* Preserve the original exception. */ }
                throw;
            }
            finally { _updating = false; }
        }

        private void SynchronizeGroup()
        {
            if(_host.Database == null || !_host.Database.IsOpen) return;
            PwGroup selected = SelectedGroup();
            if(selected == null) return;
            string root = _host.Database.RootGroup.Uuid.ToHexString();
            string group = selected.Uuid.ToHexString();
            if(root == _currentRoot && group == _currentGroup) return;

            CaptureCurrentOverride();
            _currentRoot = root;
            _currentGroup = group;
            ColumnLayout layout = _store.Resolve(root, AncestorIds(selected));
            Apply(layout);
        }

        private static IEnumerable<string> AncestorIds(PwGroup group)
        {
            for(PwGroup g = group; g != null; g = g.ParentGroup)
                yield return g.Uuid.ToHexString();
        }

        private void CaptureCurrentOverride()
        {
            if(_currentRoot == null || _currentGroup == null ||
                !_store.HasOverride(_currentRoot, _currentGroup)) return;
            _store.Set(_currentRoot, _currentGroup, Capture());
            Persist();
        }

        private void SaveGroup()
        {
            PwGroup group = SelectedGroup();
            if(group == null || _host.Database == null || !_host.Database.IsOpen)
                return;
            _store.Set(_host.Database.RootGroup.Uuid.ToHexString(),
                group.Uuid.ToHexString(), Capture());
            Persist();
        }

        private void ClearGroup()
        {
            PwGroup group = SelectedGroup();
            if(group == null || _host.Database == null || !_host.Database.IsOpen)
                return;
            string root = _host.Database.RootGroup.Uuid.ToHexString();
            string id = group.Uuid.ToHexString();
            _store.Remove(root, id);
            Persist();
            Apply(_store.Resolve(root, AncestorIds(group)));
        }

        private void SaveDefault()
        {
            _store.Default = Capture();
            Persist();
        }

        private void Persist()
        {
            _host.CustomConfig.SetString(ConfigKey, _store.Serialize());
        }

        private void Execute(Action action)
        {
            if(_disabled || _host == null) return;
            try { action(); }
            catch(Exception ex)
            {
                _disabled = true;
                MessageBox.Show("Group Views has been disabled for this session.\n" +
                    ex.Message, "KeePassGroupViews",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CleanupEvents()
        {
            if(_host == null) return;
            _host.MainWindow.UIStateUpdated -= OnUiStateUpdated;
            _host.MainWindow.FormLoadPost -= OnFormLoadPost;
            _host.MainWindow.FileClosingPre -= OnFileClosing;
        }

        public override void Terminate()
        {
            if(_host == null) return;
            if(!_disabled) Execute(CaptureCurrentOverride);
            CleanupEvents();
            _host = null;
            _groups = null;
            _entries = null;
        }
    }
}
