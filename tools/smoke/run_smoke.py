"""Uji asap Steel Kit di AutoCAD tanpa tampilan (accoreconsole).

    python tools/smoke/run_smoke.py [--acad "C:\\Program Files\\Autodesk\\AutoCAD 2026"]

Untuk tiap satuan gambar (mm dan m): muat DLL, gambar satu batang 3D per keluarga profil (berbagai arah,
acuan, dan putaran), batang 2D, penampang, label, tabel, daftar berat, lalu SKCEK membandingkan volume
setiap solid dengan luas penampang x panjang. Gagal bila ada exception, SKCEK gagal>0, atau DWG tidak tersimpan.
Hasil: tools/smoke/out/*.dwg dan *.log
"""
import os
import re
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(HERE, "out")
ACAD = sys.argv[sys.argv.index("--acad") + 1] if "--acad" in sys.argv else r"C:\Program Files\Autodesk\AutoCAD 2026"
DLL = os.path.join(ROOT, "src", "SteelKit.Acad", "bin", "Release", "net8.0-windows", "SteelKit.dll")

# satu profil per keluarga + variasi acuan/putar
MEMBERS = [
    ("WF400x200x8x13", "Tengah", 0, "0,0,0", "6000,0,0"),
    ("H300x300x10x15", "Bawah", 0, "0,0,0", "0,0,5000"),
    ("UNP150x75x6.5x10", "Atas", 90, "0,2000,0", "4000,5000,0"),
    ("C150x65x20x3.2", "TitikBerat", 0, "0,3000,3000", "6000,3000,4000"),
    ("Z200x75x20x2.3", "Tengah", 15, "0,4000,0", "6000,4000,0"),
    ("L75x75x9", "TitikBerat", 0, "0,5000,0", "3000,7000,2000"),
    ("T100x200x8x12", "Atas", 180, "0,6000,0", "6000,6000,0"),
    ("SHS100x100x3.2", "Tengah", 45, "0,7000,0", "0,7000,3000"),
    ("RHS200x100x4.5", "Bawah", 0, "0,8000,0", "5000,8000,0"),
    ("CHS114.3x4.5", "Tengah", 0, "0,9000,0", "4000,12000,3000"),
    ("SCH40-4in", "Tengah", 0, "0,10000,0", "6000,10000,0"),
    ("MED-2in", "Tengah", 0, "0,11000,0", "3000,11000,0"),
    ("PL150x12", "Tengah", 0, "0,12000,0", "1000,12000,0"),
    ("RD16", "Tengah", 0, "0,13000,0", "6000,13000,6000"),
]


def script(units, dwg):
    s = ["_.SECURELOAD 0", "_.NETLOAD \"%s\"" % DLL.replace("\\", "/"), "_.INSUNITS %d" % units]
    f = 1.0 if units == 4 else 0.001

    def pt(p):
        return ",".join("%g" % (float(v) * f) for v in p.split(","))

    for name, anchor, roll, a, b in MEMBERS:
        s += ["SKBATANG", "_Profil", name, "_Acuan", "_" + anchor, "_Putar", str(roll), pt(a), pt(b), ""]
    # rantai 3 batang (berantai seperti LINE)
    s += ["SKBATANG", "_Profil", "WF300x150x6.5x9", "_Acuan", "_Tengah", "_Putar", "0",
          pt("0,15000,0"), pt("0,15000,4000"), pt("8000,15000,5000"), pt("16000,15000,4000"), ""]
    # 2D samping dan atas
    s += ["SKBATANG2D", "_Profil", "WF400x200x8x13", pt("0,-3000,0"), pt("6000,-3000,0"), "",
          "SKBATANG2D", "_Tampak", pt("0,-4000,0"), pt("6000,-5000,0"), "", "SKBATANG2D", "_Tampak", "_Profil", "SHS100x100x3.2",
          pt("0,-6000,0"), pt("4000,-6000,0"), ""]
    # penampang semua keluarga
    x = 0
    for name, *_ in MEMBERS:
        s += ["SKPENAMPANG", "_Profil", name, pt("%d,-10000,0" % x)]
        x += 900
    s += ["SKLABEL", "_ALL", ""]
    s += ["SKTABEL", "_Profil", "WF400x200x8x13", "_Satu", pt("20000,0,0")]
    s += ["SKTABEL", "_Profil", "L75x75x9", "_Keluarga", pt("26000,0,0")]
    s += ["SKINFO", ""]
    s += ["SKBERAT", "", pt("20000,-6000,0")]
    s += ["SKCEK"]
    s += ["_.ZOOM _E", "_.SAVEAS 2018 \"%s\"" % dwg.replace("\\", "/")]
    return "\n".join(s) + "\n"


