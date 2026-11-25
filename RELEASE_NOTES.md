# Steel Kit 0.1.0 — rilis pertama

Plugin AutoCAD gratis untuk baja struktur: katalog profil → batang 3D → 28 jenis sambungan otomatis →
gambar kerja → quantity takeoff Excel, dengan asisten bahasa Indonesia (ketik atau bicara, offline).

## Unduh & pasang
1. Unduh **SteelKitSetup.exe** (±2 MB) di bawah.
2. Tutup AutoCAD, klik ganda `SteelKitSetup.exe`, klik **Pasang** (tanpa hak admin). Centang "suara offline" untuk
   sekalian mengunduh model suara (±181 MB, sekali saja).
3. Buka AutoCAD → tab ribbon **Steel Kit**, atau ketik `SKASISTEN`.

Windows SmartScreen mungkin menampilkan "Windows protected your PC" karena penginstal belum bertanda tangan kode:
klik *More info* → *Run anyway*. Copot kapan saja dari Pengaturan Windows › Aplikasi.

## Isi
- **Asisten** (`SKASISTEN`): kalimat sehari-hari → usulan batang yang bisa diperiksa & diedit → pasang + sambung otomatis.
  Suara: Whisper offline di komputer sendiri, berhenti otomatis saat diam, mikrofon fisik dipilih otomatis.
- **Pustaka profil** (`SKPROFIL`): 476 ukuran (WF, H, UNP, CNP, Z, siku, T, hollow, pipa CHS/SCH40/SCH80), semua bisa diubah/ditambah.
- **Batang 3D/2D** dengan grip regang & putar, tetap konsisten setelah MOVE/ROTATE/COPY/MIRROR.
- **28 jenis sambungan** bergambar teknik dengan rekomendasi pemakaian dan cek jarak/ukuran minimum;
  batang dipotong, diperpanjang, di-coping dan dipotong tegak rapat ke pelat otomatis.
- **Gambar kerja** (`SKLAYOUT`): lembar struktur (isometrik, denah, tampak) dan lembar detail sambungan berdimensi.
- **Kuantitas** (`SKEXCEL`): Excel bahasa Inggris, metrik — batang, pelat, baut, angkur, las.
- **Browser** (`SKBROWSER`): palet kiri per level & kategori, edit properti langsung.
- Satu tampilan kaca gelap untuk semua jendela, logo sendiri; AutoCAD 2021–2027.

## Batas tanggung jawab
Cek sambungan hanya aturan jarak/ukuran minimum menurut praktik umum — bukan cek kekuatan. Ukuran profil, kapasitas
batang, sambungan, angkur dan pondasi tetap dihitung dan disahkan engineer struktur berlisensi.
