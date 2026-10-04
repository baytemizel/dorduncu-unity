# Dördüncü — Unity 6

Dördüncü okey oyununun Unity 6 (6000.0 LTS) istemcisi: botlara karşı çevrimdışı, gerçek rakiplere karşı çevrimiçi oyun,
sıralama, turnuvalar, profil, mağaza ve sohbet. Sunucu: [dorduncu-api](https://github.com/baytemizel/dorduncu-api) (Laravel).

## Açma ve oynama

1. Unity Hub → *Add project from disk* → bu klasör (Unity 6000.0.x; farklı bir 6000.0 sürümü sorulursa kabul edin).
2. İlk açılışta `Assets/Scenes/Main.unity` otomatik oluşturulur ve derlemeye eklenir. Sahneyi açıp **Play**.
3. Çevrimiçi oyun için API adresi: `Assets/Resources/Dorduncu/config.json` → `apiUrl` (ör. `https://api.dorduncu.app`).
   Geliştirirken *Dördüncü → API: yerel* menüsü `http://127.0.0.1:8000` kullanır. Adres boşsa oyun yalnızca botlara karşı oynar.

Geliştirme kolaylığı için *Player → Allow downloads over HTTP* "Always allowed" ayarlıdır (yerel `http://` API). Yayın
derlemesinden önce HTTPS API adresi kullanıp bu ayarı "Not allowed" yapın.

Bütün arayüz koddan kurulur (`GameApp` herhangi bir sahneye kendini ekler); prefab veya sahne bağlantısı yoktur. Unity ilk
açılışta `.meta` dosyalarını üretir — bunları depoya ekleyin.

## Yapı

```
Assets/Dorduncu/Engine     Oyun motoru (saf C#, golden testli)
Assets/Dorduncu/Client     İstemci çekirdeği (saf C#): API istemcisi, masa modeli, çevrimdışı / çevrimiçi masa
Assets/Dorduncu/UI         Unity katmanı: GameApp (ekranlar ve akış), TableScreen (masa), TileView (taş), Ui (UGUI araçları),
                           UnityPlatform (UnityWebRequest / zamanlayıcı / PlayerPrefs), PlatformBridge (mağaza, reklam, giriş)
Assets/Dorduncu/Editor     Sahne kurulumu, görsel içe aktarma ayarları, menü
Assets/Dorduncu/Tests      EditMode testleri (motor golden + istemci)
Assets/Resources/Dorduncu  Görseller (masa temaları, ıstaka, çay, avatarlar), Oswald / Nunito fontları (OFL), config.json
Tools/EngineTests          Motor ve istemci testleri Unity olmadan (.NET 8)
Tools/OnlineE2E            C# istemciyle çalışan API'ye karşı uçtan uca tam maç
```

## Oyun

- **Masa:** ıstakada 2×15 yuva; taşı sürükleyerek diz, sağdaki yere (kendi atık yerin) sürükleyerek veya çift tıklayarak at,
  göstergenin üstüne bırakarak o taşla bitir. Desteye / soldaki taşa tıklayarak çek. Düğmeler: Desteden Çek, Yerden Al, Bitir
  (bitiren taş parlar), Gösterge, İpucu (en güçlü botun hamlesi; çevrimiçi ücretsiz üyelikte maç başına 3), Seri Diz, Çift Diz, Menü.
- **Çevrimdışı:** 4 zorluk (Kolay, Orta, Zor, Usta), motor ve botlar tamamen cihazda.
- **Çevrimiçi:** misafir hesap otomatik açılır; `Hemen Oyna` günlük hakkı düşer ve masaya oturur (8 sn içinde dolmazsa botlar oturur).
  Masadan kalkarsan yerine bot oynar ve lig puanı kaybedersin. Sohbet, hazır mesajlar, ifadeler ve şikâyet.
- **Temalar:** Kahvehane Kilimi (ücretsiz), Ev Akşamı, Çınar Altı, Boğaz Terası, Kış Köşkü, Ege Kıyısı (premium / satın alma).

## Mağaza, reklam, sosyal giriş

Oyun yalnızca `IPlatformBridge` arayüzünü kullanır (`Assets/Dorduncu/UI/PlatformBridge.cs`). Unity IAP, ödüllü reklam SDK'sı
(AdMob SSV: `user_id` = oyuncu kimliği) ve Google / Apple girişini bu arayüzle bağlayıp `GameApp.Bridge`'e atayın; satın
almaların doğrulanması ve hakların tanımlanması sunucudadır (`POST /v1/purchases/verify`).

## Uygulama simgesi

`Assets/Dorduncu/Branding/AppIcon.png` (1024×1024) — *Project Settings → Player → Icon* bölümüne atayın.

## Oyun motoru — `Assets/Dorduncu/Engine`

Orijinal TypeScript motorunun saf C# portu (`Dorduncu.Engine`, UnityEngine bağımlılığı yok): deste, kanıtlanabilir adil
karıştırma (HMAC-SHA256), dağıtım, seri / çift bitiş, bitiren taşı bulma, Seri Diz / Çift Diz analizi, tur durum makinesi,
5 seviyeli botlar, turnuva kurası. Çevrimdışı (botlara karşı) oyun tamamen cihazda bu motorla oynanır.

`Assets/Dorduncu/Tests/EditMode/GoldenTests.cs` motorun orijinalle **bit bit aynı** olduğunu doğrular (golden vektörler:
kripto, RNG, 200 dağıtım, 1500 el, lig puanı, turnuvalar, bot masaları ve bot-bot maçları hamle hamle). Aynı dosyalar PHP
API'sinde de geçer. Çalıştırma:

- Unity: *Window → General → Test Runner → EditMode → Run All*
- Unity olmadan: `cd Tools/EngineTests && dotnet run -c Release` (.NET 8)

## İstemci çekirdeği — `Assets/Dorduncu/Client`

UnityEngine'e bağımlı olmayan (`noEngineReferences`) ve bu yüzden düz .NET ile de test edilen katman:

| Dosya | Ne yapar |
|---|---|
| `ApiClient.cs` | Laravel API istemcisi: oturum, 401'de tek seferlik şeffaf yenileme (anahtar döndürme), tüm uçlar |
| `Tables.cs` | `ITableAdapter`: `OfflineTable` (motor + botlar cihazda) ve `OnlineTable` (REST masası, ≈1 sn yoklama) aynı mesajları üretir |
| `TableModel.cs` | Masa ekranının modeli: ıstaka sırası (2×15), Seri/Çift Diz, bitiren taş, olay kuyruğu, hamleler |
| `Protocol.cs`, `Json.cs` | Sunucu mesajları ve bağımlılıksız JSON |
| `Platform.cs` | HTTP / zamanlayıcı / kalıcı depolama arayüzleri (Unity'de UnityWebRequest, Update ve PlayerPrefs) |

Uçtan uca doğrulama (Unity olmadan, çalışan bir API'ye karşı tam maç): `cd Tools/OnlineE2E && dotnet run -c Release -- http://127.0.0.1:8000`
