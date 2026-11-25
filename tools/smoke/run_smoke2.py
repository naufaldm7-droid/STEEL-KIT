"""Uji asap Fase 2 (sambungan + batang dinamis) di accoreconsole.

    python tools/smoke/run_smoke2.py

Portal pelana 20 m (kolom, rafter, gording, balok lantai) -> SKSAMBUNGAUTO; 7 jenis sambungan di titik bebas;
edit batang (MOVE/ROTATE/COPY/SKUBAH/SKPUTAR) -> data titik harus tetap cocok; detail 2D; daftar berat; SKCEK.
"""
import math
import os
import re
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(HERE, "out")
ACAD = r"C:\Program Files\Autodesk\AutoCAD 2026"
DLL = os.path.join(ROOT, "src", "SteelKit.Acad", "bin", "Release", "net8.0-windows", "SteelKit.dll")


def f(*v):
    return ",".join("%.3f" % x for x in v)


def script(dwg):
    t = math.tan(math.radians(10))
    rise = 10000 * t
    s = ["_.SECURELOAD 0", '_.NETLOAD "%s"' % DLL.replace("\\", "/"), "_.INSUNITS 4"]
    # kolom (sayap menghadap X) dan rafter
    s += ["SKBATANG", "_Profil", "WF400x200x8x13", "_Acuan", "_Tengah", "_Putar", "90", f(0, 0, 0), f(0, 0, 6000), ""]
    s += ["SKBATANG", "_Putar", "90", f(20000, 0, 0), f(20000, 0, 6000), ""]
    s += ["SKBATANG", "_Putar", "0", f(0, 0, 6000), f(10000, 0, 6000 + rise), ""]
    s += ["SKBATANG", f(20000, 0, 6000), f(10000, 0, 6000 + rise), ""]
    # gording di atas rafter kiri (x = 3000), diputar mengikuti kemiringan
    zc = 6000 + 3000 * t
    sx, sz = math.sin(math.radians(10)), math.cos(math.radians(10))
    gx, gz = 3000 - 275 * sx, zc + 275 * sz
    s += ["SKBATANG", "_Profil", "C150x65x20x3.2", "_Putar", "-10", f(gx, -3000, gz), f(gx, 3000, gz), ""]
    # balok lantai masuk ke sayap kolom kiri
    s += ["SKBATANG", "_Profil", "WF300x150x6.5x9", "_Putar", "0", f(0, 0, 3000), f(-5000, 0, 3000), ""]
    # rangka siku: sudut + siku-T (kasus dari pengguna)
    s += ["SKBATANG", "_Profil", "L75x75x8", "_Putar", "0", f(0, 40000, 0), f(3000, 40000, 0), f(3000, 42000, 0), ""]
    s += ["SKBATANG", f(1500, 40000, 0), f(1500, 42000, 0), ""]
    # struktur tambahan untuk jenis baru
    Y = 60000
    s += ["SKBATANG", "_Profil", "WF400x200x8x13", "_Putar", "90", f(0, Y, 0), f(0, Y, 6000), ""]
    s += ["SKBATANG", f(0, Y + 6000, 0), f(0, Y + 6000, 6000), ""]
    s += ["SKBATANG", "_Profil", "RD16", "_Putar", "0", f(0, Y, 500), f(0, Y + 6000, 5500), ""]                   # rod bracing + turnbuckle
    s += ["SKBATANG", "_Profil", "H300x300x10x15", "_Putar", "0", f(10000, Y, 0), f(10000, Y, 3000), ""]       # pelat tutup
    s += ["SKBATANG", "_Profil", "WF400x200x8x13", f(7000, Y, 3200), f(13000, Y, 3200), ""]
    s += ["SKBATANG", "_Profil", "H150x150x7x10", "_Putar", "90", f(16000, Y, 200), f(16000, Y, 2000), ""]     # tiang di atas balok
    s += ["SKBATANG", "_Profil", "WF400x200x8x13", "_Putar", "0", f(13000, Y, 0), f(19000, Y, 0), ""]
    s += ["SKBATANG", "_Profil", "SHS200x200x9", f(22000, Y, 0), f(22000, Y, 4000), ""]                       # kolom hollow
    s += ["SKBATANG", "_Profil", "WF250x125x6x9", f(22000, Y, 3000), f(27000, Y, 3000), ""]
    s += ["SKBATANG", "_Profil", "WF400x200x8x13", f(30000, Y - 3000, 0), f(30000, Y + 3000, 0), ""]           # balok tumpuk
    s += ["SKBATANG", "_Profil", "WF250x125x6x9", f(27000, Y, 325), f(33000, Y, 325), ""]
    s += ["SKBATANG", "_Profil", "Z200x75x20x2.3", f(36000, Y, 0), f(42000, Y, 0), f(48000, Y, 0), ""]        # gording tumpang
    s += ["SKBATANG", "_Profil", "L75x75x8", "_Putar", "90", f(50000, Y, 0), f(54000, Y, 0), ""]               # buhul truss
    s += ["SKBATANG", "_Profil", "L50x50x5", f(52000, Y, 0), f(53500, Y, 1500), ""]
    s += ["SKBATANG", f(52000, Y, 0), f(52000, Y, 1500), ""]
    s += ["SKBATANG", f(52000, Y, 0), f(50500, Y, 1500), ""]
    s += ["SKBATANG", "_Profil", "SHS100x100x4.5", "_Putar", "0", f(56000, Y, 0), f(60000, Y, 0), ""]         # las langsung
    s += ["SKBATANG", "_Profil", "SHS60x60x3.2", f(58000, Y, 0), f(58000, Y, 1500), ""]
    s += ["SKBATANG", "_Profil", "WF500x200x10x16", f(62000, Y, 0), f(70000, Y, 0), ""]                      # balok anak dengan coping
    s += ["SKBATANG", "_Profil", "WF300x150x6.5x9", f(66000, Y, 100), f(66000, Y + 6000, 100), ""]
    s += ["SKSAMBUNGAUTO", ""]
    s += ["SKSAMBUNGAUTO", ""]          # kedua kali: semua harus dilewati (tidak dobel)
    # 7 jenis sambungan di titik bebas
    x = 40000
    for kw in ["PelatDasar", "PelatUjung", "FinPlate", "SikuGanda", "Splice", "Lutut", "Gording", "Buhul", "Rod", "Turnbuckle", "BuhulBracing", "Silang", "FlangeBrace", "SagRod", "PelatTutup", "Dudukan", "MomenLas", "MomenPelat", "KolomHollow", "Tumpuk", "KolomAngin", "Konsol", "Beton", "SplicePelat", "GordingSambung", "Truss", "LasLangsung", "AnakTangga"]:
        s += ["SKSAMBUNGTITIK", "_" + kw, f(x, 0, 0)]
        x += 3500
    # ubah parameter: angkur jadi M30 tipe kimia di pelat dasar titik
    # batang dinamis
    s += ["SKBATANG", "_Profil", "H300x300x10x15", "_Putar", "0", f(0, 20000, 0), f(6000, 20000, 0), ""]
    s += ["_.MOVE", "_L", "", f(0, 0, 0), f(0, 1000, 0)]
    s += ["_.ROTATE", "_L", "", f(0, 21000, 0), "30"]
    s += ["_.COPY", "_L", "", f(0, 0, 0), f(0, 3000, 0)]
    s += ["SKUBAH", "_L", "", "_Panjang", "4000"]
    s += ["SKPUTAR", "_L", "", "45"]
    s += ["SKBATANG2D", "_Profil", "WF300x150x6.5x9", f(0, 30000, 0), f(5000, 30000, 0), ""]
    s += ["SKUBAH", "_L", "", "_Panjang", "3500"]
    s += ["_.MOVE", "_L", "", f(0, 0, 0), f(0, 500, 0)]
    s += ["SKDETAIL", "_ALL", "", f(0, -30000, 0)]
    s += ["SKBERAT", "", f(80000, 0, 0)]
    s += ["SKLAYOUT", "_A3", "PORTAL UJI", "_.MODEL"]
    s += ["SKLEVEL", "Base:0;Lt.2:3000;Atap:6000"]
    s += ["SKEXCEL", "PORTAL UJI", "5"]
    s += ["SKCEK", "SKCEKRINCI"]
    s += ["_.ZOOM _E", '_.SAVEAS 2018 "%s"' % dwg.replace("\\", "/")]
    return "\n".join(s) + "\n"


