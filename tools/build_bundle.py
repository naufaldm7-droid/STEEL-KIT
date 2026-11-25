"""Bangun Steel Kit untuk semua versi AutoCAD dan susun paket rilis.

    python tools/build_bundle.py            # build + uji unit + bundle + zip
    python tools/build_bundle.py --install  # juga pasang ke AutoCAD pengguna ini (%APPDATA%\\Autodesk\\ApplicationPlugins)

Hasil:
    dist/SteelKitSetup-<versi>.exe         penginstal satu berkas (klik ganda, tanpa admin) - utama
    dist/SteelKit.bundle/                  (Contents/R24, R25, R26, Suara)
    dist/SteelKit-<versi>.zip              cadangan manual (bundle + INSTAL.bat + LEPAS.bat + BACA.txt)
"""
import os
import re
import shutil
import subprocess
import sys
import uuid
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
DIST = os.path.join(ROOT, "dist")
BUNDLE = os.path.join(DIST, "SteelKit.bundle")
BIN = os.path.join(ROOT, "src", "SteelKit.Acad", "bin", "Release")
VOICE_BIN = os.path.join(ROOT, "src", "SteelKit.Voice", "bin", "Release", "net48")
SETUP = os.path.join(ROOT, "src", "SteelKit.Setup")
NS = uuid.UUID("3c0c7b1e-5d0f-4a8e-9b7e-51f2a6c8d901")   # tetap -> ProductCode/UpgradeCode stabil antar versi
TARGETS = [  # (folder, target framework, SeriesMin, SeriesMax, versi AutoCAD)
    ("R24", "net48", "R24.0", "R24.3", "2021-2024"),
    ("R25", "net8.0-windows", "R25.0", "R25.9", "2025-2026"),
    ("R26", "net10.0-windows", "R26.0", "R26.9", "2027"),
]
COMMANDS = ["SKPROFIL", "SKPENAMPANG", "SKBATANG", "SKBATANG2D", "SKUBAH", "SKPUTAR", "SKLABEL", "SKTABEL", "SKBERAT", "SKINFO",
            "SKSAMBUNG", "SKSAMBUNGAUTO", "SKSAMBUNGTITIK", "SKSAMBUNGPARAM", "SKDETAIL",
            "SKASISTEN", "-SKASISTEN", "SKBROWSER", "SKLAYOUT", "SKEXCEL", "SKLEVEL",
            "SKKATALOG", "SKMUATULANG", "SKBANTUAN", "SKRIBBON", "SKCEK", "SKCEKRINCI"]


def version():
    s = open(os.path.join(ROOT, "Directory.Build.props"), encoding="utf-8").read()
    return re.search(r"<Version>([^<]+)</Version>", s).group(1)


def package_xml(ver):
    comps = []
    cmds = "\n".join('        <Command Local="%s" Global="%s" />' % (c, c) for c in COMMANDS)
    for folder, _tfm, smin, smax, acad in TARGETS:
        comps.append("""  <Components Description="AutoCAD {acad}">
    <RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="{smin}" SeriesMax="{smax}" />
    <ComponentEntry AppName="SteelKit" Version="{ver}" ModuleName="./Contents/{folder}/SteelKit.dll"
        AppDescription="Pustaka profil baja, asisten, batang dinamis, 28 jenis sambungan otomatis, layout, QTO Excel" AppType=".Net"
        LoadOnAutoCADStartup="True">
      <Commands GroupName="STEELKIT">
{cmds}
      </Commands>
    </ComponentEntry>
  </Components>""".format(acad=acad, smin=smin, smax=smax, ver=ver, folder=folder, cmds=cmds))
    return """<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" AutodeskProduct="AutoCAD" ProductType="Application"
    Name="Steel Kit" AppVersion="{ver}" FriendlyVersion="{ver}"
    Description="Pustaka profil baja Indonesia (WF, H, UNP, CNP, Z, siku, T, hollow, pipa, plat, rod) untuk AutoCAD."
    ProductCode="{{{pc}}}" UpgradeCode="{{{uc}}}" Author="@nrozan">
  <CompanyDetails Name="@nrozan" />
{comps}
</ApplicationPackage>
""".format(ver=ver, pc=str(uuid.uuid5(NS, "product-" + ver)).upper(), uc=str(uuid.uuid5(NS, "upgrade")).upper(),
           comps="\n".join(comps))


