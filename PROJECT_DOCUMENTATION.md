# Local Product Manager Project Documentation

Last updated: 2026-06-19

## 1. Project overview

Local Product Manager is a LAN-first product, file, Amazon Attribution, pricing, and social marketing management system.

The software is designed for a small ecommerce workflow where one host computer runs the server and other computers on the same local network access the web UI through a browser. The host computer can access local and network paths, import reports, connect social accounts, and proxy files/images so client computers do not need direct access to the original shared folders.

Main goals:

- Maintain a local product catalog with SKU, name, cost, category, weight, dimensions, image path, marketplace listing URLs, and Amazon ASIN relationships.
- Support Amazon parent/child variation structures without duplicating marketing attribution workflows.
- Import Amazon Business Report CSV files to create products and ASIN families.
- Import Amazon Attribution CSV files by product/SKU and store channel links for Pinterest, Facebook, Instagram, TikTok, YouTube, and Shopify.
- Serve images/files from host-accessible folders to LAN users through the website.
- Manage social accounts and analytics snapshots from YouTube API plus manual CSV imports for Pinterest and Instagram.
- Store competitor video links for research in the Video Trend library.
- Generate local bilingual Gemini AI daily/weekly marketing reports.

## 2. Technology stack

Backend:

- ASP.NET Core Web API
- Entity Framework Core
- SQLite local database
- Hosted services for scheduled background tasks
- Static Angular build served by ASP.NET Core in production/LAN mode

Frontend:

- Angular
- Standalone components
- Local REST API services
- Browser clipboard/download APIs

Database:

- SQLite file: `LocalProductManager.Api/Data/local-product-manager.db`

## 3. Running model and API base

The API base is the backend server address used by the Angular frontend.

Examples:

- On the host computer: `http://localhost:5000/api`
- From another LAN computer: `https://192.168.1.154/api`

When the ASP.NET Core app serves the Angular build, browser routes and API routes are on the same host. The frontend API service points requests to the configured API base.

LAN usage:

1. Start the backend on the host computer.
2. Make sure Windows firewall allows the backend port.
3. Other computers open the host address in a browser.
4. Guest users can browse allowed data. Admin can log in and manage protected functions.

## 4. Authentication and permissions

Default admin account:

- Username: `admin123`
- Password: `123`

Guest behavior:

- Other clients can browse data by default as guest.
- Guest and normal users should not modify protected records.

Admin-only actions:

- User management
- Add/edit/delete file storage roots
- Product imports
- Attribution CSV imports
- Attribution link editing
- Social account connect/disconnect
- Manual social metric entry
- Social report imports
- Fetch log archive
- Gemini AI report execution

Important auth endpoints:

