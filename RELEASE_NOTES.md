# Steel Kit 0.1.0 — rilis pertama

Plugin AutoCAD gratis untuk baja struktur: katalog profil → batang 3D → 28 jenis sambungan otomatis →
gambar kerja & gambar fabrikasi → quantity takeoff Excel, dengan asisten bahasa Indonesia (ketik atau bicara, offline).

## Unduh & pasang
1. Unduh **SteelKitSetup.exe** (±2 MB) di bawah.
2. Tutup AutoCAD, klik ganda `SteelKitSetup.exe`, klik **Pasang** (tanpa hak admin). Centang "suara offline" untuk
   sekalian mengunduh model suara (±181 MB, sekali saja).
3. Buka AutoCAD → tab ribbon **Steel Kit**, atau ketik `SKASISTEN`.

Urutan kerja 8 langkah dan daftar semua perintah: [halaman Steel Kit › Urutan Desain](https://naufaldm7-droid.github.io/Gesture-3D-View-/steelkit/#urutan).

Windows SmartScreen mungkin menampilkan "Windows protected your PC" karena penginstal belum bertanda tangan kode:
klik *More info* → *Run anyway*. Copot kapan saja dari Pengaturan Windows › Aplikasi.

## Isi
- **Asisten** (`SKASISTEN`): kalimat sehari-hari → usulan batang yang bisa diperiksa & diedit → pasang + sambung otomatis.
  Suara: Whisper offline di komputer sendiri, berhenti otomatis saat diam, mikrofon fisik dipilih otomatis;
  angka ucapan dibaca utuh ("WF empat ratus" → WF400, "C seratus lima puluh" → C150).
- **Pustaka profil** (`SKPROFIL`): 476 ukuran (WF, H, UNP, CNP, Z, siku, T, hollow, pipa CHS/SCH40/SCH80), semua bisa diubah/ditambah.
- **Batang 3D/2D** dengan grip regang & putar, tetap konsisten setelah MOVE/ROTATE/COPY/MIRROR.
- **28 jenis sambungan** bergambar teknik dengan rekomendasi pemakaian dan cek jarak/ukuran minimum;
  batang dipotong, diperpanjang, di-coping dan dipotong tegak rapat ke pelat otomatis.
- **Asisten mengenal istilah teknik umum** (Indonesia/Inggris, dan ejaan berlogat dari pengenal suara): kanopi/canopy/carport,
  kantilever/cantilever, menara/tower air, jembatan/bridge/JPO, rangka batang/truss, gelagar/girder, frame, column/tiang,
  beam, purlin, brace/ikatan angin, span/height/spacing. Bentuk baru: **kanopi kantilever** (satu sisi atau T), kanopi dua
  kolom, **menara rangka 4 kaki**, **jembatan rangka (Pratt)** dan jembatan gelagar. Kosakata suara Whisper diperluas.
- **Opsi sambungan** (semua bisa diubah, nilai bawaan praktik umum, dicek aturan jarak/ukuran minimum):
  apex haunch di sambungan puncak; pengaku pelat dasar tanpa/2 segaris badan/4 segaris sayap/6; pengaku badan kolom
  tiap sekian mm sepanjang kolom (sudut di-snipe); pelat buhul bracing trapesium atau otomatis; cleat gording dengan
  pengaku; jarak baris baut tambahan tetap atau dibagi rata di pelat ujung, puncak, dan lutut.
- **Standar gambar**: huruf ISOCPEUR, tinggi teks ISO 1,8/2,5/3,5/5 mm, tebal garis per layer, tabel rata kiri/kanan,
  leader 45° dengan landasan mendatar yang tidak bertumpuk atau menyilang.
- **Model 3D bebas tabrakan**: baut mengebor lubang standar di batang yang dilaluinya, rod bracing lewat lubang
  lonjong di badan host, pengaku kolom diberi snipe di radius sudut profil, ring miring rata dengan badan;
  balok miring yang hanya ditopang satu kolom (kanopi T) memakai pelat tutup miring dengan puncak kolom dipotong
  sejajar pelat; balok anak rata-atas di-coping atas & bawah; pelat buhul sudut dibatasi batang yang sebidang.
  **`SKBENTROK`** mengecek tabrakan 3D batang & sambungan dan menggambar irisannya di layer `SK-BENTROK`.
- **Gambar kerja** (`SKLAYOUT`): lembar struktur (isometrik, denah, tampak) dengan grid as & gelembung, dimensi antar as,
  penanda elevasi, marking batang, catatan umum, kotak judul lengkap (proyek, no. gambar, lembar x/n, revisi) dan
  skala grafis; lembar detail sambungan dengan simbol las (praktik ISO 2553 sistem B) dan daftar bahan.
- **Gambar fabrikasi** (`SKFABRIKASI`): satu lembar per marking — tampak dari solid sebenarnya (lubang & potongan ujung
  terlihat), dimensi dari ujung kiri ("4x110=440"), daftar lubang, tabel data marking, peringatan batang > 12 m.
- **Marking** mengikuti peran, profil, panjang, dan pola lubang/potongan; sama di Excel, layout, dan fabrikasi.
- **Kuantitas** (`SKEXCEL`): Excel bahasa Inggris, metrik — batang, pelat, baut, angkur, las.
- **Browser** (`SKBROWSER`): palet kiri per level & kategori, edit properti langsung.
- Satu tampilan kaca gelap untuk semua jendela, logo sendiri; AutoCAD 2021–2027.

## Asumsi tata letak asisten (praktik umum, bisa diubah)
Gording tepi di luar zona lutut dan dibagi rata (jarak ≤ yang diminta); gording & girt diteruskan 100 mm melewati
rafter/kolom ujung; dua rod bracing X digeser ±d/2 supaya saling melewati; turnbuckle di 0,3 panjang rod.

## Batas tanggung jawab
Cek sambungan hanya aturan jarak/ukuran minimum menurut praktik umum — bukan cek kekuatan. Ukuran profil, kapasitas
batang, sambungan, angkur dan pondasi tetap dihitung dan disahkan engineer struktur berlisensi.
