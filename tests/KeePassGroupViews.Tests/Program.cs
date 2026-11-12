using System;
using System.IO;
using System.Linq;
using KeePassGroupViews;

internal static class Program
{
    private static int _tests;

    private static void Check(bool result, string name)
    {
        _tests++;
        if(!result) throw new Exception("Failed: " + name);
    }

    private static ColumnLayout Layout(int type, string name = "")
    {
        return new ColumnLayout { Columns = {
            new ColumnSpec { Type = type, Name = name, Width = 180,
                DisplayIndex = 0, Mask = type == 2 }
        } };
    }

    private static void Reject(Action action, string name)
    {
        bool threw = false;
        try { action(); }
        catch(InvalidDataException) { threw = true; }
        catch(System.Xml.XmlException) { threw = true; }
        catch(InvalidOperationException) { threw = true; }
        Check(threw, name);
    }

    public static int Main()
    {
        var store = new LayoutStore { Default = Layout(0) };
        store.Set("database1", "parent", Layout(1));
        store.Set("database1", "child", Layout(2));
        store.Set("database2", "child", Layout(3));

        Check(store.Resolve("database1", new[] { "child", "parent" })
            .Columns[0].Type == 2, "direct override");
        Check(store.Resolve("database1", new[] { "grandchild", "parent" })
            .Columns[0].Type == 1, "parent inheritance");
        Check(store.Resolve("database1", new[] { "unknown" })
            .Columns[0].Type == 0, "default fallback");
        Check(store.Resolve("database2", new[] { "child" })
            .Columns[0].Type == 3, "database isolation");

        ColumnLayout copy = store.Resolve("database1", new[] { "child" });
        copy.Columns[0].Width = 500;
        Check(store.Resolve("database1", new[] { "child" })
            .Columns[0].Width == 180, "resolve returns a copy");

        string serialized = store.Serialize();
        LayoutStore roundtrip = LayoutStore.Deserialize(serialized);
        Check(roundtrip.Resolve("database1", new[] { "child" })
            .Columns[0].Mask, "XML roundtrip preserves masking");
        Check(roundtrip.Serialize().Length > 0, "XML reserialization");

        roundtrip.Remove("database1", "child");
        Check(roundtrip.Resolve("database1", new[] { "child", "parent" })
            .Columns[0].Type == 1, "removing an override restores inheritance");
        Check(!roundtrip.HasOverride("database1", "child"), "override removed");
        roundtrip.RemoveDatabase("database2");
        Check(roundtrip.Resolve("database2", new[] { "child" })
            .Columns[0].Type == 0, "database removal preserves default");

        Reject(() => LayoutStore.Deserialize("<!DOCTYPE test [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><test>&x;</test>"),
            "XML DTD rejected");
        Reject(() => LayoutStore.Deserialize(serialized.Replace("<Version>1</Version>",
            "<Version>99</Version>")), "unsupported store version");
        Reject(() => new ColumnLayout { Columns = {
            new ColumnSpec { Type = 0, DisplayIndex = 0 },
            new ColumnSpec { Type = 1, DisplayIndex = 0 }
        } }.Validate(), "duplicate indices rejected");
        Reject(() => new ColumnLayout().Validate(), "empty columns rejected");

        Console.WriteLine("Passed " + _tests + " layout tests.");
        return 0;
    }
}