- `GET /api/auth/me`
- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/auth/users`
- `PUT /api/auth/users/{userId}`

## 5. Main frontend views

### Products

The product catalog view lists all products and supports search, platform filtering, product detail editing, imports, Excel export, and store shortcut links.

Supported fields:

- SKU
- Name
- Category
- Cost
- Weight
- Length
- Width
- Height
- Image path
- Product type
- SKU base
- Parent product
- Amazon parent ASIN
- Marketplace listing URLs

### Pricing

The pricing estimator calculates suggested marketplace pricing from:

- Platform
- Category fee rate
- Logistics option
- Quantity
- Unit cost
- Weight
- Dimensions
- Packaging cost
- Target margin
- Buffer

Pricing logic is currently frontend-side and can be reused from product detail using the product's saved cost, category, weight, and dimensions.

### Marketing

Marketing focuses on Amazon products and Amazon Attribution links.

The product list can filter:

- All parent/child products
- Singles only
- One item per variation family

Attribution channels:

- Pinterest
- Facebook
- Instagram
- TikTok
- YouTube
- Shopify

For non-admin users, attribution fields are read-only and save/import/delete operations are hidden.

### File storage

The File Storage view lets the host register local or network folder roots. LAN users can browse files through the website even when their own computer cannot access the original shared path.

The file proxy supports:

- Root configuration
- Folder browsing
- File preview
- Image preview
- Download/open through the server

### Video trend

The Video Trend view stores social/video links for competitor research. The server attempts to parse platform, video ID, title, embed URL, and thumbnail when possible.

Supported platforms:

- YouTube
- Instagram
- TikTok
- Pinterest
- Facebook
- Other

### Social Accounts

The Social Accounts page manages analytics accounts and charts.

Current integrations:

- YouTube OAuth and API fetch
- Pinterest CSV report import
- Instagram CSV report import
- Facebook CSV report import
- Demo data removed
- Manual/API placeholders for other platforms

### API Documentation

The API Documentation view is the in-app reference for backend endpoints, feature areas, and permission behavior.

## 6. Product and marketplace API

Products:

- `GET /api/products`
- `GET /api/products/export`
- `GET /api/products/{productId}`
- `GET /api/products/duplicate-check`
- `POST /api/products`
- `PUT /api/products/{productId}`
- `DELETE /api/products/{productId}`

Listings:

- `GET /api/products/{productId}/listings`
- `POST /api/products/{productId}/listings`
- `PUT /api/products/{productId}/listings/{listingId}`
- `DELETE /api/products/{productId}/listings/{listingId}`

Amazon ASINs:

- `GET /api/products/{productId}/amazon-asins`
- `POST /api/products/{productId}/amazon-asins`
- `PUT /api/products/{productId}/amazon-asins/{asinId}`
- `DELETE /api/products/{productId}/amazon-asins/{asinId}`

Store links:

- `GET /api/store-links`
- `PUT /api/store-links/{platform}`

## 7. Import API

Amazon Business Report:

- `POST /api/imports/amazon-business-report`

Creates or updates Amazon products and groups child products under parent ASINs when variation relationships are present.

Amazon Attribution:

- `POST /api/imports/amazon-attributions`
- `POST /api/imports/amazon-attribution-files`
- `POST /api/products/{productId}/amazon-attributions/import`

Matching rules:

- Direct SKU match when possible
- Five consecutive digit match for SKU/file-name mismatch cases
- Channel columns are mapped to Pinterest, Facebook, Instagram, TikTok, YouTube, and Shopify
- Click-through/Amazon link content is imported into product attribution fields when available

## 8. Image and file API

Image/file roots:

- `GET /api/images/roots`
- `POST /api/images/roots`
- `DELETE /api/images/roots`

Matching and browsing:

- `POST /api/images/match`
- `GET /api/images/host/browse`
- `GET /api/images/preview`
- `GET /api/images/remote-preview`
- `POST /api/images/from-listing`

Product image:

- `GET /api/products/{productId}/image`
- `POST /api/products/{productId}/image/from-amazon`
- `POST /api/products/{productId}/image/from-listing`

Design notes:

- Files remain in their original local/network path.
- The server streams or proxies content.
- Client computers only need access to the web app, not the original file share.
- Remote marketplace image extraction uses platform-specific and OpenGraph-style strategies where possible.

## 9. Amazon Attribution API

Product attribution links:

- `GET /api/products/{productId}/amazon-attributions`
- `PUT /api/products/{productId}/amazon-attributions/{platform}`
- `POST /api/products/{productId}/amazon-attributions/import`

Permissions:

- Admin can import and edit.
- Guest/normal users can view and copy links.

## 10. Social analytics API

Social account management:

- `GET /api/social-accounts`
- `POST /api/social-accounts`
- `PUT /api/social-accounts/{accountId}`
- `DELETE /api/social-accounts/{accountId}`
- `POST /api/social-accounts/{accountId}/metrics`
- `POST /api/social-accounts/sync`
- `GET /api/social-accounts/debug`

Logs:

- `GET /api/social-fetch-logs`
- `POST /api/social-fetch-logs/archive`

YouTube:

- `GET /api/youtube/auth/status`
- `GET /api/youtube/auth/start`
- `GET /api/youtube/oauth/callback`
- `POST /api/youtube/auth/disconnect`
- `POST /api/youtube/sync`
- `GET /api/youtube/videos`
- `GET /api/youtube/videos/debug`

Pinterest:

- `GET /api/pinterest/auth/status`
- `GET /api/pinterest/auth/start`
- `GET /api/pinterest/oauth/callback`
- `POST /api/pinterest/auth/disconnect`
- `POST /api/pinterest/sync`
- `POST /api/pinterest/report/import`
- `GET /api/pinterest/pins`

Instagram:

- `POST /api/instagram/report/import`
- `GET /api/instagram/posts`

Facebook:

- `POST /api/facebook/report/import`
- `GET /api/facebook/posts`

Metric model:

- Account-level snapshots store clicks, views, likes, comments, shares, followers, and snapshot date.
- Content-level snapshots store video/post/pin ID, title, date, views, likes, comments, and clicks/saves.
- YouTube totals are treated as fetched total snapshots.
- Instagram report rows are imported as total/lifetime snapshots from the CSV.
- Pinterest report rows are mapped from impressions/clicks/engagement data according to the report format.
- Facebook report rows map impressions/views/reach to views, reactions/likes to likes, post/link clicks to clicks, and comments/shares to their matching fields.

## 11. AI analysis

Gemini AI analysis endpoints:

- `GET /api/social-ai-analysis/status`
- `GET /api/social-ai-analysis/reports`
- `POST /api/social-ai-analysis/run`

Configuration:

- `AiAnalysis:GeminiApiKey`
- or environment variable `GEMINI_API_KEY`
- `AiAnalysis:GeminiModel`

Recommended free model:

- `gemini-3.1-flash-lite`

Scheduled reports:

- Daily bilingual report at server time `00:00`
- Weekly bilingual report every Sunday at server time `00:00`

Saved reports:

- `LocalProductManager.Api/ai-reports/social`

Report content:

- Chinese report
- English report
- Summary
- Platform changes
- Unusual drops/spikes
- Next actions
- Data caveats
- Prompt data for audit

## 12. Video trend API

- `GET /api/video-trends`
- `GET /api/video-trends?platform={platform}`
- `POST /api/video-trends`
- `DELETE /api/video-trends/{id}`

Video trend data:

- Platform
- URL
- Title
- Description
- Author
- Platform video ID
- Embed URL
- Thumbnail URL
- Tags

## 13. Database tables

Core tables:

- `products`
- `product_listings`
- `amazon_asins`
- `amazon_attribution_links`
- `social_accounts`
- `social_metric_snapshots`
- `social_video_metric_snapshots`
- `app_users`
- `app_user_sessions`
- `video_trend_items`

The database schema is upgraded on startup through `DatabaseSchemaService`.

## 14. Current operational notes

- Demo social data seeding has been removed.
- Startup schema upgrade also removes demo/test social accounts if old data remains.
- Product SKU duplicate checking exists.
- Product export creates an Excel file.
- File storage roots and social management are protected by admin permissions.
- Static Angular output is served by ASP.NET Core when the build exists.
- Responses are intended for LAN use, not public internet exposure.

## 15. Known warnings and future improvements

Known warning:

- `SQLitePCLRaw.lib.e_sqlite3 2.1.11` currently reports a NuGet vulnerability warning during build.

Suggested future improvements:

- Add database migrations instead of manual schema upgrade SQL.
- Add richer audit logs for every admin mutation.
- Add real API connectors for Facebook, Instagram, TikTok, and Pinterest when platform approval is available.
- Move pricing calculation into a shared backend function if multiple clients need identical pricing output.
- Add report download/open buttons for saved AI reports.
- Add structured API documentation generation from controllers.
