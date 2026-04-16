# Name Profile API - Backend Stage 1

A robust ASP.NET Core Web API that integrates with three external APIs (Genderize, Agify, and Nationalize), applies classification logic, persists data using PostgreSQL, and provides full CRUD operations with proper error handling and idempotency.

This project was built as part of the Backend Wizards internship Stage 1 assessment.

## Features

- **POST /api/profiles** - Create profile (idempotent - returns existing if name already exists)
- **GET /api/profiles/{id}** - Retrieve single profile by UUID
- **GET /api/profiles** - List all profiles with optional filters (`gender`, `country_id`, `age_group`)
- **DELETE /api/profiles/{id}** - Delete profile
- Integrates with Genderize, Agify, and Nationalize APIs in parallel
- Proper 502 handling for invalid external API responses
- UUID v7 for IDs
- Automatic age group classification
- Case-insensitive filtering
- Full CORS support (`Access-Control-Allow-Origin: *`)
- Docker support for easy deployment

## Tech Stack

- ASP.NET Core 8 (Minimal API)
- Entity Framework Core + PostgreSQL
- Docker
- HttpClient with timeout and resilience

## API Endpoints

### 1. Create Profile
```http
POST /api/profiles
Content-Type: application/json

{
  "name": "ella"
}
-------------------------------------------------------------------------------
Success (201 Created)

{
  "status": "success",
  "data": {
    "id": "b3f9c1e2-7d4a-4c91-9c2a-1f0a8e5b6d12",
    "name": "ella",
    "gender": "female",
    "gender_probability": 0.99,
    "sample_size": 1234,
    "age": 46,
    "age_group": "adult",
    "country_id": "DRC",
    "country_probability": 0.85,
    "created_at": "2026-04-16T12:00:00Z"
  }
}

---------------------------------------------------------------------------
Duplicate Name
Returns 200 OK with "message": "Profile already exists"

2. Get Single Profile
GET /api/profiles/{id}

3. Get All Profiles (with filters)
GET /api/profiles?gender=male&country_id=NG&age_group=adult

Response includes count and array of simplified profiles.

4. Delete Profile
DELETE /api/profiles/{id}

Returns 204 No Content on success.

Error Responses
All errors follow this format:
{
  "status": "error",
  "message": "Error description here"
}


Local Development
Prerequisites

.NET 8 SDK
Docker Desktop (for PostgreSQL)
PostgreSQL (optional)

Running Locally

1. Start PostgreSQL with Docker:

docker run --name stage1-postgres \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=nameprofiles \
  -p 5432:5432 \
  -d postgres:16

2. Restore & Run Migrations:

dotnet restore
dotnet ef migrations add InitialCreate
dotnet ef database update

3. Run the API:

dotnet run

The API will be available at https://localhost:5001 or http://localhost:5000.