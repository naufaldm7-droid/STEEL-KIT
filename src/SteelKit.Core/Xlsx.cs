using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace SteelKit.Core
{
    /// <summary>
    /// Penulis .xlsx minimal (Office Open XML) tanpa pustaka luar: teks, angka, rumus, judul tebal, lebar kolom,
    /// baris judul dibekukan, baris total. Cukup untuk quantity takeoff yang dibuka di Excel / LibreOffice / Google Sheets.
    /// </summary>
    public static class Xlsx
    {
        public class Formula { public string Text; }
        public static Formula F(string fmt, int row) => new Formula { Text = string.Format(CultureInfo.InvariantCulture, fmt, row) };
        public static Formula F(string text) => new Formula { Text = text };

        public class Sheet
        {
            public string Name;
            public string[] Header;
            public double[] Widths;
            public List<object[]> Rows = new List<object[]>();
            public HashSet<int> TotalRows = new HashSet<int>();
            /// <summary>Nomor baris Excel berikutnya (1 = judul).</summary>
            public int Row => Rows.Count + 2;
            public void AddRow(params object[] cells) => Rows.Add(cells);
            public void AddTotal(string label, Dictionary<int, string> formulas)
            {
                var cells = new object[Header.Length];
                cells[0] = label;
                foreach (var kv in formulas) cells[kv.Key] = new Formula { Text = kv.Value };
                TotalRows.Add(Row);
                Rows.Add(cells);
            }
        }

        public class Workbook
        {
            public List<Sheet> Sheets = new List<Sheet>();
            public Sheet Add(string name, string[] header, double[] widths, bool first = false)
            {
                var s = new Sheet { Name = name, Header = header, Widths = widths };
                if (first) Sheets.Insert(0, s); else Sheets.Add(s);
                return s;
            }

            public void Save(string path)
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    Save(fs);
            }

            public void Save(Stream stream)
            {
                using (var z = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    Put(z, "[Content_Types].xml", ContentTypes());
                    Put(z, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                    Put(z, "xl/workbook.xml", WorkbookXml());
                    Put(z, "xl/_rels/workbook.xml.rels", WorkbookRels());
                    Put(z, "xl/styles.xml", Styles);
                    for (int i = 0; i < Sheets.Count; i++) Put(z, "xl/worksheets/sheet" + (i + 1) + ".xml", SheetXml(Sheets[i]));
                }
            }

            static void Put(ZipArchive z, string name, string xml)
            {
                var e = z.CreateEntry(name, CompressionLevel.Optimal);
                using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(xml);
            }

            string ContentTypes()
            {
                var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
                sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
                sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
                sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
                for (int i = 0; i < Sheets.Count; i++) sb.Append("<Override PartName=\"/xl/worksheets/sheet" + (i + 1) + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                return sb.Append("</Types>").ToString();
            }

            string WorkbookXml()
            {
                var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
                for (int i = 0; i < Sheets.Count; i++) sb.Append("<sheet name=\"" + Esc(Sheets[i].Name) + "\" sheetId=\"" + (i + 1) + "\" r:id=\"rId" + (i + 1) + "\"/>");
                return sb.Append("</sheets><calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/></workbook>").ToString();
            }

            string WorkbookRels()
            {
                var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                for (int i = 0; i < Sheets.Count; i++) sb.Append("<Relationship Id=\"rId" + (i + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + (i + 1) + ".xml\"/>");
                sb.Append("<Relationship Id=\"rId" + (Sheets.Count + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
                return sb.Append("</Relationships>").ToString();
            }

            // gaya: 0 biasa, 1 judul tebal berlatar, 2 angka 2 desimal, 3 bilangan bulat, 4 total tebal, 5 total angka tebal
            const string Styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.00\"/></numFmts>" +
                "<fonts count=\"2\"><font><sz val=\"10\"/><name val=\"Arial\"/></font><font><b/><sz val=\"10\"/><name val=\"Arial\"/></font></fonts>" +
                "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9E2EC\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style=\"thin\"><color rgb=\"FFB0B7C0\"/></left><right style=\"thin\"><color rgb=\"FFB0B7C0\"/></right><top style=\"thin\"><color rgb=\"FFB0B7C0\"/></top><bottom style=\"thin\"><color rgb=\"FFB0B7C0\"/></bottom><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"6\">" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
                "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
                "<xf numFmtId=\"3\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
                "<xf numFmtId=\"164\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
                "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";

            static string Col(int i) { var s = ""; i++; while (i > 0) { int m = (i - 1) % 26; s = (char)('A' + m) + s; i = (i - 1) / 26; } return s; }

            static string SheetXml(Sheet sh)
            {
                var ci = CultureInfo.InvariantCulture;
                var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
                sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
                sb.Append("<cols>");
                for (int i = 0; i < sh.Header.Length; i++) sb.Append("<col min=\"" + (i + 1) + "\" max=\"" + (i + 1) + "\" width=\"" + (i < sh.Widths.Length ? sh.Widths[i] : 12).ToString(ci) + "\" customWidth=\"1\"/>");
                sb.Append("</cols><sheetData>");
                sb.Append("<row r=\"1\">");
                for (int i = 0; i < sh.Header.Length; i++) sb.Append("<c r=\"" + Col(i) + "1\" t=\"inlineStr\" s=\"1\"><is><t>" + Esc(sh.Header[i]) + "</t></is></c>");
                sb.Append("</row>");
                for (int r = 0; r < sh.Rows.Count; r++)
                {
                    int rn = r + 2; bool tot = sh.TotalRows.Contains(rn);
                    sb.Append("<row r=\"" + rn + "\">");
                    var cells = sh.Rows[r];
                    for (int c = 0; c < cells.Length; c++)
                    {
                        var v = cells[c];
                        string re = Col(c) + rn;
                        if (v == null) { if (tot) sb.Append("<c r=\"" + re + "\" s=\"4\"/>"); continue; }
                        if (v is Formula f) sb.Append("<c r=\"" + re + "\" s=\"" + (tot ? 5 : 2) + "\"><f>" + Esc(f.Text.TrimStart('=')) + "</f></c>");
                        else if (v is double d) sb.Append("<c r=\"" + re + "\" s=\"" + (tot ? 5 : (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e9 ? 3 : 2)) + "\"><v>" + d.ToString("R", ci) + "</v></c>");
                        else if (v is int n) sb.Append("<c r=\"" + re + "\" s=\"" + (tot ? 5 : 3) + "\"><v>" + n.ToString(ci) + "</v></c>");
                        else sb.Append("<c r=\"" + re + "\" t=\"inlineStr\" s=\"" + (tot ? 4 : 0) + "\"><is><t xml:space=\"preserve\">" + Esc(Convert.ToString(v, ci)) + "</t></is></c>");
                    }
                    sb.Append("</row>");
                }
                sb.Append("</sheetData><pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/><pageSetup paperSize=\"9\" orientation=\"landscape\"/></worksheet>");
                return sb.ToString();
            }
        }

        static string Esc(string s) => SecurityElement.Escape(s ?? "") ?? "";
    }
}