def main():
    os.makedirs(OUT, exist_ok=True)
    dwg = os.path.join(OUT, "steelkit_fase2.dwg")
    if os.path.exists(dwg):
        os.remove(dwg)
    scr = os.path.join(OUT, "smoke_fase2.scr")
    open(scr, "w", encoding="utf-8").write(script(dwg))
    p = subprocess.run([os.path.join(ACAD, "accoreconsole.exe"), "/s", scr, "/l", "en-US"], capture_output=True, timeout=900)
    raw = p.stdout
    text = (raw.decode("utf-16-le", errors="replace") if raw[:200].count(b"\x00") > 50 else raw.decode("utf-8", errors="replace")).replace("\x00", "")
    open(os.path.join(OUT, "smoke_fase2.log"), "w", encoding="utf-8").write(text)
    for m in re.finditer(r"Quantity takeoff \(Excel\): (.+?\.xlsx)", text):
        if os.path.exists(m.group(1).strip()):
            shutil.move(m.group(1).strip(), os.path.join(OUT, "qto_portal_uji.xlsx"))
    for m in re.finditer(r"CSV: (.+?\.csv)", text):
        if os.path.exists(m.group(1).strip()):
            shutil.move(m.group(1).strip(), os.path.join(OUT, "daftar_berat_fase2.csv"))
    keep = ("Sambungan otomatis", "sudah ada", "Catatan", "PERHATIAN", "Semua sambungan", "dibuat.", "diubah", "diputar",
            "detail sambungan", "Total baja", "Quantity takeoff", "baja bersih", "Level disimpan", "SKCEK", "SKRINCI", "Exception", "gagal", "Unknown command", "tidak ada di katalog")
    for line in text.splitlines():
        if any(k in line for k in keep):
            print("  ", line.strip()[:260])
    cek = re.findall(r"SKCEK batang3d=(\d+) batang2d=(\d+) penampang=(\d+) volume_selisih_maks=([\d.]+)% gagal=(\d+) satuan=\S+ sambungan=(\d+) sambungan_aturan_gagal=(\d+) blok_kosong=(\d+) data_titik_tidak_cocok=(\d+)", text)
    ok = bool(cek) and os.path.exists(dwg) and "Exception" not in text
    if cek:
        n3, n2, ns, worst, bad, nc, ncb, empty, stale = cek[-1]
        ok = ok and bad == "0" and ncb == "0" and empty == "0" and stale == "0" and int(nc) >= 40
    print("UJI ASAP FASE 2:", "LOLOS" if ok else "GAGAL")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
