using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace KeePassGroupViews
{
    // Contains column metadata only. No entry data or passwords are serialized.
    public sealed class ColumnSpec
    {
        public int Type { get; set; }
        public string Name { get; set; }
        public bool Mask { get; set; }
        public int Width { get; set; }
        public int DisplayIndex { get; set; }

        public ColumnSpec Copy()
        {
            return new ColumnSpec { Type = Type, Name = Name ?? "",
                Mask = Mask, Width = Width, DisplayIndex = DisplayIndex };
        }
    }

    public sealed class ColumnLayout
    {
        [XmlArrayItem("Column")]
        public List<ColumnSpec> Columns { get; set; } = new List<ColumnSpec>();

        public ColumnLayout Copy()
        {
            return new ColumnLayout { Columns = Columns.Select(c => c.Copy()).ToList() };
        }

        public bool SameAs(ColumnLayout other)
        {
            if(other == null || Columns.Count != other.Columns.Count) return false;
            for(int i = 0; i < Columns.Count; ++i)
            {
                ColumnSpec a = Columns[i], b = other.Columns[i];
                if(a.Type != b.Type || !string.Equals(a.Name, b.Name,
                    StringComparison.Ordinal) || a.Mask != b.Mask ||
                    a.Width != b.Width || a.DisplayIndex != b.DisplayIndex)
                    return false;
            }
            return true;
        }

        public void Validate()
        {
            if(Columns == null || Columns.Count < 1 || Columns.Count > 256)
                throw new InvalidDataException("Invalid column count.");
            var indexes = new HashSet<int>();
            foreach(ColumnSpec c in Columns)
            {
                if(c == null || c.Type < 0 || c.Type > 1000 ||
                    c.Name == null || c.Name.Length > 1024 ||
                    c.Width < -1 || c.Width > 20000 ||
                    c.DisplayIndex < 0 || c.DisplayIndex >= Columns.Count ||
                    !indexes.Add(c.DisplayIndex))
                    throw new InvalidDataException("Invalid column definition.");
            }
        }
    }

    public sealed class GroupLayout
    {
        public string GroupId { get; set; }
        public ColumnLayout Layout { get; set; }
    }

    public sealed class DatabaseLayouts
    {
        public string RootId { get; set; }

        [XmlArrayItem("Group")]
        public List<GroupLayout> Groups { get; set; } = new List<GroupLayout>();
    }

    [XmlRoot("KeePassGroupViews")]
    public sealed class LayoutStore
    {
        public int Version { get; set; } = 1;
        public ColumnLayout Default { get; set; }

        [XmlArrayItem("Database")]
        public List<DatabaseLayouts> Databases { get; set; } = new List<DatabaseLayouts>();

        public bool HasOverride(string rootId, string groupId)
        {
            DatabaseLayouts db = Databases.FirstOrDefault(d => d.RootId == rootId);
            return (db != null && db.Groups.Any(g => g.GroupId == groupId));
        }

        // Group IDs must be ordered from selected group toward the root.
        public ColumnLayout Resolve(string rootId, IEnumerable<string> ancestors)
        {
            DatabaseLayouts db = Databases.FirstOrDefault(d => d.RootId == rootId);
            if(db != null && ancestors != null)
            {
                foreach(string id in ancestors)
                {
                    GroupLayout g = db.Groups.FirstOrDefault(x => x.GroupId == id);
                    if(g != null) return g.Layout.Copy();
                }
            }
            return Default == null ? null : Default.Copy();
        }

        public void Set(string rootId, string groupId, ColumnLayout layout)
        {
            if(string.IsNullOrEmpty(rootId) || string.IsNullOrEmpty(groupId))
                throw new ArgumentException("A database and a group are required.");
            if(layout == null) throw new ArgumentNullException("layout");
            layout.Validate();

            DatabaseLayouts db = Databases.FirstOrDefault(d => d.RootId == rootId);
            if(db == null)
            {
                db = new DatabaseLayouts { RootId = rootId };
                Databases.Add(db);
            }
            GroupLayout g = db.Groups.FirstOrDefault(x => x.GroupId == groupId);
            if(g == null)
            {
                g = new GroupLayout { GroupId = groupId };
                db.Groups.Add(g);
            }
            g.Layout = layout.Copy();
        }

        public void Remove(string rootId, string groupId)
        {
            DatabaseLayouts db = Databases.FirstOrDefault(d => d.RootId == rootId);
            if(db == null) return;
            db.Groups.RemoveAll(g => g.GroupId == groupId);
            if(db.Groups.Count == 0) Databases.Remove(db);
        }

        public void RemoveDatabase(string rootId)
        {
            Databases.RemoveAll(d => d.RootId == rootId);
        }

        public string Serialize()
        {
            Validate();
            var serializer = new XmlSerializer(typeof(LayoutStore));
            using(var writer = new StringWriter())
            {
                serializer.Serialize(writer, this);
                return writer.ToString();
            }
        }

        public static LayoutStore Deserialize(string xml)
        {
            if(string.IsNullOrWhiteSpace(xml)) return new LayoutStore();
            if(xml.Length > 4 * 1024 * 1024)
                throw new InvalidDataException("Configuration is too large.");
            var settings = new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 4 * 1024 * 1024
            };
            using(var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                var store = (LayoutStore)new XmlSerializer(typeof(LayoutStore))
                    .Deserialize(reader);
                store.Validate();
                return store;
            }
        }

        public void Validate()
        {
            if(Version != 1 || Databases == null || Databases.Count > 2048)
                throw new InvalidDataException("Unsupported layout configuration.");
            if(Default != null) Default.Validate();
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach(DatabaseLayouts d in Databases)
            {
                if(d == null || string.IsNullOrEmpty(d.RootId) ||
                    d.RootId.Length > 64 || !roots.Add(d.RootId) ||
                    d.Groups == null || d.Groups.Count > 8192)
                    throw new InvalidDataException("Invalid database layout.");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach(GroupLayout g in d.Groups)
                {
                    if(g == null || string.IsNullOrEmpty(g.GroupId) ||
                        g.GroupId.Length > 64 || !ids.Add(g.GroupId) ||
                        g.Layout == null)
                        throw new InvalidDataException("Invalid group layout.");
                    g.Layout.Validate();
                }
            }
        }
    }
}