INSTAL = r"""@echo off
rem Pasang Steel Kit untuk AutoCAD 2021-2027 (per pengguna, tanpa hak admin).
set "DEST=%APPDATA%\Autodesk\ApplicationPlugins\SteelKit.bundle"
tasklist /fi "imagename eq acad.exe" | find /i "acad.exe" >nul && (
  echo AutoCAD sedang terbuka. Tutup AutoCAD dulu, lalu jalankan file ini lagi.
  pause
  exit /b 1
)
robocopy "%~dp0SteelKit.bundle" "%DEST%" /MIR /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
  echo GAGAL menyalin ke %DEST%
  pause
  exit /b 1
)
echo Steel Kit terpasang di: %DEST%
echo.
echo Buka AutoCAD. Tab ribbon "Steel Kit" muncul, atau ketik SKPROFIL.
pause
"""

LEPAS = r"""@echo off
rem Lepas Steel Kit dari AutoCAD. Katalog profil buatan Anda (%APPDATA%\SteelKit) tidak dihapus.
set "DEST=%APPDATA%\Autodesk\ApplicationPlugins\SteelKit.bundle"
if exist "%DEST%" rmdir /s /q "%DEST%"
echo Steel Kit sudah dilepas. Mulai ulang AutoCAD.
pause
"""

BACA = """Steel Kit untuk AutoCAD {ver}
============================

Pustaka profil baja untuk AutoCAD 2021-2027: WF/IWF, H-beam, UNP, CNP (C berbibir), Z berbibir, siku,
T, hollow kotak/persegi panjang, pipa (CHS, SCH40, SCH80, medium), plat strip, dan besi bulat.

PASANG : klik dua kali SteelKitSetup.exe (atau INSTAL.bat di paket zip), lalu buka AutoCAD.
LEPAS  : Pengaturan Windows > Aplikasi > Steel Kit untuk AutoCAD > Copot (atau LEPAS.bat).

Perintah utama (juga ada di tab ribbon "Steel Kit"):
  SKPROFIL     pustaka profil: pilih / ubah / tambah ukuran, lihat properti, lalu gambar
  SKPENAMPANG  penampang 2D dengan arsir, dimensi, label
  SKBATANG     batang 3D solid dari titik ke titik (berantai)
  SKBATANG2D   tampak batang 2D samping / atas
  SKUBAH       ubah panjang/ujung/putar/profil batang (atau tarik grip ujung batang)
  SKSAMBUNG    dialog sambungan bergambar: pelat dasar + angkur, pelat ujung, fin plate,
               siku ganda, puncak/splice, lutut + haunch, cleat gording
  SKSAMBUNGAUTO  sambungan otomatis di semua titik temu batang
  SKDETAIL     detail 2D sambungan (tampak, dimensi, catatan, daftar bahan)
  SKLABEL, SKTABEL, SKBERAT (daftar berat + bahan sambungan + CSV), SKINFO, SKBANTUAN

Catatan:
- Berat, luas, inersia, dan modulus dihitung langsung dari dimensi profil.
- Ukuran bawaan adalah ukuran tipikal pasar. Cocokkan dengan katalog pemasok Anda; semua ukuran bisa
  diubah atau ditambah dan tersimpan di %APPDATA%\\SteelKit\\profil_pengguna.csv (bisa dibuka di Excel).
- Plugin ini menyiapkan geometri dan data. Cek sambungan hanya aturan jarak/ukuran minimum (praktik umum).
  Kapasitas batang, sambungan, angkur, dan pondasi tetap dihitung engineer struktur.

Lisensi MIT - Copyright (c) 2026 @nrozan
"""


