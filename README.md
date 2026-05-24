# Claude Code · Xbox Controller Companion

VS Code üzerinden Claude Code kullanırken bir Xbox controller'ı macro pad + push-to-talk mikrofonu olarak kullanmanı sağlayan Windows masaüstü uygulaması.

## Özellikler

- **A (varsayılan):** Push-to-talk. Önce VS Code'da bir yere tıkla → A'ya basılı tut → konuş → bırak. Ses yerel Whisper (small multilingual) ile transkript edilir ve son odaklı pencereye Unicode olarak yazılır (Türkçe + İngilizce teknik terim karışımını destekler).
- **B / X / Y ve diğer dijital tuşlar (LB, RB, LT, RT, D-Pad, Start, Back):** Tuş başına önceden tanımlı metin yazma.
- **Sol/Sağ analog stick:** Stick'i hareket ettirdiğinde ekranda küçük şeffaf bir radial menü açılır (4 yön). Yönü tutup stick'i bırakınca o yöne atanan metin son odaklı pencereye yazılır. 2 stick × 4 yön = 8 hızlı slot.
- Tüm eşlemeler uygulama arayüzünden düzenlenir; `%APPDATA%\CCXboxController\config.json` dosyasında saklanır.
- "Windows ile birlikte başlat" seçeneği (HKCU Run anahtarı, admin gerekmez).
- Mikrofon overlay'i fokus çalmaz (`WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`) — VS Code odakta kalır.

## Teknoloji

- .NET 10 + WPF (Windows-only)
- XInput (`xinput1_4.dll`) — controller okuma
- SendInput (Unicode) — klavye injection
- NAudio (WASAPI) — 16 kHz mono mikrofon kaydı
- Whisper.net + ggml-small.bin — yerel STT
- Registry HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run — autostart

## Build

Gereksinimler: .NET 10 SDK (veya .NET 8 SDK; `TargetFramework` değerini `net8.0-windows` olarak değiştirip yeniden derleyin).

```powershell
cd src
dotnet build CCXboxController.sln -c Release
```

Çalıştırmak için:

```powershell
dotnet run --project src/CCXboxController -c Release
```

Tek dosyalık `.exe` üretmek için:

```powershell
dotnet publish src/CCXboxController/CCXboxController.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Çıktı `src/CCXboxController/bin/Release/net10.0-windows/win-x64/publish/CCXboxController.exe`.

## İlk açılış

1. Uygulamayı başlat.
2. Alttaki "Whisper modeli" alanında **Modeli indir** butonuna bas. ~466MB `ggml-small.bin` Hugging Face'ten `%APPDATA%\CCXboxController\models\` klasörüne iner. Bir defaya mahsus.
3. Xbox controller'ı kablo ya da Bluetooth ile bağla. Üstteki nokta yeşil olmalı ("Kontrolcü bağlı").
4. Sol listeden bir tuşa tıkla, sağ panelde "Aksiyon tipi" ve metin alanını düzenle. Değişiklikler otomatik kaydedilir.

## Kullanım

| Aksiyon | Nasıl |
|---|---|
| Önceden tanımlı metin yaz | Tuşa bas — VS Code'da odaklı yere yazılır |
| Sesli yazım | VS Code Claude Code input'una tıkla → A'yı tut → konuş → bırak |
| Radial menü | Sol/sağ stick'i bir yöne ittir → menü çıkar → yön seç → bırak |

## Dosya konumları

- Config: `%APPDATA%\CCXboxController\config.json`
- Whisper modelleri: `%APPDATA%\CCXboxController\models\ggml-small.bin`

## Bilinen sınırlamalar (V1)

- Sistem tepsisi (tray) yok — pencere kapanınca uygulama biter.
- Tek profil — birden fazla uygulama (ör. Cursor + Claude Code) için ayrı profil yok.
- Whisper sadece CPU runtime ile çalışır; GPU isterseniz `Whisper.net.Runtime.CoreML` veya `Whisper.net.Runtime.Cuda` paketlerini ekleyebilirsiniz.

## Sorun giderme

- **Mikrofon kaydetmiyor:** Windows Settings → Privacy → Microphone → "Let desktop apps access your microphone" açık olmalı.
- **Kontrolcü görünmüyor:** USB ise kabloyu değiştirin; Bluetooth ise eşleştirmeyi sıfırlayın. XInput dışı pad'ler (DualShock vb.) desteklenmez — XInput emülatörü gerektirir.
- **Transkript boş geliyor:** A'ya çok kısa basıldı (<250 ms). Daha uzun tutun.
