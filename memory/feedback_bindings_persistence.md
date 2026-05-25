---
name: feedback-bindings-persistence
description: Overlay/stick bindings ve kullanıcı seçimleri kalıcı olmalı — bilgisayar yeniden başlatıldığında korunmalı
metadata:
  type: feedback
---

Kullanıcının yaptığı tüm binding seçimleri (özellikle left/right stick yönlerine atanan metinler ve diğer buton atamaları) kalıcı olmalı. Uygulama veya bilgisayar yeniden başlatıldığında bu seçimler kaybolmamalı.

**Why:** Kullanıcı explicit olarak belirtti: "ben left stick'te bir yazı yazdığım zaman ve bilgisayarı yeniden başlattığım zaman bunun orada zaten kalıyor olması gerekiyor". Bu davranış zaten `%APPDATA%\CCXboxController\config.json` üzerinden `ConfigStore` ile sağlanıyor (AppConfig.Buttons + AppConfig.Sticks).

**How to apply:** Binding/ayar değişikliği yapan herhangi bir kod yazarken (yeni UI alanı, yeni buton, yeni stick yönü) mutlaka `ConfigStore.Save()` çağrısının yapıldığından emin ol. Geçici/in-memory state ile yetinme — kullanıcının seçimleri her zaman diske yazılmalı ve uygulama açılışında yüklenmeli. Yeni eklenen ayarlar için de aynı kural geçerli.
