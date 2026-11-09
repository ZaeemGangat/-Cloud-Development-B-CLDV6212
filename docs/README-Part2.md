# CoffeeNChill - Cloud Development B (CLDV6212)

A cloud-enabled microservices system for the CoffeeNChill campus canteen, built with Azure Functions, Azure Storage, and Docker.

## Overview

This project modernizes CoffeeNChill operations by replacing paper-based menus, handwritten order slips, and filing cabinet documents with a cloud-enabled microservices architecture.

## Part 1: Azure Functions, Tables, Files & Docker Hub

### Features
- **Menu Management**: Azure Table Storage for dynamic menu items with CRUD operations
- **Document Management**: Azure Blob Storage for staff operational documents
- **HTTP Trigger Functions**: RESTful API endpoints for menu and document operations
- **Docker Containerization**: Standalone Docker containers for Azurite and Functions

### Local Setup (Part 1)

#### Prerequisites
- .NET 10.0 SDK
- Docker Desktop
- Azure Functions Core Tools

#### Running Azurite (Standalone)
```bash
docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite
```

#### Running Functions (Standalone)
```bash
cd CoffeeNChillFunctions
dotnet restore
dotnet build
dotnet run
```

#### Building Docker Image
```bash
docker build -t glitchysheep78/coffeenchill-functions:v1.0 .
```

#### Running Functions Container
```bash
docker run -p 7071:80 -e AzureWebJobsStorage="UseDevelopmentStorage=true" glitchysheep78/coffeenchill-functions:v1.0
```

### API Endpoints (Part 1)

#### Menu Management
- `POST /api/menu` - Create menu item
- `GET /api/menu` - Get all menu items
- `GET /api/menu/category/{category}` - Get items by category
- `PUT /api/menu/{category}/{id}` - Update menu item
- `DELETE /api/menu/{category}/{id}` - Delete menu item

#### Document Management
- `POST /api/documents/upload` - Upload staff document
- `GET /api/documents` - List all documents
- `GET /api/documents/download/{fileName}` - Download document

---

## Part 2: Queue Triggers & Docker Compose Orchestration

### What's New in Part 2

#### Changelog
- Added Azure Storage Queue (`orderprocessingqueue`) for asynchronous order processing
- Implemented queue-triggered Azure Function for automated order processing
- Added Orders Azure Table for order tracking
- Implemented order status lifecycle: Received → Preparing → Ready → Collected
- Created docker-compose.yml for multi-container orchestration
- Updated to v2.0 Docker image with queue functionality

### New Features
- **Order Queue System**: Asynchronous order submission to `orderprocessingqueue`
- **Queue-Triggered Processing**: Automatic background processing of orders
- **Order Tracking**: Real-time status updates in Orders Azure Table
- **Docker Compose**: Single-command orchestration of Azurite and Functions

### Local Setup (Part 2)

#### Prerequisites
- .NET 10.0 SDK
- Docker Desktop
- Docker Hub account

#### Building and Pushing v2.0 Image
```bash
cd CoffeeNChillFunctions
docker build -t glitchysheep78/coffeenchill-functions:v2.0 .
docker push glitchysheep78/coffeenchill-functions:v2.0
```

#### Running with Docker Compose
**For Local Testing (uses local build):**
```bash
docker-compose -f docker-compose.local.yml up
```

**For Production (pulls from Docker Hub):**
```bash
docker-compose up
```

This will start:
- Azurite (ports 10000, 10001, 10002)
- Functions host (port 7071)

### New API Endpoints (Part 2)

#### Order Management
- `POST /api/orders/queue` - Submit order to queue
- `GET /api/orders` - Get all orders
- `GET /api/orders/{orderDate}/{orderId}` - Get specific order

### Order Status Lifecycle
When an order is submitted to the queue, it automatically transitions through:
1. **Received** - Order placed in queue
2. **Preparing** - Barista preparing the order (2 seconds simulated)
3. **Ready** - Order ready for collection (2 seconds simulated)
4. **Collected** - Order collected by customer (2 seconds simulated)

### Example Order Payload
```json
{
  "orderId": "ORD-2026-8801",
  "customerName": "Jane Smith",
  "selectedItemSKUs": ["COF-001", "PAS-104"],
  "totalPrice": 65.00,
  "orderTimestamp": "2026-10-12T10:15:00Z"
}
```

---

## Team Contributions

### Part 1
- **Zaeem (ST10316317)**: Menu management functions and Table Storage integration
- **Jadeel (ST10339718)**: Document management functions and Blob Storage integration
- **Yash (ST10266783)**: Docker containerization and Docker Hub publishing
- **Zaeem (ST10316317)**: Postman collection and testing

### Part 2
- **Zaeem (ST10316317)**: Queue Storage service and order entity models
- **Jadeel (ST10339718)**: Queue-triggered function and order status transitions
- **Yash (ST10266783)**: Docker Compose orchestration and multi-container setup
- **Zaeem (ST10316317)**: Updated Postman collection with order queue tests

---

## Video Demonstrations

### Part 1 Video
[YouTube Link - https://youtu.be/KHqFFDEhFDk 

### Part 2 Video
[YouTube Link - https://youtu.be/KHqFFDEhFDk 

---

## Testing

### Postman Collections
- Part 1: `CoffeeNChill-Part1-Postman.json`
- Part 2: `CoffeeNChill-Part2-Postman.json`

Import these collections into Postman to test all endpoints. Set the `baseUrl` variable to:
- Local: `http://localhost:7071`
- Docker Compose: `http://localhost:7071`

---

## Azure Storage Emulation

This project uses Azurite for local Azure Storage emulation:
- **Table Storage**: Port 10000
- **Blob Storage**: Port 10001
- **Queue Storage**: Port 10002

Connection string is configured in `docker-compose.yml` for container-to-container communication.

---

## Project Structure

```
CoffeeNChill/
├── CoffeeNChillFunctions/
│   ├── Functions/
│   │   ├── MenuFunctions.cs
│   │   ├── DocumentFunctions.cs
│   │   └── OrderFunctions.cs
│   ├── Models/
│   │   ├── MenuItemEntity.cs
│   │   ├── MenuItemsDtos.cs
│   │   ├── OrderEntity.cs
│   │   └── OrderDtos.cs
│   ├── Services/
│   │   ├── TableStorageService.cs
│   │   ├── BlobStorageService.cs
│   │   ├── QueueStorageService.cs
│   │   └── OrderTableStorageService.cs
│   ├── Program.cs
│   ├── Dockerfile
│   ├── local.settings.json
│   └── host.json
├── docker-compose.yml
├── CoffeeNChill-Part1-Postman.json
└── CoffeeNChill-Part2-Postman.json
```

---

## Docker Hub Images

- **Part 1**: `glitchysheep78/coffeenchill-functions:v1.0`
- **Part 2**: `glitchysheep78/coffeenchill-functions:v2.0`

---

