# Ders Dağıtım Sistemi Pro - Tam Kapsamlı Kullanım Kılavuzu

## 1. Giriş ve Aktivasyon
Uygulamayı başlattığınızda sizi karşılayan ana ekranda veritabanı seçimi yapılır.
* **Veritabanı Yönetimi:** Her okul veya dönem için ayrı bir `.sqlite` dosyası oluşturulabilir.
* **Aktivasyon:** İstek kodu ile lisansınızı tanımlayarak tam sürüm özelliklerini aktif hale getirin.

## 2. Dashboard (Genel Durum)
Okulunuzun planlama başarısını anlık olarak izlediğiniz paneldir.
* **İstatistikler:** Toplam öğretmen, sınıf ve ders sayıları.
* **Yerleşim Oranı:** Müfredatın yüzde kaçının yerleştiğinin canlı takibi.
* **Boşluk Analizi:** En çok boşluğu (cam) kalan öğretmenlerin tespiti.
* **Nöbet Çizelgesi:** Haftalık nöbet durumlarının canlı görünümü.

## 3. Okul Yapılandırması (Ayarlar)
### A. Zaman Tablosu
Ders saatleri, teneffüsler ve kapalı saatlerin (K) yönetildiği bölümdür. "K" olarak işaretlenen saatlere asla ders atanamaz.
### B. Tanımlamalar
* **Eğitsel Kulüpler:** Sosyal etkinlik gruplarının tanımlanması.
* **Nöbet Yerleri:** Bahçe, katlar, yemekhane gibi nöbet bölgeleri.
* **Binalar:** Çok binalı okullar için fiziksel mekan yönetimi.
### C. Güvenlik
* **Şifreleme:** Uygulama girişine şifre koyma özelliği.
* **Güncelleme:** Versiyon kontrolü ve otomatik güncelleme sistemi.

## 4. Ders Havuzu ve Müfredat
* **Ders Tanımlama:** Ders kodları, isimleri ve renkleri.
* **Blok Yapısı:** Haftalık dersin bölünme şekli (Örn: 2+2+1).
* **Kardeş Dersler:** Farklı sınıfların aynı saatte aynı öğretmende veya mekanda birleşmesi.

## 5. Öğretmen Yönetimi
* **Kişisel Zaman Tablosu:** Öğretmenin gelmeyeceği günlerin (K) veya başka okuldaki derslerinin işaretlenmesi.
* **Atamalar:** Öğretmene verilen toplam ders yükünün ve sınıfların analizi.
* **Nöbet ve Kulüp:** Öğretmenin nöbet günü ve yerinin tespiti.

## 6. Dağıtım Motoru (AI)
* **Otomatik Dağıtım:** Çakışma ve kısıtlamaları gözeterek AI tabanlı yerleşim.
* **Parametreler:** Boşluk cezası, sabah önceliği ve günlere yayma dengesi.
* **Kilitli Sistem:** Memnun kalınan dersleri kilitleyip geri kalanları yeniden dağıtma.

## 7. Elle Düzenleme
* **Sürükle-Bırak:** Derslerin saatler arasında taşınması.
* **Çakışma Kontrolü:** Manuel taşıma yaparken sistemin verdiği sesli ve görsel uyarılar.

## 8. Raporlama
* **Bireysel Programlar:** Öğretmen ve sınıf bazlı PDF çıktılar.
* **Çarşaf Liste:** Tüm okulun tek sayfada görünümü.
* **e-Okul Aktarımı:** XML formatında hızlı veri aktarımı.