def run(units, label):
    os.makedirs(OUT, exist_ok=True)
    dwg = os.path.join(OUT, "steelkit_smoke_%s.dwg" % label)
    if os.path.exists(dwg):
        os.remove(dwg)
    scr = os.path.join(OUT, "smoke_%s.scr" % label)
    with open(scr, "w", encoding="utf-8") as fh:
        fh.write(script(units, dwg))
    exe = os.path.join(ACAD, "accoreconsole.exe")
    p = subprocess.run([exe, "/s", scr, "/l", "en-US"], capture_output=True, timeout=600)
    raw = p.stdout
    text = raw.decode("utf-16-le", errors="replace") if raw[:200].count(b"\x00") > 50 else raw.decode("utf-8", errors="replace")
    text = text.replace("\x00", "")
    with open(os.path.join(OUT, "smoke_%s.log" % label), "w", encoding="utf-8") as fh:
        fh.write(text)
    cek = re.findall(r"SKCEK batang3d=(\d+) batang2d=(\d+) penampang=(\d+) volume_selisih_maks=([\d.]+)% gagal=(\d+) satuan=(\S+)", text)
    problems = []
    for pat in [r"Exception", r"eInvalid", r"Unknown command", r"Perintah tidak dikenal", r"tidak ada di katalog", r"\*Invalid"]:
        for m in re.finditer(pat, text):
            problems.append(text[max(0, m.start() - 160):m.end() + 80].replace("\n", " | "))
    if not cek:
        problems.append("SKCEK tidak terbaca")
    else:
        n3, n2, ns, worst, bad, unit = cek[-1]
        print("[%s] SKCEK: batang3d=%s batang2d=%s penampang=%s selisih volume maks=%s%% gagal=%s satuan=%s" % (label, n3, n2, ns, worst, bad, unit))
        if int(bad) or int(n3) != len(MEMBERS) + 3 or int(n2) != 3 or int(ns) != len(MEMBERS):
            problems.append("jumlah/volume tidak sesuai")
    for line in text.splitlines():
        if "dimuat:" in line or "kg (tanpa" in line or "CSV:" in line or "label dibuat" in line:
            print("   ", line.strip())
    # CSV daftar berat jatuh ke Documents karena gambar uji belum bernama: pindahkan ke folder out
    for m in re.finditer(r"CSV: (.+?\.csv)", text):
        src = m.group(1).strip()
        if os.path.exists(src):
            shutil.move(src, os.path.join(OUT, "daftar_berat_%s.csv" % label))
    if not os.path.exists(dwg):
        problems.append("DWG tidak tersimpan")
    else:
        print("    DWG: %s (%d KB)" % (dwg, os.path.getsize(dwg) // 1024))
    for pr in problems[:12]:
        print("  MASALAH:", pr)
    return not problems


if __name__ == "__main__":
    if not os.path.exists(DLL):
        sys.exit("Build dulu: dotnet build src/SteelKit.Acad -c Release")
    ok = run(4, "mm") & run(6, "m")
    print("UJI ASAP:", "LOLOS" if ok else "GAGAL")
    sys.exit(0 if ok else 1)
