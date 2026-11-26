"""Uji asap alur lengkap: kalimat asisten -> batang -> sambungan otomatis -> layout -> Excel (accoreconsole).

    python tools/smoke/run_smoke3.py
"""
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
SENTENCE = "portal bentang 20 m tinggi 6 m miring 10 derajat 5 bay jarak 6 m kolom WF400 rafter WF350 gording C150x65x20x3,2 jarak 1,2 m bracing RD16"


def main():
    sys.stdout.reconfigure(errors="replace")   # konsol Windows cp1252 tidak bisa menampilkan ≤ dsb.
    os.makedirs(OUT, exist_ok=True)
    dwg = os.path.join(OUT, "steelkit_asisten.dwg")
    if os.path.exists(dwg):
        os.remove(dwg)
    s = ["_.SECURELOAD 0", '_.NETLOAD "%s"' % DLL.replace("\\", "/"), "_.INSUNITS 4",
         "-SKASISTEN", SENTENCE, "_SambungJuga", "0,0,0",
         "SKLEVEL", "Base:0;Atap:6000",
         "SKLAYOUT", "_A3", "GUDANG 20 X 30 M", "_.MODEL",
         "SKFABRIKASI", "_.MODEL",
         "SKEXCEL", "GUDANG 20 X 30 M", "5",
         "SKBENTROK", "SKCEK", "_.ZOOM _E", '_.SAVEAS 2018 "%s"' % dwg.replace("\\", "/")]
    scr = os.path.join(OUT, "smoke_asisten.scr")
    open(scr, "w", encoding="utf-8").write("\n".join(s) + "\n")
    p = subprocess.run([os.path.join(ACAD, "accoreconsole.exe"), "/s", scr, "/l", "en-US"], capture_output=True, timeout=1500)
    raw = p.stdout
    text = (raw.decode("utf-16-le", errors="replace") if raw[:200].count(b"\x00") > 50 else raw.decode("utf-8", errors="replace")).replace("\x00", "")
    open(os.path.join(OUT, "smoke_asisten.log"), "w", encoding="utf-8").write(text)
    for m in re.finditer(r"Quantity takeoff \(Excel\): (.+?\.xlsx)", text):
        src = m.group(1).strip()
        if os.path.exists(src):
            shutil.move(src, os.path.join(OUT, "qto_gudang.xlsx"))
    for line in text.splitlines():
        if any(k in line for k in ("SKBENTROK", "  batang ", "  sambungan ", "Asisten:", "asumsi:", "Sambungan otomatis", "Catatan", "PERHATIAN", "Semua sambungan", "Layout dibuat", "Gambar fabrikasi", "Quantity", "baja bersih", "SKCEK", "dibatalkan", "Exception")):
            print("  ", line.strip()[:240])
    cek = re.findall(r"SKCEK batang3d=(\d+) .*? gagal=(\d+) .*? sambungan=(\d+) sambungan_aturan_gagal=(\d+) blok_kosong=(\d+) data_titik_tidak_cocok=(\d+)", text)
    ok = bool(cek) and os.path.exists(dwg) and "dibatalkan" not in text
    if cek:
        n3, bad, nc, ncb, empty, stale = cek[-1]
        ok = ok and bad == "0" and ncb == "0" and empty == "0" and stale == "0" and int(nc) > 150 and int(n3) > 40
    fab = re.findall(r"Gambar fabrikasi dibuat: (\d+) lembar", text)
    ok = ok and bool(fab) and int(fab[-1]) >= 4
    clash = re.findall(r"SKBENTROK jumlah=(\d+)", text)
    ok = ok and bool(clash) and clash[-1] == "0"
    print("UJI ASAP ASISTEN:", "LOLOS" if ok else "GAGAL")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
