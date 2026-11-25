using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using SteelKit.Core;
using Font = System.Drawing.Font;
using Anchor = SteelKit.Core.Anchor;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(SteelKit.Acad.BrowserCommands))]

namespace SteelKit.Acad
{
    public class ProfilesAll : ProfileListBase
    {
        protected override Family[] Families => (Family[])Enum.GetValues(typeof(Family));
    }

    public class LevelNames : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) => new StandardValuesCollection(ModelData.Levels.Levels.Select(l => l.name).ToList());
    }

    public class CategoryNames : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) => new StandardValuesCollection(Categories.All);
    }

    /// <summary>Properti batang yang bisa diedit dari browser (berlaku untuk semua batang terpilih).</summary>
    public class MemberProps
    {
        [Category("1. Identitas"), DisplayName("Marking"), Description("Kosong = otomatis (C1, B2, R1, ...).")] public string Marking { get; set; }
        [Category("1. Identitas"), DisplayName("Level"), TypeConverter(typeof(LevelNames)), Description("Kosong = otomatis dari elevasi (SKLEVEL).")] public string Level { get; set; }
        [Category("1. Identitas"), DisplayName("Kategori"), TypeConverter(typeof(CategoryNames))] public string Kategori { get; set; }
        [Category("2. Batang"), DisplayName("Profil"), TypeConverter(typeof(ProfilesAll))] public string Profil { get; set; }
        [Category("2. Batang"), DisplayName("Putar (°)")] public double Putar { get; set; }
        [Category("2. Batang"), DisplayName("Acuan")] public Anchor Acuan { get; set; }
        [Category("3. Info"), DisplayName("Panjang (mm)"), ReadOnly(true)] public string Panjang { get; set; }
        [Category("3. Info"), DisplayName("Berat (kg)"), ReadOnly(true)] public string Berat { get; set; }
        [Category("3. Info"), DisplayName("Jumlah terpilih"), ReadOnly(true)] public int Jumlah { get; set; }
    }

    public class BrowserCommands
    {
        static PaletteSet palette;
        static Ui.BrowserView control;

        [CommandMethod("STEELKIT", "SKBROWSER", CommandFlags.Modal)]
        public void Show() => Guard.Run(ShowCore);
        void ShowCore()
        {
            if (palette == null)
            {
                palette = new PaletteSet("Steel Kit Browser", new Guid("6E0C5D7B-2F1A-4C3E-9B8D-7A1F0C2E5D44"))
                {
                    Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.Snappable,
                    MinimumSize = new Size(300, 500),
                };
                control = new Ui.BrowserView();
                palette.AddVisual("Model", control);
                palette.Dock = DockSides.Left;
                palette.Size = new Size(380, 800);
            }
            palette.Visible = true;
            control.Reload();
        }
    }
}