def main():
    ver = version()
    print("Steel Kit", ver)
    r = subprocess.run(["dotnet", "test", os.path.join(ROOT, "tests", "SteelKit.Tests"), "-v", "q", "--nologo"],
                       capture_output=True, text=True)
    tail = [l for l in r.stdout.splitlines() if "Passed!" in l or "Failed!" in l]
    print("Uji unit:", tail[-1].strip() if tail else r.stdout[-500:])
    if r.returncode:
        sys.exit("Uji unit gagal")
    for proj in ("SteelKit.Acad", "SteelKit.Voice"):
        r = subprocess.run(["dotnet", "build", os.path.join(ROOT, "src", proj), "-c", "Release", "--nologo", "-v", "q"],
                           capture_output=True, text=True)
        if r.returncode:
            print(r.stdout[-3000:])
            sys.exit("Build gagal: " + proj)
    if os.path.isdir(BUNDLE):
        shutil.rmtree(BUNDLE)
    for folder, tfm, *_ in TARGETS:
        dst = os.path.join(BUNDLE, "Contents", folder)
        os.makedirs(dst)
        shutil.copy2(os.path.join(BIN, tfm, "SteelKit.dll"), dst)
    # pembantu suara (satu untuk semua versi AutoCAD); hanya pustaka asli Windows x64
    suara = os.path.join(BUNDLE, "Contents", "Suara")
    os.makedirs(os.path.join(suara, "runtimes", "win-x64"))
    for f in os.listdir(VOICE_BIN):
        if f.endswith((".exe", ".dll", ".config")):
            shutil.copy2(os.path.join(VOICE_BIN, f), suara)
    nat = os.path.join(VOICE_BIN, "runtimes", "win-x64")
    for f in os.listdir(nat):
        shutil.copy2(os.path.join(nat, f), os.path.join(suara, "runtimes", "win-x64"))
    with open(os.path.join(BUNDLE, "PackageContents.xml"), "w", encoding="utf-8", newline="\r\n") as fh:
        fh.write(package_xml(ver))
    with open(os.path.join(BUNDLE, "Contents", "BACA.txt"), "w", encoding="utf-8", newline="\r\n") as fh:
        fh.write(BACA.format(ver=ver))
    zpath = os.path.join(DIST, "SteelKit-%s.zip" % ver)
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for root, _d, files in os.walk(BUNDLE):
            for f in files:
                p = os.path.join(root, f)
                z.write(p, os.path.join("SteelKit", os.path.relpath(p, DIST)))
        z.writestr("SteelKit/INSTAL.bat", INSTAL.replace("\n", "\r\n"))
        z.writestr("SteelKit/LEPAS.bat", LEPAS.replace("\n", "\r\n"))
        z.writestr("SteelKit/BACA.txt", BACA.format(ver=ver).replace("\n", "\r\n"))
    # penginstal satu berkas: bundle disisipkan ke SteelKitSetup.exe
    payload = os.path.join(SETUP, "payload.zip")
    with zipfile.ZipFile(payload, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for root, _d, files in os.walk(BUNDLE):
            for f in files:
                p = os.path.join(root, f)
                z.write(p, os.path.relpath(p, DIST))
    r = subprocess.run(["dotnet", "build", SETUP, "-c", "Release", "--nologo", "-v", "q"], capture_output=True, text=True)
    if r.returncode:
        print(r.stdout[-3000:])
        sys.exit("Build penginstal gagal")
    setup_exe = os.path.join(DIST, "SteelKitSetup-%s.exe" % ver)
    shutil.copy2(os.path.join(SETUP, "bin", "Release", "net48", "SteelKitSetup.exe"), setup_exe)
    # nama tetap untuk tautan "releases/latest/download/SteelKitSetup.exe"
    shutil.copy2(setup_exe, os.path.join(DIST, "SteelKitSetup.exe"))
    print("Bundle:", BUNDLE)
    print("Zip   :", zpath, "(%d KB)" % (os.path.getsize(zpath) // 1024))
    print("Setup :", setup_exe, "(%d KB)" % (os.path.getsize(setup_exe) // 1024))
    if "--install" in sys.argv:
        r = subprocess.run([setup_exe, "/diam"], capture_output=True, text=True)
        if r.returncode:
            sys.exit("Pasang gagal: " + (r.stderr or r.stdout).strip())
        print("Terpasang:", os.path.join(os.environ["APPDATA"], "Autodesk", "ApplicationPlugins", "SteelKit.bundle"))


if __name__ == "__main__":
    main()
