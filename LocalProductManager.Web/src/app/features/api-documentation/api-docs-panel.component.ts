import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-api-docs-panel',
  imports: [CommonModule],
  templateUrl: './api-docs-panel.component.html',
  styleUrl: './api-docs-panel.component.css',
})
export class ApiDocsPanelComponent {
  @Input({ required: true }) apiBaseUrl = '';

  readonly sections = [
    {
      title: 'Authentication and permissions',
      description: 'Local login controls admin-only actions. The host admin can manage users, storage roots, imports, attribution links, social accounts, and AI reports.',
      endpoints: [
        'GET /api/auth/me - current user/session',
        'POST /api/auth/login - login as admin or another local user',
        'POST /api/auth/logout - end current session',
        'GET /api/auth/users - admin user list',
        'PUT /api/auth/users/{userId} - update user role and permissions',
      ],
    },
    {
      title: 'Products and marketplace listings',
      description: 'Read-only product endpoints can be called from another LAN computer without a database connection. Use lookup first, then request detail, extended data, and image files.',
      endpoints: [
        'GET /api/products?keyword={text}&platform={platform}&hasAmazon={bool} - search/filter product list',
        'GET /api/products/lookup?code={INV-ID-or-SKU} - exact Inventory ID or SKU lookup',
        'GET /api/products/export - download product Excel workbook',
        'GET /api/products/{productId} - product detail',
        'GET /api/products/{productId}/extended-data - material, compliance, packaging, identifiers, suppliers, and prices',
        'GET /api/products/{productId}/image?thumbnail=512 - primary image preview',
        'GET /api/products/{productId}/image/files - all product photo/video files and media types',
        'GET /api/products/{productId}/image/files/download-all - download every product image as ZIP',
        'GET /api/products/{productId}/image/files/download-all-media - download every product photo and video as ZIP',
        'GET /api/products/{productId}/image/files/{fileName} - stream or download one product photo/video',
        'POST /api/products/{productId}/image/upload-many - upload up to 20 JPG/PNG/WebP/GIF/BMP/MP4/MOV/WebM media files',
        'GET /api/products/duplicate-check - SKU duplicate check',
        'POST /api/products - create product',
        'PUT /api/products/{productId} - update product',
        'DELETE /api/products/{productId} - delete product',
        'GET/POST/PUT/DELETE /api/products/{productId}/listings - manage platform listing URLs',
        'GET/POST/PUT/DELETE /api/products/{productId}/amazon-asins - manage ASINs',
      ],
    },
    {
      title: 'Imports and Amazon Attribution',
      description: 'CSV imports create Amazon products from Business Reports and load Amazon Attribution links by SKU or five-digit SKU family matching.',
      endpoints: [
        'POST /api/imports/amazon-business-report - import Amazon products and variation families',
        'POST /api/imports/amazon-attributions - import one attribution CSV',
        'POST /api/imports/amazon-attribution-files - bulk import attribution CSV files',
        'GET /api/products/{productId}/amazon-attributions - attribution links for one product',
        'PUT /api/products/{productId}/amazon-attributions/{platform} - save one channel link',
        'POST /api/products/{productId}/amazon-attributions/import - import a product attribution CSV',
      ],
    },
    {
      title: 'Images and file storage',
      description: 'The server can browse local or network folders, proxy previews/downloads for LAN clients, match SKU image folders, and fetch remote marketplace image previews without downloading them into product records.',
      endpoints: [
        'GET/POST/DELETE /api/images/roots - manage server storage roots',
        'POST /api/images/match - match SKU against a folder or direct product image folder',
        'GET /api/images/host/browse - browse a configured server path',
        'GET /api/images/preview - preview/download a server-accessible file',
        'GET /api/images/remote-preview - proxy remote image preview',
        'POST /api/images/from-listing - extract image candidates from marketplace listing URL',
        'GET /api/products/{productId}/image - preview product image through server',
        'POST /api/products/{productId}/image/from-amazon - use Amazon image',
        'POST /api/products/{productId}/image/from-listing - use listing image',
      ],
    },
    {
      title: 'Social accounts and analytics',
      description: 'Social Accounts stores YouTube/Pinterest/Instagram reports, account snapshots, content snapshots, charts, debug tables, and fetch logs. Manual imports are admin-only.',
      endpoints: [
        'GET/POST/PUT/DELETE /api/social-accounts - manage social accounts',
        'POST /api/social-accounts/{accountId}/metrics - save manual metric snapshot',
        'GET /api/social-accounts/debug - host-only raw debug snapshot',
        'GET /api/social-fetch-logs - fetch/import/AI log history',
        'POST /api/social-fetch-logs/archive - archive logs to server logging folder',
        'GET/POST /api/youtube/auth/* - YouTube OAuth connect/disconnect',
        'POST /api/youtube/sync - fetch YouTube channel/video data',
        'GET /api/youtube/videos - stored YouTube video metrics',
        'POST /api/pinterest/report/import - import Pinterest analytics CSV',
        'GET /api/pinterest/pins - stored Pinterest pin metrics',
        'POST /api/instagram/report/import - import Instagram CSV',
        'GET /api/instagram/posts - stored Instagram post/reel metrics',
        'POST /api/facebook/report/import - import Facebook CSV',
        'GET /api/facebook/posts - stored Facebook post metrics',
      ],
    },
    {
      title: 'Video trend research',
      description: 'A research library for competitor/social videos. Links are parsed by platform when possible, with title, author, thumbnail/embed fields saved for study.',
      endpoints: [
        'GET /api/video-trends - list saved trend items',
        'GET /api/video-trends?platform=youtube - filter by platform',
        'POST /api/video-trends - add a trend link',
        'DELETE /api/video-trends/{id} - remove a trend item',
      ],
    },
    {
      title: 'Store links, pricing, and AI analysis',
      description: 'Store links open marketplace shops from the homepage. Pricing is calculated client-side from platform/category/logistics rules. Gemini AI reports are generated by the server and saved locally.',
      endpoints: [
        'GET /api/store-links - store homepage URLs',
        'PUT /api/store-links/{platform} - save store URL',
        'GET /api/social-ai-analysis/status - Gemini configuration and report folder',
        'GET /api/social-ai-analysis/reports - recent saved AI reports',
        'POST /api/social-ai-analysis/run - run daily or weekly bilingual report',
      ],
    },
  ];
}
