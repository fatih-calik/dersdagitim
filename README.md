# Ders Dağıtım - Windows Sürümü

## Hakkında

Bu uygulama, okullarda ders programı oluşturma ve öğretmen-sınıf atamalarını otomatik olarak dağıtan bir çizelgeleme yazılımıdır. Google OR-Tools kısıt programlama (constraint programming) motoru kullanılarak optimize edilmiş ders dağıtımı yapılmaktadır.

## Özellikler

- 📚 **Ders Havuzu Yönetimi**: Müfredat derslerini tanımlama, blok ayarları
- 👥 **Öğretmen Yönetimi**: Öğretmen kısıtları, görevler, nöbet atamaları
- 🎓 **Sınıf Yönetimi**: Sınıf tanımları ve kısıtları
- 📅 **Otomatik Ders Dağıtımı**: OR-Tools ile optimize edilmiş çizelgeleme
- ✨ **İyileştirme Modu**: Mevcut programı boşluk azaltarak iyileştirme
- 📊 **İstatistikler**: Yerleşim oranı, boşluk sayısı, toplam ders saati
- 💾 **SQLite Veritabanı**: Tüm veriler yerel veritabanında saklanır
- 🔐 **Lisans Yönetimi**: MAC adresine bağlı lisans sistemi
- ₺ **Ek Ders Hesaplama**: MEB ek ders kodlarına uygun hesaplama

## Gereksinimler

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime (self-contained sürümde dahil)

## Kurulum

### Tek Dosya Sürümü (Önerilen)

1. `publish` klasöründeki `DersDagitim.exe` dosyasını istediğiniz bir konuma kopyalayın
2. Uygulamayı çalıştırın

### Geliştirici Sürümü

```powershell
cd windows
dotnet restore
dotnet build
dotnet run
```

## Veritabanı

Uygulama varsayılan olarak şu konumda veritabanı oluşturur:
```
%APPDATA%\DersDagitim\ders_dagitim.db
```

Mevcut bir veritabanını kullanmak için:
1. `.db` dosyasını yukarıdaki konuma kopyalayın
2. Veya uygulamayı argüman olarak veritabanı yoluyla başlatın:
   ```
   DersDagitim.exe "C:\path\to\database.db"
   ```

## Proje Yapısı

```
windows/
├── Models/                    # Veri modelleri
│   ├── Enums.cs              # Enum tanımları
│   ├── TimeSlot.cs           # Zaman dilimi
│   ├── Teacher.cs            # Öğretmen modeli
│   ├── SchoolClass.cs        # Sınıf modeli
│   ├── Lesson.cs             # Ders modeli
│   ├── SchoolInfo.cs         # Okul bilgileri
│   └── SchedulingModels.cs   # Çizelgeleme modelleri
│
├── Persistence/               # Veritabanı katmanı
│   ├── DatabaseManager.cs    # SQLite bağlantı yönetimi
│   ├── DatabaseSchema.cs     # Tablo şemaları ve migrasyonlar
│   ├── TeacherRepository.cs  # Öğretmen CRUD
│   ├── ClassRepository.cs    # Sınıf CRUD
│   ├── LessonRepository.cs   # Ders CRUD
│   ├── SchoolRepository.cs   # Okul ayarları
│   └── SchedulingRepositories.cs  # Atama ve dağıtım
│
├── Services/                  # İş mantığı
│   ├── SchedulingEngine.cs   # OR-Tools çizelgeleme motoru
│   └── LicenseManager.cs     # Lisans yönetimi
│
├── ViewModels/                # MVVM view modelleri
│   └── MainViewModel.cs      # Ana view model
│
├── Views/                     # WPF görünümleri (gelecekte)
│
├── Core/                      # Yardımcı sınıflar
│   └── DesignSystem.cs       # Renk ve stil sabitleri
│
├── App.xaml                   # Uygulama kaynakları
├── MainWindow.xaml            # Ana pencere
└── DersDagitim.csproj        # Proje dosyası
```

## Teknolojiler

- **.NET 8.0** - Framework
- **WPF** - Kullanıcı arayüzü
- **Microsoft.Data.Sqlite** - SQLite veritabanı
- **Google.OrTools** - Kısıt programlama motoru
- **CommunityToolkit.Mvvm** - MVVM destek kütüphanesi

## Veritabanı Şeması

Ana tablolar:
- `okul` - Okul ayarları ve lisans bilgisi
- `ders` - Ders tanımları
- `ogretmen` - Öğretmen bilgileri ve kısıtları
- `sinif` - Sınıf bilgileri ve kısıtları
- `sinif_ders` - Sınıf-ders atamaları
- `atama` - Öğretmen-ders atamaları
- `dagitim_bloklari` - Dağıtım blokları ve yerleşimler
- `zaman_tablosu` - Zaman kısıtları

## Lisans

Bu yazılım ticari lisans altındadır. Kullanım için geçerli bir lisans kodu gereklidir.

## Destek

Sorunlar ve öneriler için iletişime geçin.
