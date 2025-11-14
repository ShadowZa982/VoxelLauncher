# VoxelLauncher

<img width="258" height="258" alt="serve_logor" src="https://github.com/user-attachments/assets/61ecf942-526e-4589-85ea-b9c484e2e433" />


**VoxelLauncher** là **launcher Minecraft: Java Edition** được phát triển bởi người Việt và mã nguồn mở, hỗ trợ đầy đủ các tính năng giống Minecraft chính thức.

> Phát triển bởi: **FoxStudio**  
> GitHub: [ShadowZa982/VoxelLauncher](https://github.com/ShadowZa982/VoxelLauncher)

---

## ⚙️ Hệ Thống Yêu Cầu ( đây chỉ là tham khảo, trên thực tế sẽ khác )

| Thành phần | Chi tiết |
|-------------|-----------|
| **Hệ điều hành** | Windows 10 (version 1803+) / Windows 11 (21H2+) |
| **CPU** | Intel Core i3+ *(khuyến nghị i5/i7)* |
| **RAM** | Tối thiểu 4GB *(khuyến nghị 8GB+)* |
| **Lưu trữ** | 2GB (launcher) + 4GB+ (Minecraft) |
| **Đồ họa** | Intel HD 4000+ hoặc GPU rời |
| **Java** | Tự động tải OpenJDK 17+ |
| **Hỗ trợ** | Chỉ Windows (không hỗ trợ macOS/Linux) |

---

## 🪄 Mô Tả Tổng Quan

**VoxelLauncher** là launcher **miễn phí**, giúp bạn chơi **Minecraft Java Edition** một cách dễ dàng.

<img width="1175" height="702" alt="{10BB3E39-92AE-4320-8DC7-6B665EF56A49}" src="https://github.com/user-attachments/assets/b222ba64-4e00-4202-a809-ca4db2dfb722" />



### 🌟 Đặc điểm nổi bật

- 🎨 **Giao diện hiện đại**
- 👥 **Hỗ trợ đa tài khoản:** Microsoft (Xbox Live) + Offline
- ⚡ **Tải phiên bản tự động:** Hỗ trợ tất cả từ `0.1.0 → mới nhất` *(snapshot, release, pre-release...)*
- 🧩 **Tích hợp loader:** Fabric, Forge, Quilt, NeoForge, Vanilla
- ☕ **Tích hợp Java:** Phát hiện & tải Java 17+ nếu thiếu
- 🔁 **Cập nhật tự động:** Kiểm tra GitHub Releases + progress bar
- 🔔 **Thông báo thông minh:** Toast + badge khi bỏ qua bản cập nhật
- ⚙️ **Tùy chỉnh mạnh mẽ:** Sidebar cho Mods, Servers, Changelog
- 🔒 **Bảo mật cao:** Lưu session an toàn, hỗ trợ XboxAuth
- 🧑‍💻 **Offline Mode:** Không cần tài khoản Premium để chơi
- 🌐 **Online Mode:** Cần Premium để chơi trên server chính thức

---

## 🔐 Chức Năng Chính

### 1. Đăng Nhập & Quản Lý Tài Khoản
- **Microsoft Account:** Xác thực Xbox Live qua MSAL, tự động đăng nhập  
- **Offline Mode:** Chơi không cần internet, tùy chỉnh tên người chơi  
- **Quản lý nhiều tài khoản:** Hiển thị avatar, thời gian đăng nhập, loại tài khoản  
- **Xóa tài khoản:** Nhanh gọn, xóa vĩnh viễn  
- **Thông báo:**

  <img width="1174" height="700" alt="{EDE1625D-2F86-47ED-8FE9-9E6123E5B27B}" src="https://github.com/user-attachments/assets/517c8cdd-ba58-4f29-9150-539f76239446" />



---

### 2. Tải & Chạy Minecraft
- **Danh sách phiên bản:** Lấy dữ liệu từ Mojang (release + snapshot)  
- **Tải song song:** Dùng `GameInstaller` để tăng tốc tải assets, libraries, client  
- **Java tự động:** Phát hiện Java 8+, yêu cầu tối thiểu Java 17  
- **Tùy chỉnh RAM:** Sửa `-Xmx`, `JVM Args` trong Settings  

---

### 3. Cập Nhật Launcher
- **Kiểm tra tự động** khi khởi động  
- **Dialog cập nhật:** Hiển thị chi tiết bản mới  
- **Tải cập nhật:** Có progress bar, kiểm tra tính toàn vẹn  
- **Cài đặt tự động:** Qua `VoxelUpdater.exe`, khởi động lại sau cập nhật  
- **Pending Updates:** Bỏ qua cập nhật → hiển thị badge cảnh báo  

---

### 4. Giao Diện & Tùy Chỉnh
- **Sidebar:** Menu trượt (Info, Mods, Servers, Partners)  
- **Bottom Bar:** Nút Play lớn + Settings + Changelog  
- **Loading Screen:** Video animation + progress bar  
- **Notification:** Toast + badge cập nhật  

---

### 5. Hỗ Trợ Mods & Servers
- **Mods:** Tải mod qua Modrinth & CurseForge *(sắp có)*  
- **Servers:** Danh sách server, ping tự động  
- **Changelog:** Hiển thị ghi chú cập nhật Minecraft  

<img width="1170" height="702" alt="{E3D50D69-6FD7-4610-B3AD-7ECDF4B8182C}" src="https://github.com/user-attachments/assets/6f463fdf-4744-460c-8356-2b76b7ab64b8" />


---

## 🧰 Hướng Dẫn Cài Đặt

### Bước 1 – Tải Launcher
- Truy cập: [GitHub Releases](https://github.com/ShadowZa982/VoxelLauncher/releases)  
- Tải file: **VoxelLauncher.exe (self-contained)**  

### Bước 2 – Chạy Launcher
- Double-click `VoxelLauncher.exe`  
- Nếu thiếu **WebView2** hoặc **.NET**, Windows sẽ tự động cài  

### Bước 3 – Đăng Nhập
- **Microsoft:** Dành cho tài khoản Premium (Xbox Live)  
- **Offline:** Chơi không cần tài khoản  

### Bước 4 – Chọn Phiên Bản & Chơi
- Ví dụ: Chọn **1.20.1** → Bấm **Play**  
- Launcher sẽ tự động tải file cần thiết  

### Bước 5 – Cập Nhật
- Kiểm tra tự động mỗi khi khởi động  
- Nếu có bản mới, dialog hiện ngay với notes chi tiết  

---

## 🧩 Cài Đặt Thủ Công (nếu cần)

| Mục | Hướng dẫn |
|------|------------|
| **Java** | Sẽ được tự đọng cài đặt java chính thức từ mojang |
| **Mods** | Đặt mod vào `.minecraft/mods` |
| **Thư mục Minecraft** | `%APPDATA%\.minecraft` |

---

## 🚑 Khắc Phục Lỗi Thường Gặp

| Lỗi | Cách khắc phục |
|------|----------------|
| Java lỗi | Mở CMD → `java -version` |
| Cập nhật lỗi | Kiểm tra firewall/antivirus chặn GitHub |
| Launcher treo | Kill `VoxelLauncher.exe` trong Task Manager |
| Không tải game | Kiểm tra internet / thử VPN |
| Lỗi đăng nhập | Xóa `ms_accounts.json` trong `.minecraft` |

---

## 🧠 Hướng Dẫn Nâng Cao

### Quản Lý Phiên Bản
- Tải **Snapshot**  
- Tùy chỉnh RAM (`-Xmx2G`)  

### Mods & Resource Packs
- Dùng **Fabric/Forge/Quilt** loader  
- Đặt mods vào `.minecraft/mods`  

### Cập Nhật & Backup
- **Pending Updates:** hiển thị badge ở Notification  
- **Backup:** Sao lưu `.minecraft` trước khi mod

---

## 🔗 Liên Kết Hữu Ích

- 🌍 GitHub Repo: [ShadowZa982/VoxelLauncher](https://github.com/ShadowZa982/VoxelLauncher)  
- 📘 Minecraft Wiki: [minecraft.wiki](https://minecraft.wiki)  
- 🧱 CurseForge Mods: [curseforge.com/minecraft/mc-mods](https://curseforge.com/minecraft/mc-mods)  
- 🔮 Modrinth Mods: [modrinth.com/mods](https://modrinth.com/mods)  
- 💬 Hỗ trợ: Mở **Issue** trên GitHub hoặc tham gia **Discord FoxStudio**

---

## ⚖️ Giấy Phép & Góp Ý

- **License:** Apache 2.0 
- **Góp ý:** Fork repo → Submit Pull Request hoặc mở Issue  
- **Đóng góp:** chia sẻ cùng bạn bè!
---

## Lưu Ý
-  Nếu bạn tải phiên bản từ bất kỳ nguồn nào mà không phải từ trang chính [VoxelLauncher Github](https://github.com/ShadowZa982/VoxelLauncher) này hoặc website tạm thời [VoxelLauncher Website](https://voxellauncher.xo.je) và [Discord FoxStudio](https://discord.gg/AbQHGuPKen) thì chúng tôi sẽ không chịu trách nghiệm xử lý nếu những phiên bản đó có chứa mã độc và tải ở trang web không chính thức.
-  Chỉ tải duy nhất trên [VoxelLauncher Github](https://github.com/ShadowZa982/VoxelLauncher) này hoặc website tạm thời [VoxelLauncher Website](https://voxellauncher.xo.je) và [Discord FoxStudio](https://discord.gg/AbQHGuPKen)
---


### ❤️ Cảm ơn bạn đã sử dụng **VoxelLauncher!**
Phát triển bởi **ShadowZa982 – FoxStudio**  
Hẹn gặp lại ở phiên bản tiếp theo!
