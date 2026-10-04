# Dördüncü — Unity 6

Dördüncü okey oyununun Unity 6 (6000.0 LTS) istemcisi. Sunucu: [dorduncu-api](https://github.com/baytemizel/dorduncu-api) (Laravel).

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
