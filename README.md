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
