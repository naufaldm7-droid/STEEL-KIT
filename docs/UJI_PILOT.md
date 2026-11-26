# Uji pilot Steel Kit 0.1.x

Panduan singkat untuk 2–3 drafter/engineer yang mencoba Steel Kit di pekerjaan nyata. Satu sesi ±45 menit.
Catat hasil di tabel paling bawah; kirim balik bersama file DWG bila ada yang aneh.

## Sudah diuji otomatis (tidak perlu diulang)
- 78 unit test: sambungan, aturan jarak/ukuran minimum, lubang baut, grid/level, simbol las, marking, angka ucapan.
- Uji asap AutoCAD 2026 (accoreconsole): kalimat asisten → 54 batang → 174 sambungan → layout 8 lembar →
  12 lembar fabrikasi → Excel → **0 tabrakan 3D** (`SKBENTROK`) → `SKCEK` lolos.
- Penginstal: pasang diam berhasil, file terpasang sama persis dengan hasil build, kunci uninstall Windows ada;
  menolak dengan pesan jelas bila AutoCAD masih terbuka.
- Paket R24 (2021–2024), R25 (2025–2026), R26 (2027) dikompilasi terhadap API versi minimum masing-masing;
  tidak ada DLL pihak ketiga yang hilang.
- Suara: Whisper offline mentranskripsi rekaman ±3 detik per kalimat; angka ucapan ("empat ratus",
  "seratus lima puluh") terbaca benar.

## Belum bisa diuji di mesin pengembang — mohon diuji pilot
| No | Uji | Versi | Cara | Lolos bila |
|---|---|---|---|---|
| 1 | Pemuatan otomatis | 2021, 2022, 2023, 2024, 2025, 2027 | Pasang `SteelKitSetup.exe`, buka AutoCAD | Tab ribbon **Steel Kit** muncul tanpa NETLOAD |
| 2 | Alur penuh | minimal satu versi R24 dan 2027 | `SKASISTEN` → ketik kalimat contoh → Pasang + sambung → `SKLAYOUT` → `SKFABRIKASI` → `SKEXCEL` → `SKBENTROK` | Tidak ada error; `SKBENTROK` 0 tabrakan |
| 3 | Suara asli | semua | Centang suara offline saat pasang, klik mic di asisten, ucapkan kalimat contoh dengan suara biasa | Kalimat terbaca, angka & profil benar |
| 4 | Mikrofon | laptop dengan >1 mic (mis. webcam virtual) | Pilih mic di jendela asisten | Mic fisik terpilih otomatis atau bisa dipilih |
| 5 | Model nyata | proyek sendiri | Gambar rangka dengan `SKBATANG`, lalu `SKSAMBUNGAUTO` dan `SKBENTROK` | Sambungan masuk akal; tabrakan yang dilaporkan memang tabrakan |
| 6 | Plot | semua | Plot lembar SK-01, detail, fabrikasi ke PDF | Teks terbaca, tidak terpotong, skala sesuai label |
| 7 | Copot | semua | Pengaturan Windows › Aplikasi › Steel Kit › Uninstall | Tab hilang setelah AutoCAD dibuka ulang |

Kalimat contoh untuk uji 2 & 3:
> portal bentang dua puluh meter tinggi enam meter lima bay jarak enam meter kolom WF empat ratus rafter WF tiga ratus lima puluh gording C seratus lima puluh bracing RD enam belas

## Pertanyaan untuk pilot (jawab singkat)
1. Bagian mana yang paling menghemat waktu? Perkiraan jam yang dihemat untuk satu gudang sederhana?
2. Gambar mana yang masih harus diedit manual sebelum dikirim ke bengkel / konsultan? Apa yang diedit?
3. Ada istilah atau tampilan yang membingungkan?
4. Fitur apa yang paling dibutuhkan berikutnya?

## Catatan hasil
| Penguji | Versi AutoCAD | Uji no. | Lolos? | Catatan / file |
|---|---|---|---|---|
| | | | | |

Batas tanggung jawab tetap berlaku: Steel Kit menyiapkan geometri, gambar, dan kuantitas; cek sambungan hanya aturan
jarak/ukuran minimum praktik umum. Kapasitas dihitung dan disahkan engineer struktur berlisensi.
