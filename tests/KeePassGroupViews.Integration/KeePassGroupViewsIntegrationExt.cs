using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using KeePass;
using KeePass.App.Configuration;
using KeePass.Plugins;
using KeePass.UI;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace KeePassGroupViewsIntegration
{
    // Loaded only in CI, inside a real KeePass.exe process.
    public sealed class KeePassGroupViewsIntegrationExt : Plugin
    {
        private const string TestPassword = "ci-only-fixture-key";
        private IPluginHost _host;
        private TreeView _groups;
        private ListView _entries;
        private readonly List<string> _checks = new List<string>();
        private string _fixture;
        private string _phase;

        public override bool Initialize(IPluginHost host)
        {
            if(host == null) return false;
            _host = host;
            _fixture = Environment.GetEnvironmentVariable("KPGV_TEST_DB");
            _phase = Environment.GetEnvironmentVariable("KPGV_TEST_PHASE");
            if(string.IsNullOrEmpty(_fixture) || string.IsNullOrEmpty(_phase))
                return false;

            host.MainWindow.FormLoadPost += OnLoaded;
            return true;
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            _host.MainWindow.BeginInvoke(new MethodInvoker(Run));
        }

        private static T Find<T>(Control parent, string name) where T : Control
        {
            if(parent.Name == name && parent is T) return (T)parent;
            foreach(Control c in parent.Controls)
            {
                T found = Find<T>(c, name);
                if(found != null) return found;
            }
            return null;
        }

        private void Verify(bool condition, string label)
        {
            if(!condition) throw new InvalidOperationException("Assertion failed: " + label);
            _checks.Add(label);
        }

        private static CompositeKey FixtureKey()
        {
            var k = new CompositeKey();
            k.AddUserKey(new KcpPassword(TestPassword));
            return k;
        }

        private static PwGroup Group(string name)
        {
            return new PwGroup(true, true, name, PwIcon.Folder);
        }

        private static void Entry(PwGroup group, string title, string phone)
        {
            var entry = new PwEntry(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
            entry.Strings.Set(PwDefs.UserNameField,
                new ProtectedString(false, "test-user"));
            entry.Strings.Set(PwDefs.PasswordField,
                new ProtectedString(true, "synthetic-password-value"));
            entry.Strings.Set("Phone", new ProtectedString(false, phone));
            entry.Strings.Set("Registration", new ProtectedString(false, "2020-01-01"));
            group.AddEntry(entry, true);
        }

        private void CreateFixture()
        {
            if(File.Exists(_fixture)) File.Delete(_fixture);
            var db = new PwDatabase();
            try
            {
                db.New(IOConnectionInfo.FromPath(_fixture), FixtureKey());
                var social = Group("Social");
                var vk = Group("VK");
                var telegram = Group("Telegram");
                var wifi = Group("Wi-Fi");
                db.RootGroup.AddGroup(social, true);
                social.AddGroup(vk, true);
                social.AddGroup(telegram, true);
                db.RootGroup.AddGroup(wifi, true);
                Entry(vk, "VK test", "+10000000001");
                Entry(telegram, "Telegram test", "+10000000002");
                Entry(wifi, "Router test", "+10000000003");
                Entry(wifi, "Router test 2", "+10000000004");
                db.Save(null);
            }
            finally { db.Close(); }
        }

        private void CreateSecondFixture()
        {
            string path = _fixture + ".second.kdbx";
            if(File.Exists(path)) File.Delete(path);
            var db = new PwDatabase();
            try
            {
                db.New(IOConnectionInfo.FromPath(path), FixtureKey());
                var social = Group("Social");
                var vk = Group("VK");
                db.RootGroup.AddGroup(social, true);
                social.AddGroup(vk, true);
                Entry(vk, "Other VK test", "+10000000009");
                db.Save(null);
            }
            finally { db.Close(); }
        }

        private PwGroup GetGroup(params string[] parts)
        {
            PwGroup group = _host.Database.RootGroup;
            foreach(string part in parts)
            {
                PwGroup next = null;
                foreach(PwGroup item in group.Groups)
                    if(item.Name == part) { next = item; break; }
                if(next == null) throw new InvalidOperationException(
                    "Fixture group not found: " + part);
                group = next;
            }
            return group;
        }

        private TreeNode FindNode(TreeNodeCollection nodes, PwGroup target)
        {
            foreach(TreeNode node in nodes)
            {
                if(ReferenceEquals(node.Tag, target)) return node;
                TreeNode found = FindNode(node.Nodes, target);
                if(found != null) return found;
            }
            return null;
        }

        private void Select(PwGroup group)
        {
            TreeNode node = FindNode(_groups.Nodes, group);
            if(node == null) throw new InvalidOperationException(
                "Group missing in KeePass tree: " + group.Name);
            _groups.SelectedNode = node;
            _host.MainWindow.UpdateUI(false, null, false, null,
                true, null, false, null);
            Application.DoEvents();
        }

        private ToolStripMenuItem MenuCommand(string text)
        {
            foreach(ToolStripItem t in _host.MainWindow.ToolsMenu.DropDownItems)
            {
                var menu = t as ToolStripMenuItem;
                if(menu == null || menu.Text != "Group Views") continue;
                foreach(ToolStripItem item in menu.DropDownItems)
                {
                    var command = item as ToolStripMenuItem;
                    if(command != null && command.Text == text)
                        return command;
                }
            }
            throw new InvalidOperationException("Plugin command not found: " + text);
        }

        private void Command(string text)
        {
            MenuCommand(text).PerformClick();
            Application.DoEvents();
        }

        private void Configure(params AceColumn[] columns)
        {
            Program.Config.MainWindow.EntryListColumns =
                new List<AceColumn>(columns);
            Program.Config.MainWindow.EntryListColumnDisplayOrder =
                string.Join(" ", Enumerable.Range(0, columns.Length));
            MethodInfo update = _host.MainWindow.GetType().GetMethod(
                "UpdateColumnsEx", BindingFlags.Instance | BindingFlags.NonPublic);
            if(update == null) throw new InvalidOperationException(
                "KeePass UpdateColumnsEx not found");
            update.Invoke(_host.MainWindow, new object[] { false });
            _host.MainWindow.UpdateUI(false, null, false, null, true, null, false, null);
            Application.DoEvents();
        }

        private static AceColumn Column(AceColumnType type, string name = "",
            bool mask = false, int width = 180)
        {
            return new AceColumn(type, name, mask, width);
        }

        private bool HasColumn(string name)
        {
            return _entries.Columns.Cast<ColumnHeader>().Any(c => c.Text == name);
        }

        private static string Hash(string file)
        {
            using(var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(file)));
        }

        private void RunSetup()
        {
            CreateFixture();
            string original = Hash(_fixture);
            _host.MainWindow.OpenDatabase(IOConnectionInfo.FromPath(_fixture),
                FixtureKey(), true);
            Verify(_host.Database.IsOpen, "fixture opened in real KeePass");

            PwGroup root = _host.Database.RootGroup;
            PwGroup social = GetGroup("Social");
            PwGroup vk = GetGroup("Social", "VK");
            PwGroup telegram = GetGroup("Social", "Telegram");
            PwGroup wifi = GetGroup("Wi-Fi");

            Select(root);
            int initialCount = _entries.Columns.Count;
            Verify(initialCount >= 2, "KeePass provides its native default columns");
            Select(social);
            Configure(Column(AceColumnType.Title), Column(AceColumnType.CustomString, "Phone"));
            Command("Save columns for this group");
            Verify(HasColumn("Phone"), "custom column visible in parent group");

            Select(telegram);
            Verify(_entries.Columns.Count == 2 && HasColumn("Phone"),
                "child inherits parent layout");
            Verify(_entries.Items.Count == 1 &&
                _entries.Items[0].SubItems[1].Text == "+10000000002",
                "KeePass populates custom column cell");

            Select(wifi);
            Verify(_entries.Columns.Count == initialCount,
                "unrelated group falls back to default");

            Select(vk);
            Verify(_entries.Columns.Count == 2, "sibling inherits layout");
            Configure(Column(AceColumnType.Title), Column(AceColumnType.CustomString, "Phone"),
                Column(AceColumnType.Password, "", true, 110));
            Command("Save columns for this group");
            Verify(_entries.Columns.Count == 3, "group override stored");
            Verify(_entries.Items.Count == 1 &&
                !_entries.Items[0].SubItems[2].Text.Contains("synthetic-password"),
                "password column remains masked");

            _entries.Columns[1].Width = 230;
            _entries.Columns[1].DisplayIndex = 0;
            Select(telegram);
            Verify(_entries.Columns.Count == 2, "parent layout restored");
            Select(vk);
            Verify(_entries.Columns.Count == 3 &&
                _entries.Columns[1].Width == 230 &&
                _entries.Columns[1].DisplayIndex == 0,
                "group width and order persist across switches");

            Command("Use inherited columns");
            Verify(_entries.Columns.Count == 2, "removing override restores inheritance");

            Configure(Column(AceColumnType.Title),
                Column(AceColumnType.CustomString, "Registration"));
            Command("Save columns for this group");
            Select(wifi);
            Select(vk);
            Verify(HasColumn("Registration") && !HasColumn("Phone"),
                "explicit group override takes priority");
            Verify(_entries.Items[0].SubItems[1].Text == "2020-01-01",
                "new custom field is rendered by KeePass");

            // A date sort cannot be reused for an unrelated string column.
            Configure(Column(AceColumnType.Title),
                Column(AceColumnType.CreationTime));
            Command("Save columns for this group");
            MethodInfo sort = _host.MainWindow.GetType().GetMethod(
                "SortPasswordList", BindingFlags.Instance | BindingFlags.NonPublic);
            if(sort == null) throw new InvalidOperationException(
                "KeePass native sorting API not found");
            sort.Invoke(_host.MainWindow, new object[] {
                true, 1, (SortOrder?)SortOrder.Ascending, true
            });
            FieldInfo sorterField = _host.MainWindow.GetType().GetField(
                "m_pListSorter", BindingFlags.Instance | BindingFlags.NonPublic);
            var beforeSort = (ListSorter)sorterField.GetValue(_host.MainWindow);
            Verify(beforeSort.Column == 1 && beforeSort.CompareTimes,
                "native date sorting is enabled");
            Select(wifi);
            var afterSort = (ListSorter)sorterField.GetValue(_host.MainWindow);
            Verify(afterSort.Column < 0 && _entries.Items.Count == 2,
                "incompatible date sorting resets on group switch");
            Select(vk);
            Configure(Column(AceColumnType.Title),
                Column(AceColumnType.CustomString, "Registration"));
            Command("Save columns for this group");

            Verify(Hash(_fixture) == original,
                "view operations leave KDBX file unchanged");
            Verify(!_host.Database.Modified,
                "view operations do not mark KDBX modified");

            CreateSecondFixture();
            string otherPath = _fixture + ".second.kdbx";
            string otherHash = Hash(otherPath);
            _host.MainWindow.OpenDatabase(IOConnectionInfo.FromPath(otherPath),
                FixtureKey(), true);
            Verify(_host.Database.IsOpen &&
                _host.Database.RootGroup.Uuid != root.Uuid,
                "second database opens independently");
            Select(GetGroup("Social", "VK"));
            Verify(_entries.Columns.Count == initialCount &&
                !HasColumn("Registration"),
                "same group names in different databases do not share layouts");

            Configure(Column(AceColumnType.Title),
                Column(AceColumnType.CustomString, "Phone"));
            _entries.Columns[1].Width = 237;
            _entries.Columns[1].DisplayIndex = 0;
            Command("Save columns for this group");
            Verify(HasColumn("Phone"), "second database stores its own override");

            _host.MainWindow.OpenDatabase(IOConnectionInfo.FromPath(_fixture),
                FixtureKey(), true);
            Select(GetGroup("Social", "VK"));
            Verify(HasColumn("Registration") && !HasColumn("Phone"),
                "first database override survives a tab change");
            _host.MainWindow.OpenDatabase(IOConnectionInfo.FromPath(otherPath),
                FixtureKey(), true);
            Select(GetGroup("Social", "VK"));
            Verify(HasColumn("Phone") && !HasColumn("Registration"),
                "second database override survives a tab change");
            Verify(Hash(_fixture) == original && Hash(otherPath) == otherHash,
                "switching databases does not write either KDBX file");
        }

        private void RunVerify()
        {
            Verify(File.Exists(_fixture), "fixture retained across process restarts");
            _host.MainWindow.OpenDatabase(IOConnectionInfo.FromPath(_fixture),
                FixtureKey(), true);
            Verify(_host.Database.IsOpen, "fixture reopened after restart");
            PwGroup vk = GetGroup("Social", "VK");
            PwGroup telegram = GetGroup("Social", "Telegram");
            PwGroup wifi = GetGroup("Wi-Fi");

            Select(vk);
            Verify(_entries.Columns.Count == 2 && HasColumn("Registration"),
                "group override survives KeePass restart");
            Select(telegram);
            Verify(_entries.Columns.Count == 2 && HasColumn("Phone"),
                "inherited layout survives KeePass restart");
            Select(wifi);
            Verify(!HasColumn("Phone") && !HasColumn("Registration"),
                "default layout survives KeePass restart");

            string otherPath = _fixture + ".second.kdbx";
            Verify(File.Exists(otherPath), "second fixture retained across restart");
            _host.MainWindow.OpenDatabase(IOConnectionInfo.FromPath(otherPath),
                FixtureKey(), true);
            Select(GetGroup("Social", "VK"));
            Verify(HasColumn("Phone") && !HasColumn("Registration"),
                "second database layout survives KeePass restart");
            Verify(_entries.Columns[1].Width == 237 &&
                _entries.Columns[1].DisplayIndex == 0,
                "second database width and order survive restart");
            _host.MainWindow.OpenDatabase(IOConnectionInfo.FromPath(_fixture),
                FixtureKey(), true);
            Select(GetGroup("Social", "VK"));
            Verify(HasColumn("Registration") && !HasColumn("Phone"),
                "first database layout survives return from second database");
        }

        private void Run()
        {
            string file = Environment.GetEnvironmentVariable("KPGV_TEST_RESULT");
            try
            {
                _groups = Find<TreeView>(_host.MainWindow, "m_tvGroups");
                _entries = Find<ListView>(_host.MainWindow, "m_lvEntries");
                Verify(_groups != null && _entries != null,
                    "actual KeePass group and entry controls exist");
                if(_phase == "setup") RunSetup();
                else if(_phase == "verify") RunVerify();
                else throw new InvalidOperationException("Unknown integration test phase");
                File.WriteAllText(file, "PASS" + Environment.NewLine +
                    string.Join(Environment.NewLine, _checks));
            }
            catch(Exception ex)
            {
                File.WriteAllText(file, "FAIL" + Environment.NewLine + ex);
            }
            finally
            {
                if(_host.Database != null && _host.Database.IsOpen)
                    _host.Database.Modified = false;
                _host.MainWindow.Close();
            }
        }

        public override void Terminate()
        {
            if(_host == null) return;
            _host.MainWindow.FormLoadPost -= OnLoaded;
            _host = null;
        }
    }
}
