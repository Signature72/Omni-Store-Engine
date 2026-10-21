# OmniStore Engine 🚀
> E-Commerce Microservices System ออกแบบสำหรับโชว์ Architecture ในการสัมภาษณ์งานสาย .NET & Angular

โปรเจกต์นี้จำลองระบบ E-Commerce สเกลใหญ่ที่ต้องรับมือกับ **High Concurrency** และปัญหาคลาสสิกเวลาคนแย่งกันกดซื้อของ (เช่น แย่งของชิ้นสุดท้าย, ตัดเงินผ่านแต่สต็อกไม่ตัด, หรือ Message Broker ล่มแล้วข้อมูลไม่ตรงกัน)

---

## 💡 ไฮไลท์สถาปัตยกรรม (ทำไมถึงออกแบบท่านี้?)

### 1. Sync Reserve + Async Commit Pattern
แทนที่จะใช้ Distributed Transaction (2PC) ที่ทำให้ระบบช้าและเกิด Deadlock ง่าย เราแบ่งการ Checkout เป็น 2 จังหวะ:
- **จังหวะที่ 1 (Sync Reserve ผ่าน gRPC)**: เมื่อลูกค้ากด Checkout ทาง Order Service จะยิง gRPC ตรงไปหา Inventory Service ทันทีเพื่อ "กั๊กสต็อกชั่วคราว" (มี TTL 15 นาที) ถ้าของหมดจะ Fail ทันที ณ เสี้ยววินาทีนั้น ลูกค้าไม่ต้องรอ
- **จังหวะที่ 2 (Async Commit ผ่าน RabbitMQ)**: เมื่อจำลองการจ่ายเงินผ่านแล้ว Order Service จะบันทึก Order ลง DB และผลัก Event `OrderPaidEvent` ขึ้น RabbitMQ ให้ Inventory Service ไปตัดสต็อกแบบถาวร และ Notification Service ไปส่งสลิปผ่าน LINE

### 2. หมดปัญหาเรื่อง Dual-Write ด้วย Transactional Outbox Pattern
เวลาเขียนโค้ดบันทึก Order ลง Database แล้วต้องสั่งส่ง Message เข้า RabbitMQ ต่อ ถ้า Message Broker ดับช่วงเสี้ยววินาทีนั้น ข้อมูลใน DB จะไม่ตรงกับ Message ทันที  
เราเลยใช้ **MassTransit EF Core Outbox** บันทึก Message ลงตาราง `OutboxMessage` ใน Database Transaction เดียวกันกับคำสั่งซื้อ ทำให้การันตี 100% ว่าไม่มีข้อความตกหล่น (**At-Least-Once Delivery**)

### 3. Database-per-Service Isolation
- `auth_db`: เก็บข้อมูล User, รหัสผ่านที่แฮชด้วย BCrypt (Work Factor 12) และตาราง Refresh Token
- `order_db`: เก็บคำสั่งซื้อ รายการสินค้า และตาราง Outbox
- `inventory_db`: จัดการสต็อกสินค้า การจองสต็อก และประวัติการ Commit
*ทั้ง 3 Service แยก DbContext และ Connection String ออกจากกันเด็ดขาด*

### 4. Fast Ephemeral State ด้วย Redis
ตะกร้าสินค้า (Cart) ถูกเก็บไว้ใน Redis พร้อมตั้งค่า TTL หมดอายุอัตโนมัติ เพื่อให้ผู้ใช้กดเพิ่ม/ลดจำนวนชิ้นได้เร็วระดับมิลลิวินาที โดยไม่ต้องไปเปลือง I/O กับ Database หลัก

### 5. YARP API Gateway
Traffic จาก Frontend จะวิ่งเข้า Port `5000` ที่เดียว ตัว YARP จะทำหน้าที่:
- กระจาย Route ไปยังแต่ละ Microservice
- ป้องกัน Brute-Force ด้วย Rate Limiting (จำกัด 15 req/min สำหรับหน้า Login และ 100 req/min สำหรับ API ทั่วไป)
- แปะ `X-Correlation-ID` ทุก Request เพื่อใช้ตามรอย Distributed Tracing
- Forward JWT Token ไปยัง Microservice หลังบ้าน

---

## 🏗️ แผนผังพอร์ตและ Services

