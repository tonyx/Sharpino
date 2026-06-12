# Dockerized SQL Cache Setup & Connection Guide

This guide describes how to manage and connect to the dockerized SQL Server instance used as the L2 Cache for Sharpino.

## 🚀 Setup & Teardown

We have provided two helper scripts to simplify the lifecycle of the SQL Server database cache.

### 1. Setup the Cache Database
To start the dockerized SQL Server container, wait for it to become healthy, create the `sharpinoCache` database, and apply the `SharpinoL2Cache` table schema, run:
```bash
./setup-cache.sh
```

### 2. Teardown the Cache Database
To stop and remove the SQL Server container, including its volumes (wiping the database state clean), run:
```bash
./teardown-cache.sh
```

---

## 🔌 Connecting with SQL Clients

You can connect to this SQL Server instance using any standard SQL client, such as **DBeaver**, **Azure Data Studio**, **SQL Server Management Studio (SSMS)**, **Rider**, or **VS Code (SQL Server Extension)**.

### Connection Parameters

| Parameter | Value |
| :--- | :--- |
| **Database Engine** | Microsoft SQL Server |
| **Host / Server** | `127.0.0.1` (or `localhost`) |
| **Port** | `1433` |
| **Authentication** | SQL Server Authentication |
| **Username** | `sa` |
| **Password** | `Sharpino@1234` |
| **Database** | `sharpinoCache` |
| **Trust Server Certificate** | `True` (Required for SQL Server 2022+ local connections) |

### 🛠️ Client-Specific Notes

#### 1. DBeaver
1. Select **New Connection** -> **MS SQL Server**.
2. Set **Host** to `127.0.0.1` and **Port** to `1433`.
3. Set **Database** to `sharpinoCache`.
4. Set **Username** to `sa` and **Password** to `Sharpino@1234`.
5. Go to the **Driver properties** tab and ensure `trustServerCertificate` is set to `true`.
6. Click **Test Connection** and then **Finish**.

#### 2. Azure Data Studio / VS Code SQL Server Extension
1. Create a new connection profile.
2. Connection Type: **Microsoft SQL Server**.
3. Server: `127.0.0.1`.
4. Authentication Type: **SQL Login**.
5. User name: `sa`.
6. Password: `Sharpino@1234`.
7. Database name: `sharpinoCache`.
8. Advanced -> Set **Trust Server Certificate** to `True` (or check the checkbox in the connection window).

---

## 🔍 Verifying the Schema

Once connected, you can run the following SQL query to verify the table exists and check its contents:

```sql
USE sharpinoCache;
SELECT * FROM dbo.SharpinoL2Cache;
```
