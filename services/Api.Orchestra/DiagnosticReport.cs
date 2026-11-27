using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace MysticEgo.Api.Orchestra;

/// <summary>Three equivalent encodings of an explicitly projected, share-safe diagnostic snapshot.</summary>
public static class DiagnosticReport
{
    public sealed record Table(string Name, string[] Columns, object?[][] Rows);
    private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string ContentNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly UTF8Encoding Utf8 = new(false);

    public static byte[] Encode(Table[] tables, string format)
    {
        if (tables.Length == 0 || tables.Any(t => t.Columns.Length == 0 || t.Rows.Any(r => r.Length != t.Columns.Length)))
            throw new ArgumentException("Invalid report table");
        return format switch
        {
            "json" => JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, tables = tables.Select(t => new
            {
                name = t.Name, columns = t.Columns, rows = t.Rows.Select(row => row.Select(Normalize).ToArray())
            }) }, new JsonSerializerOptions { WriteIndented = true }),
            "txt" => Text(tables),
            "xlsx" => Workbook(tables),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static object? Normalize(object? value) => value switch
    {
        DateTimeOffset at => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        double number when !double.IsFinite(number) => null,
        float number when !float.IsFinite(number) => null,
        _ => value
    };
    private static string Display(object? value) => Convert.ToString(Normalize(value), CultureInfo.InvariantCulture) ?? "";
    private static byte[] Text(Table[] tables)
    {
        var text = new StringBuilder("MysticEgo diagnostic report | schema 1\n");
        foreach (var table in tables)
        {
            text.Append("\n[").Append(table.Name).AppendLine("]");
            text.AppendLine(string.Join("\t", table.Columns));
            foreach (var row in table.Rows)
                text.AppendLine(string.Join("\t", row.Select(v => Display(v).Replace("\\","\\\\").Replace("\r","\\r").Replace("\n","\\n").Replace("\t","\\t"))));
        }
        return Utf8.GetBytes(text.ToString());
    }

    private static byte[] Workbook(Table[] tables)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Xml(string path, Action<XmlWriter> write)
            {
                using var stream = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
                using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = Utf8 });
                write(writer);
            }
            void Attr(XmlWriter w, string key, string value) => w.WriteAttributeString(key, value);
            void Relationship(XmlWriter w, string id, string type, string target)
            {
                w.WriteStartElement("Relationship", RelNs); Attr(w,"Id",id); Attr(w,"Type",OfficeRel+"/"+type); Attr(w,"Target",target); w.WriteEndElement();
            }
            Xml("[Content_Types].xml", w =>
            {
                w.WriteStartElement("Types",ContentNs);
                void Type(string element,string key,string name,string type)
                { w.WriteStartElement(element,ContentNs); Attr(w,key,name); Attr(w,"ContentType",type); w.WriteEndElement(); }
                Type("Default","Extension","rels","application/vnd.openxmlformats-package.relationships+xml");
                Type("Default","Extension","xml","application/xml");
                Type("Override","PartName","/xl/workbook.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                for(int i=1;i<=tables.Length;i++) Type("Override","PartName",$"/xl/worksheets/sheet{i}.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                w.WriteEndElement();
            });
            Xml("_rels/.rels",w=>{w.WriteStartElement("Relationships",RelNs);Relationship(w,"rId1","officeDocument","xl/workbook.xml");w.WriteEndElement();});
            Xml("xl/_rels/workbook.xml.rels",w=>
            {
                w.WriteStartElement("Relationships",RelNs);
                for(int i=1;i<=tables.Length;i++) Relationship(w,$"rId{i}","worksheet",$"worksheets/sheet{i}.xml");
                w.WriteEndElement();
            });
            Xml("xl/workbook.xml",w=>
            {
                w.WriteStartElement("workbook",SheetNs);w.WriteStartElement("sheets",SheetNs);
                for(int i=0;i<tables.Length;i++)
                {
                    w.WriteStartElement("sheet",SheetNs);Attr(w,"name",tables[i].Name);Attr(w,"sheetId",(i+1).ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("r","id",OfficeRel,$"rId{i+1}");w.WriteEndElement();
                }
                w.WriteEndElement();w.WriteEndElement();
            });
            for(int i=0;i<tables.Length;i++)
            {
                var table=tables[i];
                Xml($"xl/worksheets/sheet{i+1}.xml",w=>
                {
                    w.WriteStartElement("worksheet",SheetNs);
                    w.WriteStartElement("sheetViews",SheetNs);w.WriteStartElement("sheetView",SheetNs);Attr(w,"workbookViewId","0");
                    w.WriteStartElement("pane",SheetNs);Attr(w,"ySplit","1");Attr(w,"topLeftCell","A2");Attr(w,"activePane","bottomLeft");Attr(w,"state","frozen");w.WriteEndElement();
                    w.WriteEndElement();w.WriteEndElement();
                    w.WriteStartElement("cols",SheetNs);
                    for(int c=0;c<table.Columns.Length;c++)
                    {
                        w.WriteStartElement("col",SheetNs);Attr(w,"min",(c+1).ToString());Attr(w,"max",(c+1).ToString());
                        Attr(w,"width",table.Columns[c].Contains("Utc",StringComparison.OrdinalIgnoreCase)?"32":"23");Attr(w,"customWidth","1");w.WriteEndElement();
                    }
                    w.WriteEndElement();
                    w.WriteStartElement("sheetData",SheetNs);
                    void Row(object?[] values,int index)
                    {
                        w.WriteStartElement("row",SheetNs);Attr(w,"r",index.ToString(CultureInfo.InvariantCulture));
                        for(int c=0;c<values.Length;c++)
                        {
                            var value=Normalize(values[c]);if(value is null)continue;
                            w.WriteStartElement("c",SheetNs);Attr(w,"r",Column(c)+index.ToString(CultureInfo.InvariantCulture));
                            if(value is bool b){Attr(w,"t","b");w.WriteElementString("v",SheetNs,b?"1":"0");}
                            else if(value is int or long or double or float or decimal)
                            {Attr(w,"t","n");w.WriteElementString("v",SheetNs,Display(value));}
                            else
                            {
                                // Inline text is never interpreted as an Excel formula.
                                Attr(w,"t","inlineStr");w.WriteStartElement("is",SheetNs);w.WriteStartElement("t",SheetNs);
                                w.WriteAttributeString("xml","space","http://www.w3.org/XML/1998/namespace","preserve");
                                w.WriteString(Display(value));w.WriteEndElement();w.WriteEndElement();
                            }
                            w.WriteEndElement();
                        }
                        w.WriteEndElement();
                    }
                    Row(table.Columns,1);
                    for(int r=0;r<table.Rows.Length;r++)Row(table.Rows[r],r+2);
                    w.WriteEndElement();
                    w.WriteStartElement("autoFilter",SheetNs);Attr(w,"ref","A1:"+Column(table.Columns.Length-1)+(table.Rows.Length+1));w.WriteEndElement();
                    w.WriteEndElement();
                });
            }
        }
        return output.ToArray();
    }

    private static string Column(int index)
    {
        var name="";
        for(index++;index>0;index=(index-1)/26)name=(char)('A'+(index-1)%26)+name;
        return name;
    }
}
