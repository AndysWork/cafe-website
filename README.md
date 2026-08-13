# Maa Tara Cafe Website

## Project Structure

- `frontend/`: Angular application
- `api/`: Azure Functions (.NET) backend

## Timezone Standard

This codebase uses **IST (India Standard Time)** as the business timezone across frontend and backend flows.

- Avoid introducing new direct UTC calls in business logic, such as `DateTime.UtcNow`, `ToUniversalTime()`, `Date.UTC(...)`, `getUTC*`, `setUTC*`, and `toISOString()` for business dates.
- Backend timestamp and date normalization should use helpers in `api/Services/MongoService.cs`:
	- `GetIstNow()`
	- `ConvertToIst(...)`
	- `ConvertToUtc(...)`
	- `NormalizeToIstCalendarDate(...)`
	- storage-day window helper `GetStorageRangeForIstDay(...)`
- Frontend business date formatting and payload preparation should use utilities in `frontend/src/app/utils/date-utils.ts`:
	- `getIstInputDate(...)`
	- `getIstIsoString(...)`
	- `getIstFileStamp(...)`

## Validation Notes

- Backend build: `dotnet build` from `api/`
- Frontend build: `npm run build` from `frontend/`
- UTC-pattern audit can be run using repository audit scripts/commands used during migration.