| Service | หน้าที่ | พอร์ต | เทคโนโลยี |
| :--- | :--- | :--- | :--- |
| **ApiGateway** | ประตูทางเข้าหลัก (Ingress & Rate Limiter) | `5000` | YARP (.NET 8) |
| **AuthService** | จัดการผู้ใช้, JWT, Refresh Token Rotation | `5101` | .NET 8, PostgreSQL, BCrypt |
| **InventoryService** | ดูแลแคตตาล็อก, สต็อกสินค้า, Event Consumer | `5102` (REST)<br/>`5202` (gRPC) | .NET 8, gRPC, MassTransit |
| **OrderService** | ตะกร้าสินค้า, Checkout Orchestrator, Outbox | `5103` | .NET 8, Redis, MassTransit Outbox |
| **NotificationService** | Event Consumer จำลองการส่ง LINE Push Notification | `5104` | .NET 8, MassTransit RabbitMQ |
| **omni-store-ui** | หน้าบ้าน Angular สำหรับเลือกสินค้าและทดสอบ Checkout | `4200` | Angular 18+, Tailwind CSS, RxJS |
| **PostgreSQL** | ฐานข้อมูลหลัก (auth_db, order_db, inventory_db) | `5432` | Postgres 16 Alpine |
| **Redis** | แคชตะกร้าสินค้าชั่วคราว | `6379` | Redis 7 Alpine |
| **RabbitMQ** | Message Broker สำหรับ Event-driven communication | `5672`<br/>`15672` (Web UI) | RabbitMQ 3.13 Management |

---

## ⚡ วิธีรันระบบบนเครื่อง (Getting Started)

### 1. สตาร์ท Infrastructure (Docker)
เปิด Docker Desktop แล้วรันคำสั่งที่ root โฟลเดอร์:
```bash
docker compose up -d
```
*(ระบบจะสร้างฐานข้อมูล `auth_db`, `order_db`, `inventory_db` ให้แบบอัตโนมัติ)*

### 2. รัน Microservices ฝั่ง Backend (.NET 8)
คุณสามารถรันแต่ละ Service ผ่าน terminal แยกกัน:
```bash
# Terminal 1: API Gateway
dotnet run --project src/ApiGateway/OmniStore.ApiGateway.csproj

# Terminal 2: Auth Service
dotnet run --project src/Services/AuthService/OmniStore.AuthService.csproj

# Terminal 3: Inventory Service (gRPC Port 5202)
dotnet run --project src/Services/InventoryService/OmniStore.InventoryService.csproj

# Terminal 4: Order Service
dotnet run --project src/Services/OrderService/OmniStore.OrderService.csproj

# Terminal 5: Notification Service
dotnet run --project src/Services/NotificationService/OmniStore.NotificationService.csproj
```

### 3. รัน Frontend (Angular 18+)
```bash
cd src/Frontend/omni-store-ui
npm install
npm start
```
เปิดบราวเซอร์ไปที่ `http://localhost:4200`

---

## 🧪 การรัน Unit Tests (29 เคส ผ่าน 100%)

ทุกฟังก์ชันหลักมี Unit Test ครอบคลุมทั้งฝั่ง Backend และ Frontend:

```bash
# รัน Unit Tests ของ .NET Microservices ทั้งหมด (21 เคส)
dotnet test OmniStoreEngine.sln

# รัน Unit Tests ของ Angular (8 เคส)
cd src/Frontend/omni-store-ui
npm test -- --watch=false
```

### รายการ Test Cases สำคัญที่ครอบคลุม:
- **AuthService**: การสมัครสมาชิก, รหัสผ่านไม่ตรง, การ Rotate Refresh Token ป้องกัน Replay Attack, ตรวจสอบ Claims ใน JWT
- **InventoryService**: การจองสต็อกสำเร็จเมื่อของพอ, การ Reject เมื่อของหมด, ความเป็น Idempotent ของ Consumer (Message ยิงซ้ำสต็อกไม่ถูกตัดเบิ้ล), การ Release คืนสต็อก
- **OrderService**: Flow การ Checkout เต็มรูปแบบ, การ Rollback คืนสต็อกเมื่อจ่ายเงินไม่ผ่าน, การบันทึก Outbox Message, การคำนวณยอดตะกร้า
- **Angular Frontend**: การเก็บ Token ใน LocalStorage, การคำนวณราคาสินค้าในตะกร้า, การสร้าง Component

---

## 👤 บัญชีและข้อมูลสำหรับทดสอบระบบ
- **RabbitMQ Dashboard**: `http://localhost:15672` (User: `omni_guest` / Password: `omni_guest123`)
- **Default Seed User**: สมัครใหม่ผ่านหน้าเว็บ หรือ Login ด้วย `architect@omnistore.io` / `P@sswordEnterprise2026`
- **ปุ่ม Seed Catalog**: ที่หน้าเว็บมีปุ่มคลิก "Seed Sample Catalog" เพื่อดึงข้อมูลสินค้าตัวอย่าง (MacBook Pro, Sony Headphones, คีย์บอร์ด Keychron ฯลฯ) เข้าสู่ InventoryDB ทันที
